using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// Continuous vertical-axis projection; cues select textures without sampling transforms.
/// </summary>
public static class SoarSpinAnimation
{
    private const float XAttackDuration = 0.24f;
    private const float FullTurnDegrees = 360f;
    private static readonly Dictionary<Creature, Tween> activeSpinTweens = new();
    private static readonly Dictionary<Creature, float> spinDegrees = new();
    private static readonly HashSet<Creature> activeVerticalSpins = [];

    public static float MaxDegreesPerSecond => FullTurnDegrees / XAttackDuration * 3f;

    public static bool IsSpinning(Creature creature) => activeSpinTweens.ContainsKey(creature);

    public static bool IsVerticalSpinActive(Creature creature) => activeVerticalSpins.Contains(creature);

    internal static async Task PlayCueSpin(Creature creature, float duration)
    {
        VerticalAxisSpinProjection? projection = CreateNinjaSlayerProjection(creature);
        if (projection == null) return;
        try
        {
            await PlayFiniteVerticalAxisProjection(creature, duration,
                p => projection.ApplyDegrees(360f * p,
                    age => 360d * Math.Max(0d, p - age / duration)));
        }
        finally
        {
            if (!IsSpinning(creature) && NinjaSlayerAimPose.Get(creature)?.OwnsSpin != true)
            {
                projection.Restore();
                EnsureAirborneSpin(creature);
            }
        }
    }

    public static async Task Accelerate(Creature creature, float duration)
    {
        activeVerticalSpins.Add(creature);
        try
        {
            await Play(creature, duration, MaxDegreesPerSecond, accelerating: true);
            StartAirborneSpin(creature, MaxDegreesPerSecond);
        }
        catch
        {
            ResetSpinVisual(creature);
            throw;
        }
    }

    public static async Task Decelerate(Creature creature, float duration)
    {
        StopAirborneSpin(creature);
        try
        {
            await Play(creature, duration, MaxDegreesPerSecond, accelerating: false);
        }
        catch
        {
            ResetSpinVisual(creature);
            throw;
        }
    }

    public static void StartAirborneSpin(Creature creature, float degreesPerSecond)
    {
        StopAirborneSpin(creature);

        var creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            return;
        }

        var visuals = GetSpinVisual(creature);
        if (visuals == null)
        {
            return;
        }

        activeVerticalSpins.Add(creature);
        float startingDegrees = spinDegrees.GetValueOrDefault(creature);
        VerticalAxisSpinProjection projection = CreateNinjaSlayerProjection(creature)!;
        projection.ApplyDegrees(startingDegrees);
        var tween = creatureNode.CreateTween();
        tween.SetLoops();
        tween.TweenMethod(
            Callable.From<float>(degrees =>
            {
                double time = tween.GetTotalElapsedTime();
                float currentDegrees = startingDegrees + degreesPerSecond * (float)time;
                spinDegrees[creature] = currentDegrees;
                projection.ApplyDegrees(currentDegrees,
                    age => startingDegrees + degreesPerSecond * Math.Max(0d, time - age));
            }),
            0f,
            FullTurnDegrees,
            FullTurnDegrees / degreesPerSecond)
            .SetTrans(Tween.TransitionType.Linear);

