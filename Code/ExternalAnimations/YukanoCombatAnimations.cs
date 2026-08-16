using Godot;
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

    public static Task PlayArrow(Creature source, Creature target) =>
        PlayProjectile(
            source,
            target,
            ResolveArrowTexture(),
            1.5f,
            spin: false,
            duration: ArrowProjectileSeconds);

    public static Task PlayShuriken(Creature source, Creature target) =>
        PlayProjectile(
            source,
            target,
            _shurikenTexture ??= ResourceLoader.Load<Texture2D>(YukanoMonster.ShurikenTexturePath),
            0.45f,
            spin: true,
            duration: CombatActionTimingRuntime.SlowAttackSeconds);

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

    private static async Task PlayProjectile(
        Creature source,
        Creature target,
        Texture2D? texture,
        float scale,
        bool spin,
        float duration)
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
            return;
        }

        Vector2 start = sourceNode.Visuals.VfxSpawnPosition.GlobalPosition;
        Vector2 end = targetNode.Visuals.VfxSpawnPosition.GlobalPosition;
        Vector2 direction = end - start;
        var projectile = new Sprite2D
        {
            Texture = texture,
            Scale = Vector2.One * scale,
            ZIndex = 10,
            Rotation = spin ? 0f : direction.Angle() - ArrowSourceAngle
        };
        room.CombatVfxContainer.AddChild(projectile);
        projectile.GlobalPosition = start;

        if (Mathf.IsZeroApprox(duration))
        {
            projectile.GlobalPosition = end;
            projectile.QueueFree();
            return;
        }

        try
        {
            Tween tween = projectile.CreateTween().SetParallel();
            tween.TweenProperty(
                    projectile,
                    new NodePath("global_position"),
                    end,
                    duration)
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
        }
        finally
        {
            if (GodotObject.IsInstanceValid(projectile))
            {
                projectile.QueueFree();
            }
        }
    }
}
