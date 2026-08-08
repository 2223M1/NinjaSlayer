using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class AncientEntranceAnimation
{
    private const float SlideDuration = 0.5f;
    private const float FallDuration = 0.6f;
    private const float RiseDuration = 0.2f;
    private const float LandingImpactTailSeconds = 0.85f;
    private const float FallDistance = 900f;
    private const float SideOffset = 1400f;
    private const float SideStartYOffset = -140f;
    private const float SideArcHeight = 260f;
    private const float LeftLandingOffset = -110f;
    private const float TumbleAngleCoefficient = 1800f;

    public enum EntranceVariant
    {
        SlideFromLeft,
        FallFromTop,
        InvertedFallFromTopLeft,
        SpinningFallFromLeft,
        SpinningFallFromRight
    }

    public static float GetDuration(EntranceVariant variant) => variant switch
    {
        EntranceVariant.SlideFromLeft => SlideDuration,
        EntranceVariant.InvertedFallFromTopLeft => FallDuration + RiseDuration,
        _ => FallDuration
    };

    public static float GetCinematicAudioDuration(EntranceVariant variant) => variant switch
    {
        EntranceVariant.SlideFromLeft => NinjaSlayerAudio.ShortWashoiSeconds,
        _ => Math.Max(NinjaSlayerAudio.LongWashoiSeconds, FallDuration + LandingImpactTailSeconds)
    };

    public static EntranceVariant FromRoll(float roll)
    {
        if (roll < 0.5f)
        {
            return EntranceVariant.SlideFromLeft;
        }

        int longIndex = Mathf.Min(3, Mathf.FloorToInt((roll - 0.5f) / 0.125f));
        return longIndex switch
        {
            0 => EntranceVariant.FallFromTop,
            1 => EntranceVariant.InvertedFallFromTopLeft,
            2 => EntranceVariant.SpinningFallFromLeft,
            _ => EntranceVariant.SpinningFallFromRight
        };
    }

    public static Task Play(Player player) =>
        Play(player, FromRoll(player.RunState.Rng.Niche.NextFloat()));

    public static async Task Play(
        Player player,
        EntranceVariant variant,
        ICinematicAnimationContext? cinematicContext = null,
        Task? startSignal = null)
    {
        Creature creature = player.Creature;
        try
        {
            switch (variant)
            {
                case EntranceVariant.SlideFromLeft:
                    await SlideFromLeft(creature, cinematicContext, startSignal);
                    break;
                case EntranceVariant.FallFromTop:
                    await FallFromTop(creature, cinematicContext, startSignal);
                    break;
                case EntranceVariant.InvertedFallFromTopLeft:
                    await InvertedFallFromTopLeft(creature, cinematicContext, startSignal);
                    break;
                case EntranceVariant.SpinningFallFromLeft:
                    await SpinningFallFromSide(creature, fromLeft: true, cinematicContext, startSignal);
                    break;
                case EntranceVariant.SpinningFallFromRight:
                    await SpinningFallFromSide(creature, fromLeft: false, cinematicContext, startSignal);
                    break;
            }
        }
        finally
        {
            SetVisualsVisible(creature);
        }
    }

    private static async Task SlideFromLeft(
        Creature creature,
        ICinematicAnimationContext? cinematicContext,
        Task? startSignal)
    {
        if (!TryGetRig(creature, out NCreature creatureNode, out _, out _, out _))
        {
            return;
        }

        Vector2 basePos = creatureNode.Position;
        try
        {
            creatureNode.Position = new Vector2(basePos.X - SideOffset, basePos.Y);
            SetVisualsVisible(creature);
            await WaitForStart(startSignal, cinematicContext);
            PlaySfx(cinematicContext, NinjaSlayerAudio.NinjaSlayerShortWashoiEvent);
            await TweenNodePosition(creatureNode, basePos, SlideDuration, Tween.EaseType.Out, Tween.TransitionType.Quad, cinematicContext);
        }
        finally
        {
            creatureNode.Position = basePos;
        }
    }

    private static async Task FallFromTop(
        Creature creature,
        ICinematicAnimationContext? cinematicContext,
        Task? startSignal)
    {
        if (!TryGetRig(creature, out _, out Node2D anchor, out _, out RigSnapshot snapshot))
        {
            return;
        }

        try
        {
            anchor.Position = snapshot.AnchorPosition + new Vector2(0f, -FallDistance);
            SetVisualsVisible(creature);
            await WaitForStart(startSignal, cinematicContext);
            PlaySfx(cinematicContext, NinjaSlayerAudio.NinjaSlayerLongWashoiEvent);
            await ByrdFallAnimation.Play(creature, FallDistance, FallDuration, cinematicContext: cinematicContext);
        }
        finally
        {
            snapshot.Restore(creature);
        }
    }

    private static async Task InvertedFallFromTopLeft(
        Creature creature,
        ICinematicAnimationContext? cinematicContext,
        Task? startSignal)
    {
        if (!TryGetRig(creature, out NCreature creatureNode, out Node2D anchor, out Node2D body, out RigSnapshot snapshot))
        {
            return;
        }

        Vector2 landingPos = snapshot.CreaturePosition + new Vector2(LeftLandingOffset, 0f);
        float invertedRotationDegrees = snapshot.BodyRotationDegrees + 180f;
        float uprightRotationDegrees = snapshot.BodyRotationDegrees + 360f;
        FixedPivotTransform? shadowPivot = CaptureShadowPivot(creatureNode.Visuals, body);
        try
        {
            creatureNode.Position = landingPos;
            ApplyBodyRotation(body, shadowPivot, invertedRotationDegrees, snapshot.BodyScale);
            anchor.Position = snapshot.AnchorPosition + new Vector2(0f, -FallDistance);
            SetVisualsVisible(creature);
            await WaitForStart(startSignal, cinematicContext);
            PlaySfx(cinematicContext, NinjaSlayerAudio.NinjaSlayerLongWashoiEvent);

            VerticalAxisSpinProjection? invertedProjection = CaptureCurrentShadowAxisProjection(
                creatureNode.Visuals,
                body);
            Task spin = invertedProjection is { } projection
                ? PlayFiniteProjection(
                    creature,
                    projection,
                    FallDuration,
                    GetTumbleAngleDegrees,
                    cinematicContext)
                : SoarSpinAnimation.PlayFiniteAirborneSpin(
                    creature,
                    FallDuration,
                    GetTumbleAngleDegrees,
                    cinematicContext);
            Task holdRotation = invertedProjection == null
                ? HoldBodyRotation(
                    body,
                    invertedRotationDegrees,
                    snapshot.BodyScale,
                    FallDuration,
                    cinematicContext,
                    shadowPivot)
                : Task.CompletedTask;
            await Task.WhenAll(
                ByrdFallAnimation.Play(creature, FallDistance, FallDuration, cinematicContext: cinematicContext),
                holdRotation,
                spin);
            body.Scale = snapshot.BodyScale;
            if (body is Sprite2D sprite)
            {
                sprite.Offset = Vector2.Zero;
            }
            ApplyBodyRotation(body, shadowPivot, invertedRotationDegrees, snapshot.BodyScale);
            await Task.WhenAll(
                TweenNodePosition(creatureNode, snapshot.CreaturePosition, RiseDuration, Tween.EaseType.Out, Tween.TransitionType.Quad, cinematicContext),
                TweenBodyRotation(
                    body,
                    uprightRotationDegrees,
                    RiseDuration,
                    cinematicContext,
                    shadowPivot));
        }
        finally
        {
            snapshot.Restore(creature);
        }
    }

    private static async Task SpinningFallFromSide(
        Creature creature,
        bool fromLeft,
        ICinematicAnimationContext? cinematicContext,
        Task? startSignal)
    {
        if (!TryGetRig(creature, out NCreature creatureNode, out Node2D anchor, out _, out RigSnapshot snapshot))
        {
            return;
        }

        float direction = fromLeft ? -1f : 1f;
        Vector2 startPos = snapshot.CreaturePosition + new Vector2(SideOffset * direction, SideStartYOffset);
        try
        {
            creatureNode.Position = startPos;
            anchor.Position = snapshot.AnchorPosition + new Vector2(0f, -FallDistance);
            SetVisualsVisible(creature);
            await WaitForStart(startSignal, cinematicContext);
            PlaySfx(cinematicContext, NinjaSlayerAudio.NinjaSlayerLongWashoiEvent);
            await Task.WhenAll(
                TweenSideFallParabola(creatureNode, snapshot.CreaturePosition, direction, FallDuration, cinematicContext),
                ByrdFallAnimation.Play(creature, FallDistance, FallDuration, cinematicContext: cinematicContext),
                SoarSpinAnimation.PlayFiniteAirborneSpin(
                    creature,
                    FallDuration,
                    GetTumbleAngleDegrees,
                    cinematicContext));
        }
        finally
        {
            snapshot.Restore(creature);
        }
    }

    private static float GetTumbleAngleDegrees(float progress)
    {
        float p = Mathf.Clamp(progress, 0f, 1f);
        return TumbleAngleCoefficient * (p + 1.2f * p * p + 0.2f * p * p * p);
    }

    private static void SetVisualsVisible(Creature creature)
    {
        var visuals = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        if (visuals != null)
        {
            visuals.Show();
        }
    }

    private static async Task TweenSideFallParabola(
        NCreature creatureNode,
        Vector2 landingPosition,
        float direction,
        float duration,
        ICinematicAnimationContext? cinematicContext)
    {
        Vector2 worldStartOffset = new(SideOffset * direction, SideStartYOffset - FallDistance);
        Vector2 controlOffset = new(SideOffset * direction * 0.55f, worldStartOffset.Y - SideArcHeight);

        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                float inverse = 1f - progress;
                Vector2 worldOffset =
                    inverse * inverse * worldStartOffset
                    + 2f * inverse * progress * controlOffset;

                // ByrdFall moves the airborne anchor with an ease-in quadratic curve.
                float anchorYOffset = -FallDistance * (1f - progress * progress);
                creatureNode.Position = landingPosition
                    + new Vector2(worldOffset.X, worldOffset.Y - anchorYOffset);
            }),
            0f,
            1f,
            duration)
            .SetTrans(Tween.TransitionType.Linear);

        await AwaitTween(creatureNode, tween, cinematicContext);
        creatureNode.Position = landingPosition;
    }

    private static bool TryGetRig(Creature creature, out NCreature creatureNode, out Node2D anchor, out Node2D body, out RigSnapshot snapshot)
    {
        creatureNode = null!;
        anchor = null!;
        body = null!;
        snapshot = default;

        creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature)!;
        if (creatureNode == null || creatureNode.Visuals == null)
        {
            return false;
        }

        anchor = NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals)!;
        if (anchor == null)
        {
            return false;
        }

        body = (Node2D?)NinjaSlayerVisualRig.GetBodySprite(creatureNode.Visuals) ?? creatureNode.Visuals;
        snapshot = RigSnapshot.Capture(creatureNode, anchor, body);
        return true;
    }

    private static async Task TweenNodePosition(NCreature creatureNode, Vector2 target, float duration, Tween.EaseType ease, Tween.TransitionType transition, ICinematicAnimationContext? cinematicContext)
    {
        Vector2 start = creatureNode.Position;
        var tween = creatureNode.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                creatureNode.Position = start.Lerp(target, progress);
            }),
            0f,
            1f,
            duration)
            .SetEase(ease)
            .SetTrans(transition);

        await AwaitTween(creatureNode, tween, cinematicContext);
    }

    private static FixedPivotTransform? CaptureShadowPivot(
        NCreatureVisuals visuals,
        Node2D body)
    {
        Sprite2D? shadow = NinjaSlayerVisualRig.GetShadow(visuals);
        Sprite2D? bodySprite = NinjaSlayerVisualRig.GetBodySprite(visuals);
        return shadow != null
            && ReferenceEquals(body, bodySprite)
            && FixedPivotTransform.TryCapture(body, shadow, out FixedPivotTransform transform)
                ? transform
                : null;
    }

    private static VerticalAxisSpinProjection? CaptureCurrentShadowAxisProjection(
        NCreatureVisuals visuals,
        Node2D body)
    {
        Node2D? bodyMarker = NinjaSlayerVisualRig.GetCinematicFocus(visuals);
        Sprite2D? shadow = NinjaSlayerVisualRig.GetShadow(visuals);
        if (bodyMarker == null
            || shadow == null
            || !ReferenceEquals(body, NinjaSlayerVisualRig.GetBodySprite(visuals)))
        {
            return null;
        }

        Vector2 markerCanvasPosition = bodyMarker.GetGlobalTransformWithCanvas().Origin;
        return VerticalAxisSpinProjection.CaptureCurrent(
            body,
            shadow.GetGlobalTransformWithCanvas().Origin.X,
            markerCanvasPosition);
    }

    private static async Task PlayFiniteProjection(
        Creature creature,
        VerticalAxisSpinProjection projection,
        float duration,
        Func<float, float> angleDegreesAtProgress,
        ICinematicAnimationContext? cinematicContext)
    {
        await SoarSpinAnimation.PlayFiniteVerticalAxisProjection(
            creature,
            duration,
            progress => projection.ApplyDegrees(angleDegreesAtProgress(progress)),
            cinematicContext,
            keepActivityAfterCompletion: true);
        projection.ApplyDegrees(angleDegreesAtProgress(1f));
    }

    private static void ApplyBodyRotation(
        Node2D body,
        FixedPivotTransform? pivot,
        float rotationDegrees,
        Vector2 scale)
    {
        if (!GodotObject.IsInstanceValid(body))
        {
            return;
        }

        if (pivot is { } fixedPivot)
        {
            fixedPivot.Apply(rotationDegrees, scale);
            return;
        }

        body.RotationDegrees = rotationDegrees;
        body.Scale = scale;
    }

    private static async Task TweenBodyRotation(
        Node2D body,
        float targetDegrees,
        float duration,
        ICinematicAnimationContext? cinematicContext,
        FixedPivotTransform? pivot = null)
    {
        float startDegrees = body.RotationDegrees;
        Vector2 scale = body.Scale;
        var tween = body.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                    ApplyBodyRotation(
                        body,
                        pivot,
                        Mathf.Lerp(startDegrees, targetDegrees, progress),
                        scale)),
                0f,
                1f,
                duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);

        await AwaitTween(body, tween, cinematicContext);
        ApplyBodyRotation(body, pivot, targetDegrees, scale);
    }

    private static async Task HoldBodyRotation(
        Node2D body,
        float rotationDegrees,
        Vector2 scale,
        float duration,
        ICinematicAnimationContext? cinematicContext,
        FixedPivotTransform? pivot = null)
    {
        var tween = body.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(_ => ApplyBodyRotation(body, pivot, rotationDegrees, scale)),
            0f,
            1f,
            duration);

        await AwaitTween(body, tween, cinematicContext);
    }

    private static async Task AwaitTween(Node owner, Tween tween, ICinematicAnimationContext? cinematicContext)
    {
        if (cinematicContext == null)
        {
            await TweenPlayback.AwaitCompletion(tween, owner);
            return;
        }

        await cinematicContext.AwaitTween(owner, tween);
    }

    private static async Task WaitForStart(Task? startSignal, ICinematicAnimationContext? cinematicContext)
    {
        if (startSignal == null)
        {
            return;
        }

        if (cinematicContext == null)
        {
            await startSignal;
            return;
        }

        await startSignal.WaitAsync(cinematicContext.CancellationToken);
    }

    private static void PlaySfx(ICinematicAnimationContext? cinematicContext, string eventPath)
    {
        if (cinematicContext == null)
        {
            NinjaSlayerCombatAudioSet.Play(eventPath);
        }
        else
        {
            cinematicContext.PlaySfx(eventPath);
        }
    }

    private readonly record struct RigSnapshot(
        Vector2 CreaturePosition,
        float CreatureRotationDegrees,
        Vector2 CreatureScale,
        Vector2 AnchorPosition,
        Vector2 BodyPosition,
        float BodyRotationDegrees,
        Vector2 BodyScale)
    {
        public static RigSnapshot Capture(NCreature creatureNode, Node2D anchor, Node2D body) =>
            new(
                creatureNode.Position,
                creatureNode.RotationDegrees,
                creatureNode.Scale,
                anchor.Position,
                body.Position,
                body.RotationDegrees,
                body.Scale);

        public void Restore(Creature creature)
        {
            var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
            if (creatureNode != null)
            {
                creatureNode.Position = CreaturePosition;
                creatureNode.RotationDegrees = CreatureRotationDegrees;
                creatureNode.Scale = CreatureScale;
            }

            var visuals = creatureNode?.Visuals;
            var anchor = NinjaSlayerVisualRig.GetAirborneAnchor(visuals);
            if (anchor != null)
            {
                anchor.Position = AnchorPosition;
            }

            var body = visuals == null ? null : (Node2D?)NinjaSlayerVisualRig.GetBodySprite(visuals) ?? visuals;
            if (body != null)
            {
                body.Position = BodyPosition;
                body.RotationDegrees = BodyRotationDegrees;
                body.Scale = BodyScale;
            }

            SoarVisualState.ResetVisualsToGround(creature);
            SoarSpinAnimation.ResetSpinVisual(creature);
            HopAnimation.SyncBasePosition(creature, Vector2.Zero);
        }
    }
}
