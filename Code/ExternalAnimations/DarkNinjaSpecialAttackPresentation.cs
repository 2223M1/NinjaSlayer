using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
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
    private const int MaximumCanvasZIndex = 4095;

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
        if (FinisherApproach.TryPlayToPeak(attacker, DarkNinjaCombatMath.DeathSlashTotalSeconds, out Task approach))
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaSlowAttackEvent);
            await approach;
            FinisherApproach.ReachImpact(attacker);
            await onDamage();
            return;
        }
        using DarkNinjaBodyMotionLease? motion = DarkNinjaBodyMotionLease.TryAcquire(attacker);
        if (motion == null)
        {
            await Cmd.Wait(DarkNinjaCombatMath.DeathSlashWindupSeconds);
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaSlowAttackEvent);
            await Cmd.Wait(
                DarkNinjaCombatMath.DeathSlashTotalSeconds
                - DarkNinjaCombatMath.DeathSlashWindupSeconds);
            await onDamage();
            return;
        }

        Vector2 start = motion.FocusCanvasPosition;
        await PlayTween(
            motion.Owner,
            DarkNinjaCombatMath.DeathSlashWindupSeconds,
            progress =>
            {
                float offset = DarkNinjaCombatMath.SampleDeathSlashWindupOffset(progress);
                motion.SetFocusCanvasPosition(start + Vector2.Right * offset);
            });

        Vector2 launchStart = motion.FocusCanvasPosition;
        Vector2 viewportSize = motion.Owner.GetViewport().GetVisibleRect().Size;
        float leftExitX = -motion.HalfWidth - ExitPadding;
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaSlowAttackEvent);

        await PlayTween(
            motion.Owner,
            DarkNinjaCombatMath.DeathSlashOutboundSeconds,
            progress =>
            {
                float travel = DarkNinjaCombatMath.SampleDeathSlashTravel(progress);
                float x = Mathf.Lerp(launchStart.X, leftExitX, travel);
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
            progress =>
            {
                float travel = DarkNinjaCombatMath.SampleDeathSlashTravel(progress);
                motion.SetFocusCanvasPosition(returnStart.Lerp(start, travel));
            });
        motion.SetFocusCanvasPosition(start);
        await onDamage();
    }

    internal static async Task PlayDarkStrike(
        Creature attacker,
        IReadOnlyList<Creature> targets,
        Func<Creature, bool> canImpact,
        Func<Creature, Task<DarkStrikeImpactOutcome>> onImpact,
        Func<Node2D, Task>? contactContinuation = null,
        int returnSide = 1,
        Func<Creature, bool>? canPenetrate = null)
    {
        Creature[] orderedTargets = OrderTargets(targets);
        if (orderedTargets.Length == 0)
        {
            return;
        }

        if (FinisherApproach.IsActive(attacker))
        {
            await PlayDarkStrikeFallback(attacker, orderedTargets, canImpact, onImpact);
            return;
        }

        using DarkNinjaDetachedVisualLease? visual = DarkNinjaDetachedVisualLease.TryAcquire(attacker);
        if (visual == null)
        {
            await PlayDarkStrikeFallback(
                attacker,
                orderedTargets,
                canImpact,
                onImpact);
            return;
        }

        int highestHealing = 0;
        bool playedSuccessfulVoice = false;
        bool playedBlockedVoice = false;
        bool attemptedImpact = false;
        int presentationIndex = 0;
        for (int index = 0; index < orderedTargets.Length; index++)
        {
            Creature target = orderedTargets[index];
            if (!CanContinue(attacker))
            {
                return;
            }

            if (!canImpact(target))
            {
                continue;
            }

            DarkNinjaStabSegment segment = DarkNinjaCombatMath.GetDarkStrikeSegment(
                presentationIndex++);
            visual.PrepareTarget(
                target,
                DarkNinjaCombatMath.SampleDarkStrikeVisualReference(
                    segment,
                    0f),
                penetratesTarget: canPenetrate?.Invoke(target) == true);
            (NCombatRoom Room, Vector2 Position, int FrontZ)? impactVfx = null;
            Task<DarkStrikeImpactOutcome>? impactTask = null;
            void TriggerImpactOnFinalMotionFrame()
            {
                if (impactTask != null || !canImpact(target))
                {
                    return;
                }

                float referenceSeconds = DarkNinjaCombatMath.SampleDarkStrikeVisualReference(
                    segment,
                    1f);
                visual.ApplyReferencePose(
                    target,
                    referenceSeconds);
                impactVfx = CaptureImpactVfx(target);
                impactTask = DarkStrikeHurtPoseFreezeContext.Run(
                    target,
                    visual.FreezeTargetHurtPose,
                    () => onImpact(target));
            }

            await PlayTween(
                visual.Owner,
                segment.MotionSeconds,
                progress =>
                {
                    float referenceSeconds = DarkNinjaCombatMath.SampleDarkStrikeVisualReference(
                        segment,
                        progress);
                    visual.ApplyReferencePose(target, referenceSeconds);
                },
                TriggerImpactOnFinalMotionFrame);

            TriggerImpactOnFinalMotionFrame();
            if (impactTask == null)
            {
                if (!CanContinue(attacker))
                {
                    return;
                }

                continue;
            }

            attemptedImpact = true;
            DarkStrikeImpactOutcome outcome = await impactTask;
            highestHealing = Math.Max(highestHealing, outcome.Healing);
            visual.SetPenetration(
                outcome.Connected && !outcome.FullyBlocked,
                DarkNinjaCombatMath.SampleDarkStrikeVisualReference(segment, 1f));

            PlayImpactFeedback(
                outcome,
                impactVfx,
                ref playedSuccessfulVoice,
                ref playedBlockedVoice);
            if (!outcome.ShouldContinue)
            {
                return;
            }

            bool hasLaterTarget = orderedTargets
                .Skip(index + 1)
                .Any(canImpact);
            await Cmd.Wait(DarkNinjaCombatMath.ResolveDarkStrikeHoldSeconds(
                segment,
                outcome.Connected && !outcome.FullyBlocked,
                hasLaterTarget));
            if (!hasLaterTarget)
            {
                break;
            }
        }

        if (!attemptedImpact || !CanContinue(attacker))
        {
            return;
        }

        if (highestHealing > 0)
        {
            await CreatureCmd.Heal(attacker, highestHealing);
        }

        if (!CanContinue(attacker))
        {
            return;
        }

        visual.ShowFullBody();
        if (contactContinuation != null)
            await contactContinuation(visual.Root);
        else
            await visual.PlayReturn(returnSide);
    }

    private static async Task PlayDarkStrikeFallback(
        Creature attacker,
        Creature[] targets,
        Func<Creature, bool> canImpact,
        Func<Creature, Task<DarkStrikeImpactOutcome>> onImpact)
    {
        int highestHealing = 0;
        bool playedSuccessfulVoice = false;
        bool playedBlockedVoice = false;
        bool attemptedImpact = false;
        int presentationIndex = 0;
        for (int index = 0; index < targets.Length; index++)
        {
            if (!CanContinue(attacker))
            {
                return;
            }

            Creature target = targets[index];
            if (!canImpact(target))
            {
                continue;
            }

            DarkNinjaStabSegment segment = DarkNinjaCombatMath.GetDarkStrikeSegment(
                presentationIndex++);
            await Cmd.Wait(segment.MotionSeconds);
            if (!canImpact(target))
            {
                continue;
            }

            (NCombatRoom Room, Vector2 Position, int FrontZ)? impactVfx = CaptureImpactVfx(target);
            using var hurtHold = DarkStrikeHurtPoseFreezeLease.TryAcquire(target);
            DarkStrikeImpactOutcome outcome = await DarkStrikeHurtPoseFreezeContext.Run(
                target, _ => hurtHold?.Capture() == true, () => onImpact(target));
            attemptedImpact = true;
            highestHealing = Math.Max(highestHealing, outcome.Healing);
            PlayImpactFeedback(
                outcome,
                impactVfx,
                ref playedSuccessfulVoice,
                ref playedBlockedVoice);
            if (!outcome.ShouldContinue)
            {
                return;
            }

            bool hasLaterTarget = targets
                .Skip(index + 1)
                .Any(canImpact);
            await Cmd.Wait(DarkNinjaCombatMath.ResolveDarkStrikeHoldSeconds(
                segment,
                outcome.Connected && !outcome.FullyBlocked,
                hasLaterTarget));
            if (!hasLaterTarget)
            {
                break;
            }
        }

        if (!attemptedImpact || !CanContinue(attacker))
        {
            return;
        }

        if (highestHealing > 0)
        {
            await CreatureCmd.Heal(attacker, highestHealing);
        }

        if (CanContinue(attacker))
        {
            await Cmd.Wait(DarkNinjaCombatMath.DarkStrikeReturnSeconds);
        }
    }

    private static bool CanContinue(Creature attacker) =>
        attacker.IsAlive
        && attacker.CombatState is { } combatState
        && combatState.ContainsCreature(attacker)
        && combatState.IsLiveCombat()
        && !CombatManager.Instance.IsOverOrEnding;

    private static (NCombatRoom Room, Vector2 Position, int FrontZ)? CaptureImpactVfx(Creature target)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null || !GodotObject.IsInstanceValid(room))
        {
            return null;
        }

        NCreature? targetNode = room.GetCreatureNode(target);
        if (targetNode == null || !GodotObject.IsInstanceValid(targetNode)) return null;
        Node2D body = NinjaSlayerVisualRig.GetAirborneAnchor(targetNode.Visuals)
            ?? targetNode.Visuals.GetCurrentBody();
        int frontZ = ResolveEffectiveZ(body);
        void Visit(Node node)
        {
            if (node is CanvasItem item && item.IsVisibleInTree())
                frontZ = Math.Max(frontZ, ResolveEffectiveZ(item));
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        Visit(body);
        return (room, targetNode.VfxSpawnPosition, Math.Min(MaximumCanvasZIndex, frontZ + 1));
    }

    private static void PlayImpactFeedback(
        DarkStrikeImpactOutcome outcome,
        (NCombatRoom Room, Vector2 Position, int FrontZ)? impactVfx,
        ref bool playedSuccessfulVoice,
        ref bool playedBlockedVoice)
    {
        if (!outcome.Connected)
        {
            return;
        }

        if (outcome.FullyBlocked)
        {
            if (!playedBlockedVoice)
            {
                playedBlockedVoice = true;
                NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaFailedEvent);
            }

            return;
        }

        if (impactVfx is { } vfx
            && ReferenceEquals(NCombatRoom.Instance, vfx.Room)
            && GodotObject.IsInstanceValid(vfx.Room)
            && GodotObject.IsInstanceValid(vfx.Room.CombatVfxContainer))
        {
            var effect = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath(VfxCmd.dramaticStabPath))
                .Instantiate<Node2D>();
            effect.Name = "DarkNinjaStabImpact";
            effect.ZAsRelative = false;
            effect.ZIndex = vfx.FrontZ;
            Node slash = effect.GetNode("slash");
            effect.RemoveChild(slash);
            slash.Free();
            effect.Position = vfx.Room.CombatVfxContainer.GetGlobalTransform().AffineInverse() * vfx.Position;
            vfx.Room.CombatVfxContainer.AddChildSafely(effect);
        }

        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaStabEvent);
        if (!playedSuccessfulVoice)
        {
            playedSuccessfulVoice = true;
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.DarkNinjaKirisuteGomenEvent);
        }
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
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null || !GodotObject.IsInstanceValid(room))
        {
            return float.MaxValue;
        }

        NCreature? targetNode = room.GetCreatureNode(target);
        return targetNode != null && GodotObject.IsInstanceValid(targetNode)
            ? targetNode.Visuals.Bounds.GetGlobalRect().GetCenter().X
            : float.MaxValue;
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
        private int _disposed;

        private DarkNinjaBodyMotionLease(
            NCreature owner,
            Node2D anchor,
            Node2D focus,
            CanvasItem anchorParent)
        {
            Owner = owner;
            _anchor = anchor;
            _focus = focus;
            _anchorParent = anchorParent;
            _anchorPosition = anchor.Position;
            FocusCanvasPosition = focus.GetGlobalTransformWithCanvas().Origin;
            HalfWidth = owner.Visuals.Bounds.GetGlobalRect().Size.X * 0.5f;
        }

        internal NCreature Owner { get; }
        internal Vector2 FocusCanvasPosition { get; private set; }
        internal float HalfWidth { get; }

        internal static DarkNinjaBodyMotionLease? TryAcquire(Creature creature)
        {
            NCombatRoom? room = NCombatRoom.Instance;
            if (room == null || !GodotObject.IsInstanceValid(room))
            {
                return null;
            }

            NCreature? owner = room.GetCreatureNode(creature);
            if (owner == null || !GodotObject.IsInstanceValid(owner))
            {
                return null;
            }

            Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(owner.Visuals)
                ?? owner.Visuals.GetCurrentBody();
            Node2D? focus = NinjaSlayerVisualRig.GetCinematicFocus(owner.Visuals)
                ?? owner.Visuals.GetCurrentBody();
            if (anchor == null
                || focus == null
                || !GodotObject.IsInstanceValid(anchor)
                || !GodotObject.IsInstanceValid(focus)
                || anchor.GetParent() is not CanvasItem parent)
            {
                return null;
            }

            return new DarkNinjaBodyMotionLease(
                owner,
                anchor,
                focus,
                parent);
        }

        internal void SetFocusCanvasPosition(Vector2 desiredCanvasPosition)
        {
            if (Volatile.Read(ref _disposed) != 0
                || !GodotObject.IsInstanceValid(_anchor)
                || !GodotObject.IsInstanceValid(_focus)
                || !GodotObject.IsInstanceValid(_anchorParent))
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

        }
    }

    private sealed class DarkNinjaDetachedVisualLease : IDisposable
    {
        private readonly CanvasItem _sceneContainer;
        private readonly Creature _attacker;
        private readonly Sprite2D _sourceBody;
        private readonly bool _sourceBodyVisible;
        private readonly Sprite2D? _shadow;
        private readonly bool _shadowVisible;
        private readonly Node2D _root;
        private readonly Sprite2D _character;
        private readonly Sprite2D _rearSword;
        private readonly Sprite2D _frontSword;
        private readonly Sprite2D _fullBody;
        private readonly DarkNinjaStolenCards? _stolenCards;
        private EntangledSpinMotionBlur? _returnBlur;
        private VerticalAxisSpinProjection? _returnProjection;
        private readonly Vector2 _baselinePosition;
        private readonly float _scaleX;
        private readonly float _scaleY;
        private readonly List<(CanvasItem Item, int Z, bool Relative)> _raisedTargetLayers = [];
        private float _strikeDirection;
        private DarkStrikeHurtPoseFreezeLease? _targetPoseFreeze;
        private bool _penetratesTarget;
        private int _disposed;

        private DarkNinjaDetachedVisualLease(
            Creature attacker,
            NCombatRoom room,
            NCreature owner,
            CanvasItem sceneContainer,
            Sprite2D sourceBody,
            Sprite2D? shadow,
            Node2D root,
            Sprite2D character,
            Sprite2D rearSword,
            Sprite2D frontSword,
            Sprite2D fullBody,
            Vector2 baselinePosition,
            float scaleX,
            float scaleY)
        {
            Room = room;
            Owner = owner;
            _attacker = attacker;
            _sceneContainer = sceneContainer;
            _sourceBody = sourceBody;
            _sourceBodyVisible = sourceBody.Visible;
            _shadow = shadow;
            _shadowVisible = shadow?.Visible ?? false;
            _root = root;
            _character = character;
            _rearSword = rearSword;
            _frontSword = frontSword;
            _fullBody = fullBody;
            _stolenCards = DarkNinjaStolenCards.Get(attacker);
            _baselinePosition = baselinePosition;
            _scaleX = scaleX;
            _scaleY = scaleY;
            try
            {
                sourceBody.Visible = false;
                _stolenCards?.ShowOn(character);
                if (shadow != null)
                {
                    shadow.Visible = false;
                }
            }
            catch
            {
                if (GodotObject.IsInstanceValid(sourceBody))
                {
                    _stolenCards?.ShowOn(sourceBody);
                    sourceBody.Visible = _sourceBodyVisible;
                }

                if (shadow != null && GodotObject.IsInstanceValid(shadow))
                {
                    shadow.Visible = _shadowVisible;
                }

                throw;
            }
        }

        internal NCombatRoom Room { get; }
        internal NCreature Owner { get; }
        internal Node2D Root => _root;

        internal static DarkNinjaDetachedVisualLease? TryAcquire(Creature creature)
        {
            Node2D? root = null;
            try
            {
                NCombatRoom? room = NCombatRoom.Instance;
                if (room == null
                    || !GodotObject.IsInstanceValid(room))
                {
                    return null;
                }

                NCreature? owner = room.GetCreatureNode(creature);
                if (owner == null || !GodotObject.IsInstanceValid(owner))
                {
                    return null;
                }

                Sprite2D? sourceBody = NinjaSlayerVisualRig.GetBodySprite(owner.Visuals);
                if (sourceBody == null || !GodotObject.IsInstanceValid(sourceBody))
                {
                    return null;
                }

                Texture2D characterTexture = PreloadManager.Cache.GetTexture2D(CharacterTexturePath);
                Texture2D swordTexture = PreloadManager.Cache.GetTexture2D(SwordTexturePath);
                Texture2D fullTexture = PreloadManager.Cache.GetTexture2D(
                    DarkNinjaMonster.CombatTexturePath);
                CanvasItem sceneContainer = room.SceneContainer;
                if (!GodotObject.IsInstanceValid(sceneContainer))
                {
                    return null;
                }

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
                if (!GodotObject.IsInstanceValid(targetBody))
                {
                    return null;
                }

                int zIndex = DarkNinjaCombatMath.ResolveDarkStrikeAttackerZIndex(
                    ResolveEffectiveZ(targetBody));
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
                var rearSword = new Sprite2D
                {
                    Name = "RearSword",
                    Texture = swordTexture,
                    ZIndex = 0,
                    Centered = false,
                    RegionEnabled = true,
                    RegionFilterClipEnabled = true,
                    Position = new Vector2(-TextureWidth * 0.5f, -TextureHeight * 0.5f),
                    RegionRect = new Rect2(0f, 0f, TextureWidth, TextureHeight)
                };
                var frontSword = new Sprite2D
                {
                    Name = "FrontSword",
                    Texture = swordTexture,
                    ZIndex = 2,
                    Centered = false,
                    RegionEnabled = true,
                    RegionFilterClipEnabled = true,
                    Position = new Vector2(-TextureWidth * 0.5f, -TextureHeight * 0.5f),
                    RegionRect = new Rect2(0f, 0f, 0f, TextureHeight),
                    Visible = false
                };
                var fullBody = new Sprite2D
                {
                    Name = "FullBody",
                    Texture = fullTexture,
                    ZIndex = 2,
                    Visible = false
                };
                root.AddChildSafely(character);
                root.AddChildSafely(rearSword);
                root.AddChildSafely(frontSword);
                root.AddChildSafely(fullBody);
                return new DarkNinjaDetachedVisualLease(
                    creature,
                    room,
                    owner,
                    sceneContainer,
                    sourceBody,
                    NinjaSlayerVisualRig.GetShadow(owner.Visuals),
                    root,
                    character,
                    rearSword,
                    frontSword,
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

        internal void PrepareTarget(
            Creature target,
            float referenceSeconds,
            bool penetratesTarget)
        {
            DarkStrikeHurtPoseFreezeLease? previousFreeze = _targetPoseFreeze;
            _targetPoseFreeze = null;
            try
            {
                RestoreTargetLayer();
                if (Volatile.Read(ref _disposed) != 0
                    || !GodotObject.IsInstanceValid(_root)
                    || !GodotObject.IsInstanceValid(Room))
                {
                    return;
                }

                NCreature? targetNode = Room.GetCreatureNode(target);
                if (targetNode != null && GodotObject.IsInstanceValid(targetNode))
                {
                    Node2D targetBody = NinjaSlayerVisualRig.GetAirborneAnchor(targetNode.Visuals)
                        ?? targetNode.Visuals.GetCurrentBody();
                    if (GodotObject.IsInstanceValid(targetBody))
                    {
                        RaiseTargetLayers(targetBody);
                    }
                    _strikeDirection = target.Side == CombatSide.Enemy
                        || NinjaSlayerFacingState.ResolveFacingLeft(targetNode) ? -1f : 1f;
                }

                _penetratesTarget = penetratesTarget;
                ApplyReferencePose(target, referenceSeconds);
                _root.Visible = true;
            }
            finally
            {
                previousFreeze?.Dispose();
            }
        }

        internal void SetPenetration(bool penetratesTarget, float referenceSeconds)
        {
            if (Volatile.Read(ref _disposed) != 0
                || !GodotObject.IsInstanceValid(_root))
            {
                return;
            }

            _penetratesTarget = penetratesTarget;
            ApplySwordDepth(referenceSeconds);
        }

        internal bool FreezeTargetHurtPose(Creature target)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            _targetPoseFreeze?.Dispose();
            _targetPoseFreeze = DarkStrikeHurtPoseFreezeLease.TryAcquire(target);
            return _targetPoseFreeze?.Capture() == true;
        }

        internal void ApplyReferencePose(Creature target, float referenceSeconds)
        {
            if (Volatile.Read(ref _disposed) != 0
                || !GodotObject.IsInstanceValid(_root)
                || !GodotObject.IsInstanceValid(_sceneContainer))
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
            float direction = _strikeDirection;
            _root.Scale = new Vector2(-_scaleX * direction, _scaleY);
            Vector2 finalPosition = targetScenePosition - new Vector2(
                contactLocal.X * -_scaleX * direction,
                contactLocal.Y * _scaleY);
            DarkNinjaPoint offset = DarkNinjaCombatMath.SampleDarkStrikeOffset(referenceSeconds);
            _root.Position = finalPosition + new Vector2(
                offset.X * _scaleX * direction,
                offset.Y * _scaleY);
            ApplySwordDepth(referenceSeconds);
        }

        internal void ShowFullBody()
        {
            try
            {
                RestoreTargetLayer();
                if (Volatile.Read(ref _disposed) != 0
                    || !GodotObject.IsInstanceValid(_root))
                {
                    return;
                }

                _character.Visible = false;
                _rearSword.Visible = false;
                _frontSword.Visible = false;
                _fullBody.Visible = true;
                _stolenCards?.ShowOn(_fullBody);
            }
            finally
            {
                _targetPoseFreeze?.Dispose();
                _targetPoseFreeze = null;
            }
        }

        private void RaiseTargetLayers(CanvasItem targetBody)
        {
            var items = new List<CanvasItem>();
            void Visit(Node node)
            {
                if (node is CanvasItem item) items.Add(item);
                foreach (Node child in node.GetChildren()) Visit(child);
            }
            Visit(targetBody);
            int minimum = items.Min(ResolveEffectiveZ);
            int maximum = items.Max(ResolveEffectiveZ);
            int attackerZ = DarkNinjaCombatMath.ResolveDarkStrikeAttackerZIndex(maximum);
            int delta = attackerZ + 1 - minimum;
            var roots = items.Where(item => item == targetBody || !item.ZAsRelative)
                .Select(item => (Item: item, Z: ResolveEffectiveZ(item))).ToArray();
            foreach (var (item, z) in roots)
            {
                _raisedTargetLayers.Add((item, item.ZIndex, item.ZAsRelative));
                item.ZAsRelative = false;
                item.ZIndex = Math.Min(MaximumCanvasZIndex - 1, z + delta);
            }
            _root.ZIndex = attackerZ;
            _frontSword.ZAsRelative = false;
            _frontSword.ZIndex = Math.Min(MaximumCanvasZIndex, maximum + delta + 1);
        }

        private void RestoreTargetLayer()
        {
            foreach (var (item, z, relative) in _raisedTargetLayers)
            {
                if (!GodotObject.IsInstanceValid(item)) continue;
                item.ZIndex = z;
                item.ZAsRelative = relative;
            }
            _raisedTargetLayers.Clear();
        }

        private void ApplySwordDepth(float referenceSeconds)
        {
            if (!GodotObject.IsInstanceValid(_rearSword)
                || !GodotObject.IsInstanceValid(_frontSword))
            {
                return;
            }

            float cutX = DarkNinjaCombatMath.ResolveDarkStrikeForegroundBladeCutTextureX(
                referenceSeconds,
                _penetratesTarget);
            float rearWidth = TextureWidth - cutX;
            _rearSword.Position = new Vector2(
                -TextureWidth * 0.5f + cutX,
                -TextureHeight * 0.5f);
            _rearSword.RegionRect = new Rect2(
                cutX,
                0f,
                rearWidth,
                TextureHeight);
            _rearSword.Visible = rearWidth > 0f;

            _frontSword.Position = new Vector2(
                -TextureWidth * 0.5f,
                -TextureHeight * 0.5f);
            _frontSword.RegionRect = new Rect2(
                0f,
                0f,
                cutX,
                TextureHeight);
            _frontSword.Visible = cutX > 0f;
        }

        internal async Task PlayReturn(int returnSide)
        {
            if (Volatile.Read(ref _disposed) != 0
                || !GodotObject.IsInstanceValid(_root)
                || !GodotObject.IsInstanceValid(_sceneContainer)
                || !GodotObject.IsInstanceValid(Owner))
            {
                return;
            }

            Transform2D canvasToScene = _sceneContainer.GetGlobalTransformWithCanvas().AffineInverse();
            Vector2 viewportSize = Owner.GetViewport().GetVisibleRect().Size;
            Vector2 leftCanvas = canvasToScene * Vector2.Zero;
            Vector2 rightCanvas = canvasToScene * new Vector2(viewportSize.X, 0f);
            float canvasScaleY = _sceneContainer.GetGlobalTransformWithCanvas().Y.Length();
            float arcHeight = ReturnArcCanvasHeight / Math.Max(canvasScaleY, 0.001f);
            float rightExtent = TextureWidth * _scaleX * 0.5f;
            var start = new DarkNinjaPoint(
                returnSide > 0 ? DarkNinjaCombatMath.ResolveDarkStrikeRightReturnStartX(
                    leftCanvas.X,
                    rightCanvas.X,
                    rightExtent) : leftCanvas.X - rightExtent,
                _baselinePosition.Y);
            var end = new DarkNinjaPoint(_baselinePosition.X, _baselinePosition.Y);
            _root.Position = new Vector2(start.X, start.Y);
            Vector2 axis = _fullBody.GetGlobalTransformWithCanvas().Origin;
            _returnProjection = VerticalAxisSpinProjection.CaptureCurrent(_fullBody, axis.X, axis);
            _returnBlur = EntangledSpinMotionBlur.Create(_fullBody, _fullBody.GetRect());

            await PlayTween(
                Owner,
                DarkNinjaCombatMath.DarkStrikeReturnSeconds,
                progress =>
                {
                    DarkNinjaPoint point = DarkNinjaCombatMath.SampleReturnParabola(
                        start,
                        end,
                        arcHeight,
                        progress);
                    if (GodotObject.IsInstanceValid(_root))
                    {
                        _root.Position = new Vector2(point.X, point.Y);
                        float elapsed = progress * DarkNinjaCombatMath.DarkStrikeReturnSeconds;
                        float turnElapsed = elapsed - (DarkNinjaCombatMath.DarkStrikeReturnSeconds - DragPoseMath.TurnSeconds);
                        float degrees = DragPoseMath.TurnAngle(0f, 180f, turnElapsed, DragPoseMath.TurnSeconds);
                        double Before(double age) => DragPoseMath.TurnAngle(0f, 180f,
                            turnElapsed - (float)age, DragPoseMath.TurnSeconds);
                        _returnProjection.ApplyDegrees(degrees, Before);
                        _returnBlur.Record(_returnProjection, degrees, Before);
                    }
                });
            if (GodotObject.IsInstanceValid(_root))
            {
                _root.Position = _baselinePosition;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (GodotObject.IsInstanceValid(_root))
            {
                _root.Visible = false;
            }

            _returnBlur?.Stop();
            _returnProjection?.Restore();
            if (GodotObject.IsInstanceValid(_sourceBody) && GodotObject.IsInstanceValid(_stolenCards))
                _stolenCards!.ShowOn(_sourceBody);
            RestoreTargetLayer();
            _targetPoseFreeze?.Dispose();
            _targetPoseFreeze = null;

            if (_attacker.IsAlive && GodotObject.IsInstanceValid(_sourceBody))
            {
                _sourceBody.Visible = _sourceBodyVisible;
            }

            if (_attacker.IsAlive
                && _shadow != null
                && GodotObject.IsInstanceValid(_shadow))
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
            if (!GodotObject.IsInstanceValid(Room))
            {
                return _sceneContainer.GetGlobalTransformWithCanvas() * _root.Position;
            }

            NCreature? targetNode = Room.GetCreatureNode(target);
            if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
            {
                return _sceneContainer.GetGlobalTransformWithCanvas() * _root.Position;
            }

            Marker2D marker = targetNode.Visuals.VfxSpawnPosition;
            return GodotObject.IsInstanceValid(marker)
                ? marker.GetGlobalTransformWithCanvas().Origin
                : targetNode.Visuals.Bounds.GetGlobalRect().GetCenter();
        }
    }

    private sealed class DarkStrikeHurtPoseFreezeLease : IDisposable
    {
        private readonly Creature _target;
        private readonly NCreature _targetNode;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Creature, DarkStrikeHurtPoseFreezeLease> Pending = new();
        private bool _captured;
        private int _disposed;

        private DarkStrikeHurtPoseFreezeLease(
            Creature target,
            NCreature targetNode)
        {
            _target = target;
            _targetNode = targetNode;
        }

        internal static DarkStrikeHurtPoseFreezeLease? TryAcquire(Creature target)
        {
            NCreature? targetNode = NCombatRoom.Instance?.GetCreatureNode(target);
            if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
            {
                return null;
            }

            return new DarkStrikeHurtPoseFreezeLease(target, targetNode);
        }

        internal bool Capture()
        {
            Cancel(_target);
            _captured = true;
            Pending.Add(_target, this);
            return true;
        }

        internal static void Cancel(Creature target)
        {
            if (Pending.TryGetValue(target, out var hold)) hold._captured = false;
            Pending.Remove(target);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            bool replay = _captured;
            if (Pending.TryGetValue(_target, out var pending) && ReferenceEquals(pending, this))
                Pending.Remove(_target);
            if (replay
                && _target.IsAlive
                && _target.CombatState is { } combatState
                && combatState.ContainsCreature(_target)
                && combatState.IsLiveCombat()
                && GodotObject.IsInstanceValid(_targetNode)
                && _targetNode.IsInsideTree())
            {
                _ = CreatureCmd.TriggerAnim(_target, "Hit", 0f);
            }
        }
    }

    internal static void CancelDeferredHurt(Creature target) => DarkStrikeHurtPoseFreezeLease.Cancel(target);

    private static int ResolveEffectiveZ(CanvasItem item)
    {
        int zIndex = item.ZIndex;
        CanvasItem current = item;
        while (current.ZAsRelative && current.GetParent() is CanvasItem parent)
        {
            zIndex = Math.Clamp(
                zIndex + parent.ZIndex,
                -MaximumCanvasZIndex,
                MaximumCanvasZIndex);
            current = parent;
        }

        return zIndex;
    }

}

internal static class DarkStrikeHurtPoseFreezeContext
{
    private static readonly AsyncLocal<Frame?> Current = new();

    internal static Task<T> Run<T>(
        Creature target,
        Func<Creature, bool> freezeTarget,
        Func<Task<T>> action)
    {
        var frame = new Frame(Current.Value, target, freezeTarget);
        Current.Value = frame;
        Task<T> task;
        try
        {
            task = action();
        }
        catch
        {
            frame.IsActive = false;
            throw;
        }
        finally
        {
            if (ReferenceEquals(Current.Value, frame))
            {
                Current.Value = frame.Previous;
            }
        }

        return Complete(task, frame);
    }

    internal static bool TryDeferHit(Creature creature)
    {
        for (Frame? frame = Current.Value; frame != null; frame = frame.Previous)
        {
            if (frame.IsActive && ReferenceEquals(frame.Target, creature))
            {
                return TryCaptureHurtResponse(frame, creature);
            }
        }

        return false;
    }

    private static bool TryCaptureHurtResponse(Frame frame, Creature creature)
    {
        if (frame.FreezeTriggered)
        {
            return frame.HurtResponseCaptured;
        }

        frame.FreezeTriggered = true;
        try
        {
            frame.HurtResponseCaptured = frame.FreezeTarget(creature);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Dark Strike hurt response could not be deferred: {exception.Message}");
        }

        return frame.HurtResponseCaptured;
    }

    private static async Task<T> Complete<T>(Task<T> task, Frame frame)
    {
        try
        {
            return await task;
        }
        finally
        {
            frame.IsActive = false;
        }
    }

    private sealed class Frame(
        Frame? previous,
        Creature target,
        Func<Creature, bool> freezeTarget)
    {
        internal Frame? Previous { get; } = previous;
        internal Creature Target { get; } = target;
        internal Func<Creature, bool> FreezeTarget { get; } = freezeTarget;
        internal bool FreezeTriggered { get; set; }
        internal bool HurtResponseCaptured { get; set; }
        internal bool IsActive { get; set; } = true;
    }
}
