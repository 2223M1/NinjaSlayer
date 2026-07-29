namespace NinjaSlayer.Code.Combat;

internal enum SoftBoundarySide
{
    Left,
    Right
}

internal readonly record struct SoftHorizontalBoundary(float Left, float Right)
{
    public bool IsValid => float.IsFinite(Left) && float.IsFinite(Right) && Right > Left + 1f;
}

internal readonly record struct SoftBoundaryContactPoint(
    float U,
    float V,
    float Penetration,
    float PreSolveNormalSpeed);

internal sealed class SoftBoundaryContactManifold(
    SoftFragmentBody body,
    SoftBoundarySide side,
    BossFragmentPoint normal,
    bool isNewContact)
{
    private readonly SoftBoundaryContactPoint[] _points = new SoftBoundaryContactPoint[2];

    public SoftFragmentBody Body { get; } = body;
    public SoftBoundarySide Side { get; } = side;
    public BossFragmentPoint Normal { get; } = normal;
    public bool IsNewContact { get; } = isNewContact;
    public int PointCount { get; private set; }
    public float MaximumPenetration { get; private set; }
    public SoftBoundaryContactPoint this[int index] => _points[Math.Clamp(index, 0, PointCount - 1)];

    public void AddPoint(SoftBoundaryContactPoint point)
    {
        if (PointCount >= _points.Length)
        {
            return;
        }

        _points[PointCount++] = point;
        MaximumPenetration = Math.Max(MaximumPenetration, point.Penetration);
    }
}

internal readonly record struct SoftBoundaryVelocityResult(
    bool Bounced,
    float ClosingSpeed,
    float SeparatingSpeed);

internal sealed class SoftBoundaryContactSolver
{
    private const float Restitution = 0.48f;
    private const float Friction = 0.12f;
    private const float MaximumCorrectionRatio = 0.08f;
    private const float VisibleBounceClosingSpeed = 40f;

    private readonly Dictionary<SoftFragmentBody, SoftCollisionVertex[]> _hullBuffers = [];
    private readonly HashSet<(int Body, SoftBoundarySide Side)> _latchedContacts = [];
    private readonly List<SoftBoundaryContactManifold> _manifolds = [];

    public IReadOnlyList<SoftBoundaryContactManifold> Build(
        IReadOnlyList<SoftFragmentBody> bodies,
        SoftHorizontalBoundary boundary)
    {
        _manifolds.Clear();
        if (!boundary.IsValid)
        {
            return _manifolds;
        }

        for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
        {
            SoftFragmentBody body = bodies[bodyIndex];
            if (!body.HasFiniteState)
            {
                continue;
            }

            SoftCollisionVertex[] hull = GetHullBuffer(body);
            body.CopyDeformedHull(hull);
            AddSide(body, hull, SoftBoundarySide.Left, boundary.Left);
            AddSide(body, hull, SoftBoundarySide.Right, boundary.Right);
        }

        return _manifolds;
    }

    public static void SolvePositions(SoftBoundaryContactManifold manifold)
    {
        for (int pointIndex = 0; pointIndex < manifold.PointCount; pointIndex++)
        {
            SoftBoundaryContactPoint point = manifold[pointIndex];
            float inverseMass = manifold.Body.GetEffectiveInverseMass(point.U, point.V);
            if (inverseMass <= 0.0001f || point.Penetration <= 0f)
            {
                continue;
            }

            float maximumCorrection = manifold.Body.ShortDimension * MaximumCorrectionRatio;
            float correction = Math.Min(point.Penetration * 0.72f, maximumCorrection);
            manifold.Body.ApplyContactPositionImpulse(
                point.U,
                point.V,
                manifold.Normal,
                correction / inverseMass);
        }
    }

