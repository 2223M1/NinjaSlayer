using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Code.Vfx;

namespace NinjaSlayer.Code.Nodes;

public partial class NYamotoKokiOrigamiMissileHitSparkVfx : NHitSparkVfx
{
    public const string ResourceScenePath =
        "res://NinjaSlayer/scenes/vfx/yamoto_koki_missile_hit_spark/hit_spark_vfx.tscn";

    private static readonly string[] ParticleNodePaths =
    [
        "vfx_common_specks_bright",
        "vfx_common_specks",
        "vfx_common_glow",
        "vfx_common_outward_streaks"
    ];

    private const string LifetimeParticleNodePath = "vfx_common_specks";

    private NCreature? _targetNode;

    public new static NHitSparkVfx? Create(Creature target, bool requireInteractable = true)
    {
        NCreature? targetNode = NCombatRoom.Instance?.GetCreatureNode(target);
        if (targetNode == null || requireInteractable && !targetNode.IsInteractable)
        {
            return null;
        }

        NYamotoKokiOrigamiMissileHitSparkVfx? vfx =
            NinjaSlayerVfxUtil.TryGenModVfxNode<NYamotoKokiOrigamiMissileHitSparkVfx>(ResourceScenePath);
        if (vfx != null)
        {
            vfx._targetNode = targetNode;
        }

        return vfx;
    }

    public override void _Ready()
    {
        if (_targetNode == null || !GodotObject.IsInstanceValid(_targetNode))
        {
            this.QueueFreeSafely();
            return;
        }

        GlobalPosition = _targetNode.VfxSpawnPosition;
        foreach (string nodePath in ParticleNodePaths)
        {
            GetNodeOrNull<GpuParticles2D>(nodePath)?.Restart();
        }

        GpuParticles2D? lifetimeParticles =
            GetNodeOrNull<GpuParticles2D>(LifetimeParticleNodePath);
        if (lifetimeParticles != null)
        {
            TaskHelper.RunSafely(FreeAfterParticles(lifetimeParticles));
        }
        else
        {
            this.QueueFreeSafely();
        }
    }

    private async Task FreeAfterParticles(GpuParticles2D particles)
    {
        await particles.AwaitSignal(GpuParticles2D.SignalName.Finished, this);
        this.QueueFreeSafely();
    }
}
