using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;
using System.Runtime.CompilerServices;

namespace NinjaSlayer.Code.ExternalAnimations;

public enum NinjaSlayerDeathKind
{
    EnemyKill,
    Other
}

public static class DeathAnimation
{
    public const float EnemyKillDurationSeconds = 0.8f;
    public const float OtherDeathDurationSeconds = 0.45f;

    private const float HitRotationDegrees = -15f;
    private const float FallRotationDegrees = -90f;
    private const float PivotRaiseTexturePixels = 80f;
    private const float FlightTargetHeightRatio = 0.28f;
    private const float FlightArcHeightRatio = 0.3f;
    private const float FlightExitPadding = 32f;

    private static readonly ConditionalWeakTable<Creature, DeathVisualState> VisualStates = new();
    private static readonly ConditionalWeakTable<Creature, ConsumedFatalDamage> ConsumedFatalDamageEntries = new();

    public static NinjaSlayerDeathKind Classify(Creature creature)
    {
        DamageReceivedEntry? fatalEntry = CombatManager.Instance?.History.Entries
            .OfType<DamageReceivedEntry>()
            .LastOrDefault(entry => entry.Receiver == creature && entry.Result.WasTargetKilled);

        var consumed = ConsumedFatalDamageEntries.GetOrCreateValue(creature);
        if (fatalEntry == null || ReferenceEquals(consumed.Entry, fatalEntry))
        {
            return NinjaSlayerDeathKind.Other;
        }

        consumed.Entry = fatalEntry;
        Creature? dealer = fatalEntry.Dealer;
        return dealer != null && dealer != creature && dealer.Side != creature.Side
            ? NinjaSlayerDeathKind.EnemyKill
            : NinjaSlayerDeathKind.Other;
    }

    public static float GetDuration(NinjaSlayerDeathKind kind) => kind switch
    {
        NinjaSlayerDeathKind.EnemyKill => EnemyKillDurationSeconds,
        _ => OtherDeathDurationSeconds
    };

    public static async Task Play(Creature creature, NinjaSlayerDeathKind kind)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        var creatureNode = room?.GetCreatureNode(creature);
        if (room == null || creatureNode == null)
        {
            return;
        }

        RestoreVisual(creature, markCurrentFatalDamageConsumed: false);
        StaggerAnimation.Reset(creature);
        SoarSpinAnimation.ResetSpinVisual(creature);
        if (SoarVisualState.IsAirborne(creature))
        {
            SoarVisualState.ResetVisualsToGround(creature);
        }

