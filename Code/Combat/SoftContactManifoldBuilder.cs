namespace NinjaSlayer.Code.Combat;

internal sealed class SoftContactManifoldBuilder
{
    private readonly Dictionary<SoftFragmentBody, SoftCollisionVertex[]> _hullBuffers = [];
    private readonly Dictionary<SoftFragmentBody, SoftCollisionVertex[]> _previousHullBuffers = [];
    private readonly Dictionary<SoftFragmentBody, SoftCollisionVertex[]> _sampleHullBuffers = [];
    private readonly HashSet<(int First, int Second)> _previousPairs = [];
    private readonly HashSet<(int First, int Second)> _currentPairs = [];
    private readonly HashSet<(int First, int Second)> _excludedPairs = [];
    private readonly HashSet<(int First, int Second)> _armedPairs = [];
    private readonly Dictionary<(int First, int Second), BossFragmentPoint> _previousNormals = [];
    private readonly Dictionary<(int First, int Second), BossFragmentPoint> _currentNormals = [];
    private readonly List<SoftContactManifold> _manifolds = [];

    private static readonly float[] TemporalSamples = [0.25f, 0.5f, 0.75f];
    private const int TimeOfImpactIterations = 8;

    public void BeginSubstep(IReadOnlyList<SoftRagdollLink> links)
    {
        _excludedPairs.Clear();
        _currentPairs.Clear();
        _currentNormals.Clear();
        for (int index = 0; index < links.Count; index++)
        {
            SoftRagdollLink link = links[index];
            if (!link.Broken)
            {
                (int First, int Second) key = PairKey(link.First, link.Second);
                _excludedPairs.Add(key);
                // A linked pair already has an explicit separation constraint. Once the
                // link breaks, collision must resume immediately instead of waiting for
                // the formerly connected outlines to separate first.
                _armedPairs.Add(key);
            }
        }
    }

    public IReadOnlyList<SoftContactManifold> Build(
        IReadOnlyList<(SoftFragmentBody First, SoftFragmentBody Second)> pairs,
        bool enableTemporalSampling)
    {
        _manifolds.Clear();
        for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            (SoftFragmentBody first, SoftFragmentBody second) = pairs[pairIndex];
            (int First, int Second) key = PairKey(first, second);
            if (_excludedPairs.Contains(key) || !first.CanCollide || !second.CanCollide)
            {
                continue;
            }

            SoftCollisionVertex[] firstHull = GetHullBuffer(first);
            SoftCollisionVertex[] secondHull = GetHullBuffer(second);
            SoftCollisionVertex[] firstPrevious = GetHullBuffer(_previousHullBuffers, first);
            SoftCollisionVertex[] secondPrevious = GetHullBuffer(_previousHullBuffers, second);
            first.CopyCollisionHull(firstHull);
            second.CopyCollisionHull(secondHull);
            first.CopyPreviousCollisionHull(firstPrevious);
            second.CopyPreviousCollisionHull(secondPrevious);
            if (!_armedPairs.Contains(key))
            {
                if (TryResolveCollision(
                        first,
                        second,
                        firstPrevious,
                        secondPrevious,
                        out _,
                        out _,
                        marginOverride: 0f))
                {
                    continue;
                }

                _armedPairs.Add(key);
            }

            bool isSwept = false;
            float timeOfImpact = 1f;
            bool colliding = TryResolveCollision(
                    first,
                    second,
                    firstHull,
                    secondHull,
                    out BossFragmentPoint normal,
                    out float penetration);
            IReadOnlyList<SoftCollisionVertex> contactFirstHull = firstHull;
            IReadOnlyList<SoftCollisionVertex> contactSecondHull = secondHull;
            if (!colliding && enableTemporalSampling)
            {
                SoftCollisionVertex[] firstSample = GetHullBuffer(_sampleHullBuffers, first);
                SoftCollisionVertex[] secondSample = GetHullBuffer(_sampleHullBuffers, second);
                float separatedAmount = 0f;
                for (int sampleIndex = 0; sampleIndex < TemporalSamples.Length; sampleIndex++)
                {
                    float amount = TemporalSamples[sampleIndex];
                    InterpolateHull(firstPrevious, firstHull, amount, firstSample);
                    InterpolateHull(secondPrevious, secondHull, amount, secondSample);
                    if (!TryResolveCollision(
                            first,
                            second,
                            firstSample,
                            secondSample,
                            out normal,
                            out penetration))
                    {
                        separatedAmount = amount;
                        continue;
                    }

                    float collisionAmount = amount;
                    for (int iteration = 0; iteration < TimeOfImpactIterations; iteration++)
                    {
                        float candidate = (separatedAmount + collisionAmount) * 0.5f;
                        InterpolateHull(firstPrevious, firstHull, candidate, firstSample);
                        InterpolateHull(secondPrevious, secondHull, candidate, secondSample);
                        if (TryResolveCollision(
                                first,
                                second,
                                firstSample,
                                secondSample,
                                out normal,
                                out penetration))
                        {
                            collisionAmount = candidate;
                        }
                        else
                        {
                            separatedAmount = candidate;
                        }
                    }

                    InterpolateHull(firstPrevious, firstHull, collisionAmount, firstSample);
                    InterpolateHull(secondPrevious, secondHull, collisionAmount, secondSample);
                    if (!TryResolveCollision(
                            first,
                            second,
                            firstSample,
                            secondSample,
                            out normal,
                            out penetration))
                    {
                        continue;
                    }
                    colliding = true;
                    isSwept = true;
                    timeOfImpact = separatedAmount;
                    contactFirstHull = firstSample;
                    contactSecondHull = secondSample;
                    break;
                }
            }

            if (!colliding)
            {
                continue;
            }

            bool hasReferenceNormal = _currentNormals.TryGetValue(
                key,
                out BossFragmentPoint referenceNormal);
            if (!hasReferenceNormal)
            {
                hasReferenceNormal = _previousNormals.TryGetValue(key, out referenceNormal);
            }

            if (hasReferenceNormal && Dot(normal, referenceNormal) < 0f)
            {
                normal = Multiply(normal, -1f);
            }

            var manifold = new SoftContactManifold(
                first,
                second,
                normal,
                !_previousPairs.Contains(key),
                isSwept,
                timeOfImpact);
            AddSupportContacts(
                manifold,
                contactFirstHull,
                contactSecondHull,
                penetration);
            if (manifold.PointCount == 0)
            {
                continue;
            }

            _currentPairs.Add(key);
            _currentNormals[key] = normal;
            _manifolds.Add(manifold);
        }

