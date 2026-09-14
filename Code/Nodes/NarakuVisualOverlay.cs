using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Combat;

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
    private NinjaSlayerFormKind? shownForm;

    public static void Sync(Creature creature)
        => SyncCore(creature);

    internal void SyncForPose()
    {
        TryBindFromTree();
        UpdateVisual();
    }

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
        if (shownForm is { } oldForm && oldForm != presentation.Kind)
            NinjaSlayerFormShatter.Schedule(creature, oldForm, Visible ? this : source);
        shownForm = presentation.Kind;
        Texture2D? sourceTexture = source.Texture;
        string? facingTexturePath = presentation == NinjaSlayerFormPresentationCatalog.Normal
            ? NinjaSlayerFormPresentationCatalog.ResolveFacingIdleTexturePath(
                sourceTexture?.ResourcePath,
                creature.GetCreatureNode() is { } actor && NinjaSlayerFacingState.ResolveFacingLeft(actor))
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
        var calibration = NinjaSlayerFormCalibration.For(presentation.Kind);
        Vector2 position = new(calibration.Position.X, calibration.Position.Y);
        Offset = Vector2.Zero;
        float scale = calibration.Scale;
        Transform2D authored = new(0f, Vector2.One * NinjaSlayerCombatVisuals.BodySpriteBaseScale,
            0f, NinjaSlayerCombatVisuals.BodySpriteBasePosition);
        Transform = source!.Transform * authored.AffineInverse()
            * new Transform2D(0f, Vector2.One * scale, 0f, position);
    }

}
