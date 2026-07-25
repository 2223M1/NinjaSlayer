using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Vfx;

namespace NinjaSlayer.Code.Nodes;

public partial class NYamotoKokiIaiPetalsVfx : GpuParticles2D
{
    public const string ScenePath =
        "res://NinjaSlayer/scenes/vfx/yamoto_koki_iai/grand_finale/vfx_grand_finale_petals.tscn";

    private CancellationTokenSource? _cts;

    public static NYamotoKokiIaiPetalsVfx? Create(Creature attacker)
    {
        NCreature? node = NCombatRoom.Instance?.GetCreatureNode(attacker);
        if (node == null)
        {
            return null;
        }

        NYamotoKokiIaiPetalsVfx? vfx = NinjaSlayerVfxUtil.TryGenModVfxNode<NYamotoKokiIaiPetalsVfx>(ScenePath);
        if (vfx != null)
        {
            vfx.GlobalPosition = node.VfxSpawnPosition;
        }

        return vfx;
    }

    public override void _Ready()
    {
        Restart();
        _cts = new CancellationTokenSource();
        TaskHelper.RunSafely(FreeAfterLifetime(_cts.Token));
    }

    public override void _ExitTree()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task FreeAfterLifetime(CancellationToken token)
    {
        await Cmd.Wait((float)Lifetime, token);
        this.QueueFreeSafely();
    }
}
