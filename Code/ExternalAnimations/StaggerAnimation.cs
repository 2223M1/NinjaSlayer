using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class StaggerAnimation
{
    private const float StaggerDuration = 0.3f;
    private const float StaggerDistance = 20f;
    private const float StaggerRotationDegrees = -15f;

    private static readonly Dictionary<Creature, Vector2> OriginalPositions = new();
    private static readonly Dictionary<Creature, float> OriginalBodyRotations = new();
    private static readonly Dictionary<Creature, Tween> ActiveTweens = new();

    public static async Task Play(Creature creature)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
            return;

        var visuals = creatureNode.Visuals;
        var bodyAnchor = NinjaSlayerVisualRig.GetAirborneAnchor(visuals)
            ?? NinjaSlayerVisualRig.GetBodySprite(visuals);

        // Kill any active tween and reset before capturing position
        if (ActiveTweens.TryGetValue(creature, out var existing) && existing.IsValid())
        {
            existing.Kill();
            if (OriginalPositions.TryGetValue(creature, out var savedPos))
                creatureNode.Position = savedPos;
            if (bodyAnchor != null && OriginalBodyRotations.TryGetValue(creature, out var savedRotation))
                bodyAnchor.RotationDegrees = savedRotation;
        }

        // Only store the true origin if we don't already have one mid-stagger
        if (!OriginalPositions.ContainsKey(creature))
            OriginalPositions[creature] = creatureNode.Position;
        if (bodyAnchor != null && !OriginalBodyRotations.ContainsKey(creature))
            OriginalBodyRotations[creature] = bodyAnchor.RotationDegrees;

        var originalPos = OriginalPositions[creature];
        var originalRotation = bodyAnchor == null ? 0f : OriginalBodyRotations[creature];
        var direction = creature.IsPlayer ? -1f : 1f;

        var tween = creatureNode.CreateTween();
        ActiveTweens[creature] = tween;

        tween.TweenMethod(
            Callable.From<float>(t =>
            {
                var easedT = t * t;
                var xOffset = Mathf.Lerp(StaggerDistance, 0f, easedT) * direction;
                creatureNode.Position = new Vector2(originalPos.X + xOffset, originalPos.Y);
                if (bodyAnchor != null)
                    bodyAnchor.RotationDegrees = originalRotation + Mathf.Lerp(StaggerRotationDegrees, 0f, easedT);
            }),
            0f,
            1f,
            StaggerDuration
        ).SetTrans(Tween.TransitionType.Linear);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);

        creatureNode.Position = originalPos;
        if (bodyAnchor != null)
            bodyAnchor.RotationDegrees = originalRotation;
        OriginalPositions.Remove(creature);
        OriginalBodyRotations.Remove(creature);
        ActiveTweens.Remove(creature);
    }
    
    public static void Reset()
    {
        foreach (Creature creature in ActiveTweens.Keys
                     .Concat(OriginalPositions.Keys)
                     .Concat(OriginalBodyRotations.Keys)
                     .Distinct()
                     .ToArray())
        {
            Reset(creature);
        }
    }

    public static void Reset(Creature creature)
    {
        if (ActiveTweens.Remove(creature, out Tween? tween) && tween.IsValid())
        {
            tween.Kill();
        }

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode != null && OriginalPositions.Remove(creature, out Vector2 originalPosition))
        {
            creatureNode.Position = originalPosition;
        }

        Node2D? bodyAnchor = creatureNode == null
            ? null
            : NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals)
                ?? NinjaSlayerVisualRig.GetBodySprite(creatureNode.Visuals);
        if (bodyAnchor != null && OriginalBodyRotations.Remove(creature, out float originalRotation))
        {
            bodyAnchor.RotationDegrees = originalRotation;
        }

        OriginalPositions.Remove(creature);
        OriginalBodyRotations.Remove(creature);
    }
}
