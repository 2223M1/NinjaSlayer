using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class OpeningGuardPower : NinjaSlayerPowerTemplate
{
    private readonly HashSet<Creature> _blockedAttackers = [];

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(typeof(OpeningPower));

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target == Owner
            && dealer is not null
            && dealer.Side != Owner.Side
            && props.IsPoweredAttack()
            && result.WasFullyBlocked
            && result.BlockedDamage > 0
            && _blockedAttackers.Add(dealer))
        {
            Flash();
        }

        return Task.CompletedTask;
    }

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (!participants.Contains(Owner))
        {
            return;
        }

        foreach (Creature attacker in _blockedAttackers.Where(c => c.IsAlive).ToList())
        {
            await PowerCmd.Apply<OpeningPower>(choiceContext, attacker, 1m, Owner, null);
        }

        await PowerCmd.Remove(this);
    }
}
