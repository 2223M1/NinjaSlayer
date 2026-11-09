using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Powers;

public sealed class KaratePower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int DisplayAmount => GetAttackAmount(Owner);
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("AttackAmount", 0)];

    // Keep actual Karate in Amount. Only attack triggers and their previews include Naraku Life.
    public static int GetAttackAmount(Creature owner) => owner.GetPowerAmount<KaratePower>()
        + owner.GetPowerAmount<OneMindOneBodyPower>() * owner.GetPowerAmount<NarakuLifePower>();

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        RefreshAttackDisplay(Owner);
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
            RefreshAttackDisplay(Owner);
        }

        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        RefreshAttackDisplay(oldOwner);
        return Task.CompletedTask;
    }

    internal static void RefreshAttackDisplay(Creature owner)
    {
        if (owner.GetPower<KaratePower>() is { } karate)
        {
            karate.DynamicVars["AttackAmount"].BaseValue = GetAttackAmount(owner);
            karate.InvokeDisplayAmountChanged();
        }
        foreach (Creature creature in owner.CombatState?.Creatures.Where(creature => creature.Side != owner.Side) ?? [])
        {
            CombatHealthBar.Refresh(creature);
        }
    }
}
