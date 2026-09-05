using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class KarateReversalPower : RedesignV1CounterPower
{
    private Creature? _counterTarget;
    private int _counterDamage;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));

    public override decimal ModifyHpLostAfterOstyLate(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        int karate = Owner.GetPowerAmount<KaratePower>();
        if (target != Owner
            || dealer == null
            || dealer.Side == Owner.Side
            || !props.IsPoweredAttack()
            || amount <= 0
            || karate <= 0)
        {
            return amount;
        }

        _counterTarget = dealer;
        _counterDamage = Math.Min((int)amount, karate);
        return amount - _counterDamage;
    }

    public override async Task AfterModifyingHpLostAfterOsty()
    {
        Creature? target = _counterTarget;
        int damage = _counterDamage;
        _counterTarget = null;
        _counterDamage = 0;
        if (target == null || damage <= 0)
        {
            return;
        }

        Flash();
        if (target.IsAlive)
        {
            await CreatureCmd.Damage(
                new ThrowingPlayerChoiceContext(),
                [target],
                damage,
                ValueProp.Unpowered | ValueProp.Move,
                Owner);
        }

        if (Owner.GetPower<KaratePower>() is { Amount: > 0 } karate)
        {
            await PowerCmd.Decrement(karate);
        }
    }

    public override Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Decrement(this)
            : Task.CompletedTask;
}
