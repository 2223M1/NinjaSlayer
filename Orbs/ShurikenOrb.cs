using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
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

    public int StackCount { get; private set; }
    public bool OwnsTransientSlot { get; private set; }
    private bool _fireAllStockOnNextEvoke;
    private bool _consumeOneStockOnNextEvoke;
    private bool _completeEvokeChainOnNextEvoke;
    private bool _generatedStrongShurikenInEvokeChain;

    public override decimal PassiveVal => StackCount;
    public override decimal EvokeVal => IsMutable
        ? ModifyOrbValue(ShurikenCombat.GetStockBaseDamage(Owner.Creature))
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
                    StackCount = orb.StackCount,
                    OwnsTransientSlot = orb.OwnsTransientSlot
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
                    orb.OwnsTransientSlot = state.OwnsTransientSlot;
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
            existing.RefreshVisuals();
            existing.ActivatePassiveFeedback();
            return;
        }

        bool ownsTransientSlot = RedesignV1Rules.ShouldOwnTransientShurikenSlot(
            player.Character.BaseOrbSlotCount,
            combatState.OrbQueue.Capacity);
        ShurikenOrb orb = (ShurikenOrb)ModelDb.Orb<ShurikenOrb>().ToMutable();
        orb.StackCount = amount;
        orb.OwnsTransientSlot = ownsTransientSlot;
        await OrbCmd.Channel(choiceContext, orb, player);
        if (player.PlayerCombatState.OrbQueue.Orbs.Contains(orb))
        {
            orb.RefreshVisuals();
            orb.ActivatePassiveFeedback();
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
        if (isOwnerDiscard
            && Owner.Creature.GetPower<RecycledBladesPower>() is { } recycled)
        {
            await recycled.AddStockAfterDiscard(choiceContext);
        }
    }

    public override Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler) =>
        FireStock(
            choiceContext,
            RedesignV1Rules.ResolveBladeCycleShuffle(
                StackCount,
                Owner.Creature.HasPower<BladeCyclePower>(),
                shuffler == Owner,
                CombatState.HittableEnemies.Count),
            null);

    public override async Task<IEnumerable<Creature>> Evoke(PlayerChoiceContext playerChoiceContext)
    {
        bool fireAllStock = _fireAllStockOnNextEvoke;
        int shots = fireAllStock ? StackCount : 1;
        bool consumeOneStock = _consumeOneStockOnNextEvoke;
        bool completeEvokeChain = fireAllStock || _completeEvokeChainOnNextEvoke;
        _fireAllStockOnNextEvoke = false;
        _consumeOneStockOnNextEvoke = false;
        _completeEvokeChainOnNextEvoke = false;
        bool generatedStrongShuriken = _generatedStrongShurikenInEvokeChain;
        bool fired = false;
        HashSet<Creature> targets = [];
        for (int index = 0; index < shots; index++)
        {
            IReadOnlyList<Creature> shotTargets =
                await FireOne(playerChoiceContext, null, evoke: true);
            if (shotTargets.Count == 0)
            {
                break;
            }

            fired = true;
            targets.UnionWith(shotTargets);
            if (!generatedStrongShuriken)
            {
                generatedStrongShuriken = await TryGenerateStrongShuriken(playerChoiceContext);
            }
        }

        if (consumeOneStock && fired)
        {
            StackCount--;
            if (StackCount == 0)
            {
                RemoveDepletedOrb();
            }
            else
            {
                RefreshVisuals();
            }
        }

        _generatedStrongShurikenInEvokeChain = completeEvokeChain
            ? false
            : generatedStrongShuriken;
        ReleaseTransientSlotIfRemoved();
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
        RefreshVisuals();
        bool generatedStrongShuriken = false;
        for (int index = 0; index < stock * triggersPerStock; index++)
        {
            if ((await FireOne(choiceContext, source, evoke: false)).Count == 0)
            {
                break;
            }

            if (!generatedStrongShuriken)
            {
                generatedStrongShuriken = await TryGenerateStrongShuriken(choiceContext);
            }
        }

        RemoveDepletedOrb();
    }

    internal void TransferTransientSlot() => OwnsTransientSlot = false;

    internal bool IsPreparedForReplacementEvoke => _fireAllStockOnNextEvoke;

    internal void PrepareForReplacementEvoke()
    {
        _fireAllStockOnNextEvoke = true;
        _completeEvokeChainOnNextEvoke = true;
    }

    internal void PrepareForContinuingEvoke() => _completeEvokeChainOnNextEvoke = false;

    internal void PrepareForSingleStockEvoke()
    {
        if (StackCount <= 0)
        {
            throw new InvalidOperationException("A Shuriken orb cannot evoke without stock.");
        }

        _consumeOneStockOnNextEvoke = true;
        _completeEvokeChainOnNextEvoke = true;
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

        int stockBeforeResolution = StackCount;
        bool generatedStrongShuriken = false;
        bool fired = false;
        for (int index = 0; index < resolution.Shots; index++)
        {
            if ((await FireOne(choiceContext, source, evoke: false)).Count == 0)
            {
                break;
            }

            fired = true;
            if (!generatedStrongShuriken)
            {
                generatedStrongShuriken = await TryGenerateStrongShuriken(choiceContext);
            }
        }

        if (!fired || resolution.RemainingStock >= stockBeforeResolution)
        {
            return;
        }

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
        bool evoke)
    {
        IReadOnlyList<Creature> candidates = CombatState.HittableEnemies;
        if (candidates.Count == 0)
        {
            return [];
        }

        IReadOnlyList<Creature> targets;
        if (Owner.Creature.GetPower<BladeSweepPower>() is { } sweep)
        {
            targets = candidates.ToArray();
            await PowerCmd.Remove(sweep);
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

        Action activate = evoke
            ? () => ActivateEvokeFeedback(targets)
            : ActivatePassiveFeedback;
        await ShurikenCombat.TriggerStockWave(
            choiceContext,
            Owner.Creature,
            targets,
            source,
            this,
            activate);
        return targets;
    }

    private async Task<bool> TryGenerateStrongShuriken(PlayerChoiceContext choiceContext)
    {
        if (Owner.Creature.GetPower<StarlessNightRedesignPower>() is not { } power)
        {
            return false;
        }

        await power.GenerateStrongShuriken(choiceContext);
        return true;
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
        ReleaseTransientSlotIfRemoved();
    }

    private void ReleaseTransientSlotIfRemoved()
    {
        var combatState = Owner.PlayerCombatState
            ?? throw new InvalidOperationException("A mutable Shuriken orb must have combat state.");
        if (!OwnsTransientSlot || combatState.OrbQueue.Orbs.Contains(this))
        {
            return;
        }

        OwnsTransientSlot = false;
        OrbCmd.RemoveSlots(Owner, 1);
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
    public bool OwnsTransientSlot { get; set; }
}
