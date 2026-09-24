using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ShurikenGenerationRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    protected override bool ShouldGlowGoldInternal => ShurikenOrb.HasGainedThisTurn(Owner);
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(8, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromOrb<ShurikenOrb>()];
    public ShurikenGenerationRedesignV1()
        : base(nameof(ShurikenGenerationRedesignV1), "ShurikenGuard", 1, CardType.Skill, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int gains = ShurikenOrb.HasGainedThisTurn(Owner) ? 2 : 1;
        for (int i = 0; i < gains; i++)
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}
