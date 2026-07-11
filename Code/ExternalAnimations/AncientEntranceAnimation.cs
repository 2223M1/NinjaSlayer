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
    private const float FallDistance = 900f;
    private const float SideOffset = 1400f;
    private const float SideStartYOffset = -140f;
    private const float SideArcHeight = 260f;
    private const float LeftLandingOffset = -110f;

    public static async Task Play(Player player)
    {
        Creature creature = player.Creature;
        float roll = player.RunState.Rng.Niche.NextFloat();
        try
        {
            if (roll < 0.5f)
            {
                await SlideFromLeft(creature);
                return;
            }

            int longIndex = Mathf.Min(3, Mathf.FloorToInt((roll - 0.5f) / 0.125f));
            switch (longIndex)
            {
                case 0:
                    await FallFromTop(creature);
                    break;
                case 1:
                    await InvertedFallFromTopLeft(creature);
                    break;
                case 2:
                    await SpinningFallFromSide(creature, fromLeft: true);
                    break;
                default:
                    await SpinningFallFromSide(creature, fromLeft: false);
                    break;
            }
        }
        finally
        {
            SetVisualsVisible(creature);
        }
    }

    private static async Task SlideFromLeft(Creature creature)
    {
        if (!TryGetRig(creature, out NCreature creatureNode, out _, out _, out _))
        {
            return;
        }

        Vector2 basePos = creatureNode.Position;
        try
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerShortWashoiEvent);
            creatureNode.Position = new Vector2(basePos.X - SideOffset, basePos.Y);
            SetVisualsVisible(creature);
            await TweenNodePosition(creatureNode, basePos, SlideDuration, Tween.EaseType.Out, Tween.TransitionType.Quad);
        }
        finally
        {
            creatureNode.Position = basePos;
        }
    }

    private static async Task FallFromTop(Creature creature)
    {
        if (!TryGetRig(creature, out _, out Node2D anchor, out _, out RigSnapshot snapshot))
        {
            return;
        }

        try
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerLongWashoiEvent);
            anchor.Position = snapshot.AnchorPosition + new Vector2(0f, -FallDistance);
            SetVisualsVisible(creature);
            await ByrdFallAnimation.Play(creature, FallDistance, FallDuration);
        }
        finally
        {
            snapshot.Restore(creature);
        }
    }

    private static async Task InvertedFallFromTopLeft(Creature creature)
    {
        if (!TryGetRig(creature, out NCreature creatureNode, out Node2D anchor, out Node2D body, out RigSnapshot snapshot))
        {
            return;
        }

        Vector2 landingPos = snapshot.CreaturePosition + new Vector2(LeftLandingOffset, 0f);
        float invertedRotationDegrees = snapshot.BodyRotationDegrees + 180f;
        float uprightRotationDegrees = snapshot.BodyRotationDegrees + 360f;
        try
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerLongWashoiEvent);
            creatureNode.Position = landingPos;
            body.RotationDegrees = invertedRotationDegrees;
            anchor.Position = snapshot.AnchorPosition + new Vector2(0f, -FallDistance);
            SoarSpinAnimation.StartAirborneSpin(creature, AlabamaDropAnimation.TumbleDegreesPerSecond);
            SetVisualsVisible(creature);

            await Task.WhenAll(
                ByrdFallAnimation.Play(creature, FallDistance, FallDuration),
                HoldBodyRotation(body, invertedRotationDegrees, FallDuration));
            SoarSpinAnimation.StopAirborneSpin(creature);
            body.Scale = snapshot.BodyScale;
            if (body is Sprite2D sprite)
            {
                sprite.Offset = Vector2.Zero;
            }
            body.RotationDegrees = invertedRotationDegrees;
            await Task.WhenAll(
                TweenNodePosition(creatureNode, snapshot.CreaturePosition, RiseDuration, Tween.EaseType.Out, Tween.TransitionType.Quad),
                TweenBodyRotation(body, uprightRotationDegrees, RiseDuration));
        }
        finally
        {
            snapshot.Restore(creature);
        }
    }

    private static async Task SpinningFallFromSide(Creature creature, bool fromLeft)
    {
        if (!TryGetRig(creature, out NCreature creatureNode, out Node2D anchor, out _, out RigSnapshot snapshot))
        {
            return;
        }

        float direction = fromLeft ? -1f : 1f;
        Vector2 startPos = snapshot.CreaturePosition + new Vector2(SideOffset * direction, SideStartYOffset);
        try
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerLongWashoiEvent);
            creatureNode.Position = startPos;
            anchor.Position = snapshot.AnchorPosition + new Vector2(0f, -FallDistance);
            SoarSpinAnimation.StartAirborneSpin(creature, AlabamaDropAnimation.TumbleDegreesPerSecond);
            SetVisualsVisible(creature);

            await Task.WhenAll(
                TweenSideFallParabola(creatureNode, snapshot.CreaturePosition, direction, FallDuration),
                ByrdFallAnimation.Play(creature, FallDistance, FallDuration));
        }
        finally
        {
            snapshot.Restore(creature);
        }
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
        float duration)
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

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
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

    private static async Task TweenNodePosition(NCreature creatureNode, Vector2 target, float duration, Tween.EaseType ease, Tween.TransitionType transition)
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

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
    }

    private static async Task TweenBodyRotation(Node2D body, float targetDegrees, float duration)
    {
        var tween = body.CreateTween();
        tween.TweenProperty(body, "rotation_degrees", targetDegrees, duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);

        await body.ToSignal(tween, Tween.SignalName.Finished);
    }

    private static async Task HoldBodyRotation(Node2D body, float rotationDegrees, float duration)
    {
        var tween = body.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(_ =>
            {
                body.RotationDegrees = rotationDegrees;
            }),
            0f,
            1f,
            duration);

        await body.ToSignal(tween, Tween.SignalName.Finished);
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
