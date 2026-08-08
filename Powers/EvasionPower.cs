using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class EvasionPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    internal bool CanEvade(Creature target, ValueProp props, Creature dealer)
    {
        return target == Owner
            && Amount > 0
            && !AttackIntentPreviewContext.IsActive
            && dealer.Side != Owner.Side
            && props.IsCardOrMonsterMove();
    }

    internal async Task ResolveDodge()
    {
        if (Owner.Monster is DarkNinjaMonster darkNinja)
        {
            darkNinja.PlayEvasionInsultOnce();
        }

        CombatDodgeAnimation.NotifyImpact(Owner);
        Flash();
        await PowerCmd.Decrement(this);
    }

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (participants.Contains(Owner))
        {
            await PowerCmd.Remove(this);
        }
    }
}
