using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>
/// Continuous vertical-axis spin during Soar. Tutorial has no equivalent; uses code tween on body sprite
/// while VisualCue idle/attack cues handle textures. Scale-only (no X lunge) to avoid SpinPivot ghosting.
/// </summary>
public static class SoarSpinAnimation
{
    private const float XAttackDuration = 0.24f;
    private const float FullTurnDegrees = 360f;
    private const float MinScaleRatio = 0.18f;
    private static readonly Dictionary<Creature, Tween> activeSpinTweens = new();
    private static readonly Dictionary<Creature, float> spinDegrees = new();

    public static float MaxDegreesPerSecond => FullTurnDegrees / XAttackDuration * 3f;

    public static bool IsSpinning(Creature creature) => activeSpinTweens.ContainsKey(creature);

    public static async Task Accelerate(Creature creature, float duration)
    {
        await Play(creature, duration, MaxDegreesPerSecond, accelerating: true);
        StartAirborneSpin(creature, MaxDegreesPerSecond);
    }

    public static async Task Decelerate(Creature creature, float duration)
    {
        StopAirborneSpin(creature);
        await Play(creature, duration, MaxDegreesPerSecond, accelerating: false);
    }

    public static void StartAirborneSpin(Creature creature, float degreesPerSecond)
    {
        StopAirborneSpin(creature);

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
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
        var tween = creatureNode.CreateTween();
        tween.SetLoops();
        tween.TweenMethod(
            Callable.From<float>(degrees =>
            {
                float currentDegrees = startingDegrees + degrees;
                spinDegrees[creature] = Mathf.PosMod(currentDegrees, FullTurnDegrees);
                ApplyVerticalSpin(visuals, currentDegrees);
            }),
            0f,
            FullTurnDegrees,
            FullTurnDegrees / degreesPerSecond)
            .SetTrans(Tween.TransitionType.Linear);

        activeSpinTweens[creature] = tween;
    }

    public static void StopAirborneSpin(Creature creature)
    {
        if (activeSpinTweens.Remove(creature, out Tween? tween) && tween.IsValid())
        {
            tween.Kill();
        }
    }

    public static void ResetSpinVisual(Creature creature)
    {
        StopAirborneSpin(creature);
        spinDegrees.Remove(creature);

        var visuals = GetSpinVisual(creature);
        if (visuals == null)
        {
            return;
        }

        visuals.Scale = new Vector2(GetNormalScaleX(visuals), visuals.Scale.Y);
        visuals.RotationDegrees = 0f;
        if (visuals is Sprite2D sprite)
        {
            sprite.Offset = Vector2.Zero;
        }
    }

    public static void EnsureAirborneSpin(Creature creature)
    {
        if (!SoarVisualState.IsAirborne(creature) || IsSpinning(creature))
        {
            return;
        }

        StartAirborneSpin(creature, MaxDegreesPerSecond);
    }

    private static async Task Play(Creature creature, float duration, float maxDegreesPerSecond, bool accelerating)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        var visuals = GetSpinVisual(creature);
        if (visuals == null)
        {
            return;
        }

        float elapsed = 0f;
        float degrees = spinDegrees.GetValueOrDefault(creature);
        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(t =>
            {
                float delta = Mathf.Max(0f, t - elapsed);
                elapsed = t;
                float ratio = duration <= 0f ? 1f : Mathf.Clamp(t / duration, 0f, 1f);
                float speed = maxDegreesPerSecond * (accelerating ? ratio * ratio : (1f - ratio) * (1f - ratio));
                degrees += speed * delta;
                spinDegrees[creature] = Mathf.PosMod(degrees, FullTurnDegrees);
                ApplyVerticalSpin(visuals, degrees);
            }),
            0f,
            duration,
            duration);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);

        if (!accelerating)
        {
            spinDegrees.Remove(creature);
            visuals.Scale = new Vector2(GetNormalScaleX(visuals), visuals.Scale.Y);
            visuals.RotationDegrees = 0f;
        }
    }

    private static void ApplyVerticalSpin(Node2D visuals, float degrees)
    {
        float scaleRatio = Mathf.Cos(Mathf.DegToRad(degrees));
        if (Mathf.Abs(scaleRatio) < MinScaleRatio)
        {
            scaleRatio = scaleRatio < 0f ? -MinScaleRatio : MinScaleRatio;
        }

        visuals.RotationDegrees = 0f;
        visuals.Scale = new Vector2(GetNormalScaleX(visuals) * scaleRatio, visuals.Scale.Y);
    }

    private static Node2D? GetSpinVisual(Creature creature)
    {
        var visualsRoot = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        if (visualsRoot == null)
        {
            return null;
        }

        var body = NinjaSlayerVisualRig.GetBodySprite(visualsRoot);
        if (body != null)
        {
            return body;
        }

        return visualsRoot;
    }

    private static float GetNormalScaleX(Node2D visuals)
    {
        return Mathf.Abs(visuals.Scale.Y) > 0.001f ? Mathf.Abs(visuals.Scale.Y) : Mathf.Abs(visuals.Scale.X);
    }
}
