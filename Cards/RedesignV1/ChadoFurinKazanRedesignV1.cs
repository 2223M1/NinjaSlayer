using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ChadoFurinKazanRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];

    public ChadoFurinKazanRedesignV1()
        : base(nameof(ChadoFurinKazanRedesignV1), "SenchaStorm", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var selected = (await CardSelectCmd.FromHand(
            choiceContext,
            Owner,
            new CardSelectorPrefs(SelectionScreenPrompt, DynamicVars.Cards.IntValue),
            card => card != this,
            this)).ToList();
        foreach (CardModel card in selected.AsEnumerable().Reverse())
            await CardPileCmd.AddGeneratedCardToCombat(card.CreateClone(), PileType.Draw, Owner, CardPilePosition.Top);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}
