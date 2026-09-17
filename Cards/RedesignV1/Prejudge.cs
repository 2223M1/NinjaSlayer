using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class Prejudge : NinjaSlayerRedesignCardTemplate
{
    public Prejudge() : base(new NinjaSlayerCardSpec(nameof(Prejudge), 1, CardType.Skill,
        CardRarity.Basic, TargetType.Self, true), nameof(Prejudge)) { }
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Scry", 2), new BlockVar(4, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ScryResult result = await ScryCmd.Execute(choiceContext, Owner, DynamicVars["Scry"].IntValue);
        for (int i = 0; i < result.Discarded; i++)
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }
    protected override void OnUpgrade() => DynamicVars["Scry"].UpgradeValueBy(1);
}
