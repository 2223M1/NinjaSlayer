using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class StaggerAnimation
{
    internal const float DefaultRotationDegrees = -18f;
    internal const float MirroredRotationDegrees = 18f;
    private static readonly Dictionary<Creature, StaggerState> ActiveStates = [];

    public static bool IsActive(Creature creature) => ActiveStates.ContainsKey(creature)
        || NinjaSlayerAimPose.Get(creature)?.HasHurt == true;

    internal static async Task WaitForCompletion(Creature creature)
    {
        while (ActiveStates.TryGetValue(creature, out StaggerState? state))
            await state.Completion.Task;
    }

    internal static Action? PauseCurrent(Creature creature)
    {
        if (NinjaSlayerAimPose.Get(creature) is { } pose) return pose.PauseHurt();
        if (!ActiveStates.TryGetValue(creature, out StaggerState? state)) return null;
        state.Tween.Pause();
        return () => { if (state.Active && state.Tween.IsValid()) state.Tween.Play(); };
    }

    public static async Task Play(Creature creature, float rotationDegrees = DefaultRotationDegrees)
    {
        float duration = CombatActionTimingRuntime.VisualSeconds(0.15f);
        if (NinjaSlayerAimPose.Get(creature) is { } pose)
        {
            var motion = pose.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Hurt, duration);
            pose.SyncNow();
            if (motion != null) await motion.Completion;
            return;
        }
        Reset(creature);
        if (duration <= 0f || creature.GetCreatureNode() is not { } node) return;
        Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(node.Visuals);
        if (anchor == null) return;
        NinjaSlayerRapidAnimationCoordinator.EnsureLifecycle(creature);
        var state = new StaggerState(node.CreateTween(), anchor, node.Visuals.VfxSpawnPosition);
        ActiveStates.Add(creature, state);
        float direction = creature.Side == CombatSide.Player ? -1f : 1f;
        void Apply(float progress)
        {
            if (!state.Active) return;
            float envelope = 1f - progress * progress;
            var transform = new Transform2D(Mathf.DegToRad(rotationDegrees * envelope),
                new Vector2(1f + .04f * envelope, 1f - .045f * envelope), 0f, Vector2.Zero);
            transform.Origin = state.CenterPosition + Vector2.Right * (28f * direction * envelope)
                - transform.BasisXform(state.CenterPosition);
            anchor.Transform = transform * state.AnchorTransform;
            state.Center.Position = transform * state.CenterPosition;
        }
        Apply(0f);
        NinjaSlayerShadowController.Get(creature)?.BeginAction(ShadowActionKind.Hurt, 0f, duration);
        try
        {
            state.Tween.TweenMethod(Callable.From<float>(Apply), 0f, 1f, duration);
            await TweenPlayback.AwaitCompletion(state.Tween, node);
        }
        finally
        {
            if (ActiveStates.TryGetValue(creature, out var current) && ReferenceEquals(current, state))
                ActiveStates.Remove(creature);
            state.Restore();
        }
    }

    public static void Reset()
    {
        foreach (Creature creature in ActiveStates.Keys.ToArray()) Reset(creature);
    }

    public static void Reset(Creature creature)
    {
        NinjaSlayerAimPose.Get(creature)?.ClearHurt();
        if (ActiveStates.Remove(creature, out var state)) state.Restore();
    }

    private sealed class StaggerState(Tween tween, Node2D anchor, Node2D center)
    {
        internal readonly Tween Tween = tween;
        internal readonly Node2D Center = center;
        internal readonly Transform2D AnchorTransform = anchor.Transform;
        internal readonly Vector2 CenterPosition = center.Position;
        internal readonly TaskCompletionSource Completion = new();
        internal bool Active = true;

        internal void Restore()
        {
            if (!Active) return;
            Active = false;
            if (Tween.IsValid()) Tween.Kill();
            if (GodotObject.IsInstanceValid(anchor)) anchor.Transform = AnchorTransform;
            if (GodotObject.IsInstanceValid(Center)) Center.Position = CenterPosition;
            Completion.TrySetResult();
        }
    }
}
