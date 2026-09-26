using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class NinjaSlayerModelInitializationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_model_initialization";
    public static string Description => "Register optional integrations after mod loading and before assembly/model discovery.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(OneTimeInitialization), nameof(OneTimeInitialization.ExecuteEssential), Type.EmptyTypes)];

    [HarmonyPriority(Priority.First + 1)]
    public static void Prefix() => Entry.InitializeOptionalContent();
}
