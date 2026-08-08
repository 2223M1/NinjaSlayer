using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class DarkNinjaSpecialAttackPresentation
{
    private const string CharacterTexturePath =
        "res://NinjaSlayer/images/monsters/dark_ninja_character.png";
    private const string SwordTexturePath =
        "res://NinjaSlayer/images/monsters/dark_ninja_sword.png";

    private const float TextureWidth = 733f;
    private const float TextureHeight = 649f;
    private const float ExitPadding = 32f;
    private const float ReturnArcCanvasHeight = 180f;

    internal static IEnumerable<string> AssetPaths =>
        [CharacterTexturePath, SwordTexturePath];

    internal static async Task PlayDeathSlash(
        Creature attacker,
        Func<Task> onDamage)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaSlowAttackEvent);
        using DarkNinjaBodyMotionLease? motion = DarkNinjaBodyMotionLease.TryAcquire(attacker);
        if (motion == null)
        {
            await Cmd.Wait(DarkNinjaCombatMath.DeathSlashTotalSeconds);
            await onDamage();
            return;
        }

        await Cmd.Wait(DarkNinjaCombatMath.DeathSlashWindupSeconds);
        Vector2 start = motion.FocusCanvasPosition;
        Vector2 viewportSize = motion.Owner.GetViewport().GetVisibleRect().Size;
        float leftExitX = -motion.HalfWidth - ExitPadding;

        await PlayTween(
            motion.Owner,
            DarkNinjaCombatMath.DeathSlashOutboundSeconds,
            progress =>
            {
                float x = Mathf.Lerp(start.X, leftExitX, progress);
                motion.SetFocusCanvasPosition(new Vector2(x, start.Y));
            });

        await Cmd.Wait(DarkNinjaCombatMath.DeathSlashOffscreenSeconds);
        motion.SetFocusCanvasPosition(new Vector2(
            viewportSize.X + motion.HalfWidth + ExitPadding,
            start.Y));
        Vector2 returnStart = motion.FocusCanvasPosition;
        await PlayTween(
            motion.Owner,
            DarkNinjaCombatMath.DeathSlashReturnSeconds,
            progress => motion.SetFocusCanvasPosition(returnStart.Lerp(start, progress)));
        motion.SetFocusCanvasPosition(start);
        await onDamage();
    }

    internal static async Task PlayDarkStrike(
        Creature attacker,
        IReadOnlyList<Creature> targets,
        Func<Creature, Task<int>> onImpact)
    {
        Creature[] orderedTargets = OrderTargets(targets);
        if (orderedTargets.Length == 0)
        {
            return;
        }

        using DarkNinjaDetachedVisualLease? visual = DarkNinjaDetachedVisualLease.TryAcquire(attacker);
        if (visual == null)
        {
            await PlayDarkStrikeFallback(attacker, orderedTargets, onImpact);
            return;
        }

        int highestHealing = 0;
        for (int index = 0; index < orderedTargets.Length; index++)
        {
            Creature target = orderedTargets[index];
            if (!target.IsAlive)
            {
                continue;
            }

            DarkNinjaStabSegment segment = DarkNinjaCombatMath.GetDarkStrikeSegment(index);
            visual.PrepareTarget(target, segment.ReferenceStartSeconds);
            using CreatureScreenHalfOverlayLease? overlay =
                CreatureScreenHalfOverlayLease.TryAcquire(
                    visual.Room,
                    target,
                    visual.OverlayZIndex);
            Task<int>? impactTask = null;
            Task holdTask = Task.CompletedTask;
            void TriggerImpactOnFinalMotionFrame()
            {
                if (impactTask != null || !target.IsAlive)
                {
                    return;
                }

                visual.ApplyReferencePose(
                    target,
                    DarkNinjaCombatMath.DarkStrikeReferenceMotionSeconds);
                overlay?.SyncMask();
                impactTask = onImpact(target);
                holdTask = Cmd.Wait(segment.HoldSeconds);
            }

            await PlayTween(
                visual.Owner,
                segment.MotionSeconds,
                progress =>
                {
                    float referenceSeconds = segment.ReferenceStartSeconds
                        + segment.MotionSeconds * progress;
                    visual.ApplyReferencePose(target, referenceSeconds);
                    overlay?.SyncMask();
                },
                TriggerImpactOnFinalMotionFrame);

            TriggerImpactOnFinalMotionFrame();
            if (impactTask == null)
            {
                continue;
            }

            highestHealing = Math.Max(highestHealing, await impactTask);
            await holdTask;
        }

        if (highestHealing > 0 && attacker.IsAlive)
        {
            await CreatureCmd.Heal(attacker, highestHealing);
        }

        visual.ShowFullBody();
        await visual.PlayWrappedReturn();
    }

    private static async Task PlayDarkStrikeFallback(
        Creature attacker,
        Creature[] targets,
        Func<Creature, Task<int>> onImpact)
    {
        int highestHealing = 0;
        for (int index = 0; index < targets.Length; index++)
        {
            if (!targets[index].IsAlive)
            {
                continue;
            }

            DarkNinjaStabSegment segment = DarkNinjaCombatMath.GetDarkStrikeSegment(index);
            await Cmd.Wait(segment.MotionSeconds);
            if (!targets[index].IsAlive)
            {
                continue;
            }

            highestHealing = Math.Max(highestHealing, await onImpact(targets[index]));
            await Cmd.Wait(segment.HoldSeconds);
        }

        if (highestHealing > 0 && attacker.IsAlive)
        {
            await CreatureCmd.Heal(attacker, highestHealing);
        }

        await Cmd.Wait(DarkNinjaCombatMath.DarkStrikeReturnSeconds);
    }

    private static Creature[] OrderTargets(IReadOnlyList<Creature> targets)
    {
        Creature[] validTargets = targets.Where(target => target.IsAlive).ToArray();
        float[] canvasX = validTargets
            .Select(ResolveTargetCanvasCenterX)
            .ToArray();
        return DarkNinjaCombatMath.OrderTargetsByCanvasX(canvasX)
            .Select(index => validTargets[index])
            .ToArray();
    }

    private static float ResolveTargetCanvasCenterX(Creature target)
    {
        NCreature? targetNode = NCombatRoom.Instance?.GetCreatureNode(target);
        return targetNode?.Visuals.Bounds.GetGlobalRect().GetCenter().X ?? float.MaxValue;
    }

    private static async Task PlayTween(
        Node owner,
        float duration,
        Action<float> apply,
        Action? onFinalFrame = null)
    {
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
        {
            return;
        }

        Tween tween = owner.CreateTween();
        tween.TweenMethod(Callable.From(apply), 0f, 1f, duration)
            .SetTrans(Tween.TransitionType.Linear);
        if (onFinalFrame != null)
        {
            tween.TweenCallback(Callable.From(onFinalFrame));
        }

        await TweenPlayback.AwaitCompletion(tween, owner);
    }

    private sealed class DarkNinjaBodyMotionLease : IDisposable
    {
        private readonly Node2D _anchor;
        private readonly Node2D _focus;
        private readonly CanvasItem _anchorParent;
        private readonly Vector2 _anchorPosition;
        private readonly Sprite2D? _shadow;
        private readonly bool _shadowVisible;
        private int _disposed;

        private DarkNinjaBodyMotionLease(
            NCreature owner,
            Node2D anchor,
            Node2D focus,
            CanvasItem anchorParent,
            Sprite2D? shadow)
        {
            Owner = owner;
            _anchor = anchor;
            _focus = focus;
            _anchorParent = anchorParent;
            _anchorPosition = anchor.Position;
            _shadow = shadow;
            _shadowVisible = shadow?.Visible ?? false;
            FocusCanvasPosition = focus.GetGlobalTransformWithCanvas().Origin;
            HalfWidth = owner.Visuals.Bounds.GetGlobalRect().Size.X * 0.5f;
            if (shadow != null)
            {
                shadow.Visible = false;
            }
        }

        internal NCreature Owner { get; }
        internal Vector2 FocusCanvasPosition { get; private set; }
        internal float HalfWidth { get; }

        internal static DarkNinjaBodyMotionLease? TryAcquire(Creature creature)
        {
            NCreature? owner = NCombatRoom.Instance?.GetCreatureNode(creature);
            Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(owner?.Visuals)
                ?? owner?.Visuals.GetCurrentBody();
            Node2D? focus = NinjaSlayerVisualRig.GetCinematicFocus(owner?.Visuals)
                ?? owner?.Visuals.GetCurrentBody();
            if (owner == null
                || anchor == null
                || focus == null
                || anchor.GetParent() is not CanvasItem parent)
            {
                return null;
            }

            return new DarkNinjaBodyMotionLease(
                owner,
                anchor,
                focus,
                parent,
                NinjaSlayerVisualRig.GetShadow(owner.Visuals));
        }

        internal void SetFocusCanvasPosition(Vector2 desiredCanvasPosition)
        {
            if (Volatile.Read(ref _disposed) != 0
                || !GodotObject.IsInstanceValid(_anchor)
                || !GodotObject.IsInstanceValid(_focus))
            {
                return;
            }

            Vector2 desiredParentPosition = _anchorParent.GetGlobalTransformWithCanvas()
                .AffineInverse()
                * desiredCanvasPosition;
            Vector2 focusOffset = _anchor.Transform.BasisXform(_focus.Position);
            _anchor.Position = desiredParentPosition - focusOffset;
            FocusCanvasPosition = desiredCanvasPosition;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (GodotObject.IsInstanceValid(_anchor))
            {
                _anchor.Position = _anchorPosition;
            }

            if (_shadow != null && GodotObject.IsInstanceValid(_shadow))
            {
                _shadow.Visible = _shadowVisible;
            }
        }
    }

    private sealed class DarkNinjaDetachedVisualLease : IDisposable
    {
        private readonly CanvasItem _sceneContainer;
        private readonly Sprite2D _sourceBody;
        private readonly bool _sourceBodyVisible;
        private readonly Sprite2D? _shadow;
        private readonly bool _shadowVisible;
        private readonly Node2D _root;
        private readonly Sprite2D _character;
        private readonly Sprite2D _sword;
        private readonly Sprite2D _fullBody;
        private readonly Vector2 _baselinePosition;
        private readonly float _scaleX;
        private readonly float _scaleY;
        private int _disposed;

        private DarkNinjaDetachedVisualLease(
            NCombatRoom room,
            NCreature owner,
            CanvasItem sceneContainer,
            Sprite2D sourceBody,
            Sprite2D? shadow,
            Node2D root,
            Sprite2D character,
            Sprite2D sword,
            Sprite2D fullBody,
            Vector2 baselinePosition,
            float scaleX,
            float scaleY)
        {
            Room = room;
            Owner = owner;
            _sceneContainer = sceneContainer;
            _sourceBody = sourceBody;
            _sourceBodyVisible = sourceBody.Visible;
            _shadow = shadow;
            _shadowVisible = shadow?.Visible ?? false;
            _root = root;
            _character = character;
            _sword = sword;
            _fullBody = fullBody;
            _baselinePosition = baselinePosition;
            _scaleX = scaleX;
            _scaleY = scaleY;
            sourceBody.Visible = false;
            if (shadow != null)
            {
                shadow.Visible = false;
            }
        }

        internal NCombatRoom Room { get; }
        internal NCreature Owner { get; }
        internal int OverlayZIndex => Math.Clamp(_root.ZIndex + 2, -4095, 4095);

        internal static DarkNinjaDetachedVisualLease? TryAcquire(Creature creature)
        {
            Node2D? root = null;
            try
            {
                NCombatRoom? room = NCombatRoom.Instance;
                NCreature? owner = room?.GetCreatureNode(creature);
                Sprite2D? sourceBody = NinjaSlayerVisualRig.GetBodySprite(owner?.Visuals);
                if (room == null || owner == null || sourceBody == null)
                {
                    return null;
                }

                Texture2D characterTexture = PreloadManager.Cache.GetTexture2D(CharacterTexturePath);
                Texture2D swordTexture = PreloadManager.Cache.GetTexture2D(SwordTexturePath);
                Texture2D fullTexture = PreloadManager.Cache.GetTexture2D(
                    DarkNinjaMonster.CombatTexturePath);
                CanvasItem sceneContainer = room.SceneContainer;
                Transform2D bodyToScene = sceneContainer.GetGlobalTransformWithCanvas()
                    .AffineInverse()
                    * sourceBody.GetGlobalTransformWithCanvas();
                float scaleX = bodyToScene.X.Length();
                float scaleY = bodyToScene.Y.Length();
                if (!float.IsFinite(scaleX)
                    || !float.IsFinite(scaleY)
                    || scaleX <= 0f
                    || scaleY <= 0f)
                {
                    return null;
                }

                Node2D targetBody = owner.Visuals.GetCurrentBody();
                int zIndex = CreatureScreenHalfOverlayLease.ResolveEffectiveZ(targetBody) + 1;
                root = new Node2D
                {
                    Name = "DarkNinjaDarkStrike",
                    Position = bodyToScene.Origin,
                    Scale = new Vector2(-scaleX, scaleY),
                    ZAsRelative = false,
                    ZIndex = Math.Clamp(zIndex, -4095, 4095),
                    Visible = false
                };
                room.SceneContainer.AddChildSafely(root);
                var character = new Sprite2D
                {
                    Name = "Character",
                    Texture = characterTexture,
                    ZIndex = 0
                };
                var sword = new Sprite2D
                {
                    Name = "Sword",
                    Texture = swordTexture,
                    ZIndex = 1
                };
                var fullBody = new Sprite2D
                {
                    Name = "FullBody",
                    Texture = fullTexture,
                    ZIndex = 0,
                    Visible = false
                };
                root.AddChildSafely(character);
                root.AddChildSafely(sword);
                root.AddChildSafely(fullBody);
                return new DarkNinjaDetachedVisualLease(
                    room,
                    owner,
                    sceneContainer,
                    sourceBody,
                    NinjaSlayerVisualRig.GetShadow(owner.Visuals),
                    root,
                    character,
                    sword,
                    fullBody,
                    bodyToScene.Origin,
                    scaleX,
                    scaleY);
            }
            catch (Exception exception)
            {
                if (root != null && GodotObject.IsInstanceValid(root))
                {
                    root.QueueFreeSafely();
                }

                Entry.Logger.Warn($"Dark Strike detached presentation was unavailable: {exception.Message}");
                return null;
            }
        }

        internal void PrepareTarget(Creature target, float referenceSeconds)
        {
            NCreature? targetNode = Room.GetCreatureNode(target);
            if (targetNode != null)
            {
                _root.ZIndex = Math.Clamp(
                    CreatureScreenHalfOverlayLease.ResolveEffectiveZ(
                        targetNode.Visuals.GetCurrentBody()) + 1,
                    -4095,
                    4095);
            }

            ApplyReferencePose(target, referenceSeconds);
            _root.Visible = true;
        }

        internal void ApplyReferencePose(Creature target, float referenceSeconds)
        {
            if (Volatile.Read(ref _disposed) != 0
                || !GodotObject.IsInstanceValid(_root))
            {
                return;
            }

            Vector2 targetCanvasPosition = ResolveImpactCanvasPosition(target);
            Vector2 targetScenePosition = _sceneContainer.GetGlobalTransformWithCanvas()
                .AffineInverse()
                * targetCanvasPosition;
            Vector2 contactLocal = new(
                DarkNinjaCombatMath.DarkStrikeContactTextureX - TextureWidth * 0.5f,
                DarkNinjaCombatMath.DarkStrikeContactTextureY - TextureHeight * 0.5f);
            Vector2 finalPosition = targetScenePosition - new Vector2(
                contactLocal.X * -_scaleX,
                contactLocal.Y * _scaleY);
            DarkNinjaPoint offset = DarkNinjaCombatMath.SampleDarkStrikeOffset(referenceSeconds);
            _root.Position = finalPosition + new Vector2(
                offset.X * _scaleX,
                offset.Y * _scaleY);
        }

        internal void ShowFullBody()
        {
            _character.Visible = false;
            _sword.Visible = false;
            _fullBody.Visible = true;
        }

        internal async Task PlayWrappedReturn()
        {
            Vector2 start = _root.Position;
            Transform2D canvasToScene = _sceneContainer.GetGlobalTransformWithCanvas().AffineInverse();
            Vector2 viewportSize = Owner.GetViewport().GetVisibleRect().Size;
            Vector2 leftCanvas = canvasToScene * Vector2.Zero;
            Vector2 rightCanvas = canvasToScene * new Vector2(viewportSize.X, 0f);
            float viewportWidth = Math.Abs(rightCanvas.X - leftCanvas.X);
            float canvasScaleY = _sceneContainer.GetGlobalTransformWithCanvas().Y.Length();
            float arcHeight = ReturnArcCanvasHeight / Math.Max(canvasScaleY, 0.001f);
            float rightExtent = TextureWidth * _scaleX * 0.5f;
            float wrapThreshold = Math.Min(leftCanvas.X, rightCanvas.X) - rightExtent;
            DarkNinjaPoint unwrappedStart = new(start.X, start.Y);
            DarkNinjaPoint unwrappedEnd = new(
                _baselinePosition.X - viewportWidth,
                _baselinePosition.Y);

            await PlayTween(
                Owner,
                DarkNinjaCombatMath.DarkStrikeReturnSeconds,
                progress =>
                {
                    DarkNinjaPoint point = DarkNinjaCombatMath.SampleReturnParabola(
                        unwrappedStart,
                        unwrappedEnd,
                        arcHeight,
                        progress);
                    float displayX = point.X <= wrapThreshold
                        ? point.X + viewportWidth
                        : point.X;
                    _root.Position = new Vector2(displayX, point.Y);
                });
            _root.Position = _baselinePosition;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (GodotObject.IsInstanceValid(_sourceBody))
            {
                _sourceBody.Visible = _sourceBodyVisible;
            }

            if (_shadow != null && GodotObject.IsInstanceValid(_shadow))
            {
                _shadow.Visible = _shadowVisible;
            }

            if (GodotObject.IsInstanceValid(_root))
            {
                _root.QueueFreeSafely();
            }
        }

        private Vector2 ResolveImpactCanvasPosition(Creature target)
        {
            NCreature? targetNode = Room.GetCreatureNode(target);
            if (targetNode == null)
            {
                return _sceneContainer.GetGlobalTransformWithCanvas() * _root.Position;
            }

            Marker2D marker = targetNode.Visuals.VfxSpawnPosition;
            return GodotObject.IsInstanceValid(marker)
                ? marker.GetGlobalTransformWithCanvas().Origin
                : targetNode.Visuals.Bounds.GetGlobalRect().GetCenter();
        }
    }
}
