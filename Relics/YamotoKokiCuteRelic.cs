using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class YamotoKokiCuteRelic : NinjaSlayerRelicTemplate
{
    private const string CombatsKey = "Combats";
    private const float MissileAttackIntervalSeconds = 0.2f;
    private int _combatsLeft = 5;
    private bool _hasPlayedEntrance;
    private bool _hasPlayedFarewell;

    public override RelicRarity Rarity => RelicRarity.Event;
    public override bool AddsPet => true;
    public override bool SpawnsPets => true;
    public override bool IsUsedUp => CombatsLeft <= 0;
    public override bool ShowCounter => true;
    public override int DisplayAmount => Math.Max(0, CombatsLeft);

    public override RelicAssetProfile AssetProfile =>
        NinjaSlayerRelicAssets.For(this);

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar(CombatsKey, CombatsLeft)
    ];

    [SavedProperty]
    public int CombatsLeft
    {
        get => _combatsLeft;
        set
        {
            AssertMutable();
            _combatsLeft = value;
            DynamicVars[CombatsKey].BaseValue = _combatsLeft;
            InvokeDisplayAmountChanged();
            Status = IsUsedUp ? RelicStatus.Disabled : RelicStatus.Normal;
        }
    }

    [SavedProperty]
    public bool HasPlayedEntrance
    {
        get => _hasPlayedEntrance;
        set
        {
            AssertMutable();
            _hasPlayedEntrance = value;
        }
    }

    [SavedProperty]
    public bool HasPlayedFarewell
    {
        get => _hasPlayedFarewell;
        set
        {
            AssertMutable();
            _hasPlayedFarewell = value;
        }
    }

    public override async Task BeforeCombatStart()
    {
        if (IsUsedUp || !YamotoKokiPartyState.IsController(this))
        {
            return;
        }

        Flash();
        Creature? existingCompanion = YamotoKokiPartyState.FindLivingCompanion(Owner.RunState);
        bool created = existingCompanion == null;
        Creature yamotoKoki = existingCompanion
            ?? await PlayerCmd.AddPet<YamotoKokiMonster>(Owner);

        CompanionIntentLifecycle.BeginCombat(yamotoKoki);
        await AssignIntent(yamotoKoki, YamotoKokiMonster.SummonMissileMoveId);
        if (created && !YamotoKokiPartyState.HasPlayedEntrance(Owner.RunState))
        {
            HasPlayedEntrance = true;
            _ = TaskHelper.RunSafely(YamotoKokiCombatAnimations.PlayEntrance(yamotoKoki));
        }
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        if (IsUsedUp || IsMelted)
        {
            return Task.CompletedTask;
        }

        CombatsLeft = Math.Max(0, CombatsLeft - 1);
        if (YamotoKokiPartyState.GetActiveRelicCount(Owner.RunState) != 0
            || YamotoKokiPartyState.HasPlayedFarewell(Owner.RunState))
        {
            return Task.CompletedTask;
        }

        Creature? yamotoKoki = YamotoKokiPartyState.FindLivingCompanion(Owner.RunState);
        if (yamotoKoki == null || yamotoKoki.IsDead)
        {
            return Task.CompletedTask;
        }

        CompanionIntentLifecycle.Invalidate(yamotoKoki);
        HasPlayedFarewell = true;
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiByeEvent);
        _ = TaskHelper.RunSafely(YamotoKokiCombatAnimations.PlayFarewell(yamotoKoki));
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner || !YamotoKokiPartyState.IsController(this))
        {
            return;
        }

        await PerformTurnStartActions();
    }

    private async Task PerformTurnStartActions()
    {
        int turnNumber = Owner.PlayerCombatState?.TurnNumber ?? 0;
        List<Creature> armedMissiles = Owner.PlayerCombatState?.Pets
            .Where(pet => pet.Monster is YamotoKokiOrigamiMissile missile
                && missile.CanExplodeOnTurn(turnNumber))
            .ToList() ?? [];
        if (armedMissiles.Count > 0)
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiFastAttackEvent);
        }

        Creature? yamotoKoki = YamotoKokiPartyState.FindLivingCompanion(Owner.RunState);
        YamotoKokiMonster? monster = yamotoKoki?.Monster as YamotoKokiMonster;
        MoveState? scheduledMove = monster?.NextMove;

        // Race only visual clocks. This hook alone owns launch RNG, impacts and deaths.
        // Process a landed missile before launching another, never wait for the whole volley.
        var flights = new Queue<(YamotoKokiOrigamiMissile Missile, Creature? Target, Task Flight)>();
        var visualTasks = new List<Task>();
        Task nextLaunch = Task.CompletedTask;
        int launched = 0;
        try
        {
            while (flights.Count > 0 || launched < armedMissiles.Count && !CombatManager.Instance.IsOverOrEnding)
            {
                if (flights.TryPeek(out var landed) && landed.Flight.IsCompleted)
                {
                    flights.Dequeue();
                    await landed.Flight;
                    await landed.Missile.CompleteExplosion(landed.Target);
                    continue;
                }
                bool canLaunch = launched < armedMissiles.Count && !CombatManager.Instance.IsOverOrEnding;
                if (canLaunch && nextLaunch.IsCompleted)
                {
                    await nextLaunch;
                    var missile = (YamotoKokiOrigamiMissile)armedMissiles[launched++].Monster!;
                    Creature? target = missile.BeginExplosion();
                    Task flight = target == null ? Task.CompletedTask : missile.LaunchAtTarget(target);
                    flights.Enqueue((missile, target, flight));
                    visualTasks.Add(flight);
                    nextLaunch = Cmd.Wait(MissileAttackIntervalSeconds);
                    continue;
                }
                if (canLaunch && flights.TryPeek(out var pending))
                    await Task.WhenAny(nextLaunch, pending.Flight);
                else if (canLaunch) await nextLaunch;
                else if (flights.TryPeek(out var last)) await last.Flight;
            }
        }
        finally
        {
            await Task.WhenAll(visualTasks);
        }

        if (yamotoKoki == null || yamotoKoki.IsDead || monster == null || scheduledMove == null
            || CombatManager.Instance.IsOverOrEnding) return;
        Flash();
        if (yamotoKoki.GetCreatureNode() is { } node)
            _ = TaskHelper.RunSafely(node.PerformIntent());
        await scheduledMove.PerformMove(yamotoKoki.CombatState!.HittableEnemies);

        monster.MoveStateMachine?.OnMovePerformed(scheduledMove);
        if (CombatManager.Instance.IsOverOrEnding)
        {
            CompanionIntentLifecycle.Invalidate(yamotoKoki);
            return;
        }

        await AssignRandomIntent(yamotoKoki);
    }

    private static async Task AssignRandomIntent(Creature yamotoKoki)
    {
        if (yamotoKoki.Monster is not YamotoKokiMonster monster)
        {
            return;
        }

        if (monster.MoveStateMachine == null)
        {
            monster.SetUpForCombat();
        }

        CompanionIntentGeneration generation = CompanionIntentLifecycle.Capture(yamotoKoki);
        if (!CanChooseNextIntent(generation))
        {
            return;
        }

        ICombatState combatState = yamotoKoki.CombatState!;
        IReadOnlyList<Creature> enemies = combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive && enemy.IsHittable)
            .ToList();
        int nextTurn = (yamotoKoki.PetOwner?.PlayerCombatState?.TurnNumber ?? 0) + 1;
        IReadOnlyList<Creature> nextTurnMissiles = yamotoKoki.PetOwner?.PlayerCombatState?.Pets
            .Where(pet => pet.Monster is YamotoKokiOrigamiMissile missile
                && missile.CanExplodeOnTurn(nextTurn))
            .ToList() ?? [];
        bool forceIai = FinisherForecast.EvaluateYamotoKokiNextTurn(
                yamotoKoki,
                enemies,
                nextTurnMissiles)
            == FinisherForecastOutcome.Guaranteed;
        MoveState next = forceIai
            ? (MoveState)monster.MoveStateMachine!.States[YamotoKokiMonster.IaiSlashMoveId]
            : YamotoKokiMonster.PickRandomMove(
                monster.MoveStateMachine!,
                yamotoKoki.PetOwner!.RunState.Rng.MonsterAi);
        await AssignIntent(yamotoKoki, next, generation);
    }

    private static async Task AssignIntent(Creature yamotoKoki, string moveId)
    {
        if (yamotoKoki.Monster is not YamotoKokiMonster monster)
        {
            return;
        }

        if (monster.MoveStateMachine == null)
        {
            monster.SetUpForCombat();
        }

        MoveState next = (MoveState)monster.MoveStateMachine!.States[moveId];
        await AssignIntent(yamotoKoki, next, CompanionIntentLifecycle.Capture(yamotoKoki));
    }

    private static async Task AssignIntent(
        Creature yamotoKoki,
        MoveState next,
        CompanionIntentGeneration generation)
    {
        if (yamotoKoki.Monster is not YamotoKokiMonster monster)
        {
            return;
        }

        if (!CanSetMove(generation))
        {
            return;
        }

        monster.SetMoveImmediate(next, forceTransition: true);

        NCreature? node = yamotoKoki.GetCreatureNode();
        ICombatState? combatState = yamotoKoki.CombatState;
        if (node != null
            && combatState != null
            && combatState.IsLiveCombat()
            && combatState.HittableEnemies.Any(enemy => enemy.IsAlive && enemy.IsHittable)
            && CompanionIntentLifecycle.PrepareContainerForWrite(generation))
        {
            await node.UpdateIntent(combatState.HittableEnemies);
            if (!CompanionIntentLifecycle.IsCurrent(generation))
            {
                CompanionIntentLifecycle.RehideIfInactive(generation);
            }
        }
    }

    private static bool CanSetMove(CompanionIntentGeneration generation)
    {
        Creature yamotoKoki = generation.Creature;
        return CompanionIntentLifecycle.IsCurrent(generation)
            && !yamotoKoki.IsDead
            && yamotoKoki.CombatState != null
            && !CombatManager.Instance.IsOverOrEnding;
    }

    private static bool CanChooseNextIntent(CompanionIntentGeneration generation) =>
        CanSetMove(generation)
        && generation.Creature.CombatState is { } combatState
        && combatState.IsLiveCombat()
        && combatState.HittableEnemies.Any(enemy => enemy.IsAlive && enemy.IsHittable);

}
