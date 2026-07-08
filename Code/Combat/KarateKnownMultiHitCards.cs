using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace NinjaSlayer.Code.Combat;

internal static class KarateKnownMultiHitCards
{
    private static readonly Dictionary<Type, int> HitCounts = new()
    {
        [typeof(TwinStrike)] = 2,
        [typeof(Thrash)] = 2,
        [typeof(RipAndTear)] = 2,
        [typeof(Uproar)] = 2,
        [typeof(Refract)] = 2,
        [typeof(AstralPulse)] = 2,
        [typeof(Maul)] = 2,
        [typeof(DaggerSpray)] = 2,
    };

    public static bool TryGetHitCount(CardModel card, out int hitCount)
    {
        return HitCounts.TryGetValue(card.GetType(), out hitCount);
    }
}
