using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class GuardStance : RedesignV1UncommonCard
{
    public GuardStance() : base(nameof(GuardStance), "Contraption", 1, CardType.Power, TargetType.Self) { }
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(2, ValueProp.Unpowered)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<KaratePower>()];

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<GuardStancePower>(choiceContext, Owner.Creature, DynamicVars.Block.BaseValue, Owner.Creature, this);

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(1);
}
