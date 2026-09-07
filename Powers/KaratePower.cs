using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Combat;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Powers;

public sealed class KaratePower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        RefreshOpposingHealthBars(Owner);
        return Task.CompletedTask;
    }

    public override Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (ReferenceEquals(power, this))
        {
            RefreshOpposingHealthBars(Owner);
        }

        return Task.CompletedTask;
    }

    public override async Task AfterRemoved(Creature oldOwner)
    {
        RefreshOpposingHealthBars(oldOwner);
        // Direct removal does not dispatch AfterPowerAmountChanged. Zero-stack removal already did.
        if (Amount != 0 && !CombatManager.Instance.IsOverOrEnding && oldOwner.CombatState is { } combat)
            foreach (var chain in combat.Players.SelectMany(player => player.Creature.Powers.OfType<ChopChainPower>()).ToArray())
                await chain.CountChange(new ThrowingPlayerChoiceContext());
    }

    private static void RefreshOpposingHealthBars(Creature owner)
    {
        foreach (Creature creature in owner.CombatState?.Creatures.Where(creature => creature.Side != owner.Side) ?? [])
        {
            CombatHealthBar.Refresh(creature);
        }
    }
}
