using Godot;

namespace NinjaSlayer.Code.Nodes;

public partial class NinjaSlayerDeathJitter : Node2D
{
    private const float SampleSeconds = 0.05f;
    private const float MaxPositionOffset = 2f;
    private const float MaxRotationOffsetDegrees = 0.5f;

    private float _elapsed;
    private Vector2 _fromPosition;
    private Vector2 _targetPosition;
    private float _fromRotation;
    private float _targetRotation;

    public override void _Ready()
    {
        SampleNextTarget();
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        while (_elapsed >= SampleSeconds)
        {
            _elapsed -= SampleSeconds;
            _fromPosition = Position;
            _fromRotation = RotationDegrees;
            SampleNextTarget();
        }

        float progress = Mathf.Clamp(_elapsed / SampleSeconds, 0f, 1f);
        Position = _fromPosition.Lerp(_targetPosition, progress);
        RotationDegrees = Mathf.Lerp(_fromRotation, _targetRotation, progress);
    }

    public void StopAndReset()
    {
        SetProcess(false);
        Position = Vector2.Zero;
        Rotation = 0f;
    }

    private void SampleNextTarget()
    {
        float angle = GD.Randf() * Mathf.Tau;
        float radius = GD.Randf() * MaxPositionOffset;
        _targetPosition = Vector2.FromAngle(angle) * radius;
        _targetRotation = (float)GD.RandRange(-MaxRotationOffsetDegrees, MaxRotationOffsetDegrees);
    }
}
