using HarmonyLib;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Ancients;
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

    [HarmonyAfter("com.ritsukage.sts2-RitsuLib.framework-content-registry")]
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
