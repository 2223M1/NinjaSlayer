using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class TonyRetention : RedesignV1UncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];
    public TonyRetention() : base(nameof(TonyRetention), nameof(TonyRetention), 1, CardType.Skill, TargetType.Self) { }
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(4, ValueProp.Move), new DynamicVar("Scry", 4)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars["Scry"].IntValue);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2);
        DynamicVars["Scry"].UpgradeValueBy(2);
    }
}
