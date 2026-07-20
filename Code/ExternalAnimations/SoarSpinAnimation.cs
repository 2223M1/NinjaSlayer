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
    private static readonly HashSet<Creature> activeVerticalSpins = [];

    public static float MaxDegreesPerSecond => FullTurnDegrees / XAttackDuration * 3f;

    public static bool IsSpinning(Creature creature) => activeSpinTweens.ContainsKey(creature);

    public static bool IsVerticalSpinActive(Creature creature) => activeVerticalSpins.Contains(creature);

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
        finally
        {
            ResetSpinVisual(creature);
        }
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

        activeVerticalSpins.Add(creature);
        float startingDegrees = spinDegrees.GetValueOrDefault(creature);
        var tween = creatureNode.CreateTween();
        tween.SetLoops();
        tween.TweenMethod(
            Callable.From<float>(degrees =>
            {
                float currentDegrees = startingDegrees + degrees;
                spinDegrees[creature] = Mathf.PosMod(currentDegrees, FullTurnDegrees);
                ApplyVerticalSpin(creature, currentDegrees);
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
                ApplyVerticalSpin(creature, degrees);
            }),
            0f,
            duration,
            duration);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);

        if (!accelerating)
        {
            spinDegrees.Remove(creature);
            RestoreVerticalSpin(creature, visuals);
        }
    }

    private static void ApplyVerticalSpin(Creature creature, float degrees)
    {
        Sprite2D? visuals = GetSpinVisual(creature);
        Node2D? focus = GetSpinFocus(creature);
        if (visuals == null || focus == null)
        {
            return;
        }

        float scaleRatio = Mathf.Cos(Mathf.DegToRad(degrees));
        if (Mathf.Abs(scaleRatio) < MinScaleRatio)
        {
            scaleRatio = scaleRatio < 0f ? -MinScaleRatio : MinScaleRatio;
        }

        float scaleX = GetNormalScaleX(visuals) * scaleRatio;
        visuals.RotationDegrees = 0f;
        visuals.Offset = Vector2.Zero;
        visuals.Scale = new Vector2(scaleX, visuals.Scale.Y);
        visuals.Position = new Vector2(
            focus.Position.X - NinjaSlayerVisualRig.SpinPivotDeltaX * scaleX,
            visuals.Position.Y);
    }

    private static Sprite2D? GetSpinVisual(Creature creature)
    {
        var visualsRoot = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        if (visualsRoot == null)
        {
            return null;
        }

        return NinjaSlayerVisualRig.GetBodySprite(visualsRoot);
    }

    private static Node2D? GetSpinFocus(Creature creature) =>
        NinjaSlayerVisualRig.GetCinematicFocus(NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals);

    private static void RestoreVerticalSpin(Creature creature, Node2D visuals)
    {
        float scaleX = GetNormalScaleX(visuals);
        visuals.Scale = new Vector2(scaleX, visuals.Scale.Y);
        visuals.RotationDegrees = 0f;
        if (visuals is not Sprite2D sprite)
        {
            return;
        }

        sprite.Offset = Vector2.Zero;
        if (GetSpinFocus(creature) is { } focus)
        {
            sprite.Position = new Vector2(
                focus.Position.X - NinjaSlayerVisualRig.SpinPivotDeltaX * scaleX,
                sprite.Position.Y);
        }
    }

    private static float GetNormalScaleX(Node2D visuals)
    {
        return Mathf.Abs(visuals.Scale.Y) > 0.001f ? Mathf.Abs(visuals.Scale.Y) : Mathf.Abs(visuals.Scale.X);
    }
}
