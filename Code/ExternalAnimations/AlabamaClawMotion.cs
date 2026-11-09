using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Backgrounds;

namespace NinjaSlayer.Code.ExternalAnimations;

// The crab's targeting body has no artwork. Its native SpineBoneNode drives one
// claw in the shared background rig; apply the targeting body's motion there.
internal sealed class AlabamaClawMotion : IDisposable
{
    private readonly Node2D _target;
    private readonly Node2D _arm;
    private readonly Node2D _spine;
    private readonly MegaSprite _sprite;
    private readonly Transform2D _targetInverse;
    private readonly Transform2D _armBaseline;
    private readonly Transform2D _armCanvasBaseline;
    private readonly Callable _apply;
    private bool _disposed;

    internal AlabamaClawMotion(NCreature target, NCombatRoom room)
    {
        _target = target.Body;
        var crab = room.Background.GetNode<NKaiserCrabBossBackground>("%KaiserCrab");
        _arm = crab.GetNode<Node2D>(target.Entity.Monster is Crusher ? "%ArmBoneL" : "%ArmBoneR");
        _spine = crab.GetNode<Node2D>("%Visuals");
        _sprite = new MegaSprite(_spine);
        _targetInverse = _target.GetGlobalTransformWithCanvas().AffineInverse();
        _armBaseline = _arm.Transform;
        _armCanvasBaseline = _arm.GetGlobalTransformWithCanvas();
        _apply = Callable.From<GodotObject>(_ => Apply());
        Error connection = _sprite.ConnectBeforeAnimationStateApply(_apply);
        if (connection != Error.Ok)
            throw new InvalidOperationException($"Could not connect Alabama claw motion: {connection}.");
        _target.TreeExiting += Dispose;
        _spine.TreeExiting += Dispose;
        RenderingServer.FramePreDraw += RefreshSkeleton;
    }

    private void Apply()
    {
        _arm.Transform = _spine.GetGlobalTransformWithCanvas().AffineInverse()
            * _target.GetGlobalTransformWithCanvas() * _targetInverse * _armCanvasBaseline;
    }

    private void RefreshSkeleton()
    {
        // Tweens advance after Spine's _Process. Sample without advancing time
        // so the visible claw and the grappler use the same frame's pose.
        _spine.Call("update_skeleton", 0f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RenderingServer.FramePreDraw -= RefreshSkeleton;
        if (GodotObject.IsInstanceValid(_target)) _target.TreeExiting -= Dispose;
        if (!GodotObject.IsInstanceValid(_spine)) return;
        _spine.TreeExiting -= Dispose;
        _sprite.DisconnectBeforeAnimationStateApply(_apply);
        _arm.Transform = _armBaseline;
        if (_spine.IsInsideTree()) RefreshSkeleton();
    }
}
