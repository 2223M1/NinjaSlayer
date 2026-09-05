using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class NinjaGreetingRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Innate, CardKeyword.Ethereal, CardKeyword.Exhaust];

    public NinjaGreetingRedesignV1()
        : base(nameof(NinjaGreetingRedesignV1), "StunStrike", 3, CardType.Skill, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.Stun(cardPlay.Target!);
        foreach (CardModel card in PileType.Hand.GetPile(Owner).Cards)
        {
            card.GiveSingleTurnRetain();
        }
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
