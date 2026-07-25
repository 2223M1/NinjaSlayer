using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.Nodes;

public partial class NYamotoKokiGasBombIdleBob : Node
{
    internal const float Amplitude = 10f;
    internal const float PeriodSeconds = 2f;

    private Node2D? _body;
    private Control? _damageAmount;
    private Vector2 _bodyBasePosition;
    private Vector2 _damageBasePosition;
    private float _elapsed;
    private bool _isActive;

    public override void _Ready()
    {
        NCreatureVisuals visuals = GetParent<NCreatureVisuals>();
        _body = visuals.GetNode<Node2D>("%Visuals");
        _damageAmount = visuals.GetNodeOrNull<Control>("%DamageAmount");
        _bodyBasePosition = _body.Position;
        _damageBasePosition = _damageAmount?.Position ?? Vector2.Zero;
        _isActive = true;
    }

    public override void _Process(double delta)
    {
        if (!_isActive)
        {
            return;
        }

        _elapsed = Mathf.PosMod(_elapsed + (float)delta, PeriodSeconds);
        ApplyOffset(GetOffsetY(_elapsed));
    }

    public override void _ExitTree()
    {
        ResetPositions();
    }

    public void StopAndReset()
    {
        _isActive = false;
        SetProcess(false);
        ResetPositions();
    }

    internal static float GetOffsetY(float elapsed) =>
        Mathf.Sin(Mathf.Tau * elapsed / PeriodSeconds) * Amplitude;

    private void ApplyOffset(float offsetY)
    {
        Vector2 offset = Vector2.Down * offsetY;
        if (GodotObject.IsInstanceValid(_body))
        {
            _body!.Position = _bodyBasePosition + offset;
        }

        if (GodotObject.IsInstanceValid(_damageAmount))
        {
            _damageAmount!.Position = _damageBasePosition + offset;
        }
    }

    private void ResetPositions()
    {
        if (GodotObject.IsInstanceValid(_body))
        {
            _body!.Position = _bodyBasePosition;
        }

        if (GodotObject.IsInstanceValid(_damageAmount))
        {
            _damageAmount!.Position = _damageBasePosition;
        }
    }
}
