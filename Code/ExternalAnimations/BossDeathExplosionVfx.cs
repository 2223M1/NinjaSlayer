using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class BossDeathExplosionVfx
{
    private const string SpriteSheetPath =
        "res://NinjaSlayer/images/vfx/boss_dismemberment/realistic_explosion.png";
    private const int Columns = 6;
    private const int FrameCount = 16;
    private const int FrameWidth = 72;
    private const int FrameHeight = 101;
    private const int SourceTopPadding = 15;
    private const double FramesPerSecond = 19.2;
    private const float FadeSeconds = 0.25f;
    private const float DuplicateCenterTolerance = 12f;
    private static SpriteFrames? _spriteFrames;

    public static IEnumerable<string> AssetPaths => [SpriteSheetPath];

    public static void PlayBurst(
        NCombatRoom room,
        IEnumerable<Vector2> globalCenters,
        float referenceWidth,
        int zIndex)
    {
        if (!GodotObject.IsInstanceValid(room) || !room.IsInsideTree())
        {
            return;
        }

        List<Vector2> centers = [];
        foreach (Vector2 center in globalCenters)
        {
            if (centers.All(existing => existing.DistanceTo(center) > DuplicateCenterTolerance))
            {
                centers.Add(center);
            }
        }

        if (centers.Count == 0)
        {
            return;
        }

        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerExplosionEvent);
        foreach (Vector2 center in centers)
        {
            PlayVisual(room, center, referenceWidth, zIndex);
        }
    }

    private static void PlayVisual(
        NCombatRoom room,
        Vector2 globalCenter,
        float referenceWidth,
        int zIndex)
    {
        SpriteFrames? frames = GetSpriteFrames();
        if (frames == null)
        {
            return;
        }

        var root = new Node2D
        {
            Name = "NinjaSlayerBossExplosion",
            ZIndex = zIndex + 8
        };
        room.CombatVfxContainer.AddChildSafely(root);
        if (!GodotObject.IsInstanceValid(root) || !root.IsInsideTree())
        {
            return;
        }

        root.GlobalPosition = globalCenter;

        var animation = new AnimatedSprite2D
        {
            Name = "ExplosionAnimation",
            SpriteFrames = frames,
            Centered = true,
            Scale = Vector2.One * (Mathf.Clamp(referenceWidth, 190f, 430f) * 1.55f / FrameWidth)
        };
        root.AddChild(animation);
        animation.Play("default");

        Tween tween = root.CreateTween();
        tween.TweenInterval(FrameCount / FramesPerSecond);
        tween.TweenProperty(animation, "modulate:a", 0f, FadeSeconds);
        tween.TweenCallback(Callable.From(root.QueueFreeSafely));
    }

    private static SpriteFrames? GetSpriteFrames()
    {
        if (_spriteFrames != null)
        {
            return _spriteFrames;
        }

        Texture2D? texture = ResourceLoader.Load<Texture2D>(SpriteSheetPath);
        if (texture == null)
        {
            Entry.Logger.Warn($"Boss explosion sprite sheet is unavailable: {SpriteSheetPath}");
            return null;
        }

        var frames = new SpriteFrames();
        frames.Clear("default");
        frames.SetAnimationSpeed("default", FramesPerSecond);
        frames.SetAnimationLoop("default", false);
        for (int i = 0; i < FrameCount; i++)
        {
            frames.AddFrame("default", new AtlasTexture
            {
                Atlas = texture,
                Region = new Rect2(
                    i % Columns * FrameWidth,
                    SourceTopPadding + i / Columns * FrameHeight,
                    FrameWidth,
                    FrameHeight)
            });
        }

        _spriteFrames = frames;
        return frames;
    }
}
