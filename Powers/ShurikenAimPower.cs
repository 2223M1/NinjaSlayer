using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ShurikenAimPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile => new(
        IconPath: ModelDb.Power<AccuracyPower>().IconPath,
        BigIconPath: ModelDb.Power<AccuracyPower>().ResolvedBigIconPath);
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromOrb<ShurikenOrb>()];

    internal static decimal Multiplier(Creature target) => target.GetPowerAmount<ShurikenAimPower>() > 0 ? 1.5m : 1m;

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource
#if !NINJASLAYER_LEGACY_DAMAGE_API
        , CardPlay? cardPlay
#endif
    ) => target == Owner && props.IsPoweredAttack()
        && cardSource?.Tags.Contains(NinjaSlayerCardTags.Shuriken) == true ? Multiplier(Owner) : 1m;

    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants) => side == CombatSide.Enemy
            ? PowerCmd.TickDownDuration(this) : Task.CompletedTask;
}
