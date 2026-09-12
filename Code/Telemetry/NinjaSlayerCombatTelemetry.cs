using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Telemetry;

namespace NinjaSlayer.Code.Telemetry;

internal static class NinjaSlayerCombatTelemetry
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static RunSavedData<BalanceCombatData> _saved = null!;
    private static CombatHistory? _history;
    private static ICombatState? _state;
    private static RunState? _run;
    private static Player? _player;
    private static BalanceCombat? _combat;
    private static int _cursor;
    private static readonly Dictionary<CardModel, int> CardInstances = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<DamageResult, BattleAction> DamageActions = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<DamageResult, FinisherProtectionToken> ProtectedHits = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Creature, int> CommittingDeaths = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Creature, int> ObservedCreatures = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<PowerModel, int> PowerAmounts = new(ReferenceEqualityComparer.Instance);
    private static bool _public;
    private static bool _balance;
    private static bool _recorded;
    private static bool _registered;
    internal static bool Capturing => _combat is not null;

    public static void Register()
    {
        _saved = RitsuLibFramework.GetRunSavedDataStore(NinjaSlayerIds.ModId)
            .Register<BalanceCombatData>("balance_combats_v1", options: new RunSavedDataOptions
            { WritePolicy = RunSavedDataWritePolicy.WhenNonDefault });
        NinjaSlayerReplay.Register();
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(evt =>
        {
            if (evt.CombatState is { } state) Start((RunState)evt.RunState, state);
        });
        RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(evt => Record((RunState)evt.RunState, evt.Room, true));
        RitsuLibFramework.SubscribeLifecycle<RoomExitedEvent>(evt =>
        {
            if (evt.RunManager.DebugOnlyGetState() is { } run && LocalContext.GetMe(run) is { } player
                && player.Character is NinjaSlayerCharacter
                && TelemetryApi.GetClient(NinjaSlayerIds.ModId).IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId))
                _saved.Modify(run, data => data.Floors[$"{run.TotalFloor}/{player.NetId}"] = new BalanceFloor
                {
                    PlayerId = player.NetId.ToString(CultureInfo.InvariantCulture), DeckSize = player.Deck.Cards.Count,
                    Hp = player.Creature.CurrentHp, MaxHp = player.Creature.MaxHp, Gold = player.Gold
                });
            Stop();
        });
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(_ => Stop());
        RitsuLibFramework.SubscribeLifecycle<SideTurnStartedEvent>(_ => Emit(new BattleAction { Kind = "turn" }));
        _registered = true;
    }

    private static void Start(RunState run, ICombatState state)
    {
        Stop();
        _player = LocalContext.GetMe(run);
        if (_player?.Character is not NinjaSlayerCharacter || NinjaSlayer.Code.Nodes.NinjaSlayerFreeControl.WasUsed(run)) return;
        _balance = TelemetryApi.GetClient(NinjaSlayerIds.ModId).IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId);
        _public = NinjaSlayerTelemetryConsent.ReplayEnabled;
        if (!_balance && !_public) return;
        _run = run;
        _recorded = false;
        _state = state;
        _combat = new BalanceCombat
        {
            Floor = run.TotalFloor, RoomIndex = run.CurrentMapPointHistoryEntry!.Rooms.Count - 1,
            Version = NinjaSlayerVersion.Current, MeasurementVersion = 3,
            Players = [new BalancePlayerCombat { PlayerId = _player.NetId.ToString(CultureInfo.InvariantCulture), Metrics = new() }],
            Coverage = "complete"
        };
        _history = CombatManager.Instance.History;
        _cursor = _history.Entries.Count();
        _history.Changed += ObserveHistory;
        if (_public) NinjaSlayerReplay.StartCombat(run, _combat);
        Emit(new BattleAction { Kind = "combat_start", Actor = Actor(_player.Creature), Hp = _player.Creature.CurrentHp, MaxHp = _player.Creature.MaxHp });
        foreach (Creature creature in state.Creatures) ObserveCreature(creature);
        foreach (CardModel card in _player.Piles.SelectMany(pile => pile.Cards)) PileChanged(card);
        Emit(new BattleAction { Kind = "snapshot_end", Actor = "self" });
    }

    internal static void ConsentChanged()
    {
        if (!_registered) return;
        if (!NinjaSlayerTelemetryConsent.ReplayEnabled) NinjaSlayerReplay.WithdrawActiveRun();
        if (_combat is null) return;
        if (_public && !NinjaSlayerTelemetryConsent.ReplayEnabled)
        {
            _public = false;
        }
        if (!TelemetryApi.GetClient(NinjaSlayerIds.ModId).IsEnabled(NinjaSlayerBalanceTelemetry.BalanceRequestId)) _balance = false;
        if (!_balance && !_public) Stop();
        // Mid-combat opt-in begins at the next room; no earlier actions become public.
    }

    private static void Stop()
    {
        if (_history is not null) _history.Changed -= ObserveHistory;
        foreach (Creature creature in ObservedCreatures.Keys)
        {
            creature.PowerIncreased -= PowerIncreased;
            creature.PowerDecreased -= PowerDecreased;
            creature.PowerRemoved -= PowerRemoved;
        }
        ObservedCreatures.Clear(); PowerAmounts.Clear();
        _history = null; _state = null; _combat = null; _run = null; _player = null;
        _cursor = 0; _public = false; _balance = false;
        CardInstances.Clear(); DamageActions.Clear(); ProtectedHits.Clear(); CommittingDeaths.Clear();
    }

    public static void Record(RunState run, CombatRoom room, bool won)
    {
        ConsentChanged();
        if (_combat is null || _recorded || !ReferenceEquals(_state, room.CombatState)) return;
        ObserveHistory();
        _combat.Encounter = room.Encounter.Id.ToString();
        _combat.Won = won;
        _combat.Rounds = room.CombatState.RoundNumber;
        Emit(new BattleAction { Kind = "combat_end", Amount = won ? 1 : 0 });
        if (_balance) _saved.Modify(run, data => data.Combats[$"{_combat.Floor}/{_combat.RoomIndex}"] = _combat);
        if (_public) NinjaSlayerReplay.EndCombat(run, _combat);
        _recorded = true;
    }

    public static JsonNode Export(RunState run) => JsonSerializer.SerializeToNode(_saved.Get(run), JsonOptions)!;

    private static void ObserveHistory()
    {
        ConsentChanged();
        if (_history is null || _combat is null) return;
        // Both supported hosts expose the same append-only List through IEnumerable.
        var entries = (IReadOnlyList<CombatHistoryEntry>)_history.Entries;
        if (entries.Count < _cursor) _cursor = 0; // Native Clear precedes the first combat action.
        while (_cursor < entries.Count) Observe(entries[_cursor++]);
    }

    private static void Observe(CombatHistoryEntry entry)
    {
        var action = new BattleAction { Actor = Actor(entry.Actor) };
        CardModel? card = entry switch
        {
            CardDrawnEntry e => e.Card, CardDiscardedEntry e => e.Card,
            CardExhaustedEntry e => e.Card, CardGeneratedEntry e => e.Card,
            CardPlayStartedEntry e => e.CardPlay.Card, CardPlayFinishedEntry e => e.CardPlay.Card,
            CardAfflictedEntry e => e.Card, _ => null
        };
        if (card is not null)
        {
            if (card.Owner != _player) return;
            SnapshotCard(action, card);
        }
        switch (entry)
        {
            case CardDrawnEntry: action.Kind = "draw"; break;
            case CardDiscardedEntry: action.Kind = "discard"; break;
            case CardExhaustedEntry: action.Kind = "exhaust"; break;
            case CardGeneratedEntry: action.Kind = "generate"; break;
            case CardAfflictedEntry e: action.Kind = "afflict"; action.Source = e.Affliction.Id.ToString(); break;
            case CardPlayStartedEntry e:
                action.Kind = "play";
                action.Auto = e.CardPlay.IsAutoPlay; action.Repeat = e.CardPlay.PlayIndex;
                action.Energy = e.CardPlay.IsFirstInSeries ? e.CardPlay.Resources.EnergySpent : 0;
                action.Stars = e.CardPlay.IsFirstInSeries ? e.CardPlay.Resources.StarsSpent : 0;
                action.Target = Actor(e.CardPlay.Target);
                break;
            case CardPlayFinishedEntry: action.Kind = "resolve"; break;
            case DamageReceivedEntry e:
                if (e.Dealer?.Player != _player && e.Receiver.Player != _player) return;
                if (e.Receiver.Player is { } receiver && receiver != _player) return;
                action.Kind = "hit"; action.Actor = Actor(e.Dealer); action.Target = Actor(e.Receiver);
                action.HpLoss = e.Result.UnblockedDamage;
                action.Blocked = e.Result.BlockedDamage; action.Overkill = e.Result.OverkillDamage;
                if (ProtectedHits.TryGetValue(e.Result, out var protection))
                {
                    if (protection.TemporaryHpBumpApplied) action.HpLoss--;
                    action.Overkill = Math.Max(0, protection.DisplayDamage - action.HpLoss.Value - 1);
                }
                action.Killed = e.Result.WasTargetKilled;
                action.Hp = e.Receiver.CurrentHp;
                action.Source = e.CardSource is Cards.RedesignV1.BlackFlameRedesignV1 ? "black_flame" : "unattributed";
                if (e.CardSource is { } source && source.Owner == _player && e.Result.Props.IsPoweredAttack())
                {
                    SnapshotCard(action, source);
                    action.Source = source.Id.ToString();
                }
                DamageActions.Add(e.Result, action);
                break;
            case BlockGainedEntry e:
                if (e.Receiver.Player != _player) return;
                action.Kind = "block"; action.Amount = e.Amount; action.Actor = "self";
                if (e.CardPlay is { } play && play.Card.Owner == _player) SnapshotCard(action, play.Card);
                break;
            case MonsterPerformedMoveEntry e:
                action.Kind = "move"; action.Model = e.Monster.Id.ToString(); action.Source = e.Move.Id;
                break;
            case PowerReceivedEntry: return; // Native creature events record actual amounts, including direct removal.
            case PotionUsedEntry e:
                if (e.Potion.Owner != _player) return;
                action.Kind = "potion"; action.Model = e.Potion.Id.ToString(); action.Target = Actor(e.Target); break;
            case OrbChanneledEntry e:
                if (e.Orb.Owner != _player) return;
                action.Kind = "channel"; action.Model = e.Orb.Id.ToString(); break;
            default: return;
        }
        Emit(action);
    }

    private static string? Actor(Creature? creature)
    {
        if (creature is null) return null;
        if (creature.Player is { } player)
            return player == _player ? "self" : "uncollected";
        return $"{creature.ModelId}/{creature.CombatId}";
    }

    private static void SnapshotCard(BattleAction action, CardModel card)
    {
        action.Model = card.Id.ToString(); action.Upgrade = card.CurrentUpgradeLevel;
        if (!CardInstances.TryGetValue(card, out int instance)) CardInstances.Add(card, instance = CardInstances.Count + 1);
        action.Instance = instance;
        action.Vars = card.DynamicVars.ToDictionary(pair => pair.Key, pair => pair.Value.BaseValue);
        action.Pile = (card.Pile?.Type ?? PileType.None).ToString();
    }

    internal static void PileChanged(CardModel card)
    {
        if (_combat is null || card.Owner != _player) return;
        var action = new BattleAction { Kind = "pile", Actor = "self" };
        SnapshotCard(action, card);
        Emit(action);
    }

    private static void Emit(BattleAction action)
    {
        ConsentChanged();
        if (_combat is null || _recorded) return;
        action.Round = _state!.RoundNumber; action.Side = _state.CurrentSide.ToString();
        Accumulate(_combat.Players[0], action);
        if (_public && NinjaSlayerTelemetryConsent.ReplayEnabled) NinjaSlayerReplay.Append(_run!, action);
    }

    internal static void Mechanic(string kind, Creature owner, decimal amount, string? model = null)
    {
        if (_combat is null || owner.Player != _player) return;
        Emit(new BattleAction { Kind = kind, Actor = "self", Amount = amount, Model = model });
    }

    internal static void HpChanged(Creature creature, decimal delta)
    {
        if (_combat is null || !ObservedCreatures.TryGetValue(creature, out int previousHp)) return;
        // Finisher protection can temporarily raise one HP and immediately remove it again.
        // Its native callback delta is not real HP loss; compare these immediate observed states.
        delta = creature.CurrentHp - previousHp;
        ObservedCreatures[creature] = creature.CurrentHp;
        if (delta == 0) return;
        Emit(new BattleAction { Kind = delta > 0 ? "heal" : "hp_loss", Actor = Actor(creature), Amount = Math.Abs(delta), Hp = creature.CurrentHp, MaxHp = creature.MaxHp });
    }

    internal static void ProtectedDamage(DamageResult result, FinisherProtectionToken token)
    {
        if (_combat is not null) ProtectedHits.Add(result, token);
    }

    internal static void BeforeFinisherDeath(Creature target)
    {
        if (_combat is not null) CommittingDeaths[target] = target.CurrentHp;
    }

    internal static void AfterDeath(Creature target)
    {
        if (_combat is null || !target.IsDead) return;
        HpChanged(target, 0);
        if (!CommittingDeaths.Remove(target, out int hpBefore)) return;
        var result = ProtectedHits.Keys.FirstOrDefault(result => ReferenceEquals(result.Receiver, target));
        if (result is null || !DamageActions.TryGetValue(result, out var hit)) return; // The original hit may belong to an uncollected player.
        Emit(new BattleAction { Kind = "hit", Actor = hit.Actor, Target = hit.Target, Source = hit.Source,
            Model = hit.Model, Instance = hit.Instance, Upgrade = hit.Upgrade, Vars = hit.Vars,
            HpLoss = hpBefore - target.CurrentHp, Blocked = 0, Overkill = 0, Hp = target.CurrentHp, Killed = true });
    }

    internal static void ObserveCreature(Creature creature)
    {
        if (_combat is null || (creature.Player is { } player && player != _player)
            || !ObservedCreatures.TryAdd(creature, creature.CurrentHp)) return;
        creature.PowerIncreased += PowerIncreased;
        creature.PowerDecreased += PowerDecreased;
        creature.PowerRemoved += PowerRemoved;
        Emit(new BattleAction { Kind = "creature", Actor = Actor(creature), Model = creature.ModelId.ToString(), Hp = creature.CurrentHp, MaxHp = creature.MaxHp });
        foreach (PowerModel power in creature.Powers)
        {
            PowerAmounts[power] = power.Amount;
            Emit(new BattleAction { Kind = "power_snapshot", Target = Actor(creature), Model = power.Id.ToString(), Value = power.Amount });
        }
    }

    private static void PowerIncreased(PowerModel power, int change, bool silent) => RecordPower(power, power.Amount);
    private static void PowerDecreased(PowerModel power, bool silent) => RecordPower(power, power.Amount);
    private static void PowerRemoved(PowerModel power) => RecordPower(power, 0);
    private static void RecordPower(PowerModel power, int amount)
    {
        if (_combat is null || _recorded) return; // Combat-end cleanup is outside the measured battle.
        int delta = amount - PowerAmounts.GetValueOrDefault(power);
        PowerAmounts[power] = amount;
        if (delta == 0) return;
        Emit(new BattleAction { Kind = "power", Target = Actor(power.Owner), Model = power.Id.ToString(), Amount = delta, Value = amount });
        if (power is Powers.NarakuLifePower && delta > 0) Mechanic("naraku_gained", power.Owner, delta);
        if (power is Powers.KaratePower && delta != 0) Mechanic(delta > 0 ? "karate_gained" : "karate_lost", power.Owner, Math.Abs(delta));
    }

    // The command that owns an independent effect supplies its source, never the last played card.
    internal static void AttributeDamage(IEnumerable<DamageResult> results, string source)
    {
        if (_combat is null) return;
        foreach (var result in results)
        {
            if (!DamageActions.TryGetValue(result, out var action)) continue;
            var metrics = _combat.Players[0].Metrics!;
            if (action.Actor == "self" && action.Target != "self")
            {
                decimal damage = action.HpLoss!.Value + action.Blocked!.Value;
                Add(metrics.DamageBySource, action.Source!, -damage);
                Add(metrics.DamageBySource, source, damage);
            }
            action.Source = source; action.Model = null; action.Instance = null; action.Upgrade = null; action.Vars = null;
        }
    }

    internal static void Accumulate(BalancePlayerCombat player, BattleAction action)
    {
        BalanceMetrics metrics = player.Metrics!;
        BalanceCardUse? card = null;
        if (action.Model?.StartsWith("CARD.", StringComparison.Ordinal) == true && action.Actor == "self")
        {
            if (!player.Cards.TryGetValue(action.Model, out card)) player.Cards.Add(action.Model, card = new());
            string variant = $"{action.Model}/{action.Upgrade}";
            if (!metrics.CardVariants.TryGetValue(variant, out var byUpgrade)) metrics.CardVariants.Add(variant, byUpgrade = new());
            CountUse(byUpgrade, action);
            CountUse(card, action);
        }
        switch (action.Kind)
        {
            case "hit":
                if (action.Target == "self") metrics.Blocked += action.Blocked!.Value;
                else if (action.Actor == "self")
                {
                    metrics.Damage += action.HpLoss!.Value;
                    metrics.EnemyBlocked += action.Blocked!.Value;
                    if (action.Killed == true) metrics.Kills++;
                    Add(metrics.DamageBySource, action.Source!, action.HpLoss.Value + action.Blocked.Value);
                }
                break;
            case "hp_loss": if (action.Actor == "self") metrics.HpLost += action.Amount!.Value; break;
            case "heal": if (action.Actor == "self") metrics.Healed += action.Amount!.Value; break;
            case "block": metrics.BlockGenerated += action.Amount!.Value; break;
            case "power":
                if (action.Target == "self") Add(metrics.PowerChanges, action.Model!, action.Amount!.Value);
                break;
            case "draw": case "play": case "resolve": case "combat_start": case "combat_end":
            case "turn": case "move": case "potion": case "channel": case "afflict": case "pile": case "creature": case "snapshot_end": case "power_snapshot": break;
            default: Add(metrics.Mechanics, action.Kind, action.Amount ?? 1); break;
        }
        if (action.Model == ModelDb.Card<Cards.RedesignV1.ChadoEnergyRedesignV1>().Id.ToString()
            && action.Kind is "generate" or "exhaust")
            Add(metrics.Mechanics, action.Kind == "generate" ? "chado_generated" : "chado_exhausted", 1);
    }

    private static void CountUse(BalanceCardUse use, BattleAction action)
    {
        switch (action.Kind)
        {
            case "draw": use.Drawn++; break;
            case "play":
                use.Started++;
                if (action.Repeat == 0)
                {
                    if (action.Auto == true) use.AutoPlays++; else use.ManualPlays++;
                    use.EnergySpent += action.Energy!.Value; use.StarsSpent += action.Stars!.Value;
                }
                break;
            case "resolve": use.Finished++; break;
            case "discard": use.Discarded++; break;
            case "exhaust": use.Exhausted++; break;
            case "generate": use.Generated++; break;
            case "hit":
                if (action.Target != "self") { use.Damage += action.HpLoss!.Value; use.EnemyBlocked += action.Blocked!.Value; }
                break;
            case "block": use.Block += action.Amount!.Value; break;
        }
    }

    private static void Add(Dictionary<string, decimal> counts, string key, decimal value) => counts[key] = counts.GetValueOrDefault(key) + value;
}

