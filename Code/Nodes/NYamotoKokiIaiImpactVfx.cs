using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Vfx;

namespace NinjaSlayer.Code.Nodes;

public partial class NYamotoKokiIaiImpactVfx : Node2D
{
    public const string ScenePath =
        "res://NinjaSlayer/scenes/vfx/yamoto_koki_iai/vfx_grand_finale_impact.tscn";

    [Export]
    private Node2D? _centerParticles;

    [Export]
    private Node2D? _groundParticles;

    private CancellationTokenSource? _cts;

    public static NYamotoKokiIaiImpactVfx? Create(Creature target)
    {
        NCreature? node = NCombatRoom.Instance?.GetCreatureNode(target);
        if (node == null)
        {
            return null;
        }

        NYamotoKokiIaiImpactVfx? vfx = NinjaSlayerVfxUtil.TryGenModVfxNode<NYamotoKokiIaiImpactVfx>(ScenePath);
        vfx?.Initialize(node.VfxSpawnPosition, node.GetBottomOfHitbox());
        return vfx;
    }

    public override void _Ready()
    {
        RestartParticles(this);
        _cts = new CancellationTokenSource();
        TaskHelper.RunSafely(FreeAfterLifetime(_cts.Token));
    }

    public override void _ExitTree()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void Initialize(Vector2 centerPosition, Vector2 groundPosition)
    {
        if (_centerParticles != null)
        {
            _centerParticles.GlobalPosition = centerPosition;
        }

        if (_groundParticles != null)
        {
            _groundParticles.GlobalPosition = groundPosition;
        }
    }

    private async Task FreeAfterLifetime(CancellationToken token)
    {
        await Cmd.Wait(2f, token);
        this.QueueFreeSafely();
    }

    private static void RestartParticles(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is GpuParticles2D particles)
            {
                particles.Restart();
            }

            RestartParticles(child);
        }
    }
}
