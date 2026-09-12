using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Events;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;

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
