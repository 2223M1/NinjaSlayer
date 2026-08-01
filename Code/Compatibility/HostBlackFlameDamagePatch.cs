using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

#pragma warning disable CA1707 // Harmony reserves double-underscore parameter names.

namespace NinjaSlayer.Code.Patches;

public sealed partial class BlackFlameDamagePatch
{
#if NINJASLAYER_LEGACY_DAMAGE_API
    public static void Postfix(
        CardModel? cardSource,
        ref Task<IEnumerable<DamageResult>> __result) =>
        TrackResults(cardSource, suppliedCardPlay: null, ref __result);
#else
    public static void Postfix(
        CardModel? cardSource,
        CardPlay? cardPlay,
        ref Task<IEnumerable<DamageResult>> __result) =>
        TrackResults(cardSource, cardPlay, ref __result);
#endif
}

#pragma warning restore CA1707
