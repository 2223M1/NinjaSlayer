using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Compatibility;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class DoomHurtPoseController
{
    public static bool TryFreeze(NCreature creatureNode)
    {
        if (!creatureNode.SpineAnimation.IsValid)
        {
            return false;
        }

        creatureNode.SetAnimationTrigger("Hit");
        MegaTrackEntry? track = creatureNode.SpineAnimation.GetCurrentTrack();
        using IDisposable trackLease = GameCompatibility.NativeHandles.Lease(track);
        if (track?.GetAnimationName() != "hurt")
        {
            return false;
        }

        float trackTime = GameCompatibility.CreaturePresentation.GetHurtAnimationTrackOffset(
            creatureNode.Entity.Monster);
        track.SetTrackTime(trackTime);
        track.SetTimeScale(0f);
        return true;
    }

    public static void Resume(NCreature creatureNode)
    {
        if (!GodotObject.IsInstanceValid(creatureNode) || !creatureNode.SpineAnimation.IsValid)
        {
            return;
        }

        MegaTrackEntry? track = creatureNode.SpineAnimation.GetCurrentTrack();
        using IDisposable trackLease = GameCompatibility.NativeHandles.Lease(track);
        if (track?.GetAnimationName() == "hurt")
        {
            track.SetTimeScale(1f);
        }
    }

    public static void Resume(IEnumerable<NCreature> creatureNodes)
    {
        foreach (NCreature creatureNode in creatureNodes)
        {
            Resume(creatureNode);
        }
    }
}
