using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class SlowAttackAnimation
{
    internal static float ReferencePeakSeconds => CombatActionTimingRuntime.VisualSeconds(0.5f);
    internal static float CompanionPeakSeconds => CombatActionTimingRuntime.CompanionSlowAttackSeconds;

    public static async Task Play(Creature creature)
    {
        float gate = CombatActionTimingRuntime.TriggerSeconds(
            NinjaSlayerAimPose.IsKick(NinjaSlayerAttackExecution.CurrentPlay?.Card) ? 0.25f : 0.2f);
        if (NinjaSlayerFinisherCinematic.TryPlayOwnedAction(creature, gate, out Task owned))
        {
            await owned;
            return;
        }
        if (RapidCardPresentationContext.IsActive && creature.Player?.Character is INinjaSlayerCharacter)
        {
            await NinjaSlayerRapidAnimationCoordinator.PlayAttackToPeak(creature,
                NinjaSlayerCombatVisuals.SlowAttackLungeDistance, gate, FinisherActionTrajectory.SlowProgress,
                returnSeconds: CombatActionTimingRuntime.ReturnSeconds);
            return;
        }
        await PlayLunge(creature, NinjaSlayerCombatVisuals.SlowAttackLungeDistance,
            gate, CombatActionTimingRuntime.VisualSeconds(0.1f), CombatActionTimingRuntime.ReturnSeconds);
    }

    // ActsFromThePast/Animations/SlowAttackAnimation.cs: 90px, pow10 outbound,
    // 0.5s gameplay gate, and a concurrent 0.5s SmoothStep return.
    internal static Task PlayReference(Creature creature) =>
        PlayLunge(creature, 90f, ReferencePeakSeconds, ReferencePeakSeconds, ReferencePeakSeconds);

    private static async Task PlayLunge(Creature creature, float distance, float gate, float outbound, float recovery)
    {
        if (creature.GetCreatureNode() is not { } node || gate <= 0f)
        {
            await Cmd.Wait(gate);
            return;
        }
        Node2D visuals = node.Visuals;
        Vector2 baseline = default;
        bool active = true;
        Tween tween = node.CreateTween();
        var peak = new TaskCompletionSource();
        void Restore()
        {
            if (!active) return;
            active = false;
            if (tween.IsValid()) tween.Kill();
            if (GodotObject.IsInstanceValid(visuals)) visuals.Position = baseline;
            NinjaSlayerShadowController.Get(creature)?.ResetAction();
            peak.TrySetResult();
        }
        long generation = NinjaSlayerRapidAnimationCoordinator.RegisterReturnTail(creature, null, Restore);
        baseline = visuals.Position;
        float direction = creature.Monster is Monsters.YamotoKokiMonster
            ? node.Body.Transform.Determinant() < 0f ? -1f : 1f
            : creature.Side == CombatSide.Player ? 1f : -1f;
        NinjaSlayerShadowController.Get(creature)?.BeginAction(ShadowActionKind.SlowAttack, outbound, recovery, hold: true);
        void Apply(float offset)
        {
            if (!active) return;
            visuals.Position = baseline + Vector2.Right * (direction * distance * offset);
        }
        tween.TweenMethod(Callable.From<float>(elapsed =>
            Apply(Mathf.Pow(Mathf.Clamp(elapsed / outbound, 0f, 1f), 10f))), 0f, gate, gate);
        tween.TweenCallback(Callable.From(() =>
        {
            NinjaSlayerShadowController.Get(creature)?.BeginReturn(recovery);
            peak.TrySetResult();
        }));
        tween.TweenMethod(Callable.From<float>(p => Apply(1f - Mathf.SmoothStep(0f, 1f, p))), 0f, 1f, recovery);
        _ = TaskHelper.RunSafely(Finish());
        await peak.Task;

        async Task Finish()
        {
            try { await TweenPlayback.AwaitCompletion(tween, node); }
            finally
            {
                Restore();
                NinjaSlayerRapidAnimationCoordinator.CompleteVisualTail(creature, generation);
            }
        }
    }
}
