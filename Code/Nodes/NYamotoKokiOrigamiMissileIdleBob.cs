using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.Nodes;

public partial class NYamotoKokiOrigamiMissileIdleBob : Node
{
    internal const float Amplitude = 10f;
    internal const float PeriodSeconds = 2f;

    private Node2D? _body;
    private Control? _damageLabelContainer;
    private Vector2 _bodyBasePosition;
    private Vector2 _damageLabelBasePosition;
    private float _elapsed;
    private bool _isActive;

    public override void _Ready()
    {
        NCreatureVisuals visuals = GetParent<NCreatureVisuals>();
        _body = visuals.GetNode<Node2D>("%Visuals");
        _damageLabelContainer = visuals.GetNodeOrNull<Control>("%DamageLabelContainer");
        _bodyBasePosition = _body.Position;
        _damageLabelBasePosition = _damageLabelContainer?.Position ?? Vector2.Zero;
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

        if (GodotObject.IsInstanceValid(_damageLabelContainer))
        {
            _damageLabelContainer!.Position = _damageLabelBasePosition + offset;
        }
    }

    private void ResetPositions()
    {
        if (GodotObject.IsInstanceValid(_body))
        {
            _body!.Position = _bodyBasePosition;
        }

        if (GodotObject.IsInstanceValid(_damageLabelContainer))
        {
            _damageLabelContainer!.Position = _damageLabelBasePosition;
        }
    }
}