public sealed class BalanceCombatData
{
    public Dictionary<string, BalanceCombat> Combats { get; set; } = new();
    public Dictionary<string, BalanceFloor> Floors { get; set; } = new();
}
public sealed class BalanceFloor
{
    public string PlayerId { get; set; } = "";
    public int DeckSize { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Gold { get; set; }
}
public sealed class BalanceCombat
{
    public int Floor { get; set; }
    public int RoomIndex { get; set; }
    public string Encounter { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Won { get; set; }
    public int Rounds { get; set; }
    public int? MeasurementVersion { get; set; }
    public string? Coverage { get; set; }
    public List<BalancePlayerCombat> Players { get; set; } = new();
}
public sealed class BalancePlayerCombat
{
    public string PlayerId { get; set; } = "";
    public Dictionary<string, BalanceCardUse> Cards { get; set; } = new();
    public BalanceMetrics? Metrics { get; set; }
}
public sealed class BalanceCardUse
{
    public int Drawn { get; set; }
    public int Started { get; set; }
    public int Finished { get; set; }
    public int ManualPlays { get; set; }
    public int AutoPlays { get; set; }
    public int EnergySpent { get; set; }
    public int StarsSpent { get; set; }
    public int Discarded { get; set; }
    public int Exhausted { get; set; }
    public int Generated { get; set; }
    public decimal Damage { get; set; }
    public decimal EnemyBlocked { get; set; }
    public decimal Block { get; set; }
}
public sealed class BalanceMetrics
{
    public decimal Damage { get; set; }
    public decimal EnemyBlocked { get; set; }
    public decimal HpLost { get; set; }
    public decimal Blocked { get; set; }
    public decimal BlockGenerated { get; set; }
    public decimal Healed { get; set; }
    public int Kills { get; set; }
    public Dictionary<string, decimal> DamageBySource { get; set; } = new();
    public Dictionary<string, decimal> Mechanics { get; set; } = new();
    public Dictionary<string, decimal> PowerChanges { get; set; } = new();
    public Dictionary<string, BalanceCardUse> CardVariants { get; set; } = new();
}
public sealed class BattleAction
{
    public string Kind { get; set; } = "";
    public int Round { get; set; }
    public string Side { get; set; } = "";
    public string? Actor { get; set; }
    public string? Target { get; set; }
    public string? Model { get; set; }
    public string? Source { get; set; }
    public string? Pile { get; set; }
    public int? Instance { get; set; }
    public int? Upgrade { get; set; }
    public bool? Auto { get; set; }
    public int? Repeat { get; set; }
    public int? Energy { get; set; }
    public int? Stars { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Value { get; set; }
    public int? HpLoss { get; set; }
    public int? Blocked { get; set; }
    public int? Overkill { get; set; }
    public bool? Killed { get; set; }
    public int? Hp { get; set; }
    public int? MaxHp { get; set; }
    public Dictionary<string, decimal>? Vars { get; set; }
}
