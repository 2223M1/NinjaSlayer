namespace NinjaSlayer.Code.Combat;

internal readonly record struct SoftCollisionVertex(
    BossFragmentPoint Position,
    float U,
    float V);

internal readonly record struct SoftContactPoint(
    float FirstU,
    float FirstV,
    float SecondU,
    float SecondV,
    float Penetration,
    float PreSolveNormalSpeed);

internal sealed class SoftContactManifold(
    SoftFragmentBody first,
    SoftFragmentBody second,
    BossFragmentPoint normal,
    bool isNewContact,
    bool isSwept = false,
    float timeOfImpact = 1f)
{
    private readonly SoftContactPoint[] _points = new SoftContactPoint[2];

    public SoftFragmentBody First { get; } = first;
    public SoftFragmentBody Second { get; } = second;
    public BossFragmentPoint Normal { get; } = normal;
    public bool IsNewContact { get; } = isNewContact;
    public bool IsSwept { get; } = isSwept;
    public float TimeOfImpact { get; } = Math.Clamp(timeOfImpact, 0f, 1f);
    public int PointCount { get; private set; }
    public float MaximumPenetration { get; private set; }
    public SoftContactPoint this[int index] => _points[Math.Clamp(index, 0, PointCount - 1)];

    public void AddPoint(SoftContactPoint point)
    {
        if (PointCount < _points.Length)
        {
            _points[PointCount++] = point;
            MaximumPenetration = Math.Max(MaximumPenetration, point.Penetration);
        }
    }
}

internal readonly record struct SoftContactVelocityResult(
    bool Bounced,
    float MaximumClosingSpeed,
    float MaximumSeparatingSpeed);