    public static SoftBoundaryVelocityResult SolveVelocities(
        SoftBoundaryContactManifold manifold)
    {
        bool bounced = false;
        float maximumClosing = 0f;
        float maximumSeparating = 0f;
        for (int pointIndex = 0; pointIndex < manifold.PointCount; pointIndex++)
        {
            SoftBoundaryContactPoint point = manifold[pointIndex];
            BossFragmentPoint velocity = manifold.Body.GetVelocityAt(point.U, point.V);
            float normalSpeed = Dot(velocity, manifold.Normal);
            float inverseMass = manifold.Body.GetEffectiveInverseMass(point.U, point.V);
            if (!float.IsFinite(normalSpeed)
                || !float.IsFinite(point.PreSolveNormalSpeed)
                || inverseMass <= 0.0001f)
            {
                continue;
            }

            maximumClosing = Math.Max(maximumClosing, Math.Max(0f, -point.PreSolveNormalSpeed));
            float targetNormalSpeed = manifold.IsNewContact && point.PreSolveNormalSpeed < -1f
                ? -Restitution * point.PreSolveNormalSpeed
                : 0f;
            float normalImpulse = Math.Max(0f, (targetNormalSpeed - normalSpeed) / inverseMass);
            if (normalImpulse <= 0.0001f)
            {
                continue;
            }

            manifold.Body.ApplyVelocityImpulse(
                point.U,
                point.V,
                manifold.Normal,
                normalImpulse);
            velocity = manifold.Body.GetVelocityAt(point.U, point.V);
            float separatingSpeed = Dot(velocity, manifold.Normal);
            maximumSeparating = Math.Max(maximumSeparating, separatingSpeed);
            bounced |= manifold.IsNewContact
                && point.PreSolveNormalSpeed <= -VisibleBounceClosingSpeed
                && separatingSpeed > 1f;

            BossFragmentPoint tangent = new(-manifold.Normal.Y, manifold.Normal.X);
            float tangentSpeed = Dot(velocity, tangent);
            float tangentImpulse = Math.Clamp(
                -tangentSpeed / inverseMass,
                -Friction * normalImpulse,
                Friction * normalImpulse);
            manifold.Body.ApplyVelocityImpulse(
                point.U,
                point.V,
                tangent,
                tangentImpulse);
        }

        return new SoftBoundaryVelocityResult(
            bounced,
            maximumClosing,
            maximumSeparating);
    }

    private void AddSide(
        SoftFragmentBody body,
        IReadOnlyList<SoftCollisionVertex> hull,
        SoftBoundarySide side,
        float boundary)
    {
        int firstIndex = -1;
        int secondIndex = -1;
        float firstPenetration = 0f;
        float secondPenetration = 0f;
        float sidePenetration = float.NegativeInfinity;
        for (int index = 0; index < hull.Count; index++)
        {
            float penetration = side == SoftBoundarySide.Left
                ? boundary - hull[index].Position.X
                : hull[index].Position.X - boundary;
            sidePenetration = Math.Max(sidePenetration, penetration);
            if (penetration <= 0f)
            {
                continue;
            }

            if (penetration > firstPenetration)
            {
                secondPenetration = firstPenetration;
                secondIndex = firstIndex;
                firstPenetration = penetration;
                firstIndex = index;
            }
            else if (penetration > secondPenetration)
            {
                secondPenetration = penetration;
                secondIndex = index;
            }
        }

        (int Body, SoftBoundarySide Side) key = (body.Id, side);
        if (firstIndex < 0)
        {
            float rearmDistance = Math.Max(2f, body.ShortDimension * 0.04f);
            if (-sidePenetration >= rearmDistance)
            {
                _latchedContacts.Remove(key);
            }

            return;
        }

        var manifold = new SoftBoundaryContactManifold(
            body,
            side,
            side == SoftBoundarySide.Left
                ? new BossFragmentPoint(1f, 0f)
                : new BossFragmentPoint(-1f, 0f),
            _latchedContacts.Add(key));
        AddPoint(manifold, hull[firstIndex], firstPenetration);
        if (secondIndex >= 0)
        {
            AddPoint(manifold, hull[secondIndex], secondPenetration);
        }

        _manifolds.Add(manifold);
    }

    private static void AddPoint(
        SoftBoundaryContactManifold manifold,
        SoftCollisionVertex vertex,
        float penetration)
    {
        float speed = Dot(
            manifold.Body.GetVelocityAt(vertex.U, vertex.V),
            manifold.Normal);
        manifold.AddPoint(new SoftBoundaryContactPoint(
            vertex.U,
            vertex.V,
            penetration,
            speed));
    }

    private SoftCollisionVertex[] GetHullBuffer(SoftFragmentBody body)
    {
        if (!_hullBuffers.TryGetValue(body, out SoftCollisionVertex[]? hull)
            || hull.Length != body.HullPointCount)
        {
            hull = new SoftCollisionVertex[body.HullPointCount];
            _hullBuffers[body] = hull;
        }

        return hull;
    }

    private static float Dot(BossFragmentPoint first, BossFragmentPoint second) =>
        first.X * second.X + first.Y * second.Y;
}
