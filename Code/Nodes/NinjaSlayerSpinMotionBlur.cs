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
    private const float SoarSpinIntensityFloor = 0.45f;

    private static readonly StringName BlurStrengthParam = new("blur_strength");
    private static readonly StringName BlurSignParam = new("blur_sign");

    // The template resource is loaded once per process; each creature keeps its own duplicated
    // instance for the lifetime of its body sprite. Re-duplicating on every spin threshold
    // crossing swapped a live sprite's material several times per second.
    private static ShaderMaterial? blurMaterialTemplate;

    private Sprite2D? body;
    private Creature? creature;
    private Material? originalBodyMaterial;
    private ShaderMaterial? blurMaterialInstance;
    private bool blurArmed;
    private float lastScaleX;
    private bool hasLastScaleX;

    public override void _Ready()
    {
        var anchor = GetParent()?.GetNodeOrNull<Node2D>(NinjaSlayerVisualRig.AirborneAnchorName);
        body = anchor?.GetNodeOrNull<Sprite2D>("%Visuals");

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

        if (creature != null && SoarSpinAnimation.IsVerticalSpinActive(creature))
        {
            intensity = Mathf.Max(intensity, SoarSpinIntensityFloor);
        }

        if (intensity <= 0f)
        {
            Disarm();
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

        lastScaleX = currentScaleX;
        hasLastScaleX = true;
    }

    public void Reset()
    {
        Disarm();
        blurMaterialInstance = null;
        hasLastScaleX = false;
    }

    /// <summary>
    /// Detaches the blur without discarding the duplicated material, so re-arming on the next
    /// spin costs a single material assignment instead of another <c>Duplicate()</c>.
    /// </summary>
    private void Disarm()
    {
        if (!blurArmed)
        {
            return;
        }

        blurArmed = false;
        if (body != null && GodotObject.IsInstanceValid(body))
        {
            body.Material = originalBodyMaterial;
        }
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
        if (body == null)
        {
            return;
        }

        if (blurMaterialInstance == null)
        {
            if (blurMaterialTemplate == null || !GodotObject.IsInstanceValid(blurMaterialTemplate))
            {
                blurMaterialTemplate = GD.Load<ShaderMaterial>(BlurMaterialPath);
            }

            if (blurMaterialTemplate == null)
            {
                return;
            }

            blurMaterialInstance = (ShaderMaterial)blurMaterialTemplate.Duplicate();
        }

        if (!blurArmed)
        {
            // Re-read the sprite's own material on every arm, so a material another system
            // installed between spins is still the one restored on disarm.
            if (!ReferenceEquals(body.Material, blurMaterialInstance))
            {
                originalBodyMaterial = body.Material;
            }

            blurArmed = true;
            body.Material = blurMaterialInstance;
        }
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