        creatureNode.SetAnimationTrigger("Dead");

        Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals);
        Sprite2D? body = NinjaSlayerVisualRig.GetBodySprite(creatureNode.Visuals);
        if (anchor == null || body == null)
        {
            await creatureNode.ToSignal(
                creatureNode.GetTree().CreateTimer(GetDuration(kind)),
                SceneTreeTimer.SignalName.Timeout);
            return;
        }

        var state = DeathVisualState.Capture(anchor, body);
        VisualStates.Add(creature, state);

        if (kind == NinjaSlayerDeathKind.EnemyKill)
        {
            await PlayEnemyKillFlight(creatureNode, anchor, body, state);
        }
        else
        {
            await PlayOtherDeathFall(creature, creatureNode, anchor, body, state);
        }
    }

    public static void RestoreVisual(Creature creature, bool markCurrentFatalDamageConsumed = true)
    {
        if (markCurrentFatalDamageConsumed)
        {
            DamageReceivedEntry? fatalEntry = CombatManager.Instance?.History.Entries
                .OfType<DamageReceivedEntry>()
                .LastOrDefault(entry => entry.Receiver == creature && entry.Result.WasTargetKilled);
            if (fatalEntry != null)
            {
                ConsumedFatalDamageEntries.GetOrCreateValue(creature).Entry = fatalEntry;
            }
        }

        if (!VisualStates.TryGetValue(creature, out DeathVisualState? state))
        {
            return;
        }

        VisualStates.Remove(creature);
        state.Restore(creature);
    }

    private static async Task PlayEnemyKillFlight(
        Node creatureNode,
        Node2D anchor,
        Sprite2D body,
        DeathVisualState state)
    {
        Node2D? focus = NinjaSlayerVisualRig.GetCinematicFocus((creatureNode as MegaCrit.Sts2.Core.Nodes.Combat.NCreature)?.Visuals);
        CanvasItem? anchorParent = anchor.GetParent() as CanvasItem;
        if (focus == null || anchorParent == null)
        {
            await creatureNode.ToSignal(
                creatureNode.GetTree().CreateTimer(EnemyKillDurationSeconds),
                SceneTreeTimer.SignalName.Timeout);
            return;
        }

        anchor.RotationDegrees = HitRotationDegrees;
        Vector2 start = focus.GetGlobalTransformWithCanvas().Origin;
        Vector2 viewportSize = creatureNode.GetViewport().GetVisibleRect().Size;
        float rightExtent = GetRightVisualExtent(anchor, start, body);
        Vector2 end = new(-rightExtent - FlightExitPadding, viewportSize.Y * FlightTargetHeightRatio);
        Vector2 control = (start + end) * 0.5f + Vector2.Up * viewportSize.Y * FlightArcHeightRatio;

        Tween tween = creatureNode.CreateTween();
        state.Tween = tween;
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    Vector2 desiredFocus = QuadraticBezier(start, control, end, progress);
                    SetFocusCanvasPosition(anchor, focus, anchorParent, desiredFocus);
                }),
                0f,
                1f,
                EnemyKillDurationSeconds)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
    }

    private static async Task PlayOtherDeathFall(
        Creature creature,
        Node creatureNode,
        Node2D anchor,
        Sprite2D body,
        DeathVisualState state)
    {
        Vector2 pivotTextureOffset = NinjaSlayerVisualRig.SpinAxisBottomOffset
            + Vector2.Up * PivotRaiseTexturePixels;
        Vector2 pivotPosition = body.Transform * pivotTextureOffset;

        var pivot = new Node2D
        {
            Name = "NinjaSlayerDeathPivot",
            Position = pivotPosition
        };
        anchor.AddChild(pivot);

        var jitter = new NinjaSlayerDeathJitter
        {
            Name = "NinjaSlayerDeathJitter"
        };
        pivot.AddChild(jitter);
        body.Reparent(jitter, keepGlobalTransform: true);
        NarakuVisualOverlay.Sync(creature);

        state.Pivot = pivot;
        state.Jitter = jitter;

        Tween tween = creatureNode.CreateTween();
        state.Tween = tween;
        tween.TweenProperty(pivot, "rotation_degrees", FallRotationDegrees, OtherDeathDurationSeconds)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);

        await creatureNode.ToSignal(tween, Tween.SignalName.Finished);
    }

    private static float GetRightVisualExtent(Node2D anchor, Vector2 focusCanvasPosition, Sprite2D fallbackBody)
    {
        float rightExtent = 0f;
        IEnumerable<Sprite2D> sprites = anchor.FindChildren("*", nameof(Sprite2D), recursive: true, owned: false)
            .OfType<Sprite2D>()
            .Where(sprite => sprite.Visible && sprite.Texture != null);

        foreach (Sprite2D sprite in sprites.DefaultIfEmpty(fallbackBody))
        {
            Rect2 rect = sprite.GetRect();
            Transform2D transform = sprite.GetGlobalTransformWithCanvas();
            Vector2[] corners =
            [
                rect.Position,
                new Vector2(rect.End.X, rect.Position.Y),
                rect.End,
                new Vector2(rect.Position.X, rect.End.Y)
            ];
            rightExtent = Math.Max(
                rightExtent,
                corners.Max(corner => (transform * corner).X - focusCanvasPosition.X));
        }

        return rightExtent;
    }

    private static void SetFocusCanvasPosition(
        Node2D anchor,
        Node2D focus,
        CanvasItem anchorParent,
        Vector2 desiredCanvasPosition)
    {
        Vector2 desiredParentPosition = anchorParent.GetGlobalTransformWithCanvas().AffineInverse()
            * desiredCanvasPosition;
        Vector2 focusOffset = anchor.Transform.BasisXform(focus.Position);
        anchor.Position = desiredParentPosition - focusOffset;
    }

    private static Vector2 QuadraticBezier(Vector2 start, Vector2 control, Vector2 end, float progress)
    {
        float inverse = 1f - progress;
        return inverse * inverse * start
            + 2f * inverse * progress * control
            + progress * progress * end;
    }

    private sealed class ConsumedFatalDamage
    {
        public DamageReceivedEntry? Entry { get; set; }
    }

    private sealed class DeathVisualState
    {
        private readonly Node _bodyParent;
        private readonly int _bodyIndex;
        private readonly Vector2 _bodyPosition;
        private readonly float _bodyRotationDegrees;
        private readonly Vector2 _bodyScale;
        private readonly Vector2 _bodyOffset;
        private readonly Vector2 _anchorPosition;
        private readonly float _anchorRotationDegrees;
        private readonly Vector2 _anchorScale;

        private DeathVisualState(Node2D anchor, Sprite2D body)
        {
            Anchor = anchor;
            Body = body;
            _bodyParent = body.GetParent();
            _bodyIndex = body.GetIndex();
            _bodyPosition = body.Position;
            _bodyRotationDegrees = body.RotationDegrees;
            _bodyScale = body.Scale;
            _bodyOffset = body.Offset;
            _anchorPosition = anchor.Position;
            _anchorRotationDegrees = anchor.RotationDegrees;
            _anchorScale = anchor.Scale;
        }

        public Node2D Anchor { get; }
        public Sprite2D Body { get; }
        public Node2D? Pivot { get; set; }
        public NinjaSlayerDeathJitter? Jitter { get; set; }
        public Tween? Tween { get; set; }

        public static DeathVisualState Capture(Node2D anchor, Sprite2D body) => new(anchor, body);

        public void Restore(Creature creature)
        {
            if (Tween?.IsValid() == true)
            {
                Tween.Kill();
            }

            Jitter?.StopAndReset();
            if (GodotObject.IsInstanceValid(Body) && GodotObject.IsInstanceValid(_bodyParent))
            {
                if (!ReferenceEquals(Body.GetParent(), _bodyParent))
                {
                    Body.Reparent(_bodyParent, keepGlobalTransform: false);
                }

                _bodyParent.MoveChild(Body, Math.Min(_bodyIndex, _bodyParent.GetChildCount() - 1));
                Body.Position = _bodyPosition;
                Body.RotationDegrees = _bodyRotationDegrees;
                Body.Scale = _bodyScale;
                Body.Offset = _bodyOffset;
            }

            if (GodotObject.IsInstanceValid(Anchor))
            {
                Anchor.Position = _anchorPosition;
                Anchor.RotationDegrees = _anchorRotationDegrees;
                Anchor.Scale = _anchorScale;
            }

            NarakuVisualOverlay.Sync(creature);
            if (Pivot != null && GodotObject.IsInstanceValid(Pivot))
            {
                Pivot.QueueFree();
            }
        }
    }
}
