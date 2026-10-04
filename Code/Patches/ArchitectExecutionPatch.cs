using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Events;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using HarmonyLib;

namespace NinjaSlayer.Code.Patches;

internal sealed class ArchitectDeathResourcePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_architect_death_resource";
    public static string Description => "Keep native Architect animations and add the private Spine death track.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(MonsterModel), nameof(MonsterModel.CreateVisuals))];
    public static void Postfix(MonsterModel __instance, NCreatureVisuals __result)
    {
        if (__instance is not Architect) return;
        // Install before the scene enters the tree: changing skeleton data while
        // Spine's generated children are in _Ready re-enters their creation.
        Node sprite = __result.GetNode<Node2D>("%Visuals");
        Resource original = sprite.Call("get_skeleton_data_res").As<Resource>();
        Resource resource = original.Duplicate();
        resource.Set("skeleton_file_res", ResourceLoader.Load<Resource>(BossDismembermentPresentation.ArchitectSkeletonPath));
        sprite.Call("set_skeleton_data_res", resource);
    }
}

internal sealed class ArchitectExecutionStartPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_architect_execution_start";
    public static string Description => "Await NinjaSlayer's Architect execution only when Continue is chosen.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(TheArchitect), "WinRun")
    ];

    internal static bool ShouldReplace(TheArchitect eventModel) =>
        eventModel.Owner?.Character is INinjaSlayerCharacter
        && LocalContext.IsMe(eventModel.Owner);

    public static bool Prefix(TheArchitect __instance, ref Task __result)
    {
        if (!ArchitectExecutionStartPatch.ShouldReplace(__instance)) return true;
        AccessTools.Method(typeof(EventModel), "ClearCurrentOptions").Invoke(__instance, null);
        __result = ArchitectExecutionCinematic.Play(__instance);
        return false;
    }
}

internal sealed class ArchitectEntrancePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_architect_entrance";
    public static string Description => "Keep NinjaSlayer's entrance in the opening native dialogue.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(TheArchitect), "PlayCurrentLine")];

    public static void Postfix(TheArchitect __instance, int ____currentLineIndex, ref Task __result)
    {
        if (____currentLineIndex == 0 && ArchitectExecutionStartPatch.ShouldReplace(__instance))
            __result = EnterAfterLine(__result, __instance);
    }

    private static async Task EnterAfterLine(Task line, TheArchitect model)
    {
        await line;
        await AncientEntranceAnimation.Play(model.Owner!);
    }
}