        return _manifolds;
    }

    public void EndSubstep()
    {
        _previousPairs.Clear();
        _previousNormals.Clear();
        foreach ((int First, int Second) pair in _currentPairs)
        {
            _previousPairs.Add(pair);
            _previousNormals[pair] = _currentNormals[pair];
        }
    }

    private static void AddSupportContacts(
        SoftContactManifold manifold,
        IReadOnlyList<SoftCollisionVertex> firstHull,
        IReadOnlyList<SoftCollisionVertex> secondHull,
        float penetration)
    {
        ResolveSupportEdge(firstHull, manifold.Normal, maximize: true, out int firstA, out int firstB);
        ResolveSupportEdge(secondHull, manifold.Normal, maximize: false, out int secondA, out int secondB);
        SoftCollisionVertex firstStart = firstHull[firstA];
        SoftCollisionVertex firstEnd = firstHull[firstB];
        SoftCollisionVertex secondStart = secondHull[secondA];
        SoftCollisionVertex secondEnd = secondHull[secondB];
        BossFragmentPoint tangent = Normalize(new BossFragmentPoint(-manifold.Normal.Y, manifold.Normal.X));
        float referenceMinimum = Math.Min(
            Dot(firstStart.Position, tangent),
            Dot(firstEnd.Position, tangent));
        float referenceMaximum = Math.Max(
            Dot(firstStart.Position, tangent),
            Dot(firstEnd.Position, tangent));

        AddClippedPoint(
            manifold,
            firstStart,
            firstEnd,
            secondStart,
            secondEnd,
            tangent,
            referenceMinimum,
            referenceMaximum,
            penetration);
        if (secondB != secondA)
        {
            AddClippedPoint(
                manifold,
                firstStart,
                firstEnd,
                secondEnd,
                secondStart,
                tangent,
                referenceMinimum,
                referenceMaximum,
                penetration);
        }
    }

    private static void AddClippedPoint(
        SoftContactManifold manifold,
        SoftCollisionVertex referenceStart,
        SoftCollisionVertex referenceEnd,
        SoftCollisionVertex incidentStart,
        SoftCollisionVertex incidentEnd,
        BossFragmentPoint tangent,
        float tangentMinimum,
        float tangentMaximum,
        float penetration)
    {
        float incidentProjection = Math.Clamp(
            Dot(incidentStart.Position, tangent),
            tangentMinimum,
            tangentMaximum);
        SoftCollisionVertex incident = InterpolateAtProjection(
            incidentStart,
            incidentEnd,
            tangent,
            incidentProjection);
        SoftCollisionVertex reference = InterpolateAtProjection(
            referenceStart,
            referenceEnd,
            tangent,
            incidentProjection);
        BossFragmentPoint relativeVelocity = Subtract(
            manifold.Second.GetVelocityAt(incident.U, incident.V),
            manifold.First.GetVelocityAt(reference.U, reference.V));
        manifold.AddPoint(new SoftContactPoint(
            reference.U,
            reference.V,
            incident.U,
            incident.V,
            Math.Max(0f, penetration / 2f),
            Dot(relativeVelocity, manifold.Normal)));
    }

    private static SoftCollisionVertex InterpolateAtProjection(
        SoftCollisionVertex start,
        SoftCollisionVertex end,
        BossFragmentPoint axis,
        float projection)
    {
        float startProjection = Dot(start.Position, axis);
        float endProjection = Dot(end.Position, axis);
        float denominator = endProjection - startProjection;
        float amount = MathF.Abs(denominator) <= 0.001f
            ? 0f
            : Math.Clamp((projection - startProjection) / denominator, 0f, 1f);
        return new SoftCollisionVertex(
            Lerp(start.Position, end.Position, amount),
            Lerp(start.U, end.U, amount),
            Lerp(start.V, end.V, amount));
    }

    private static bool TryResolveCollision(
        SoftFragmentBody first,
        SoftFragmentBody second,
        IReadOnlyList<SoftCollisionVertex> firstHull,
        IReadOnlyList<SoftCollisionVertex> secondHull,
        out BossFragmentPoint normal,
        out float penetration,
        float? marginOverride = null)
    {
        penetration = float.PositiveInfinity;
        normal = default;
        float margin = marginOverride
            ?? first.CollisionMargin * first.CollisionMarginScale
                + second.CollisionMargin * second.CollisionMarginScale;
        if (!TestAxes(firstHull, secondHull, margin, ref penetration, ref normal)
            || !TestAxes(secondHull, firstHull, margin, ref penetration, ref normal))
        {
            return false;
        }

        BossFragmentPoint direction = Subtract(
            AveragePosition(secondHull),
            AveragePosition(firstHull));
        if (LengthSquared(direction) <= 0.01f)
        {
            direction = Subtract(second.CenterVelocity, first.CenterVelocity);
        }

        if (LengthSquared(direction) <= 0.01f)
        {
            float angle = (first.Id * 0.754877666f + second.Id * 0.569840296f) * MathF.Tau;
            direction = new BossFragmentPoint(MathF.Cos(angle), MathF.Sin(angle));
        }

        if (Dot(normal, direction) < 0f)
        {
            normal = Multiply(normal, -1f);
        }

        return float.IsFinite(penetration) && penetration > 0f;
    }

    private static bool TestAxes(
        IReadOnlyList<SoftCollisionVertex> axesSource,
        IReadOnlyList<SoftCollisionVertex> other,
        float margin,
        ref float minimumOverlap,
        ref BossFragmentPoint minimumAxis)
    {
        for (int index = 0; index < axesSource.Count; index++)
        {
            BossFragmentPoint current = axesSource[index].Position;
            BossFragmentPoint next = axesSource[(index + 1) % axesSource.Count].Position;
            BossFragmentPoint edge = Subtract(next, current);
            float length = Length(edge);
            if (length <= 0.001f)
            {
                continue;
            }

            BossFragmentPoint axis = new(-edge.Y / length, edge.X / length);
            Project(axesSource, axis, out float firstMinimum, out float firstMaximum);
            Project(other, axis, out float secondMinimum, out float secondMaximum);
            float overlap = MathF.Min(firstMaximum, secondMaximum)
                - MathF.Max(firstMinimum, secondMinimum)
                + margin;
            if (overlap <= 0f)
            {
                return false;
            }

            if (overlap < minimumOverlap)
            {
                minimumOverlap = overlap;
                minimumAxis = axis;
            }
        }

        return float.IsFinite(minimumOverlap);
    }

    private static void ResolveSupportEdge(
        IReadOnlyList<SoftCollisionVertex> hull,
        BossFragmentPoint normal,
        bool maximize,
        out int first,
        out int second)
    {
        first = 0;
        second = hull.Count > 1 ? 1 : 0;
        float best = maximize ? float.NegativeInfinity : float.PositiveInfinity;
        for (int index = 0; index < hull.Count; index++)
        {
            int next = (index + 1) % hull.Count;
            float projection = (Dot(hull[index].Position, normal)
                + Dot(hull[next].Position, normal)) * 0.5f;
            bool better = maximize ? projection > best : projection < best;
            if (better)
            {
                best = projection;
                first = index;
                second = next;
            }
        }
    }

    private static void Project(
        IReadOnlyList<SoftCollisionVertex> polygon,
        BossFragmentPoint axis,
        out float minimum,
        out float maximum)
    {
        minimum = Dot(polygon[0].Position, axis);
        maximum = minimum;
        for (int index = 1; index < polygon.Count; index++)
        {
            float projection = Dot(polygon[index].Position, axis);
            minimum = Math.Min(minimum, projection);
            maximum = Math.Max(maximum, projection);
        }
    }

    private SoftCollisionVertex[] GetHullBuffer(SoftFragmentBody body)
    {
        return GetHullBuffer(_hullBuffers, body);
    }

    private static SoftCollisionVertex[] GetHullBuffer(
        Dictionary<SoftFragmentBody, SoftCollisionVertex[]> buffers,
        SoftFragmentBody body)
    {
        if (!buffers.TryGetValue(body, out SoftCollisionVertex[]? hull)
            || hull.Length != body.HullPointCount)
        {
            hull = new SoftCollisionVertex[body.HullPointCount];
            buffers[body] = hull;
        }

        return hull;
    }

    private static void InterpolateHull(
        SoftCollisionVertex[] previous,
        SoftCollisionVertex[] current,
        float amount,
        SoftCollisionVertex[] destination)
    {
        int count = Math.Min(destination.Length, Math.Min(previous.Length, current.Length));
        for (int index = 0; index < count; index++)
        {
            destination[index] = new SoftCollisionVertex(
                Lerp(previous[index].Position, current[index].Position, amount),
                Lerp(previous[index].U, current[index].U, amount),
                Lerp(previous[index].V, current[index].V, amount));
        }
    }

    private static (int First, int Second) PairKey(SoftFragmentBody first, SoftFragmentBody second) =>
        first.Id < second.Id ? (first.Id, second.Id) : (second.Id, first.Id);

    private static BossFragmentPoint Lerp(
        BossFragmentPoint first,
        BossFragmentPoint second,
        float amount) =>
        new(Lerp(first.X, second.X, amount), Lerp(first.Y, second.Y, amount));

    private static float Lerp(float first, float second, float amount) =>
        first + (second - first) * amount;

    private static BossFragmentPoint Subtract(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X - second.X, first.Y - second.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float scalar) =>
        new(point.X * scalar, point.Y * scalar);

    private static float Dot(BossFragmentPoint first, BossFragmentPoint second) =>
        first.X * second.X + first.Y * second.Y;

    private static float Length(BossFragmentPoint point) => MathF.Sqrt(LengthSquared(point));

    private static float LengthSquared(BossFragmentPoint point) =>
        point.X * point.X + point.Y * point.Y;

    private static BossFragmentPoint Normalize(BossFragmentPoint point)
    {
        float length = Length(point);
        return length <= 0.001f ? new BossFragmentPoint(1f, 0f) : Multiply(point, 1f / length);
    }

    private static BossFragmentPoint AveragePosition(
        IReadOnlyList<SoftCollisionVertex> hull)
    {
        BossFragmentPoint sum = default;
        for (int index = 0; index < hull.Count; index++)
        {
            sum = new BossFragmentPoint(
                sum.X + hull[index].Position.X,
                sum.Y + hull[index].Position.Y);
        }

        return hull.Count == 0
            ? default
            : Multiply(sum, 1f / hull.Count);
    }
}
