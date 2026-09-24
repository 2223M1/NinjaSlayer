using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class SawatariWeaponVisuals
{
    internal const float DualCycleSeconds = 7f * 1001f / 24000f;
    // Episode 08 08:14.5, background-registered rigid body translation, frames 6..13.
    // Source body height is 400px; the accepted runtime body is 537 * .52px.
    private static readonly float[] DualTravel = [0, 78.955f, 155.924f, 163.715f, 171.413f, 176.116f, 83.669f, 0];
    private static readonly float[] DualHeight = [-107.391f, -69.098f, -31.431f, -20.355f, -9.207f, 0, -54.116f, -107.391f];

    internal static async Task PlayDual(Creature source, Creature target, int hits, Func<Task<bool>> impact)
    {
        if (hits <= 0) return;
        float cycle = CombatActionTimingRuntime.VisualSeconds(DualCycleSeconds);
        if (Get(source) is not { } visual || source.GetCreatureNode() is not { } actor || cycle <= 0f)
        {
            for (int i = 0; i < hits && source.IsAlive && target.IsAlive; i++)
                if (!await impact()) break;
            return;
        }
        Node2D anchor = NinjaSlayerVisualRig.GetAirborneAnchor(actor.Visuals)!;
        Node2D center = actor.Visuals.VfxSpawnPosition;
        Transform2D baseline = default;
        Vector2 core = default;
        bool active = true;
        Tween? tween = null;
        void Restore()
        {
            if (!active) return;
            active = false;
            tween?.Kill();
            if (GodotObject.IsInstanceValid(anchor) && GodotObject.IsInstanceValid(center))
                StaggerAnimation.ApplyAttackPose(source, anchor, center, baseline, core);
            NinjaSlayerShadowController.Get(source)?.ResetAction();
        }
        long generation = NinjaSlayerRapidAnimationCoordinator.RegisterReturnTail(source, null, Restore);
        (baseline, core) = StaggerAnimation.CaptureAttackPose(source, anchor, center);
        float direction = visual._body.FlipH ? 1 : -1;
        double frameRemainder = 0;
        int hit = 0;
        void Apply(float frame)
        {
            int index = Math.Min((int)frame, 6);
            float p = frame - index;
            const float scale = 537f * .52f / 400f;
            float height = Mathf.Lerp(DualHeight[index], DualHeight[index + 1], p) * scale;
            if (hit == 0) height *= Mathf.SmoothStep(0, 1, Mathf.Min(frame / 2f, 1));
            if (hit == hits - 1) height *= Mathf.SmoothStep(0, 1, Mathf.Min((7f - frame) / 2f, 1));
            Vector2 shift = new(Mathf.Lerp(DualTravel[index], DualTravel[index + 1], p) * scale * direction, height);
            var motion = Transform2D.Identity.Translated(shift);
            StaggerAnimation.ApplyAttackPose(source, anchor, center, motion * baseline, motion * core);
        }
        async Task<bool> Move(float from, float to)
        {
            if (!active || !source.IsAlive) return false;
            double duration = cycle * (to - from) / 7d - frameRemainder;
            if (duration <= 0) { Apply(to); frameRemainder = -duration; return true; }
            tween = visual.CreateTween();
            tween.TweenMethod(Callable.From<float>(Apply), from, to, duration);
            bool completed = await TweenPlayback.AwaitCompletion(tween, visual);
            if (completed) frameRemainder = Math.Max(0, tween.GetTotalElapsedTime() - duration);
            return completed && active;
        }
        bool detachedReturn = false;
        try
        {
            visual._poseTween?.Kill();
            for (int hand = 0; hand < 2; hand++) visual._hands[hand].RotationDegrees = UprightDegrees[hand];
            for (; hit < hits && active && source.IsAlive && target.IsAlive; hit++)
            {
                NinjaSlayerShadowController.Get(source)?.BeginAction(ShadowActionKind.SlowAttack,
                    cycle * 2f / 7f, cycle * 5f / 7f, hold: true);
                if (!await Move(0, 2)) break;
                bool continueAttack = await impact();
                NinjaSlayerShadowController.Get(source)?.BeginReturn(cycle * 5f / 7f);
                if ((hit == hits - 1 || !continueAttack) && source.Side == CombatSide.Player
                    && source.PetOwner != null
                    && FinisherSessionRegistry.GetActiveSession()?.Actor != source)
                {
                    detachedReturn = true;
                    _ = TaskHelper.RunSafely(FinishReturn());
                    break;
                }
                if (!await Move(2, 7) || !continueAttack) break;
            }
        }
        finally
        {
            if (!detachedReturn)
            {
                Restore();
                NinjaSlayerRapidAnimationCoordinator.CompleteVisualTail(source, generation);
            }
        }

        async Task FinishReturn()
        {
            try { await Move(2, 7); }
            finally
            {
                Restore();
                NinjaSlayerRapidAnimationCoordinator.CompleteVisualTail(source, generation);
            }
        }
    }
}
