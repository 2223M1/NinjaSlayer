using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NarakuVisualOverlay : Sprite2D
{
    private const string NodeName = "NarakuVisualOverlay";
    private const string NarakuTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/naraku.png";
    private const string OneBodyOneSoulTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/one_body_one_soul.png";
    private const float NarakuScale = 0.5f;
    private const float BodyTextureHeight = 1080f;

    private Creature? creature;
    private Sprite2D? source;
    private string? activeTexturePath;

    public static void Sync(Creature creature)
    {
        NinjaSlayerVisualRig.SyncShadowScale(creature);

        var visualsRoot = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        var source = visualsRoot?.GetNodeOrNull<Sprite2D>("%Visuals");
        if (visualsRoot == null || source == null)
        {
            return;
        }

        var parent = source.GetParent() ?? (Node)(NinjaSlayerVisualRig.GetAirborneAnchor(visualsRoot) ?? visualsRoot);
        var overlay = visualsRoot.FindChild(NodeName, recursive: true) as NarakuVisualOverlay;
        if (overlay == null)
        {
            overlay = new NarakuVisualOverlay { Name = NodeName };
            parent.AddChild(overlay);
        }
        else if (overlay.GetParent() != parent)
        {
            overlay.Reparent(parent);
        }

        overlay.creature = creature;
        overlay.source = source;
        overlay.Centered = true;
        overlay.FlipH = source.FlipH;
        overlay.FlipV = source.FlipV;
        overlay.ZIndex = source.ZIndex;
        overlay.ZAsRelative = source.ZAsRelative;
        overlay.ShowBehindParent = source.ShowBehindParent;
        parent.MoveChild(overlay, source.GetIndex() + 1);
        overlay.UpdateVisual();
    }

    public override void _Process(double delta)
    {
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (creature == null || source == null || !GodotObject.IsInstanceValid(source))
        {
            Visible = false;
            return;
        }

        string? formTexturePath = ResolveFormTexturePath(creature);
        source.Visible = formTexturePath == null;
        Visible = formTexturePath != null;
        if (formTexturePath == null)
        {
            return;
        }

        if (activeTexturePath != formTexturePath)
        {
            Texture = PreloadManager.Cache.GetTexture2D(formTexturePath);
            activeTexturePath = formTexturePath;
        }

        float y = NinjaSlayerCombatVisuals.BodySpriteBasePosition.Y;
        if (formTexturePath == NarakuTexturePath)
        {
            y += NinjaSlayerCombatVisuals.NarakuFormYOffset;
        }

        Position = new Vector2(0f, y);
        Offset = Vector2.Zero;
        float scale = GetFormScale(formTexturePath);
        Scale = new Vector2(
            Mathf.Sign(source.Scale.X == 0f ? 1f : source.Scale.X) * scale,
            scale);
        Rotation = 0f;
        Skew = 0f;
        Modulate = source.Modulate;
        SelfModulate = source.SelfModulate;
        FlipH = source.FlipH;
        FlipV = source.FlipV;
    }

    private float GetFormScale(string formTexturePath)
    {
        if (formTexturePath == NarakuTexturePath)
        {
            return NarakuScale;
        }

        float height = Texture?.GetHeight() ?? 0f;
        if (height <= 0f)
        {
            return NinjaSlayerCombatVisuals.BodySpriteBaseScale;
        }

        return BodyTextureHeight * NinjaSlayerCombatVisuals.BodySpriteBaseScale / height;
    }

    private static string? ResolveFormTexturePath(Creature creature)
    {
        if (creature.HasPower<NarakuPower>())
        {
            return NarakuTexturePath;
        }

        if (creature.HasPower<OneBodyOneSoulPower>())
        {
            return OneBodyOneSoulTexturePath;
        }

        return null;
    }
}
