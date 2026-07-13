using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;

namespace NinjaSlayer.Content;

public static class NinjaSlayerDebugCardCatalog
{
    private static readonly Type[] BaselineCards =
    [
        // Basics
        typeof(DefendNinjaSlayer),
        typeof(KarateStraight),
        typeof(Meditation),
        typeof(StrikeNinjaSlayer),

        // Commons
        typeof(Chop),
        typeof(DiscardDefense),
        typeof(BurningStrike),
        typeof(IHit),
        typeof(KataDrill),
        typeof(LuckyStrike),
        typeof(NinjaApathy),
        typeof(NinjaWhip),
        typeof(NinjaWall),
        typeof(PalmThrust),
        typeof(PerfectChop),
        typeof(RestGuard),
        typeof(ShurikenSpread),
        typeof(ShurikenStock),
        typeof(ShurikenThrow),
        typeof(SipTea),
        typeof(SteepTea),
        typeof(TeaHitsPeople),
        typeof(ThrowKunai),
        typeof(WhiskSlash),

        // Ancients
        typeof(CollapseFist),
        typeof(OneBodyOneSoul),
        typeof(ZazenDrink),
    ];

    // Add a baseline card type here to omit it from debug mode.
    private static readonly Type[] RemovedCards = [];

    // Add (typeof(CurrentCard), typeof(TestReplacement)) here to replace a baseline card.
    private static readonly (Type Original, Type Replacement)[] Replacements = [];

    // Add any registered test card type here without changing its normal-mode pool registration.
    private static readonly Type[] AdditionalCards = [
        typeof(OpeningGuard)
    ];

    public static CardModel[] CreateCards()
    {
        HashSet<Type> removed = RemovedCards.ToHashSet();
        Dictionary<Type, Type> replacements = Replacements.ToDictionary(pair => pair.Original, pair => pair.Replacement);

        IEnumerable<Type> selectedTypes = BaselineCards
            .Where(type => !removed.Contains(type))
            .Select(type => replacements.GetValueOrDefault(type, type))
            .Concat(AdditionalCards)
            .Distinct();

        return selectedTypes.Select(ResolveCard).ToArray();
    }

    private static CardModel ResolveCard(Type cardType)
    {
        if (!cardType.IsSubclassOf(typeof(CardModel)))
        {
            throw new InvalidOperationException($"Debug card pool entry {cardType.FullName} is not a CardModel.");
        }

        return ModelDb.GetById<CardModel>(ModelDb.GetId(cardType));
    }
}
