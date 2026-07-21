using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Events;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class ArchitectDialogueSuppressionPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_architect_dialogue_suppression";
    public static string Description => "Replace NinjaSlayer's Architect dialogue with the execution cinematic.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(TheArchitect), "PlayCurrentLine")
    ];

    public static bool Prefix(TheArchitect __instance, ref Task __result)
    {
        if (!ShouldReplace(__instance))
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }

    internal static bool ShouldReplace(TheArchitect eventModel) =>
        eventModel.Owner?.Character is INinjaSlayerCharacter
        && LocalContext.IsMe(eventModel.Owner);
}

internal sealed class ArchitectExecutionStartPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_architect_execution_start";
    public static string Description => "Start NinjaSlayer's Architect execution after room initialization.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(TheArchitect), nameof(TheArchitect.OnRoomEnter))
    ];

    public static void Postfix(TheArchitect __instance)
    {
        if (ArchitectDialogueSuppressionPatch.ShouldReplace(__instance))
        {
            ArchitectExecutionCinematic.TryStart(__instance);
        }
    }
}
