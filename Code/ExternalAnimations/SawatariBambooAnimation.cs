using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class SawatariBambooAnimation
{
    // Episode 08, 07:31: six source frames at 24000/1001 fps.
    internal const float CycleSeconds = 6f * 1001f / 24000f;
    internal const float PeakPhase = 4f / 6f;
    private static readonly float[] Travel = [0f, .327f, .759f, .904f, 1f, .473f, 0f];
    private static readonly float[] Tilt = [0f, .15f, .87f, 10.71f, 19.49f, 9.36f, 0f];
    private static readonly float[] Height = [0f, .25f, -.05f, -3.30f, -5.87f, -2.89f, 0f];

    internal static async Task Play(Creature creature, int hits, Func<Task> impact)
    {
        if (hits <= 0) return;
        float cycle = CombatActionTimingRuntime.VisualSeconds(CycleSeconds);
        if (creature.GetCreatureNode() is not { } node || cycle <= 0f)
        {
            for (int i = 0; i < hits && creature.IsAlive; i++) await impact();
            return;
        }
        Node2D anchor = NinjaSlayerVisualRig.GetAirborneAnchor(node.Visuals)!;
        Node2D center = node.Visuals.VfxSpawnPosition;
        Transform2D baseline = default;
        Vector2 core = default;
        Tween? tween = null;
        bool active = true;
        void Restore()
        {
            if (!active) return;
            active = false;
            if (tween?.IsValid() == true) tween.Kill();
            if (GodotObject.IsInstanceValid(anchor) && GodotObject.IsInstanceValid(center))
                StaggerAnimation.ApplyAttackPose(creature, anchor, center, baseline, core);
            NinjaSlayerShadowController.Get(creature)?.ResetAction();
        }
        long generation = NinjaSlayerRapidAnimationCoordinator.RegisterReturnTail(creature, null, Restore);
        (baseline, core) = StaggerAnimation.CaptureAttackPose(creature, anchor, center);
        float direction = creature.Side == CombatSide.Player ? 1f : -1f;
        double frameRemainder = 0d;
        void Apply(float phase)
        {
            if (!active) return;
            float frame = Mathf.Clamp(phase * 6f, 0f, 6f);
            int index = Math.Min((int)frame, 5);
            float p = frame - index;
            float x = Mathf.Lerp(Travel[index], Travel[index + 1], p) * 126f * direction;
            float y = Mathf.Lerp(Height[index], Height[index + 1], p);
            float angle = Mathf.DegToRad(Mathf.Lerp(Tilt[index], Tilt[index + 1], p) * direction);
            var transform = new Transform2D(angle, Vector2.Zero);
            transform.Origin = core + new Vector2(x, y) - transform.BasisXform(core);
            StaggerAnimation.ApplyAttackPose(creature, anchor, center, transform * baseline, transform * core);
        }
        async Task<bool> Move(float from, float to)
        {
            if (!active || !creature.IsAlive) return false;
            double duration = cycle * (to - from) - frameRemainder;
            if (duration <= 0d)
            {
                Apply(to);
                frameRemainder = -duration;
                return true;
            }
            tween = node.CreateTween();
            tween.TweenMethod(Callable.From<float>(Apply), from, to, duration);
            bool completed = await TweenPlayback.AwaitCompletion(tween, node);
            // Carry only render-frame overshoot; genuine damage/Hook waits remain serial.
            if (completed) frameRemainder = Math.Max(0d, tween.GetTotalElapsedTime() - duration);
            return completed && active;
        }
        try
        {
            for (int i = 0; i < hits && active && creature.IsAlive; i++)
            {
                NinjaSlayerShadowController.Get(creature)?.BeginAction(
                    ShadowActionKind.SlowAttack, cycle * PeakPhase, cycle * (1f - PeakPhase), hold: true);
                if (!await Move(0f, PeakPhase)) break;
                await impact();
                NinjaSlayerShadowController.Get(creature)?.BeginReturn(cycle * (1f - PeakPhase));
                if (!await Move(PeakPhase, 1f)) break;
            }
        }
        finally
        {
            Restore();
            NinjaSlayerRapidAnimationCoordinator.CompleteVisualTail(creature, generation);
        }
    }
}
