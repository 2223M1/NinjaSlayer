using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;

namespace NinjaSlayer.Code.Nodes;

public partial class NYamotoKokiGasBombVfx : Node
{
    private MegaSprite _megaSprite = null!;
    private GpuParticles2D _bitParticles = null!;
    private GpuParticles2D _dotParticles = null!;
    private Sprite2D _paperKraneCore = null!;
    private bool _hasBurst;

    public override void _Ready()
    {
        Node2D body = GetParent<Node2D>();
        _dotParticles = GetNode<GpuParticles2D>("../SmokeBallSlot/DotParticles");
        _bitParticles = GetNode<GpuParticles2D>("../SmokeBallSlot/BitParticles");
        _paperKraneCore = GetNode<Sprite2D>("../SmokeBallSlot/PaperKraneCore");
        _dotParticles.Emitting = false;
        _bitParticles.Emitting = false;
        _bitParticles.OneShot = true;

        _megaSprite = new MegaSprite(body);
        _megaSprite.ConnectAnimationEvent(
            Callable.From<GodotObject, GodotObject, GodotObject, GodotObject>(OnAnimationEvent));
    }

    private void OnAnimationEvent(
        GodotObject _,
        GodotObject __,
        GodotObject ___,
        GodotObject spineEvent)
    {
        switch (new MegaEvent(spineEvent).GetData().GetEventName())
        {
            case "burst":
                EnsureBurst();
                break;
            case "idle_particles":
                if (!_hasBurst)
                {
                    _dotParticles.Emitting = true;
                }
                break;
            case "dissipate":
                _dotParticles.Emitting = false;
                break;
        }
    }

    public void EnsureBurst()
    {
        if (_hasBurst)
        {
            return;
        }

        _hasBurst = true;
        _paperKraneCore.Hide();
        _dotParticles.Emitting = false;
        _bitParticles.Restart();
    }
}
