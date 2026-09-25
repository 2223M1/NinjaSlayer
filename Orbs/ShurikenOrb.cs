using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Godot;

namespace NinjaSlayer.Orbs;

[RegisterOrb]
public sealed class ShurikenOrb : ModOrbTemplate
{
    internal const string SavedDataSlot = "shuriken_orb_state";
    internal const string VisualsScenePath =
        "res://NinjaSlayer/scenes/orbs/shuriken_orb.tscn";

    private sealed class GainTurn
    {
        public int Round;
        public CombatSide Side;
    }
    private static readonly ConditionalWeakTable<PlayerCombatState, GainTurn> LastGain = new();
    internal static bool HasGainedThisTurn(Player player) =>
        player.PlayerCombatState is { } state && player.Creature.CombatState is { } combat
        && LastGain.TryGetValue(state, out GainTurn? turn)
        && turn.Round == combat.RoundNumber && turn.Side == combat.CurrentSide;

    public int StackCount { get; private set; }
    internal bool UsesDedicatedSlot => Owner.Character is INinjaSlayerCharacter;

    public override decimal PassiveVal => StackCount;
    public override decimal EvokeVal => IsMutable
        ? ModifyOrbValue(RedesignV1Rules.ShurikenBaseDamage)
        : RedesignV1Rules.ShurikenBaseDamage;
    public override ModOrbValueDisplayMode ValueDisplayMode => ModOrbValueDisplayMode.Both;
    public override Color DarkenedColor => new("805900");
    public override OrbAssetProfile AssetProfile => new(
        ShurikenCombat.ProjectileTexturePath,
        VisualsScenePath);

    protected override Node2D? TryCreateOrbSprite() =>
        RitsuGodotNodeFactories.CreateFromScenePath<Node2D>(VisualsScenePath);

    internal static void RegisterSavedData(string modId)
    {
        RitsuLibFramework.GetModelSavedDataStore(modId)
            .RegisterComputed<ShurikenOrb, ShurikenOrbState>(
                SavedDataSlot,
                orb => new ShurikenOrbState
                {
                    StackCount = orb.StackCount
                },
                (orb, state) =>
                {
                    if (state is null)
                    {
                        return;
                    }
                    if (state.StackCount < 0)
                    {
                        throw new InvalidDataException("Shuriken orb stock cannot be negative.");
                    }

                    orb.StackCount = state.StackCount;
                },
                () => new ShurikenOrbState());
    }

    internal static ShurikenOrb? Find(Player player) =>
        player.PlayerCombatState?.OrbQueue.Orbs.OfType<ShurikenOrb>().FirstOrDefault();

    internal static async Task AddStock(
        PlayerChoiceContext choiceContext,
        Player player,
        int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var combatState = player.PlayerCombatState
            ?? throw new InvalidOperationException("Shuriken stock can only be gained during combat.");
        ShurikenOrb? existing = Find(player);
        if (existing is not null)
        {
            existing.StackCount += amount;
            Code.Telemetry.NinjaSlayerCombatTelemetry.Mechanic("shuriken_stock", player.Creature, amount);
            existing.RefreshVisuals();
            existing.ActivatePassiveFeedback();
            await existing.AfterStockGained(choiceContext);
            return;
        }

        ShurikenOrb orb = (ShurikenOrb)ModelDb.Orb<ShurikenOrb>().ToMutable();
        orb.StackCount = amount;
        await OrbCmd.Channel(choiceContext, orb, player);
        if (player.PlayerCombatState.OrbQueue.Orbs.Contains(orb))
        {
            Code.Telemetry.NinjaSlayerCombatTelemetry.Mechanic("shuriken_stock", player.Creature, amount);
            orb.RefreshVisuals();
            orb.ActivatePassiveFeedback();
            await orb.AfterStockGained(choiceContext);
        }
    }

    private async Task AfterStockGained(PlayerChoiceContext choiceContext)
    {
        GainTurn turn = LastGain.GetOrCreateValue(Owner.PlayerCombatState!);
        turn.Round = CombatState.RoundNumber;
        turn.Side = CombatState.CurrentSide;
        int damage = (int)EvokeVal;
        foreach (PowerModel power in Owner.Creature.Powers.ToArray())
        {
            if (power is StarlessNightRedesignPower starless)
                await starless.GenerateStrongShuriken(damage);
            else if (power is ShurikenDrawPower draw)
                await draw.AfterStockGained(choiceContext);
        }
    }

