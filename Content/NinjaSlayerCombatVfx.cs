using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Vfx;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCombatVfx
{
    private const string BurnDamageSfx = "event:/sfx/characters/attack_fire";

    public static AttackCommand WithDefectStrikeHitFx(this AttackCommand command) =>
        command.WithHitFx(VfxCmd.bluntPath, null, TmpSfx.bluntAttack);

    public static AttackCommand WithHeavyBluntHitFx(this AttackCommand command) =>
        command.WithHitFx(VfxCmd.heavyBluntPath, null, TmpSfx.heavyAttack);

    public static void PlayDefectStrikeHitFx(Creature target)
    {
        VfxCmd.PlayOnCreatureCenter(target, VfxCmd.bluntPath);
        NDebugAudioManager.Instance?.Play(TmpSfx.bluntAttack);
    }

    internal static void PlaySlashHitFx(Creature target)
    {
        VfxCmd.PlayOnCreatureCenter(target, VfxCmd.slashPath);
        NDebugAudioManager.Instance?.Play(TmpSfx.slashAttack);
    }

    internal static void PlaySawatariBambooHit(Creature attacker, Creature target, bool connects)
    {
        NDebugAudioManager.Instance?.Play(TmpSfx.daggerThrow, .8f);
        if (!connects || NCombatRoom.Instance is not { } room
            || target.GetCreatureNode() is not { } targetNode) return;
        var impact = new Node2D { Name = "SawatariBambooImpact", Scale = Vector2.One * .35f };
        var template = ResourceLoader.Load<PackedScene>("res://scenes/vfx/vfx_dramatic_stab.tscn").Instantiate<Node2D>();
        foreach (string name in new[] { "Flash", "Sparks" })
        {
            var particles = template.GetNode<GpuParticles2D>(name);
            particles.Owner = null;
            template.RemoveChild(particles);
            impact.AddChild(particles);
            particles.ProcessMaterial = (ParticleProcessMaterial)particles.ProcessMaterial.Duplicate(true);
            var material = (ParticleProcessMaterial)particles.ProcessMaterial;
            material.ColorRamp = new GradientTexture1D
            {
                Gradient = new Gradient
                {
                    Offsets = [0f, .35f, 1f],
                    Colors = [Colors.White, new Color(1f, .8f, .3f), new Color(1f, .65f, .2f, 0f)]
                }
            };
            material.HueVariationMin = material.HueVariationMax = 0f;
            particles.SpeedScale = name == "Flash" ? 8f : 4f;
            particles.Amount = name == "Flash" ? 4 : 12;
            particles.Emitting = true;
        }
        template.Free();
        room.CombatVfxContainer.AddChildSafely(impact);
        impact.GlobalPosition = targetNode.VfxSpawnPosition;
        if (attacker.GetCreatureNode() is { } attackerNode)
            impact.GlobalRotation = (targetNode.VfxSpawnPosition - attackerNode.VfxSpawnPosition).Angle();
        Tween cleanup = impact.CreateTween();
        cleanup.TweenInterval(.15f);
        cleanup.TweenCallback(Callable.From(impact.QueueFree));
    }

    public static void PlayYamotoKokiIaiPetals(Creature attacker)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null)
        {
            return;
        }

        NYamotoKokiIaiPetalsVfx? petals = NYamotoKokiIaiPetalsVfx.Create(attacker);
        if (petals != null)
        {
            room.CombatVfxContainer.AddChildSafely(petals);
        }

    }

    public static void PlayYamotoKokiIaiImpact(IEnumerable<Creature> targets)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null)
        {
            return;
        }

        bool playedImpact = false;
        foreach (Creature target in targets)
        {
            NYamotoKokiIaiImpactVfx? impact = NYamotoKokiIaiImpactVfx.Create(target);
            if (impact != null)
            {
                room.CombatVfxContainer.AddChildSafely(impact);
                playedImpact = true;
            }
        }

        if (playedImpact)
        {
            NDebugAudioManager.Instance?.Play(TmpSfx.slashAttack);
        }
    }

    public static void PreloadYamotoKokiIaiFeedback()
    {
        NinjaSlayerVfxUtil.PreloadModVfxScene(NYamotoKokiIaiPetalsVfx.ScenePath);
        NinjaSlayerVfxUtil.PreloadModVfxScene(NYamotoKokiIaiImpactVfx.ScenePath);
        NinjaSlayerVfxUtil.PreloadModVfxScene(NYamotoKokiOrigamiMissileHitSparkVfx.ResourceScenePath);
    }

    public static void PlayBurnStatusFeedback(IEnumerable<Creature> targets)
    {
        if (NCombatRoom.Instance is { } room)
        {
            foreach (Creature target in targets)
            {
                NNinjaSlayerGroundFireVfx? vfx = NNinjaSlayerGroundFireVfx.Create(target);
                if (vfx is not null)
                {
                    room.CombatVfxContainer.AddChildSafely(vfx);
                }
            }
        }
        // Match vanilla Burn: audio follows the visual command even without a combat scene.
        SfxCmd.Play(BurnDamageSfx);
    }
}
