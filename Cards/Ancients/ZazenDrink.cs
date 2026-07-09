using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class ZazenDrink : ModCardTemplate, IDrawCastSkillCard
{
    private const int energyCost = 0;
    private const CardType type = CardType.Skill;
    private const CardRarity rarity = CardRarity.Ancient;
    private const TargetType targetType = TargetType.Self;
    private const bool shouldShowInCardLibrary = true;

    // ponytail: reuse tea art until Zazen Drink gets dedicated card art.
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("ChadoCard");

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CardsVar(5)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)
    ];

    public ZazenDrink() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await DrawUntilFullDiscardingStatuses(choiceContext);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }

    private async Task DrawUntilFullDiscardingStatuses(PlayerChoiceContext choiceContext)
    {
        HashSet<CardModel> discardedStatuses = new(ReferenceEqualityComparer.Instance);

        while (MissingCardsInHand() > 0)
        {
            await CardPileCmd.ShuffleIfNecessary(choiceContext, Owner);
            CardModel? nextCard = PileType.Draw.GetPile(Owner).Cards.FirstOrDefault();
            if (nextCard == null)
            {
                break;
            }

            if (nextCard.Type == CardType.Status && discardedStatuses.Contains(nextCard))
            {
                break;
            }

            CardModel? drawnCard = await CardPileCmd.Draw(choiceContext, Owner);
            if (drawnCard == null)
            {
                break;
            }

            if (drawnCard.Type == CardType.Status)
            {
                discardedStatuses.Add(drawnCard);
                await CardCmd.Discard(choiceContext, drawnCard);
            }
        }
    }

    private int MissingCardsInHand() => CardPile.MaxCardsInHand - PileType.Hand.GetPile(Owner).Cards.Count;
}
