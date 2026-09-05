using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Commands;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class AbandonThoughtRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Breath", 2)];

    public AbandonThoughtRedesignV1()
        : base(nameof(AbandonThoughtRedesignV1), "BrewTea", 0, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        IReadOnlyList<CardModel> cards = PileType.Draw.GetPile(Owner).Cards;
        if (cards.Count > 0)
        {
            await CardCmd.Exhaust(choiceContext, cards[0]);
        }

        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue);
    }

    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
}
