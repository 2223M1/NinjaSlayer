using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NarakuVisualOverlay : Sprite2D
{
    private const string NodeName = "NarakuVisualOverlay";
    private const string FullyReleasedNarakuTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/naraku.png";
    private const string NormalIdleTexturePrefix = "res://NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_";
    private const string NarakuIdleTexturePrefix = "res://NinjaSlayer/images/characters/ninja_slayer/naraku_idle/NinjaSlayer_naraku_idle_";
    private const string OneBodyOneSoulTexturePath = "res://NinjaSlayer/images/characters/ninja_slayer/one_body_one_soul.png";
    private const int NarakuIdleFrameCount = 22;
    private const float NarakuScale = 0.5f;
    private const float BodyTextureHeight = 1080f;

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

        FormVisual form = ResolveFormVisual(creature);
        source.Visible = form == FormVisual.None;
        Visible = form != FormVisual.None;
        if (form == FormVisual.None)
        {
            return;
        }

        string texturePath = form switch
        {
            FormVisual.Naraku => ResolveNarakuIdleTexturePath(source),
            FormVisual.FullyReleasedNaraku => FullyReleasedNarakuTexturePath,
            FormVisual.OneBodyOneSoul => OneBodyOneSoulTexturePath,
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null)
        };
        if (activeTexturePath != texturePath)
        {
            Texture = PreloadManager.Cache.GetTexture2D(texturePath);
            activeTexturePath = texturePath;
        }

        if (form == FormVisual.Naraku)
        {
            CopySourceTransform();
        }
        else
        {
            ApplyLegacyFormTransform(form);
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

    private void ApplyLegacyFormTransform(FormVisual form)
    {
        Centered = true;
        float y = NinjaSlayerCombatVisuals.BodySpriteBasePosition.Y;
        if (form == FormVisual.FullyReleasedNaraku)
        {
            y += NinjaSlayerCombatVisuals.NarakuFormYOffset;
        }

        Position = new Vector2(0f, y);
        Offset = Vector2.Zero;
        float scale = GetLegacyFormScale(form);
        Scale = new Vector2(
            Mathf.Sign(source!.Scale.X == 0f ? 1f : source.Scale.X) * scale,
            scale);
        Rotation = 0f;
        Skew = 0f;
    }

    private float GetLegacyFormScale(FormVisual form)
    {
        if (form == FormVisual.FullyReleasedNaraku)
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

    private static string ResolveNarakuIdleTexturePath(Sprite2D source)
    {
        string resourcePath = source.Texture?.ResourcePath ?? string.Empty;
        int frame = 1;
        if (resourcePath.StartsWith(NormalIdleTexturePrefix, StringComparison.Ordinal)
            && resourcePath.EndsWith(".png", StringComparison.Ordinal))
        {
            ReadOnlySpan<char> frameText = resourcePath.AsSpan(
                NormalIdleTexturePrefix.Length,
                resourcePath.Length - NormalIdleTexturePrefix.Length - ".png".Length);
            if (!int.TryParse(frameText, out frame) || frame is < 1 or > NarakuIdleFrameCount)
            {
                frame = 1;
            }
        }

        return $"{NarakuIdleTexturePrefix}{frame:0000}.png";
    }

    private static FormVisual ResolveFormVisual(Creature creature)
    {
        if (NinjaSlayerFormState.IsFullyReleasedNaraku(creature))
        {
            return FormVisual.FullyReleasedNaraku;
        }

        if (NinjaSlayerFormState.IsNaraku(creature))
        {
            return FormVisual.Naraku;
        }

        if (creature.HasPower<OneBodyOneSoulPower>())
        {
            return FormVisual.OneBodyOneSoul;
        }

        return FormVisual.None;
    }

    private enum FormVisual
    {
        None,
        Naraku,
        FullyReleasedNaraku,
        OneBodyOneSoul
    }
}
