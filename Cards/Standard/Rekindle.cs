using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Cards.Standard;

public sealed class Rekindle : NinjaSlayerUncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<BlackFlame>();

    public Rekindle() : base(nameof(Rekindle), 1, CardType.Skill, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new NarakuLifeVar(3)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var selected = await CardSelectCmd.FromHand(choiceContext, Owner,
            new CardSelectorPrefs(SelectionScreenPrompt, 1), card => card != this && card.IsTransformable, this);
        foreach (CardModel card in selected)
            await CardCmd.TransformTo<BlackFlame>(card);
        await PowerCmd.Apply<RekindlePower>(choiceContext, Owner.Creature,
            DynamicVars["NarakuLife"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars["NarakuLife"].UpgradeValueBy(1);
}
