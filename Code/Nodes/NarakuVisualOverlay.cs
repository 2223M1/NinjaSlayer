using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NarakuVisualOverlay : Sprite2D
{
    private const string NodeName = "NarakuVisualOverlay";
    private const string TexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/naraku.png";

    private Creature? creature;
    private Sprite2D? source;

    public static void Sync(Creature creature)
    {
        var visualsRoot = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        var source = visualsRoot?.GetNodeOrNull<Sprite2D>("%Visuals");
        if (visualsRoot == null || source == null)
        {
            return;
        }

        var overlay = visualsRoot.GetNodeOrNull<NarakuVisualOverlay>(NodeName);
        if (overlay == null)
        {
            overlay = new NarakuVisualOverlay { Name = NodeName };
            visualsRoot.AddChild(overlay);
        }

        overlay.creature = creature;
        overlay.source = source;
        overlay.Texture ??= PreloadManager.Cache.GetTexture2D(TexturePath);
        overlay.Centered = source.Centered;
        overlay.FlipH = source.FlipH;
        overlay.FlipV = source.FlipV;
        overlay.ZIndex = source.ZIndex + 1;
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

        bool isNaraku = creature.HasPower<NarakuPower>();
        source.Visible = !isNaraku;
        Visible = isNaraku;
        if (!isNaraku)
        {
            return;
        }

        Position = source.Position;
        Rotation = source.Rotation;
        Scale = source.Scale;
        Skew = source.Skew;
        Modulate = source.Modulate;
        SelfModulate = source.SelfModulate;
        Offset = source.Offset;
        FlipH = source.FlipH;
        FlipV = source.FlipV;
    }
}