    public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        bool isOwnerDiscard = card.Owner == Owner;
        await FireStock(
            choiceContext,
            RedesignV1Rules.ResolveShurikenDiscard(
                StackCount,
                isOwnerDiscard,
                CombatState.HittableEnemies.Count),
            card);
    }

    public override Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler) =>
        FireStock(
            choiceContext,
            RedesignV1Rules.ResolveBladeCycleShuffle(
                StackCount,
                Owner.Creature.HasPower<BladeCyclePower>(),
                shuffler == Owner,
                CombatState.HittableEnemies.Count,
                Owner.Creature.GetPowerAmount<BladeCyclePower>()),
            null);

    public override async Task<IEnumerable<Creature>> Evoke(PlayerChoiceContext playerChoiceContext)
    {
        int shots = StackCount;
        HashSet<Creature> targets = [];
        for (int index = 0; index < shots; index++)
        {
            IReadOnlyList<Creature> shotTargets =
                await FireOne(playerChoiceContext, null, notifyEvokeHooks: false);
            if (shotTargets.Count == 0)
            {
                break;
            }

            targets.UnionWith(shotTargets);
        }

        // OrbCmd owns removal: repeated evokes retain the same stock until its
        // final dequeue, including the native Shatter loop.
        if (!Owner.PlayerCombatState!.OrbQueue.Orbs.Contains(this))
        {
            Code.Telemetry.NinjaSlayerCombatTelemetry.Mechanic("shuriken_stock", Owner.Creature, -StackCount);
            StackCount = 0;
        }
        return targets;
    }

    internal async Task FireConsumedVolley(
        PlayerChoiceContext choiceContext,
        int triggersPerStock,
        CardModel? source)
    {
        int stock = StackCount;
        if (stock <= 0 || triggersPerStock <= 0)
        {
            return;
        }

        StackCount = 0;
        Code.Telemetry.NinjaSlayerCombatTelemetry.Mechanic("shuriken_stock", Owner.Creature, -stock);
        RefreshVisuals();
        for (int index = 0; index < stock * triggersPerStock; index++)
        {
            if ((await FireOne(choiceContext, source)).Count == 0)
            {
                break;
            }

        }

        RemoveDepletedOrb();
    }

    internal void RefreshVisuals()
    {
        if (!IsMutable)
        {
            return;
        }

        NCombatRoom.Instance?.GetCreatureNode(Owner.Creature)?.OrbManager?.UpdateVisuals(
            OrbEvokeType.None);
    }

    private async Task FireStock(
        PlayerChoiceContext choiceContext,
        ShurikenStockResolution resolution,
        CardModel? source)
    {
        if (resolution.Shots <= 0)
        {
            return;
        }

        bool fired = false;
        for (int index = 0; index < resolution.Shots; index++)
        {
            if ((await FireOne(choiceContext, source)).Count == 0)
            {
                break;
            }

            fired = true;
        }

        if (!fired)
        {
            return;
        }

        Code.Telemetry.NinjaSlayerCombatTelemetry.Mechanic("shuriken_stock", Owner.Creature, resolution.RemainingStock - StackCount);
        StackCount = resolution.RemainingStock;
        if (StackCount == 0)
        {
            RemoveDepletedOrb();
        }
        else
        {
            RefreshVisuals();
        }
    }

    private async Task<IReadOnlyList<Creature>> FireOne(
        PlayerChoiceContext choiceContext,
        CardModel? source,
        bool notifyEvokeHooks = true)
    {
        if (CombatManager.Instance.IsOverOrEnding)
        {
            return [];
        }
        IReadOnlyList<Creature> candidates = CombatState.HittableEnemies;
        if (candidates.Count == 0)
        {
            return [];
        }

        IReadOnlyList<Creature> targets;
        if (Owner.Creature.HasPower<BladeSweepPower>())
        {
            targets = candidates.ToArray();
        }
        else
        {
            Creature? target = Owner.RunState.Rng.CombatTargets.NextItem(candidates);
            if (target is null)
            {
                return [];
            }
            targets = [target];
        }

        await ShurikenCombat.TriggerStockWave(
            choiceContext,
            Owner.Creature,
            targets,
            source,
            this,
            () => ActivateEvokeFeedback(targets));
        // OrbCmd dispatches this for external evokes; automatic stock shots own that dispatch.
        Code.Telemetry.NinjaSlayerCombatTelemetry.Mechanic("shuriken_evoked", Owner.Creature, 1);
        if (notifyEvokeHooks && Owner.Creature.CombatState is { } combatState)
        {
            await Hook.AfterOrbEvoked(choiceContext, combatState, this, targets);
        }
        return targets;
    }

    private void RemoveDepletedOrb()
    {
        var combatState = Owner.PlayerCombatState
            ?? throw new InvalidOperationException("A mutable Shuriken orb must have combat state.");
        if (!combatState.OrbQueue.Remove(this))
        {
            return;
        }

        NCombatRoom.Instance?.GetCreatureNode(Owner.Creature)?.OrbManager?.EvokeOrbAnim(this);
        RemoveInternal();
    }

    private void ActivatePassiveFeedback()
    {
#if NINJASLAYER_CHANNEL_STABLE
        Trigger();
#else
        ActivatePassive();
#endif
    }

    private void ActivateEvokeFeedback(IReadOnlyList<Creature> targets)
    {
#if NINJASLAYER_CHANNEL_STABLE
        Trigger();
#else
        ActivateEvoke(targets.ToArray());
#endif
    }
}

internal sealed class ShurikenOrbState
{
    public int StackCount { get; set; }
}
