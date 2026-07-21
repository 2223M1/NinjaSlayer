using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Combat;

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
        using MegaTrackEntry? track = creatureNode.SpineAnimation.GetCurrentTrack();
        if (track?.GetAnimationName() != "hurt")
        {
            return false;
        }

        float trackTime = creatureNode.Entity.Monster?.HurtAnimationTrackOffsetForDoom ?? 0.1f;
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

        using MegaTrackEntry? track = creatureNode.SpineAnimation.GetCurrentTrack();
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
