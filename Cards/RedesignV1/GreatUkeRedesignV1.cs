using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class GreatUkeRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<MegaCrit.Sts2.Core.Models.Powers.BufferPower>()];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public GreatUkeRedesignV1()
        : base(nameof(GreatUkeRedesignV1), "OmnidirectionalThrow", 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        CardModel[] statuses = new[] { PileType.Draw, PileType.Hand, PileType.Discard }
            .SelectMany(pile => pile.GetPile(Owner).Cards).Where(card => card.Type == CardType.Status).ToArray();
        foreach (CardModel card in statuses)
        {
            if (!card.Keywords.Contains(CardKeyword.Unplayable))
                await CardCmd.AutoPlay(choiceContext, card, null);
            if (card.Pile?.Type != PileType.Exhaust)
                await CardCmd.Exhaust(choiceContext, card);
        }
        await PowerCmd.Apply<BufferPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
