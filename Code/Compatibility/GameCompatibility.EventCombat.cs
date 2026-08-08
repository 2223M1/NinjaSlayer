using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    public static EncounterModel ResolveEventCombatEncounter(
        EncounterModel canonicalEncounter)
    {
#if NINJASLAYER_CHANNEL_STABLE
        return canonicalEncounter.ToMutable();
#else
        return canonicalEncounter;
#endif
    }
}
