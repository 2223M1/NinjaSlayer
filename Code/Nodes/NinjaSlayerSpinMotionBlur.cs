using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerSpinMotionBlur : Node
{
    private const string BlurMaterialPath = "res://NinjaSlayer/materials/vfx/ninja_slayer_spin_motion_blur_mat.tres";

    private const float SpinSpeedThreshold = 0.8f;
    private const float MaxSpinSpeed = 6f;
    private const float MaxBlurStrength = 0.65f;
    private const float GhostThreshold = 0.15f;
    private const float GhostSpawnInterval = 0.02f;
    private const float SoarSpinIntensityFloor = 0.45f;

    private static readonly StringName BlurStrengthParam = new("blur_strength");
    private static readonly StringName BlurSignParam = new("blur_sign");
    private static readonly float[] GhostAlphas = [0.35f, 0.2f, 0.1f];

    private Sprite2D? body;
    private readonly Sprite2D?[] ghosts = new Sprite2D?[3];
    private Creature? creature;
    private Material? originalBodyMaterial;
    private ShaderMaterial? blurMaterialInstance;
    private float lastScaleX;
    private bool hasLastScaleX;
    private float ghostSpawnTimer;

    public override void _Ready()
    {
        var anchor = GetParent()?.GetNodeOrNull<Node2D>(NinjaSlayerVisualRig.AirborneAnchorName);
        body = anchor?.GetNodeOrNull<Sprite2D>("%Visuals");
        for (var i = 0; i < ghosts.Length; i++)
        {
            ghosts[i] = anchor?.GetNodeOrNull<Sprite2D>($"SpinGhost{i}");
        }

        creature = FindCreature();
    }

    public override void _Process(double delta)
    {
        if (body == null || !GodotObject.IsInstanceValid(body))
        {
            return;
        }

        creature ??= FindCreature();
        if (creature?.IsDead == true)
        {
            Reset();
            return;
        }

        float deltaSeconds = (float)delta;
        if (deltaSeconds <= 0f)
        {
            return;
        }

        float currentScaleX = body.Scale.X;
        float spinSpeed = hasLastScaleX ? Mathf.Abs(currentScaleX - lastScaleX) / deltaSeconds : 0f;
        float intensity = Mathf.Clamp(
            (spinSpeed - SpinSpeedThreshold) / (MaxSpinSpeed - SpinSpeedThreshold),
            0f,
            1f);

        if (creature != null && SoarSpinAnimation.IsSpinning(creature))
        {
            intensity = Mathf.Max(intensity, SoarSpinIntensityFloor);
        }

        if (intensity <= 0f)
        {
            Reset();
            lastScaleX = currentScaleX;
            hasLastScaleX = true;
            return;
        }

        EnsureBlurMaterial();
        if (blurMaterialInstance != null)
        {
            float blurSign = hasLastScaleX && currentScaleX < lastScaleX ? -1f : 1f;
            blurMaterialInstance.SetShaderParameter(BlurStrengthParam, intensity * MaxBlurStrength);
            blurMaterialInstance.SetShaderParameter(BlurSignParam, blurSign);
        }

        if (intensity >= GhostThreshold)
        {
            ghostSpawnTimer += deltaSeconds;
            if (ghostSpawnTimer >= GhostSpawnInterval)
            {
                ghostSpawnTimer = 0f;
                PushGhostSnapshot();
            }

            UpdateGhostVisibility(intensity);
        }
        else
        {
            HideGhosts();
        }

        lastScaleX = currentScaleX;
        hasLastScaleX = true;
    }

    public void Reset()
    {
        if (body != null && GodotObject.IsInstanceValid(body))
        {
            body.Material = originalBodyMaterial;
        }

        blurMaterialInstance = null;
        hasLastScaleX = false;
        ghostSpawnTimer = 0f;
        HideGhosts();
    }

    public static NinjaSlayerSpinMotionBlur? Get(Creature creature)
    {
        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        var visuals = creatureNode?.Visuals;
        if (visuals == null)
        {
            return null;
        }

        return visuals.GetNodeOrNull<NinjaSlayerSpinMotionBlur>("SpinMotionBlur");
    }

    private void EnsureBlurMaterial()
    {
        if (body == null || blurMaterialInstance != null)
        {
            return;
        }

        originalBodyMaterial = body.Material;
        var template = GD.Load<ShaderMaterial>(BlurMaterialPath);
        if (template == null)
        {
            return;
        }

        blurMaterialInstance = (ShaderMaterial)template.Duplicate();
        body.Material = blurMaterialInstance;
    }

    private void PushGhostSnapshot()
    {
        if (body == null)
        {
            return;
        }

        for (var i = ghosts.Length - 1; i > 0; i--)
        {
            CopySpriteState(ghosts[i - 1], ghosts[i]);
        }

        CopySpriteState(body, ghosts[0]);
    }

    private void UpdateGhostVisibility(float intensity)
    {
        float alphaScale = Mathf.Clamp(intensity, 0f, 1f);
        for (var i = 0; i < ghosts.Length; i++)
        {
            var ghost = ghosts[i];
            if (ghost == null)
            {
                continue;
            }

            if (ghost.Texture == null)
            {
                ghost.Visible = false;
                continue;
            }

            ghost.Visible = true;
            ghost.Modulate = new Color(1f, 1f, 1f, GhostAlphas[i] * alphaScale);
        }
    }

    private void HideGhosts()
    {
        foreach (var ghost in ghosts)
        {
            if (ghost == null)
            {
                continue;
            }

            ghost.Visible = false;
            ghost.Modulate = new Color(1f, 1f, 1f, 0f);
        }
    }

    private static void CopySpriteState(Sprite2D? source, Sprite2D? target)
    {
        if (source == null || target == null)
        {
            return;
        }

        target.Texture = source.Texture;
        target.Position = source.Position;
        target.Offset = source.Offset;
        target.Scale = source.Scale;
        target.RotationDegrees = source.RotationDegrees;
        target.FlipH = source.FlipH;
        target.FlipV = source.FlipV;
    }

    private Creature? FindCreature()
    {
        for (Node? node = this; node != null; node = node.GetParent())
        {
            if (node is NCreature creatureNode)
            {
                return creatureNode.Entity;
            }
        }

        return null;
    }
}
