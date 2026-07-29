namespace NinjaSlayer.Code.Combat;

internal sealed class SoftBodyLaunchActuator(
    SoftFragmentBody body,
    BossFragmentPoint targetVelocity,
    float targetAngularVelocityRadians,
    float durationSeconds = BossFountainLaunchProfile.LaunchActuatorSeconds)
{
    private readonly float _durationSeconds = Math.Max(0.001f, durationSeconds);
    private readonly BossFragmentPoint _deformationTargetVelocity =
        ClampMagnitude(targetVelocity, BossFountainLaunchProfile.MaximumDeformationSpeed);
    private float _elapsedSeconds;
    private float _appliedWeight;

    public SoftFragmentBody Body { get; } = body;
    public BossFragmentPoint TargetVelocity { get; } = targetVelocity;
    public float TargetAngularVelocityRadians { get; } = targetAngularVelocityRadians;
    public bool IsComplete => _appliedWeight >= 0.99999f;

    public void Begin()
    {
        Body.Release(TargetVelocity, TargetAngularVelocityRadians);
        _elapsedSeconds = 0f;
        _appliedWeight = 0f;
    }

    public void Advance(float seconds)
    {
        if (IsComplete || !float.IsFinite(seconds) || seconds <= 0f)
        {
            return;
        }

        _elapsedSeconds = Math.Min(_durationSeconds, _elapsedSeconds + seconds);
        float progress = _elapsedSeconds / _durationSeconds;
        float remaining = 1f - progress;
        float cumulativeWeight = 1f - remaining * remaining * remaining;
        float deltaWeight = Math.Max(0f, cumulativeWeight - _appliedWeight);
        _appliedWeight = cumulativeWeight;
        Body.ApplyLaunchVelocityDelta(
            default,
            Multiply(_deformationTargetVelocity, deltaWeight),
            angularVelocityDelta: 0f,
            differentialFraction: 0.45f);
    }

    private static BossFragmentPoint ClampMagnitude(BossFragmentPoint point, float maximum)
    {
        float length = MathF.Sqrt(point.X * point.X + point.Y * point.Y);
        return length <= maximum || length <= 0.001f
            ? point
            : Multiply(point, maximum / length);
    }

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float scalar) =>
        new(point.X * scalar, point.Y * scalar);
}
