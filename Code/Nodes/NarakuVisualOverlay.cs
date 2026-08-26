using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NarakuVisualOverlay : Sprite2D
{
    private const string NodeName = "NarakuVisualOverlay";

    private Creature? creature;
    private Sprite2D? source;
    private string? activeTexturePath;

    // UpdateVisual runs every frame. The texture path only changes when the presentation or the
    // source sprite's own texture changes, and for the synchronized idle sequence resolving it
    // rebuilt an interpolated "...0000.png" string every frame.
    private NinjaSlayerFormPresentation? resolvedPresentation;
    private Texture2D? resolvedSourceTexture;
    private Color mirroredModulate = Colors.White;
    private Color mirroredSelfModulate = Colors.White;
    private Material? mirroredMaterial;
    private bool mirroredFlipH;
    private bool mirroredFlipV;
    private bool hasMirroredState;

    public static void Sync(Creature creature)
        => SyncCore(creature);

    private static void SyncCore(Creature creature)
    {
        NinjaSlayerVisualRig.SyncShadowScale(creature);

        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode == null)
        {
            return;
        }

        NCreatureVisuals visualsRoot = creatureNode.Visuals;
        Sprite2D source = visualsRoot.GetNode<Sprite2D>("%Visuals");
        Node parent = source.GetParent()
            ?? throw new InvalidOperationException("The NinjaSlayer body sprite has no parent node.");
        NarakuVisualOverlay overlay = visualsRoot.FindChild(NodeName, recursive: true) as NarakuVisualOverlay
            ?? throw new InvalidOperationException("The NinjaSlayer visual rig is missing its Naraku overlay.");
        if (!ReferenceEquals(overlay.GetParent(), parent))
        {
            overlay.Reparent(parent);
        }

        overlay.Bind(creature, source);
        overlay.Centered = true;
        overlay.FlipH = source.FlipH;
        overlay.FlipV = source.FlipV;
        overlay.ZIndex = source.ZIndex;
        overlay.ZAsRelative = source.ZAsRelative;
        overlay.ShowBehindParent = source.ShowBehindParent;
        parent.MoveChild(overlay, source.GetIndex() + 1);
        overlay.UpdateVisual();
    }

    public override void _Ready()
    {
        TryBindFromTree();
        UpdateVisual();
    }

    public override void _Process(double delta)
    {
        TryBindFromTree();
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
        Texture2D? sourceTexture = source.Texture;
        string? facingTexturePath = presentation == NinjaSlayerFormPresentationCatalog.Normal
            ? NinjaSlayerFormPresentationCatalog.ResolveFacingIdleTexturePath(
                sourceTexture?.ResourcePath,
                source.GetParent() is Node2D anchor && anchor.Scale.X < 0f)
            : null;
        bool usesOverlay = presentation.UsesOverlay || facingTexturePath != null;
        source.Visible = !usesOverlay;
        Visible = usesOverlay;
        if (!usesOverlay)
        {
            return;
        }

        if (activeTexturePath is null
            || !ReferenceEquals(resolvedSourceTexture, sourceTexture)
            || resolvedPresentation != presentation)
        {
            string texturePath = facingTexturePath
                ?? NinjaSlayerFormPresentationCatalog.ResolveBodyTexturePath(
                    presentation,
                    sourceTexture?.ResourcePath)
                ?? throw new InvalidOperationException("Overlay form presentation did not resolve a texture path.");
            if (activeTexturePath != texturePath)
            {
                Texture = PreloadManager.Cache.GetTexture2D(texturePath);
                activeTexturePath = texturePath;
            }

            resolvedSourceTexture = sourceTexture;
            resolvedPresentation = presentation;
        }

        if (presentation.BodyTransformMode == NinjaSlayerBodyTransformMode.Source)
        {
            CopySourceTransform();
        }
        else
        {
            ApplyLegacyFormTransform(presentation);
        }

        MirrorSourceAppearance();
    }

    private void Bind(Creature owner, Sprite2D body)
    {
        creature = owner;
        source = body;
    }

    private void TryBindFromTree()
    {
        if (creature != null
            && source != null
            && GodotObject.IsInstanceValid(source))
        {
            return;
        }

        for (Node? node = this; node != null; node = node.GetParent())
        {
            if (source == null && node is NCreatureVisuals visuals)
            {
                source = NinjaSlayerVisualRig.GetBodySprite(visuals);
            }

            if (creature == null && node is NCreature creatureNode)
            {
                creature = creatureNode.Entity;
            }
        }
    }

    /// <summary>
    /// Copies the source sprite's appearance, writing across the Godot interop boundary only for
    /// the properties that actually changed since the previous frame.
    /// </summary>
    private void MirrorSourceAppearance()
    {
        Color modulate = source!.Modulate;
        Color selfModulate = source.SelfModulate;
        Material? material = source.Material;
        bool flipH = source.FlipH;
        bool flipV = source.FlipV;

        if (!hasMirroredState || mirroredModulate != modulate)
        {
            Modulate = modulate;
            mirroredModulate = modulate;
        }

        if (!hasMirroredState || mirroredSelfModulate != selfModulate)
        {
            SelfModulate = selfModulate;
            mirroredSelfModulate = selfModulate;
        }

        if (!hasMirroredState || !ReferenceEquals(mirroredMaterial, material))
        {
            Material = material;
            mirroredMaterial = material;
        }

        if (!hasMirroredState || mirroredFlipH != flipH)
        {
            FlipH = flipH;
            mirroredFlipH = flipH;
        }

        if (!hasMirroredState || mirroredFlipV != flipV)
        {
            FlipV = flipV;
            mirroredFlipV = flipV;
        }

        hasMirroredState = true;
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
