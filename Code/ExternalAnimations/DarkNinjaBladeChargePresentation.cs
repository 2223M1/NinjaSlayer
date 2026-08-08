using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class DarkNinjaBladeChargePresentation
{
    private const string RootName = "DarkNinjaBladeCharge";
    private const string HeadGlowTexturePath =
        "res://NinjaSlayer/images/vfx/common/common_glow.png";
    private const float TextureWidth = 733f;
    private const float TextureHeight = 649f;
    private const float RevealSeconds = 0.45f;
    private const float HoldSeconds = 0.1f;
    private const float FadeSeconds = 0.2f;
    private const float StartRevealValue = (TextureHeight - 370f) / TextureHeight * 100f;
    private static readonly Color BladeColor = new(246f / 255f, 1f, 21f / 255f);

    internal static IEnumerable<string> AssetPaths =>
        [DarkNinjaMonster.BladeGlowTexturePath, HeadGlowTexturePath];

    internal static async Task Play(Creature creature)
    {
        Node2D? root = null;
        try
        {
            Sprite2D? body = NinjaSlayerVisualRig.GetBodySprite(
                NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals);
            if (body == null || !GodotObject.IsInstanceValid(body))
            {
                return;
            }

            body.GetNodeOrNull<Node>(RootName)?.QueueFreeSafely();
            Texture2D bladeGlow = PreloadManager.Cache.GetTexture2D(
                DarkNinjaMonster.BladeGlowTexturePath);
            Texture2D headGlow = PreloadManager.Cache.GetTexture2D(HeadGlowTexturePath);
            var additiveMaterial = new CanvasItemMaterial
            {
                BlendMode = CanvasItemMaterial.BlendModeEnum.Add
            };
            root = new Node2D
            {
                Name = RootName,
                ZIndex = 1
            };
            body.AddChild(root);

            var blade = new TextureProgressBar
            {
                Position = new Vector2(-TextureWidth * 0.5f, -TextureHeight * 0.5f),
                Size = new Vector2(TextureWidth, TextureHeight),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                MinValue = 0d,
                MaxValue = 100d,
                Value = StartRevealValue,
                FillMode = (int)TextureProgressBar.FillModeEnum.BottomToTop,
                TextureProgress = bladeGlow,
                TintProgress = BladeColor,
                Material = additiveMaterial
            };
            var head = new Sprite2D
            {
                Texture = headGlow,
                Position = ToLocal(DarkNinjaCombatMath.SampleBladeChargePath(0f)),
                Scale = new Vector2(0.5f, 0.5f),
                Material = additiveMaterial
            };
            root.AddChild(blade);
            root.AddChild(head);

            Tween reveal = root.CreateTween();
            reveal.TweenMethod(
                    Callable.From<float>(progress =>
                    {
                        if (!GodotObject.IsInstanceValid(root))
                        {
                            return;
                        }

                        blade.Value = Mathf.Lerp(StartRevealValue, 100f, progress);
                        head.Position = ToLocal(
                            DarkNinjaCombatMath.SampleBladeChargePath(progress));
                    }),
                    0f,
                    1f,
                    RevealSeconds)
                .SetEase(Tween.EaseType.InOut)
                .SetTrans(Tween.TransitionType.Quad);
            await TweenPlayback.AwaitCompletion(reveal, root);
            if (!GodotObject.IsInstanceValid(root) || !root.IsInsideTree())
            {
                return;
            }

            blade.Value = 100d;
            head.Position = ToLocal(DarkNinjaCombatMath.SampleBladeChargePath(1f));
            await Cmd.Wait(HoldSeconds);

            Tween fade = root.CreateTween();
            fade.TweenProperty(root, new NodePath("modulate:a"), 0f, FadeSeconds)
                .SetEase(Tween.EaseType.In)
                .SetTrans(Tween.TransitionType.Quad);
            await TweenPlayback.AwaitCompletion(fade, root);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Dark Ninja blade charge was unavailable: {exception.Message}");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(root))
            {
                root.QueueFreeSafely();
            }
        }
    }

    private static Vector2 ToLocal(DarkNinjaPoint point) =>
        new(point.X - TextureWidth * 0.5f, point.Y - TextureHeight * 0.5f);
}
