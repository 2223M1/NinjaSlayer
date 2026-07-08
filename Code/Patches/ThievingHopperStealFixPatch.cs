using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal static class NinjaSlayerStealDeckResolver
{
    internal static bool IsValidDeckCard(CardModel? card) =>
        card != null
        && !card.HasBeenRemovedFromState
        && card.Pile?.Type == PileType.Deck;

    internal static CardModel? ResolveDeckVersion(CardModel combatCopy)
    {
        CardModel? deckVersion = combatCopy.DeckVersion;
        if (IsValidDeckCard(deckVersion) && !ReferenceEquals(deckVersion, combatCopy))
        {
            return deckVersion;
        }

        Player? owner = combatCopy.Owner;
        if (owner == null)
        {
            return null;
        }

        IEnumerable<CardModel> candidates = owner.Deck.Cards.Where(c =>
            c.Id == combatCopy.Id && c.IsUpgraded == combatCopy.IsUpgraded);

        if (combatCopy.FloorAddedToDeck.HasValue)
        {
            candidates = candidates.Where(c => c.FloorAddedToDeck == combatCopy.FloorAddedToDeck);
        }

        return candidates.FirstOrDefault(IsValidDeckCard);
    }
}

public sealed class NinjaSlayerSwipePowerStealPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_swipe_power_steal_deck_version";

    public static string Description =>
        "Resolve a valid RunState deck card before Thieving Hopper removes NinjaSlayer cards from the deck.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(SwipePower), nameof(SwipePower.Steal), [typeof(CardModel)])];

    public static void Prefix(CardModel card)
    {
        if (card.Pool is not NinjaSlayerCardPool)
        {
            return;
        }

        card.DeckVersion = NinjaSlayerStealDeckResolver.ResolveDeckVersion(card);
    }
}
