using MegaCrit.Sts2.Core.Commands;
using NinjaSlayer.Code.Combat;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class TornadoFistCadencePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_tornado_fist_cadence";
    public static string Description => "Skip generic damage and power pacing waits during Tornado Fist hits.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(Cmd),
            nameof(Cmd.CustomScaledWait),
            [typeof(float), typeof(float), typeof(bool), typeof(CancellationToken)])
    ];

    public static bool Prefix(float fastSeconds, float standardSeconds, ref Task __result)
    {
        if (!TornadoFistCadenceContext.IsActive
            || fastSeconds != 0.1f
            || standardSeconds is not (0.2f or 0.25f))
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }
}
