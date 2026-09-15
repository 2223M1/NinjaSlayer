using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Vfx;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCombatVfx
{
    private const string BurnDamageSfx = "event:/sfx/characters/attack_fire";
    internal const string MacheteHitSfx = "event:/sfx/enemy/enemy_attacks/vantom/vantom_dismember";

    public static AttackCommand WithDefectStrikeHitFx(this AttackCommand command) =>
        command.WithHitFx(VfxCmd.bluntPath, null, TmpSfx.bluntAttack);

    public static AttackCommand WithHeavyBluntHitFx(this AttackCommand command) =>
        command.WithHitFx(VfxCmd.heavyBluntPath, null, TmpSfx.heavyAttack)
            .WithHitVfxSpawnedAtBase();

    public static void PlayDefectStrikeHitFx(Creature target)
    {
        VfxCmd.PlayOnCreatureCenter(target, VfxCmd.bluntPath);
        NDebugAudioManager.Instance?.Play(TmpSfx.bluntAttack);
    }

    internal static void PlayDarkCounterHitFx(Creature target)
    {
        VfxCmd.PlayOnCreatureCenter(target, VfxCmd.slashPath);
        SfxCmd.Play("event:/sfx/characters/ironclad/ironclad_attack");
    }

    internal static void PlayMacheteHitFx(Creature target)
    {
        VfxCmd.PlayOnCreatureCenter(target, VfxCmd.slashPath);
        SfxCmd.Play(MacheteHitSfx);
    }

    internal static void PlaySawatariFlyingSlash(Creature source, Creature target)
    {
        if (TestMode.IsOn || target.IsDead || source.GetCreatureNode() is not { } attacker
            || target.GetCreatureNode() is not { } victim || target.GetVfxContainer() is not { } container) return;
        Vector2 travel = victim.VfxSpawnPosition - attacker.VfxSpawnPosition;
        Vector2 direction = travel.Normalized();
        if (direction == Vector2.Zero) direction = Vector2.Right;
        // Reflect horizontally for a leftward attack, keeping the native slash upright.
        Vector2 up = direction.Orthogonal() * (direction.X < 0f ? 1f : -1f);
        var effect = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath(VfxCmd.flyingSlashPath)).Instantiate<Node2D>();
        effect.Transform = container.GetGlobalTransform().AffineInverse()
            * new Transform2D(direction, up, victim.VfxSpawnPosition);
        container.AddChildSafely(effect);
        Node spineNode = effect.GetNode("SpineSprite");
        var spine = new MegaSprite(spineNode);
        effect.RunWhenSpineReady(spine, _ =>
        {
            spineNode.Call("update_skeleton", 0f);
            var skeleton = spine.GetSkeleton()!;
            GodotObject[] bones = [skeleton.FindBone("Small")!.BoundObject,
                skeleton.FindBone("mid")!.BoundObject, skeleton.FindBone("large")!.BoundObject];
            float[] starts = bones.Select(bone => bone.Call("get_x").AsSingle()).ToArray();
            float[] authored = (float[])starts.Clone();
            // Remap only keyed translation. The native attachment sizes, curves,
            // colors and NVfxSpine completion/cleanup remain unchanged.
            spine.ConnectBeforeAnimationStateApply(Callable.From<GodotObject>(_ =>
            {
                for (int i = 0; i < bones.Length; i++) bones[i].Call("set_x", authored[i]);
            }));
            spine.ConnectBeforeWorldTransformsChange(Callable.From<GodotObject>(_ =>
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    authored[i] = bones[i].Call("get_x").AsSingle();
                    bones[i].Call("set_x", authored[i] / -starts[i] * travel.Length());
                }
            }));
            spineNode.Call("update_skeleton", 0f);
        });
    }

    internal static void PlaySawatariBambooHit(Creature target, bool connects)
    {
        if (!connects) return;
        NDebugAudioManager.Instance?.Play(TmpSfx.bluntAttack);
        if (TestMode.IsOn || target.IsDead || target.GetCreatureNode() is not { } actor
            || target.GetVfxContainer() is not { } container) return;
        var impact = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath(VfxCmd.dramaticStabPath))
            .Instantiate<Node2D>();
        // Native _Ready enables every particle child, so detach the line first.
        Node line = impact.GetNode("slash");
        impact.RemoveChild(line);
        line.Free();
        impact.Position = container.GetGlobalTransform().AffineInverse() * actor.VfxSpawnPosition;
        container.AddChildSafely(impact);
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
            NDebugAudioManager.Instance?.Play(TmpSfx.heavyAttack);
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
