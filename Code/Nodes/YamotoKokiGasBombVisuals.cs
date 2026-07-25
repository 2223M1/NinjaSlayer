using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Nodes;

internal static class YamotoKokiGasBombVisuals
{
    public const string VisualsPath = "res://scenes/creature_visuals/gas_bomb.tscn";

    private const float MissileScale = 0.5f;
    private const float PaperKraneScale = 0.7f;
    private const string StaticSmokeSlotName = "smoke_tex1";
    private const string SetAttachmentMethod = "set_attachment";
    private const string DamageFontPath = "res://themes/kreon_bold_glyph_space_one.tres";
    private const string PaperKraneTexturePath =
        "res://NinjaSlayer/images/relics/YamotoKokiCuteRelic.png";

    public static NCreatureVisuals Create()
    {
        NCreatureVisuals visuals = ModelDb.Monster<GasBomb>().CreateVisuals();
        Node2D body = visuals.GetNode<Node2D>("%Visuals");

        RemoveVanillaVfx(body);
        DisableCanvasItem(body.GetNode<CanvasItem>("SmokeTrailSlot"));
        DisableParticles(body.GetNode<GpuParticles2D>("SmokeBallSlot/PuffParticles"));
        DisableParticles(body.GetNode<GpuParticles2D>("SmokeBallSlot/ExplodePuffParticles"));
        AddPaperKrane(body);

        body.AddChild(new NYamotoKokiGasBombVfx
        {
            Name = nameof(NYamotoKokiGasBombVfx)
        });
        AddDamageAmount(visuals);
        visuals.AddChild(new NYamotoKokiGasBombIdleBob
        {
            Name = nameof(NYamotoKokiGasBombIdleBob)
        });
        visuals.SetScaleAndHue(MissileScale, 0f);
        return visuals;
    }

    public static void RemoveStaticSmokeAttachment(MegaSkeleton skeleton)
    {
        GodotObject nativeSkeleton = skeleton.BoundObject;
        if (!nativeSkeleton.HasMethod(SetAttachmentMethod))
        {
            GD.PushError(
                $"Yamoto Koki missile could not remove Spine slot '{StaticSmokeSlotName}': "
                + $"{SetAttachmentMethod} is unavailable.");
            return;
        }

        using Variant _ = nativeSkeleton.Call(
            SetAttachmentMethod,
            StaticSmokeSlotName,
            string.Empty);
        GC.KeepAlive(skeleton);
    }

    private static void RemoveVanillaVfx(Node2D body)
    {
        Node? vanillaVfx = body.GetNodeOrNull("NGasBombVfx");
        if (vanillaVfx == null)
        {
            return;
        }

        body.RemoveChild(vanillaVfx);
        vanillaVfx.Free();
    }

    private static void DisableCanvasItem(CanvasItem item)
    {
        item.Hide();
        item.ProcessMode = Node.ProcessModeEnum.Disabled;
    }

    private static void DisableParticles(GpuParticles2D particles)
    {
        particles.Emitting = false;
        DisableCanvasItem(particles);
    }

    private static void AddPaperKrane(Node2D body)
    {
        Node2D smokeBallSlot = body.GetNode<Node2D>("SmokeBallSlot");
        Sprite2D paperKrane = new()
        {
            Name = "PaperKraneCore",
            Texture = GD.Load<Texture2D>(PaperKraneTexturePath),
            FlipH = true,
            ShowBehindParent = true,
            ZIndex = -1,
            Scale = Vector2.One * PaperKraneScale
        };
        smokeBallSlot.AddChild(paperKrane);
    }

    private static void AddDamageAmount(NCreatureVisuals visuals)
    {
        Label damageAmount = new()
        {
            Name = "DamageAmount",
            UniqueNameInOwner = true,
            ZIndex = 20,
            OffsetLeft = 38f,
            OffsetTop = -104f,
            OffsetRight = 78f,
            OffsetBottom = -64f,
            Scale = Vector2.One * 2f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = YamotoKokiGasBomb.ExplodeDamage.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        damageAmount.AddThemeColorOverride("font_color", new Color("#fff6e2"));
        damageAmount.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25f));
        damageAmount.AddThemeColorOverride("font_outline_color", new Color(0.2f, 0.2f, 0.2f, 0.9f));
        damageAmount.AddThemeConstantOverride("shadow_offset_x", 3);
        damageAmount.AddThemeConstantOverride("shadow_offset_y", 3);
        damageAmount.AddThemeConstantOverride("outline_size", 12);
        damageAmount.AddThemeFontOverride("font", GD.Load<Font>(DamageFontPath));
        damageAmount.AddThemeFontSizeOverride("font_size", 24);

        visuals.AddChild(damageAmount);
        damageAmount.Owner = visuals;
    }
}