        activeSpinTweens[creature] = tween;
    }

    public static async Task PlayFiniteAirborneSpin(
        Creature creature,
        float duration,
        Func<float, float> angleDegreesAtProgress,
        ICinematicAnimationContext? cinematicContext = null)
    {
        VerticalAxisSpinProjection? projection = CreateNinjaSlayerProjection(creature);
        if (projection == null)
        {
            return;
        }

        float startingDegrees = spinDegrees.GetValueOrDefault(creature);
        projection.ApplyDegrees(startingDegrees);
        await PlayFiniteVerticalAxisProjection(
            creature,
            duration,
            progress =>
            {
                float currentDegrees = startingDegrees + angleDegreesAtProgress(progress);
                spinDegrees[creature] = currentDegrees;
                projection.ApplyDegrees(currentDegrees, age => startingDegrees + angleDegreesAtProgress(
                    duration > 0f ? Mathf.Max(0f, progress - (float)age / duration) : progress));
            },
            cinematicContext,
            keepActivityAfterCompletion: true);
    }

    internal static async Task PlayFiniteVerticalAxisProjection(
        Creature creature,
        float duration,
        Action<float> applyAtProgress,
        ICinematicAnimationContext? cinematicContext = null,
        bool keepActivityAfterCompletion = false)
    {
        StopAirborneSpin(creature);

        var creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            return;
        }

        activeVerticalSpins.Add(creature);
        applyAtProgress(0f);
        if (duration <= 0f)
        {
            applyAtProgress(1f);
            if (!keepActivityAfterCompletion) activeVerticalSpins.Remove(creature);
            return;
        }
        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(applyAtProgress),
                0f,
                1f,
                duration)
            .SetTrans(Tween.TransitionType.Linear);

        activeSpinTweens[creature] = tween;
        try
        {
            if (cinematicContext == null)
            {
                await TweenPlayback.AwaitCompletion(tween, creatureNode);
            }
            else
            {
                await cinematicContext.AwaitTween(creatureNode, tween);
            }
        }
        finally
        {
            if (activeSpinTweens.TryGetValue(creature, out Tween? activeTween)
                && ReferenceEquals(activeTween, tween))
            {
                activeSpinTweens.Remove(creature);
                if (tween.IsValid())
                {
                    tween.Kill();
                }

                if (!keepActivityAfterCompletion)
                {
                    activeVerticalSpins.Remove(creature);
                }
            }
        }
    }

    public static void StopAirborneSpin(Creature creature)
    {
        if (activeSpinTweens.Remove(creature, out Tween? tween) && tween.IsValid())
        {
            tween.Kill();
        }
    }

    internal static void SuspendForCinematic(Creature creature)
    {
        if (!IsSpinning(creature) && !IsVerticalSpinActive(creature))
        {
            return;
        }

        StopAirborneSpin(creature);
        activeVerticalSpins.Remove(creature);
        NinjaSlayerSpinMotionBlur.Get(creature)?.Reset();

        Sprite2D? visuals = GetSpinVisual(creature);
        if (visuals != null)
        {
            RestoreVerticalSpin(creature, visuals);
        }
    }

    public static void ResetSpinVisual(Creature creature)
    {
        StopAirborneSpin(creature);
        spinDegrees.Remove(creature);
        activeVerticalSpins.Remove(creature);

        NinjaSlayerSpinMotionBlur.Get(creature)?.Reset();

        var visuals = GetSpinVisual(creature);
        if (visuals == null)
        {
            return;
        }

        RestoreVerticalSpin(creature, visuals);
    }

    public static void EnsureAirborneSpin(Creature creature)
    {
        if (NinjaSlayerHellTornadoVisual.Get(creature)?.Active == true
            || !SoarVisualState.IsAirborne(creature) || IsSpinning(creature)
            || NinjaSlayerAimPose.Get(creature)?.OwnsSpin == true)
        {
            return;
        }

        StartAirborneSpin(creature, MaxDegreesPerSecond);
    }

    private static async Task Play(Creature creature, float duration, float maxDegreesPerSecond, bool accelerating)
    {
        var creatureNode = creature.GetCreatureNode();
        if (creatureNode == null)
        {
            return;
        }

        var visuals = GetSpinVisual(creature);
        if (visuals == null)
        {
            return;
        }

        float startingDegrees = spinDegrees.GetValueOrDefault(creature);
        VerticalAxisSpinProjection projection = CreateNinjaSlayerProjection(creature)!;
        float Angle(float p) => startingDegrees + maxDegreesPerSecond * duration
            * (accelerating ? p * p * p / 3f : p - p * p + p * p * p / 3f);
        await PlayFiniteVerticalAxisProjection(creature, duration,
            p =>
            {
                float degrees = Angle(p);
                spinDegrees[creature] = degrees;
                projection.ApplyDegrees(degrees,
                    age => Angle(duration > 0f ? Mathf.Max(0f, p - (float)age / duration) : p));
            }, keepActivityAfterCompletion: true);

        if (!accelerating)
        {
            spinDegrees.Remove(creature);
            activeVerticalSpins.Remove(creature);
            projection.Restore();
        }
    }

    private static VerticalAxisSpinProjection? CreateNinjaSlayerProjection(Creature creature)
    {
        NinjaSlayerFreeControl.Get(creature)?.ReleaseSpinProjection();
        Sprite2D? visuals = GetSpinVisual(creature);
        Node2D? focus = GetSpinFocus(creature);
        Node2D? axis = GetSpinAxis(creature) ?? focus;
        return visuals == null || focus == null
            ? null
            : VerticalAxisSpinProjection.CaptureNinjaSlayer(
                visuals,
                focus,
                axis!,
                GetNormalScaleX(visuals));
    }

    private static Sprite2D? GetSpinVisual(Creature creature)
    {
        var visualsRoot = creature.GetCreatureNode()?.Visuals;
        if (visualsRoot == null)
        {
            return null;
        }

        return NinjaSlayerVisualRig.GetBodySprite(visualsRoot);
    }

    private static Node2D? GetSpinFocus(Creature creature) =>
        NinjaSlayerVisualRig.GetCinematicFocus(creature.GetCreatureNode()?.Visuals);

    private static Node2D? GetSpinAxis(Creature creature) =>
        NinjaSlayerVisualRig.GetGroundContact(creature.GetCreatureNode()?.Visuals);

    private static void RestoreVerticalSpin(Creature creature, Node2D visuals)
    {
        if (visuals is not Sprite2D sprite || GetSpinFocus(creature) is not { } focus)
        {
            return;
        }

        Node2D axis = GetSpinAxis(creature) ?? focus;

        VerticalAxisSpinProjection.CaptureNinjaSlayer(
                sprite,
                focus,
                axis,
                GetNormalScaleX(visuals))
            .ApplyDegrees(0f);
        sprite.Offset = Vector2.Zero;
    }

    private static float GetNormalScaleX(Node2D visuals)
    {
        return Mathf.Abs(visuals.Scale.Y) > 0.001f ? Mathf.Abs(visuals.Scale.Y) : Mathf.Abs(visuals.Scale.X);
    }
}
