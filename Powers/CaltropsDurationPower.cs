using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class CaltropsDurationPower : RedesignV1CounterPower
{
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override PowerAssetProfile AssetProfile => new(
        IconPath: ModelDb.Power<ThornsPower>().IconPath,
        BigIconPath: ModelDb.Power<ThornsPower>().ResolvedBigIconPath);
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<ThornsPower>()];
    public int ThornsAmount { get; set; }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext,
        CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == Owner.Side) return;
        if (Amount > 1)
        {
            await PowerCmd.Decrement(this);
            return;
        }

        Flash();
        await PowerCmd.Apply<ThornsPower>(choiceContext, Owner, -ThornsAmount, Owner, null);
        await PowerCmd.Remove(this);
    }
}
