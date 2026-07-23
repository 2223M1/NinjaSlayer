using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerTelemetryIdentityLaunchPatch : IPatchMethod
{
    public static string PatchId => "ninja_slayer_telemetry_identity_launch";
    public static string Description => "Refresh local telemetry identity after a run is launched";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(RunManager), nameof(RunManager.Launch), [])
    ];

    public static void Postfix(RunState __result) => NinjaSlayerBalanceTelemetry.RefreshIdentity(__result);
}

public sealed class NinjaSlayerTelemetryIdentityCleanupPatch : IPatchMethod
{
    public static string PatchId => "ninja_slayer_telemetry_identity_cleanup";
    public static string Description => "Clear local telemetry identity when a run is cleaned up";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(RunManager), nameof(RunManager.CleanUp), [typeof(bool)])
    ];

    public static Exception? Finalizer(Exception? __exception)
    {
        try
        {
            NinjaSlayerBalanceTelemetry.ClearIdentity();
        }
        catch (Exception cleanupException)
        {
            Entry.Logger.Warn($"Failed to clear NinjaSlayer telemetry identity: {cleanupException.Message}");
        }

        return __exception;
    }
}
