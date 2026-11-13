using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using STS2RitsuLib;

namespace NinjaSlayer.Tests;

// Godot loads test dependencies from memory, so Assembly.Location is empty.
// Map only RitsuLib's resource root to the explicitly supplied complete installation.
internal static class RitsuTestAssets
{
    private static string _directory = null!;

    internal static void Configure()
    {
        _directory = Environment.GetEnvironmentVariable("NINJASLAYER_RITSULIB_MOD_DIR")
            ?? throw new InvalidOperationException("Supply NINJASLAYER_RITSULIB_MOD_DIR for the complete RitsuLib test installation.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "mod_manifest.json")));
        string expected = typeof(RitsuLibFramework).Assembly.GetName().Version!.ToString(3);
        if (manifest.RootElement.GetProperty("version").GetString() != expected
            || !File.Exists(Path.Combine(_directory, "assets.zip")))
            throw new InvalidOperationException("The RitsuLib test assets must match the referenced framework version.");
        Type assets = typeof(STS2RitsuLib.Utils.ResourcePack).Assembly.GetType("STS2RitsuLib.RitsuAssetStore", true)!;
        new Harmony("NinjaSlayer.Tests.RitsuAssets").Patch(
            AccessTools.PropertyGetter(assets, "RootDirectory"),
            prefix: new HarmonyMethod(typeof(RitsuTestAssets), nameof(ResolveRoot)));
    }

    private static bool ResolveRoot(ref string __result)
    {
        __result = _directory;
        return false;
    }
}
