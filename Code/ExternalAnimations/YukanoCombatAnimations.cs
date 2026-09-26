using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class YukanoCombatAnimations
{
    public const string ArrowAtlasPath =
        "res://animations/monsters/crossbow_ruby_raider/crossbow_ruby_raider.png";

    private const float ArrowProjectileSeconds = 0.25f;
    private const float ArrowSourceAngle = 3f * Mathf.Pi / 4f;
    private static readonly Rect2 ArrowRegion = new(226f, 101f, 48f, 38f);
    private static Texture2D? _closedTexture;
    private static Texture2D? _openTexture;
    private static Texture2D? _shurikenTexture;
    private static AtlasTexture? _arrowTexture;
    private static bool _arrowFailureLogged;

    public static void SetSpeaking(Creature creature, bool speaking)
    {
        Sprite2D? body = NinjaSlayerVisualRig.GetBodySprite(creature.GetCreatureNode()?.Visuals);
        if (body == null)
        {
            return;
        }

        Texture2D? texture = speaking
            ? _openTexture ??= ResourceLoader.Load<Texture2D>(YukanoMonster.OpenTexturePath)
            : _closedTexture ??= ResourceLoader.Load<Texture2D>(YukanoMonster.ClosedTexturePath);
        if (texture != null)
        {
            body.Texture = texture;
        }
    }

    public static Task<bool> PlayArrow(Creature source, Creature target) =>
        PlayRangedAttack(
            source,
            target,
            ResolveArrowTexture(),
            1.5f,
            spin: false,
            duration: CombatActionTimingRuntime.VisualSeconds(ArrowProjectileSeconds));

    public static Task<bool> PlayShuriken(Creature source, Creature target) =>
        PlayRangedAttack(
            source,
            target,
            _shurikenTexture ??= ResourceLoader.Load<Texture2D>(YukanoMonster.ShurikenTexturePath),
            0.45f,
            spin: true,
            duration: SlowAttackAnimation.CompanionPeakSeconds);

    private static async Task<bool> PlayRangedAttack(
        Creature source, Creature target, Texture2D? texture, float scale, bool spin, float duration)
    {
        NCreature? node = source.GetCreatureNode();
        Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(node?.Visuals);
        if (node == null || anchor == null || duration <= 0f)
        {
            return await PlayProjectile(source, target, texture, scale, spin, duration);
        }

        Sprite2D body = NinjaSlayerVisualRig.GetBodySprite(node.Visuals)!;
        Node2D center = node.Visuals.VfxSpawnPosition;
        Transform2D baseline = default;
        Vector2 core = default;
        Tween? tween = null;
        YukanoArrowPopup? popup = null;
        bool active = true;
        bool completed = false;
        void Restore()
        {
            if (!active) return;
            active = false;
            if (!completed && GodotObject.IsInstanceValid(popup)) popup!.Cancel();
            if (tween?.IsValid() == true) tween.Kill();
            if (GodotObject.IsInstanceValid(anchor) && GodotObject.IsInstanceValid(center))
                StaggerAnimation.ApplyAttackPose(source, anchor, center, baseline, core);
        }
        long generation = NinjaSlayerRapidAnimationCoordinator.RegisterReturnTail(source, null, Restore);
        (baseline, core) = StaggerAnimation.CaptureAttackPose(source, anchor, center);
        Vector2 direction = target.GetCreatureNode() is { } victim
            ? anchor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse()
                .BasisXform(victim.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin
                    - center.GetGlobalTransformWithCanvas().Origin).Normalized()
            : Vector2.Right;
        float facing = direction.X < 0f ? -1f : 1f;
        float motionSeconds = CombatActionTimingRuntime.VisualSeconds(spin ? ShurikenThrowMotion.DurationSeconds : .25f);
        float release = spin ? ShurikenThrowMotion.ReleaseProgress : .4f;
        Vector2[] contour = ShadowBodyGeometry.Resolve("Yukano", false, false, false);
        var offsets = new System.Numerics.Vector2[contour.Length];
        Task<bool> flight = Task.FromResult(false);
        void Apply(float progress)
        {
            if (!active || source.IsDead) return;
            (float degrees, float distance) = spin ? ShurikenThrowMotion.Sample(progress) : ArrowPose(progress);
            float angle = Mathf.DegToRad(degrees) * facing;
            for (int i = 0; i < contour.Length; i++)
            {
                Vector2 pixel = (contour[i] - Vector2.One * .5f) * body.Texture.GetSize();
                if (body.FlipH) pixel.X = -pixel.X;
                if (body.FlipV) pixel.Y = -pixel.Y;
                Vector2 offset = baseline * body.Transform * (pixel + body.Offset) - core;
                offsets[i] = new(offset.X, offset.Y);
            }
            Vector2 posedCore = core + direction * distance;
            posedCore.Y = Math.Min(posedCore.Y, core.Y) + GroundedPoseMath.SupportY(offsets, 0f)
                - GroundedPoseMath.SupportY(offsets, angle);
            var transform = new Transform2D(angle, Vector2.Zero);
            transform.Origin = posedCore - transform.BasisXform(core);
            StaggerAnimation.ApplyAttackPose(source, anchor, center, transform * baseline, posedCore);
        }
        void Release()
        {
            if (!active || !source.IsAlive || !target.IsAlive) return;
            if (spin) NDebugAudioManager.Instance?.Play(TmpSfx.daggerThrow);
            flight = PlayProjectile(source, target, texture, scale, spin, duration, () => active);
        }
        try
        {
            if (!spin) popup = YukanoArrowPopup.TryStart(source, target, Apply, Release);
            YukanoArrowPopup.LaunchResult launch = popup == null
                ? YukanoArrowPopup.LaunchResult.Fallback : await popup.Launch;
            if (!active || launch == YukanoArrowPopup.LaunchResult.Cancelled) return false;
            tween = node.CreateTween();
            if (launch != YukanoArrowPopup.LaunchResult.Released)
            {
                tween.TweenMethod(Callable.From<float>(Apply), 0f, release, motionSeconds * release);
                tween.TweenCallback(Callable.From(Release));
            }
            tween.TweenMethod(Callable.From<float>(Apply), release, 1f, motionSeconds * (1f - release));
            await TweenPlayback.AwaitCompletion(tween, node);
            completed = await flight && active;
            return completed;
        }
        finally
        {
            Restore();
            NinjaSlayerRapidAnimationCoordinator.CompleteVisualTail(source, generation);
        }
    }

    private static (float Degrees, float Distance) ArrowPose(float progress)
    {
        if (progress < .24f)
        {
            float p = Mathf.SmoothStep(0f, 1f, progress / .24f);
            return (-4f * p, -8f * p);
        }
        if (progress < .4f)
        {
            float p = Mathf.SmoothStep(0f, 1f, (progress - .24f) / .16f);
            return (Mathf.Lerp(-4f, 5f, p), Mathf.Lerp(-8f, 10f, p));
        }
        float tail = 1f - Mathf.SmoothStep(0f, 1f, (progress - .4f) / .6f);
        return (5f * tail, 10f * tail);
    }

    private static AtlasTexture? ResolveArrowTexture()
    {
        if (_arrowTexture != null)
        {
            return _arrowTexture;
        }

        Texture2D? atlas = ResourceLoader.Load<Texture2D>(ArrowAtlasPath);
        if (atlas != null)
        {
            _arrowTexture = new AtlasTexture
            {
                Atlas = atlas,
                Region = ArrowRegion
            };
            return _arrowTexture;
        }

        if (!_arrowFailureLogged)
        {
            _arrowFailureLogged = true;
            Entry.Logger.Warn(
                $"Yukano arrow texture could not be loaded from {ArrowAtlasPath}; damage timing will continue without the projectile.");
        }

        return null;
    }

    private static async Task<bool> PlayProjectile(
        Creature source,
        Creature target,
        Texture2D? texture,
        float scale,
        bool spin,
        float duration,
        Func<bool>? stillOwned = null)
    {
        NCreature? sourceNode = source.GetCreatureNode();
        NCreature? targetNode = target.GetCreatureNode();
        NCombatRoom? room = NCombatRoom.Instance;
        if (texture == null
            || sourceNode == null
            || targetNode == null
            || room == null
            || !GodotObject.IsInstanceValid(room))
        {
            await Cmd.Wait(duration);
            return source.IsAlive && target.IsAlive && (stillOwned?.Invoke() ?? true);
        }

        Vector2 start = sourceNode.Visuals.VfxSpawnPosition.GlobalPosition;
        Vector2 end = targetNode.Visuals.VfxSpawnPosition.GlobalPosition;
        Vector2 direction = end - start;
        var projectile = new Sprite2D
        {
            Name = spin ? "YukanoShuriken" : "YukanoArrow",
            Texture = texture,
            Scale = Vector2.One * scale,
            ZIndex = 10,
            Rotation = spin ? 0f : direction.Angle() - ArrowSourceAngle
        };
        room.CombatVfxContainer.AddChild(projectile);
        FinisherRangedAction.For(source)?.Track(projectile);
        projectile.GlobalPosition = start;

        if (Mathf.IsZeroApprox(duration))
        {
            projectile.GlobalPosition = end;
            if (FinisherRangedAction.For(source)?.Retain(projectile) != true) projectile.QueueFree();
            return true;
        }

        try
        {
            bool arrived = false;
            Tween tween = projectile.CreateTween().SetParallel();
            tween.TweenMethod(Callable.From<float>(progress =>
                {
                    if (!(stillOwned?.Invoke() ?? true) || !source.IsAlive || !target.IsAlive)
                    {
                        tween.Kill();
                        return;
                    }
                    projectile.GlobalPosition = start.Lerp(end, progress);
                    arrived = progress >= 1f;
                }), 0f, 1f, duration)
                .SetEase(Tween.EaseType.In)
                .SetTrans(Tween.TransitionType.Quad);
            if (spin)
            {
                tween.TweenProperty(
                    projectile,
                    new NodePath("rotation"),
                    2f * Mathf.Pi,
                    duration);
            }

            await TweenPlayback.AwaitCompletion(tween, projectile);
            return arrived && ReferenceEquals(NCombatRoom.Instance, room);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(projectile))
            {
                if (FinisherRangedAction.For(source)?.Retain(projectile) != true) projectile.QueueFree();
            }
        }
    }
}
