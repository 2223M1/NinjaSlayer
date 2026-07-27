namespace NinjaSlayer.Code.Combat;

internal sealed class BossSoftBodySolver
{
    public const int Substeps = 2;
    public const int ConstraintIterations = 6;

    private const float FrameShapeMatchingStrength = 0.34f;
    private const float Restitution = 0.48f;
    private const float Friction = 0.22f;

    private readonly SoftCollisionBroadphase _broadphase = new();
    private readonly Dictionary<SoftFragmentBody, BossFragmentPoint[]> _hullBuffers = [];
    private readonly List<SoftContact> _contacts = [];

    public SoftBodyStepMetrics Step(
        IReadOnlyList<SoftFragmentBody> bodies,
        IReadOnlyList<SoftRagdollLink> links,
        float seconds,
        float gravity,
        float airDrag,
        float? floorY = null)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f)
        {
            return default;
        }

        int contacts = 0;
        int brokenLinks = 0;
        float substepSeconds = seconds / Substeps;
        float frameStrength = 1f - MathF.Pow(
            1f - FrameShapeMatchingStrength,
            Math.Max(seconds, 0f) * 60f);
        float shapeStrength = 1f - MathF.Pow(
            1f - frameStrength,
            1f / (Substeps * ConstraintIterations));
        for (int substep = 0; substep < Substeps; substep++)
        {
            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                if (!bodies[bodyIndex].HasFiniteState)
                {
                    continue;
                }

                bodies[bodyIndex].BeginSubstep();
                bodies[bodyIndex].Predict(substepSeconds, gravity, airDrag);
            }

            for (int linkIndex = 0; linkIndex < links.Count; linkIndex++)
            {
                links[linkIndex].BeginSubstep();
            }

            IReadOnlyList<(SoftFragmentBody First, SoftFragmentBody Second)> pairs =
                _broadphase.BuildPairs(bodies);
            for (int iteration = 0; iteration < ConstraintIterations; iteration++)
            {
                for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
                {
                    if (bodies[bodyIndex].HasFiniteState)
                    {
                        bodies[bodyIndex].SolveInternalConstraints(substepSeconds, shapeStrength);
                    }
                }

                for (int linkIndex = 0; linkIndex < links.Count; linkIndex++)
                {
                    if (links[linkIndex].Solve(substepSeconds))
                    {
                        brokenLinks++;
                    }
                }

                BuildContacts(pairs);
                contacts += _contacts.Count;
                for (int contactIndex = 0; contactIndex < _contacts.Count; contactIndex++)
                {
                    SolveContactPosition(_contacts[contactIndex]);
                }

                if (floorY.HasValue)
                {
                    for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
                    {
                        if (bodies[bodyIndex].HasFiniteState)
                        {
                            contacts += bodies[bodyIndex].ConstrainToFloor(floorY.Value);
                        }
                    }
                }
            }

            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                if (bodies[bodyIndex].HasFiniteState)
                {
                    bodies[bodyIndex].FinalizeVelocities(substepSeconds);
                }
            }

            for (int contactIndex = 0; contactIndex < _contacts.Count; contactIndex++)
            {
                SolveContactVelocity(_contacts[contactIndex]);
            }

            for (int contactIndex = 0; contactIndex < _contacts.Count; contactIndex++)
            {
                SoftContact contact = _contacts[contactIndex];
                if (contactIndex > 0
                    && ReferenceEquals(_contacts[contactIndex - 1].First, contact.First)
                    && ReferenceEquals(_contacts[contactIndex - 1].Second, contact.Second))
                {
                    continue;
                }

                SolveCenterVelocity(contact);
            }

            if (floorY.HasValue)
            {
                for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
                {
                    if (bodies[bodyIndex].HasFiniteState)
                    {
                        bodies[bodyIndex].ApplyFloorVelocity(floorY.Value);
                    }
                }
            }
        }

        return new SoftBodyStepMetrics(contacts, brokenLinks);
    }

    private void BuildContacts(
        IReadOnlyList<(SoftFragmentBody First, SoftFragmentBody Second)> pairs)
    {
        _contacts.Clear();
        for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            (SoftFragmentBody first, SoftFragmentBody second) = pairs[pairIndex];
            BossFragmentPoint[] firstHull = GetHullBuffer(first);
            BossFragmentPoint[] secondHull = GetHullBuffer(second);
            first.CopyCollisionHull(firstHull);
            second.CopyCollisionHull(secondHull);
            if (!TryResolveCollision(
                    first,
                    second,
                    firstHull,
                    secondHull,
                    out BossFragmentPoint normal,
                    out float penetration))
            {
                continue;
            }

            ResolveSupportIndices(firstHull, normal, maximize: true, out int firstA, out int firstB);
            ResolveSupportIndices(secondHull, normal, maximize: false, out int secondA, out int secondB);
            AddContact(first, second, firstA, secondA, normal, penetration * 0.5f);
            if (firstB != firstA || secondB != secondA)
            {
                AddContact(first, second, firstB, secondB, normal, penetration * 0.5f);
            }
        }
    }

    private void AddContact(
        SoftFragmentBody first,
        SoftFragmentBody second,
        int firstHullIndex,
        int secondHullIndex,
        BossFragmentPoint normal,
        float penetration)
    {
        SoftBodyHullPoint firstPoint = first.GetHullPoint(firstHullIndex);
        SoftBodyHullPoint secondPoint = second.GetHullPoint(secondHullIndex);
        BossFragmentPoint relativeVelocity = Subtract(
            second.GetVelocityAt(secondPoint.U, secondPoint.V),
            first.GetVelocityAt(firstPoint.U, firstPoint.V));
        BossFragmentPoint relativeCenterVelocity = Subtract(
            second.CenterVelocity,
            first.CenterVelocity);
        _contacts.Add(new SoftContact(
            first,
            second,
            firstPoint.U,
            firstPoint.V,
            secondPoint.U,
            secondPoint.V,
            normal,
            Math.Min(Math.Max(0f, penetration), 72f),
            Dot(relativeVelocity, normal),
            Dot(relativeCenterVelocity, normal)));
    }

    private static void SolveContactPosition(SoftContact contact)
    {
        float firstInverseMass = contact.First.GetEffectiveInverseMass(contact.FirstU, contact.FirstV);
        float secondInverseMass = contact.Second.GetEffectiveInverseMass(contact.SecondU, contact.SecondV);
        float denominator = firstInverseMass + secondInverseMass;
        if (denominator <= 0.0001f || contact.Penetration <= 0f)
        {
            return;
        }

        float lambda = contact.Penetration * 0.72f / denominator;
        contact.First.ApplyContactPositionImpulse(
            contact.FirstU,
            contact.FirstV,
            Multiply(contact.Normal, -1f),
            lambda);
        contact.Second.ApplyContactPositionImpulse(
            contact.SecondU,
            contact.SecondV,
            contact.Normal,
            lambda);
    }

    private static void SolveContactVelocity(SoftContact contact)
    {
        BossFragmentPoint firstVelocity = contact.First.GetVelocityAt(contact.FirstU, contact.FirstV);
        BossFragmentPoint secondVelocity = contact.Second.GetVelocityAt(contact.SecondU, contact.SecondV);
        BossFragmentPoint relative = Subtract(secondVelocity, firstVelocity);
        float normalSpeed = Dot(relative, contact.Normal);
        if (!float.IsFinite(normalSpeed)
            || !float.IsFinite(contact.PreSolveNormalSpeed))
        {
            return;
        }

        float firstInverseMass = contact.First.GetEffectiveInverseMass(contact.FirstU, contact.FirstV);
        float secondInverseMass = contact.Second.GetEffectiveInverseMass(contact.SecondU, contact.SecondV);
        float denominator = firstInverseMass + secondInverseMass;
        if (denominator <= 0.0001f)
        {
            return;
        }

        float targetNormalSpeed = contact.PreSolveNormalSpeed < -0.5f
            ? -Restitution * contact.PreSolveNormalSpeed
            : Math.Max(0f, contact.PreSolveNormalSpeed);
        float normalImpulse = (targetNormalSpeed - normalSpeed) / denominator;
        if (!float.IsFinite(normalImpulse))
        {
            return;
        }

        if (MathF.Abs(normalImpulse) > 0.0001f)
        {
            ApplyImpulsePair(contact, contact.Normal, normalImpulse);
        }

        BossFragmentPoint tangentVelocity = Subtract(
            relative,
            Multiply(contact.Normal, Dot(relative, contact.Normal)));
        float tangentLength = Length(tangentVelocity);
        if (tangentLength <= 0.001f)
        {
            return;
        }

        BossFragmentPoint tangent = Multiply(tangentVelocity, 1f / tangentLength);
        float tangentImpulse = Math.Clamp(
            -Dot(relative, tangent) / denominator,
            -Friction * MathF.Abs(normalImpulse),
            Friction * MathF.Abs(normalImpulse));
        ApplyImpulsePair(contact, tangent, tangentImpulse);
    }

    private static void ApplyImpulsePair(
        SoftContact contact,
        BossFragmentPoint direction,
        float impulse)
    {
        contact.First.ApplyVelocityImpulse(
            contact.FirstU,
            contact.FirstV,
            direction,
            -impulse);
        contact.Second.ApplyVelocityImpulse(
            contact.SecondU,
            contact.SecondV,
            direction,
            impulse);
    }

    private static void SolveCenterVelocity(SoftContact contact)
    {
        BossFragmentPoint relative = Subtract(
            contact.Second.CenterVelocity,
            contact.First.CenterVelocity);
        float normalSpeed = Dot(relative, contact.Normal);
        if (!float.IsFinite(normalSpeed)
            || !float.IsFinite(contact.PreSolveCenterNormalSpeed))
        {
            return;
        }

        float targetNormalSpeed = contact.PreSolveCenterNormalSpeed < -0.5f
            ? -Restitution * contact.PreSolveCenterNormalSpeed
            : Math.Max(0f, contact.PreSolveCenterNormalSpeed);
        float inverseMass = 1f / contact.First.Mass + 1f / contact.Second.Mass;
        float impulse = (targetNormalSpeed - normalSpeed) / inverseMass;
        if (!float.IsFinite(impulse) || MathF.Abs(impulse) <= 0.0001f)
        {
            return;
        }

        contact.First.ApplyCenterVelocityImpulse(contact.Normal, -impulse);
        contact.Second.ApplyCenterVelocityImpulse(contact.Normal, impulse);
    }

    private static bool TryResolveCollision(
        SoftFragmentBody first,
        SoftFragmentBody second,
        IReadOnlyList<BossFragmentPoint> firstHull,
        IReadOnlyList<BossFragmentPoint> secondHull,
        out BossFragmentPoint normal,
        out float penetration)
    {
        penetration = float.PositiveInfinity;
        normal = default;
        float margin = first.CollisionMargin * first.CollisionMarginScale
            + second.CollisionMargin * second.CollisionMarginScale;
        if (!TestAxes(firstHull, secondHull, margin, ref penetration, ref normal)
            || !TestAxes(secondHull, firstHull, margin, ref penetration, ref normal))
        {
            return false;
        }

        BossFragmentPoint direction = Subtract(second.Center, first.Center);
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
        IReadOnlyList<BossFragmentPoint> axesSource,
        IReadOnlyList<BossFragmentPoint> other,
        float margin,
        ref float minimumOverlap,
        ref BossFragmentPoint minimumAxis)
    {
        for (int index = 0; index < axesSource.Count; index++)
        {
            BossFragmentPoint current = axesSource[index];
            BossFragmentPoint next = axesSource[(index + 1) % axesSource.Count];
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

    private static void Project(
        IReadOnlyList<BossFragmentPoint> polygon,
        BossFragmentPoint axis,
        out float minimum,
        out float maximum)
    {
        minimum = Dot(polygon[0], axis);
        maximum = minimum;
        for (int index = 1; index < polygon.Count; index++)
        {
            float projection = Dot(polygon[index], axis);
            minimum = Math.Min(minimum, projection);
            maximum = Math.Max(maximum, projection);
        }
    }

    private static void ResolveSupportIndices(
        IReadOnlyList<BossFragmentPoint> hull,
        BossFragmentPoint normal,
        bool maximize,
        out int best,
        out int second)
    {
        best = 0;
        second = 0;
        float bestProjection = maximize ? float.NegativeInfinity : float.PositiveInfinity;
        float secondProjection = bestProjection;
        for (int index = 0; index < hull.Count; index++)
        {
            float projection = Dot(hull[index], normal);
            bool isBetter = maximize ? projection > bestProjection : projection < bestProjection;
            if (isBetter)
            {
                second = best;
                secondProjection = bestProjection;
                best = index;
                bestProjection = projection;
                continue;
            }

            bool isSecond = maximize ? projection > secondProjection : projection < secondProjection;
            if (isSecond)
            {
                second = index;
                secondProjection = projection;
            }
        }
    }

    private BossFragmentPoint[] GetHullBuffer(SoftFragmentBody body)
    {
        if (!_hullBuffers.TryGetValue(body, out BossFragmentPoint[]? hull)
            || hull.Length != body.HullPointCount)
        {
            hull = new BossFragmentPoint[body.HullPointCount];
            _hullBuffers[body] = hull;
        }

        return hull;
    }

    private static BossFragmentPoint Subtract(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X - second.X, first.Y - second.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float value) =>
        new(point.X * value, point.Y * value);

    private static float Dot(BossFragmentPoint first, BossFragmentPoint second) =>
        first.X * second.X + first.Y * second.Y;

    private static float Length(BossFragmentPoint point) => MathF.Sqrt(LengthSquared(point));

    private static float LengthSquared(BossFragmentPoint point) =>
        point.X * point.X + point.Y * point.Y;

    private readonly record struct SoftContact(
        SoftFragmentBody First,
        SoftFragmentBody Second,
        float FirstU,
        float FirstV,
        float SecondU,
        float SecondV,
        BossFragmentPoint Normal,
        float Penetration,
        float PreSolveNormalSpeed,
        float PreSolveCenterNormalSpeed);
}
