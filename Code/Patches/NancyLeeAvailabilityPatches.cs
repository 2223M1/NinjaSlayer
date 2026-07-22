using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Ancients;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NancyLeeCandidatePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_nancy_lee_candidate_filter";

    public static string Description =>
        "Exclude Nancy Lee from Glory Ancient candidates when the run has no NinjaSlayer character.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Glory), nameof(Glory.GetUnlockedAncients), [typeof(UnlockState)])];

    public static void Postfix(ref IEnumerable<AncientEventModel> __result)
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null || NinjaSlayerContentAccess.HasNinjaSlayer(runState))
        {
            return;
        }

        __result = __result.Where(ancient => ancient is not NancyLee).ToArray();
    }
}

public sealed class NancyLeeLoadedRunPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_nancy_lee_loaded_run_filter";

    public static string Description =>
        "Replace Nancy Lee in loaded non-NinjaSlayer runs with another Glory Ancient.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(ActModel), nameof(ActModel.ValidateRoomsAfterLoad), [typeof(Rng)])];

    public static void Postfix(ActModel __instance, Rng rng)
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (__instance is not Glory
            || runState == null
            || NinjaSlayerContentAccess.HasNinjaSlayer(runState)
            || !GameCompatibility.Nancy.TryGetRooms(__instance, out RoomSet? rooms)
            || rooms is null
            || !rooms.HasAncient
            || rooms.Ancient is not NancyLee)
        {
            return;
        }

        AncientEventModel? replacement = rng.NextItem(
            __instance.GetUnlockedAncients(runState.UnlockState)
                .Where(ancient => ancient is not NancyLee));

        if (replacement != null)
        {
            rooms.Ancient = replacement;
        }
    }
}
