using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;

namespace NinjaSlayer.Cards.Standard;

public sealed class FlameGuard : NinjaSlayerCommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(12, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<BlackFlame>()];

    public FlameGuard()
        : base(nameof(FlameGuard), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await NinjaSlayerCardCmd.AddGeneratedCard<BlackFlame>(Owner, PileType.Draw);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}
