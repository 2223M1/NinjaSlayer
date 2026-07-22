using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NarakuVisualOverlay : Sprite2D
{
    private const string NodeName = "NarakuVisualOverlay";

    private Creature? creature;
    private Sprite2D? source;
    private string? activeTexturePath;

    public static void Sync(Creature creature)
    {
        try
        {
            SyncCore(creature);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Failed to synchronize Naraku visual overlay: {ex}");
        }
    }

    private static void SyncCore(Creature creature)
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

        NinjaSlayerFormPresentation presentation = NinjaSlayerFormState.GetPresentation(creature);
        source.Visible = !presentation.UsesOverlay;
        Visible = presentation.UsesOverlay;
        if (!presentation.UsesOverlay)
        {
            return;
        }

        string texturePath = NinjaSlayerFormPresentationCatalog.ResolveBodyTexturePath(
            presentation,
            source.Texture?.ResourcePath)
            ?? throw new InvalidOperationException("Overlay form presentation did not resolve a texture path.");
        if (activeTexturePath != texturePath)
        {
            Texture = PreloadManager.Cache.GetTexture2D(texturePath);
            activeTexturePath = texturePath;
        }

        if (presentation.BodyTransformMode == NinjaSlayerBodyTransformMode.Source)
        {
            CopySourceTransform();
        }
        else
        {
            ApplyLegacyFormTransform(presentation);
        }

        Modulate = source.Modulate;
        SelfModulate = source.SelfModulate;
        Material = source.Material;
        FlipH = source.FlipH;
        FlipV = source.FlipV;
    }

    private void CopySourceTransform()
    {
        Centered = source!.Centered;
        Position = source.Position;
        Offset = source.Offset;
        Scale = source.Scale;
        Rotation = source.Rotation;
        Skew = source.Skew;
    }

    private void ApplyLegacyFormTransform(NinjaSlayerFormPresentation presentation)
    {
        Centered = true;
        Position = new Vector2(0f, NinjaSlayerCombatVisuals.BodySpriteBasePosition.Y + presentation.BodyYOffset);
        Offset = Vector2.Zero;
        float scale = GetLegacyFormScale(presentation);
        float sourceScaleRatio = Mathf.Abs(source!.Scale.Y) > 0.001f
            ? Mathf.Abs(source.Scale.X / source.Scale.Y)
            : 1f;
        Scale = new Vector2(
            Mathf.Sign(source.Scale.X == 0f ? 1f : source.Scale.X) * scale * sourceScaleRatio,
            scale);
        Rotation = 0f;
        Skew = 0f;
    }

    private float GetLegacyFormScale(NinjaSlayerFormPresentation presentation)
    {
        if (presentation.FixedBodyScale.HasValue)
        {
            return presentation.FixedBodyScale.Value;
        }

        float height = Texture?.GetHeight() ?? 0f;
        if (height <= 0f)
        {
            return NinjaSlayerCombatVisuals.BodySpriteBaseScale;
        }

        return NinjaSlayerFormPresentationCatalog.ReferenceBodyTextureHeight
            * NinjaSlayerCombatVisuals.BodySpriteBaseScale
            / height;
    }
}
