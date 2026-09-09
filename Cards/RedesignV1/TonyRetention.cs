using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class TonyRetention : RedesignV1UncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry), HoverTipFactory.FromKeyword(CardKeyword.Retain)];

    public TonyRetention() : base(nameof(TonyRetention), "ReadyBlade", 2, CardType.Skill, TargetType.Self) { }
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(5, ValueProp.Move), new DynamicVar("Scry", 3)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars["Scry"].IntValue);
        foreach (CardModel card in PileType.Hand.GetPile(Owner).Cards) card.GiveSingleTurnRetain();
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
        DynamicVars["Scry"].UpgradeValueBy(1);
    }
}
