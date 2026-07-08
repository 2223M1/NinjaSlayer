using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using NinjaSlayer.Code.Vfx;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NNinjaSlayerGroundFireVfx : Node2D
{
    private static readonly StringName OuterColor = new("OuterColor");
    private static readonly StringName InnerColor = new("InnerColor");

    public const string ScenePath = "res://NinjaSlayer/scenes/vfx/burn/ninja_slayer_ground_fire.tscn";

    private Tween? tween;
    private Node2D mainFire = null!;
    private GpuParticles2D ember = null!;
    private GpuParticles2D flameSprites = null!;

    public static IEnumerable<string> AssetPaths => [ScenePath];

    public static NNinjaSlayerGroundFireVfx? Create(Creature target)
    {
        if (TestMode.IsOn)
        {
            return null;
        }

        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(target);
        if (creatureNode == null)
        {
            return null;
        }

        NinjaSlayerVfxUtil.EnsureCached(AssetPaths);
        var vfx = NinjaSlayerVfxUtil.GenVfxNode<NNinjaSlayerGroundFireVfx>(ScenePath);
        vfx.GlobalPosition = creatureNode.GetBottomOfHitbox();
        return vfx;
    }

    public override void _Ready()
    {
        mainFire = GetNode<Node2D>("MainFire");
        ember = GetNode<GpuParticles2D>("Ember");
        flameSprites = GetNode<GpuParticles2D>("FlameSprites");
        TaskHelper.RunSafely(AnimateIn());
    }

    public override void _ExitTree()
    {
        tween?.Kill();
    }

    private async Task AnimateIn()
    {
        mainFire.Modulate = Colors.Transparent;
        mainFire.Scale = Vector2.Zero;
        ember.Emitting = true;
        Task emberDone = ember.AwaitSignal(GpuParticles2D.SignalName.Finished, this);
        tween = CreateTween().SetParallel();
        tween.TweenProperty(mainFire, "scale", Vector2.One * 4f, 0.5)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(mainFire, "modulate:a", 0.9f, 0.5)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
        if (await tween.AwaitFinished(this))
        {
            flameSprites.Emitting = true;
            tween = CreateTween().SetParallel();
            tween.TweenProperty(mainFire, "modulate:a", 0f, 0.5);
            tween.TweenProperty(flameSprites, "modulate:a", 0f, 0.5);
            tween.TweenProperty(mainFire, "scale", Vector2.One * 2f, 2.0)
                .SetEase(Tween.EaseType.InOut)
                .SetTrans(Tween.TransitionType.Cubic);
            if (await tween.AwaitFinished(this))
            {
                flameSprites.Emitting = false;
                await emberDone;
                this.QueueFreeSafely();
            }
        }
    }
}
