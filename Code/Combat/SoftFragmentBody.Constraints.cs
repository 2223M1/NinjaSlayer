namespace NinjaSlayer.Code.Combat;

internal sealed partial class SoftFragmentBody
{
    private void SolveDistance(ref DistanceConstraint constraint, float seconds)
    {
        SoftParticle first = _particles[constraint.First];
        SoftParticle second = _particles[constraint.Second];
        BossFragmentPoint delta = Subtract(second.Position, first.Position);
        float length = Length(delta);
        if (length <= 0.0001f)
        {
            return;
        }

        float inverseMass = first.InverseMass + second.InverseMass;
        float alpha = (constraint.Compliance / Mass)
            / Math.Max(seconds * seconds, 0.000001f);
        float target = constraint.RestLength * TargetLinearScale;
        float value = length - target;
        float deltaLambda = (-value - alpha * constraint.Lambda) / (inverseMass + alpha);
        constraint.Lambda += deltaLambda;
        BossFragmentPoint normal = Multiply(delta, 1f / length);
        first.Position = Add(first.Position, Multiply(normal, -first.InverseMass * deltaLambda));
        second.Position = Add(second.Position, Multiply(normal, second.InverseMass * deltaLambda));
        _particles[constraint.First] = first;
        _particles[constraint.Second] = second;
    }

    private void SolveArea(ref QuadAreaConstraint constraint, float seconds)
    {
        float orientation = constraint.RestArea < 0f ? -1f : 1f;
        float restArea = MathF.Abs(constraint.RestArea);
        float target = restArea * TargetLinearScale * TargetLinearScale;
        SoftParticle p0 = _particles[constraint.I0];
        SoftParticle p1 = _particles[constraint.I1];
        SoftParticle p2 = _particles[constraint.I2];
        SoftParticle p3 = _particles[constraint.I3];
        float area = QuadArea(p0.Position, p1.Position, p2.Position, p3.Position)
            * orientation;
        BossFragmentPoint gradient0 = Multiply(
            Perpendicular(Subtract(p1.Position, p3.Position)),
            0.5f * orientation);
        BossFragmentPoint gradient1 = Multiply(
            Perpendicular(Subtract(p2.Position, p0.Position)),
            0.5f * orientation);
        BossFragmentPoint gradient2 = Multiply(
            Perpendicular(Subtract(p3.Position, p1.Position)),
            0.5f * orientation);
        BossFragmentPoint gradient3 = Multiply(
            Perpendicular(Subtract(p0.Position, p2.Position)),
            0.5f * orientation);
        float denominator = p0.InverseMass * LengthSquared(gradient0)
            + p1.InverseMass * LengthSquared(gradient1)
            + p2.InverseMass * LengthSquared(gradient2)
            + p3.InverseMass * LengthSquared(gradient3);
        float normalizedCompliance = _material.AreaCompliance
            * CharacteristicLength
            * CharacteristicLength
            / Mass;
        float alpha = normalizedCompliance / Math.Max(seconds * seconds, 0.000001f);
        float deltaLambda = (-(area - target) - alpha * constraint.Lambda)
            / Math.Max(denominator + alpha, 0.0001f);
        constraint.Lambda += deltaLambda;
        p0.Position = Add(p0.Position, Multiply(gradient0, p0.InverseMass * deltaLambda));
        p1.Position = Add(p1.Position, Multiply(gradient1, p1.InverseMass * deltaLambda));
        p2.Position = Add(p2.Position, Multiply(gradient2, p2.InverseMass * deltaLambda));
        p3.Position = Add(p3.Position, Multiply(gradient3, p3.InverseMass * deltaLambda));
        _particles[constraint.I0] = p0;
        _particles[constraint.I1] = p1;
        _particles[constraint.I2] = p2;
        _particles[constraint.I3] = p3;
    }

    private void SolveAreaValue(
        int i0,
        int i1,
        int i2,
        float target,
        float orientation,
        float compliance,
        float seconds,
        ref float lambda,
        bool accumulate)
    {
        SoftParticle p0 = _particles[i0];
        SoftParticle p1 = _particles[i1];
        SoftParticle p2 = _particles[i2];
        float area = TriangleArea(p0.Position, p1.Position, p2.Position) * orientation;
        BossFragmentPoint gradient0 = Multiply(
            Perpendicular(Subtract(p1.Position, p2.Position)),
            0.5f * orientation);
        BossFragmentPoint gradient1 = Multiply(
            Perpendicular(Subtract(p2.Position, p0.Position)),
            0.5f * orientation);
        BossFragmentPoint gradient2 = Multiply(
            Perpendicular(Subtract(p0.Position, p1.Position)),
            0.5f * orientation);
        float denominator = p0.InverseMass * LengthSquared(gradient0)
            + p1.InverseMass * LengthSquared(gradient1)
            + p2.InverseMass * LengthSquared(gradient2);
        float alpha = compliance / Math.Max(seconds * seconds, 0.000001f);
        float previousLambda = accumulate ? lambda : 0f;
        float deltaLambda = (-(area - target) - alpha * previousLambda) / Math.Max(denominator + alpha, 0.0001f);
        if (accumulate)
        {
            lambda += deltaLambda;
        }

        p0.Position = Add(p0.Position, Multiply(gradient0, p0.InverseMass * deltaLambda));
        p1.Position = Add(p1.Position, Multiply(gradient1, p1.InverseMass * deltaLambda));
        p2.Position = Add(p2.Position, Multiply(gradient2, p2.InverseMass * deltaLambda));
        _particles[i0] = p0;
        _particles[i1] = p1;
        _particles[i2] = p2;
    }

    private void ApplyShapeMemoryForces(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f)
        {
            return;
        }

        BossFragmentPoint center = Center;
        BossFragmentPoint centerVelocity = CenterVelocity;
        float rotation = ResolveBestFitRotation();
        float angularVelocity = ResolveAngularVelocity();
        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        float omega = MathF.Tau * Math.Clamp(_material.ShapeMemoryFrequencyHz, 0.5f, 6f);
        float damping = 2f * Math.Clamp(_material.ShapeMemoryDampingRatio, 0f, 1f) * omega;
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            BossFragmentPoint rest = Multiply(
                Subtract(particle.RestPosition, _restCenter),
                TargetLinearScale);
            BossFragmentPoint goal = new(
                center.X + rest.X * cosine - rest.Y * sine,
                center.Y + rest.X * sine + rest.Y * cosine);
            BossFragmentPoint radius = Subtract(particle.Position, center);
            BossFragmentPoint rigidVelocity = Add(
                centerVelocity,
                new BossFragmentPoint(
                    -radius.Y * angularVelocity,
                    radius.X * angularVelocity));
            BossFragmentPoint residual = Subtract(particle.Position, goal);
            BossFragmentPoint residualVelocity = Subtract(particle.Velocity, rigidVelocity);
            _workVectors[index] = Add(
                Multiply(residual, -omega * omega),
                Multiply(residualVelocity, -damping));
        }

        RemoveRigidVectorComponents(_workVectors, center);
        float maximumAcceleration = ShortDimension * omega * omega * 0.72f;
        for (int index = 0; index < ParticleCount; index++)
        {
            BossFragmentPoint acceleration = ClampLength(
                _workVectors[index],
                maximumAcceleration);
            SoftParticle particle = _particles[index];
            particle.Velocity = Add(
                particle.Velocity,
                Multiply(acceleration, seconds));
            _particles[index] = particle;
        }
    }

    private void ApplyUniformScaleExpansion(float scale)
    {
        if (!float.IsFinite(scale) || scale <= 1f)
        {
            return;
        }

        BossFragmentPoint center = Center;
        BossFragmentPoint centerVelocity = CenterVelocity;
        float angularVelocity = ResolveAngularVelocity();
        BossFragmentPoint previousCenter = default;
        for (int index = 0; index < ParticleCount; index++)
        {
            previousCenter = Add(previousCenter, _particles[index].PreviousPosition);
            BossFragmentPoint radius = Subtract(_particles[index].Position, center);
            BossFragmentPoint rigidVelocity = Add(
                centerVelocity,
                new BossFragmentPoint(
                    -radius.Y * angularVelocity,
                    radius.X * angularVelocity));
            _workVectors[index] = Subtract(_particles[index].Velocity, rigidVelocity);
        }

        previousCenter = Multiply(previousCenter, 1f / ParticleCount);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.Position = Add(
                center,
                Multiply(Subtract(particle.Position, center), scale));
            particle.PreviousPosition = Add(
                previousCenter,
                Multiply(Subtract(particle.PreviousPosition, previousCenter), scale));
            BossFragmentPoint radius = Subtract(particle.Position, center);
            BossFragmentPoint rigidVelocity = Add(
                centerVelocity,
                new BossFragmentPoint(
                    -radius.Y * angularVelocity,
                    radius.X * angularVelocity));
            particle.Velocity = Add(
                rigidVelocity,
                Multiply(_workVectors[index], scale));
            _particles[index] = particle;
        }
    }

    private bool ProjectDistanceSafety(DistanceConstraint constraint)
    {
        SoftParticle first = _particles[constraint.First];
        SoftParticle second = _particles[constraint.Second];
        BossFragmentPoint delta = Subtract(second.Position, first.Position);
        float length = Length(delta);
        if (length <= 0.0001f)
        {
            return false;
        }

        float target = constraint.RestLength * TargetLinearScale;
        float bounded = Math.Clamp(
            length,
            target * _material.MinimumEdgeRatio,
            target * _material.MaximumEdgeRatio);
        if (MathF.Abs(length - bounded) <= 0.0001f)
        {
            return false;
        }

        float inverseMass = first.InverseMass + second.InverseMass;
        if (inverseMass <= 0.0001f)
        {
            return false;
        }

        BossFragmentPoint normal = Multiply(delta, 1f / length);
        float correction = (bounded - length) / inverseMass;
        first.Position = Add(first.Position, Multiply(normal, -first.InverseMass * correction));
        second.Position = Add(second.Position, Multiply(normal, second.InverseMass * correction));
        _particles[constraint.First] = first;
        _particles[constraint.Second] = second;
        return true;
    }

    private float ResolveMaximumPredictedParticleSpeed(float seconds)
    {
        float inverseSeconds = 1f / Math.Max(seconds, 0.0001f);
        float maximum = 0f;
        for (int index = 0; index < ParticleCount; index++)
        {
            maximum = Math.Max(
                maximum,
                Length(Subtract(
                    _particles[index].Position,
                    _particles[index].PreviousPosition)) * inverseSeconds);
        }

        return maximum;
    }

    private void DampNonRigidVelocity(float residualMultiplier)
    {
        residualMultiplier = Math.Clamp(residualMultiplier, 0f, 1f);
        BossFragmentPoint center = Center;
        BossFragmentPoint centerVelocity = CenterVelocity;
        float angularVelocity = ResolveAngularVelocity();
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            BossFragmentPoint radius = Subtract(particle.Position, center);
            BossFragmentPoint rigidVelocity = Add(
                centerVelocity,
                new BossFragmentPoint(
                    -radius.Y * angularVelocity,
                    radius.X * angularVelocity));
            BossFragmentPoint residual = Subtract(particle.Velocity, rigidVelocity);
            particle.Velocity = Add(rigidVelocity, Multiply(residual, residualMultiplier));
            _particles[index] = particle;
        }
    }

    private float ResolveAngularVelocity()
    {
        BossFragmentPoint center = Center;
        BossFragmentPoint centerVelocity = CenterVelocity;
        float numerator = 0f;
        float denominator = 0f;
        for (int index = 0; index < ParticleCount; index++)
        {
            BossFragmentPoint radius = Subtract(_particles[index].Position, center);
            BossFragmentPoint relativeVelocity = Subtract(
                _particles[index].Velocity,
                centerVelocity);
            numerator += Cross(radius, relativeVelocity);
            denominator += LengthSquared(radius);
        }

        return denominator <= 0.001f ? 0f : numerator / denominator;
    }

    private float ResolveBestFitRotation()
    {
        BossFragmentPoint center = Center;
        float covarianceA = 0f;
        float covarianceB = 0f;
        for (int index = 0; index < ParticleCount; index++)
        {
            BossFragmentPoint rest = Subtract(_particles[index].RestPosition, _restCenter);
            BossFragmentPoint current = Subtract(_particles[index].Position, center);
            covarianceA += rest.X * current.X + rest.Y * current.Y;
            covarianceB += rest.X * current.Y - rest.Y * current.X;
        }

        return MathF.Atan2(covarianceB, covarianceA);
    }

    private void ApplyPositionImpulse(
        int index,
        float weight,
        BossFragmentPoint direction,
        float lambda,
        bool preserveVelocity)
    {
        SoftParticle particle = _particles[index];
        BossFragmentPoint correction = Multiply(
            direction,
            particle.InverseMass * weight * lambda);
        particle.Position = Add(particle.Position, correction);
        if (preserveVelocity)
        {
            particle.PreviousPosition = Add(particle.PreviousPosition, correction);
        }

        _particles[index] = particle;
    }

    private void ApplyVelocityImpulse(
        int index,
        float weight,
        BossFragmentPoint direction,
        float impulse)
    {
        SoftParticle particle = _particles[index];
        particle.Velocity = Add(
            particle.Velocity,
            Multiply(direction, particle.InverseMass * weight * impulse));
        _particles[index] = particle;
    }

    private DistanceConstraint[] BuildDistanceConstraints()
    {
        var constraints = new List<DistanceConstraint>(49);
        for (int row = 0; row < GridSize; row++)
        {
            for (int column = 0; column < GridSize; column++)
            {
                if (column + 1 < GridSize)
                {
                    AddDistance(
                        constraints,
                        column,
                        row,
                        column + 1,
                        row,
                        DistanceConstraintKind.Structural);
                }

                if (row + 1 < GridSize)
                {
                    AddDistance(
                        constraints,
                        column,
                        row,
                        column,
                        row + 1,
                        DistanceConstraintKind.Structural);
                }

                if (column + 1 < GridSize && row + 1 < GridSize)
                {
                    if ((column + row & 1) == 0)
                    {
                        AddDistance(
                            constraints,
                            column,
                            row,
                            column + 1,
                            row + 1,
                            DistanceConstraintKind.Shear);
                    }
                    else
                    {
                        AddDistance(
                            constraints,
                            column + 1,
                            row,
                            column,
                            row + 1,
                            DistanceConstraintKind.Shear);
                    }
                }

                if (column + 2 < GridSize)
                {
                    AddDistance(
                        constraints,
                        column,
                        row,
                        column + 2,
                        row,
                        DistanceConstraintKind.Bend);
                }

                if (row + 2 < GridSize)
                {
                    AddDistance(
                        constraints,
                        column,
                        row,
                        column,
                        row + 2,
                        DistanceConstraintKind.Bend);
                }
            }
        }

        return constraints.ToArray();
    }

    private QuadAreaConstraint[] BuildAreaConstraints()
    {
        var constraints = new List<QuadAreaConstraint>(9);
        for (int row = 0; row < GridSize - 1; row++)
        {
            for (int column = 0; column < GridSize - 1; column++)
            {
                int i0 = Index(column, row);
                int i1 = Index(column + 1, row);
                int i2 = Index(column + 1, row + 1);
                int i3 = Index(column, row + 1);
                constraints.Add(new QuadAreaConstraint(
                    i0,
                    i1,
                    i2,
                    i3,
                    QuadArea(
                        _particles[i0].RestPosition,
                        _particles[i1].RestPosition,
                        _particles[i2].RestPosition,
                        _particles[i3].RestPosition)));
            }
        }

        return constraints.ToArray();
    }

    private TriangleBarrier[] BuildTriangleBarriers()
    {
        var barriers = new List<TriangleBarrier>(18);
        for (int row = 0; row < GridSize - 1; row++)
        {
            for (int column = 0; column < GridSize - 1; column++)
            {
                int i0 = Index(column, row);
                int i1 = Index(column + 1, row);
                int i2 = Index(column + 1, row + 1);
                int i3 = Index(column, row + 1);
                barriers.Add(CreateTriangleBarrier(i0, i1, i2));
                barriers.Add(CreateTriangleBarrier(i0, i2, i3));
            }
        }

        return barriers.ToArray();
    }

    private TriangleBarrier CreateTriangleBarrier(int i0, int i1, int i2) => new(
        i0,
        i1,
        i2,
        TriangleArea(
            _particles[i0].RestPosition,
            _particles[i1].RestPosition,
            _particles[i2].RestPosition));

    private void AddDistance(
        ICollection<DistanceConstraint> constraints,
        int firstColumn,
        int firstRow,
        int secondColumn,
        int secondRow,
        DistanceConstraintKind kind)
    {
        int first = Index(firstColumn, firstRow);
        int second = Index(secondColumn, secondRow);
        constraints.Add(new DistanceConstraint(
            first,
            second,
            Length(Subtract(
                _particles[second].RestPosition,
                _particles[first].RestPosition)),
            ResolveCompliance(kind),
            kind));
    }

    private float ResolveCompliance(DistanceConstraintKind kind) => kind switch
    {
        DistanceConstraintKind.Structural => _material.StructuralCompliance,
        DistanceConstraintKind.Shear => _material.ShearCompliance,
        DistanceConstraintKind.Bend => _material.BendCompliance,
        _ => _material.StructuralCompliance
    };

    private static void ResolveRestDimensions(
        IReadOnlyList<BossFragmentPoint> points,
        out float width,
        out float height)
    {
        float minimumX = points[0].X;
        float maximumX = points[0].X;
        float minimumY = points[0].Y;
        float maximumY = points[0].Y;
        for (int index = 1; index < points.Count; index++)
        {
            BossFragmentPoint point = points[index];
            minimumX = Math.Min(minimumX, point.X);
            maximumX = Math.Max(maximumX, point.X);
            minimumY = Math.Min(minimumY, point.Y);
            maximumY = Math.Max(maximumY, point.Y);
        }

        width = Math.Max(1f, maximumX - minimumX);
        height = Math.Max(1f, maximumY - minimumY);
    }

    private static BossFragmentPoint[] BuildGrid(SoftBodyBounds bounds)
    {
        var result = new BossFragmentPoint[ParticleCount];
        for (int row = 0; row < GridSize; row++)
        {
            for (int column = 0; column < GridSize; column++)
            {
                float u = column / (float)(GridSize - 1);
                float v = row / (float)(GridSize - 1);
                result[Index(column, row)] = new BossFragmentPoint(
                    bounds.X + bounds.Width * u,
                    bounds.Y + bounds.Height * v);
            }
        }

        return result;
    }

    private static SoftBodyHullPoint[] BuildHull(
        SoftBodyBounds bounds,
        IReadOnlyList<BossFragmentPoint> hull) =>
        hull.Select(point => new SoftBodyHullPoint(
            point,
            Math.Clamp((point.X - bounds.X) / Math.Max(bounds.Width, 0.001f), 0f, 1f),
            Math.Clamp((point.Y - bounds.Y) / Math.Max(bounds.Height, 0.001f), 0f, 1f)))
            .ToArray();

    private static BossFragmentPoint Average(IReadOnlyList<BossFragmentPoint> points)
    {
        BossFragmentPoint sum = default;
        for (int index = 0; index < points.Count; index++)
        {
            sum = Add(sum, points[index]);
        }

        return Multiply(sum, 1f / Math.Max(1, points.Count));
    }

    private static void ResolveWeights(
        float u,
        float v,
        out int i00,
        out int i10,
        out int i01,
        out int i11,
        out float w00,
        out float w10,
        out float w01,
        out float w11)
    {
        float gridX = Math.Clamp(u, 0f, 1f) * (GridSize - 1);
        float gridY = Math.Clamp(v, 0f, 1f) * (GridSize - 1);
        int column = Math.Clamp((int)MathF.Floor(gridX), 0, GridSize - 2);
        int row = Math.Clamp((int)MathF.Floor(gridY), 0, GridSize - 2);
        float tx = Math.Clamp(gridX - column, 0f, 1f);
        float ty = Math.Clamp(gridY - row, 0f, 1f);
        i00 = Index(column, row);
        i10 = Index(column + 1, row);
        i01 = Index(column, row + 1);
        i11 = Index(column + 1, row + 1);
        w00 = (1f - tx) * (1f - ty);
        w10 = tx * (1f - ty);
        w01 = (1f - tx) * ty;
        w11 = tx * ty;
    }

    private static int Index(int column, int row) => row * GridSize + column;

    private static float TriangleArea(
        BossFragmentPoint first,
        BossFragmentPoint second,
        BossFragmentPoint third) =>
        Cross(Subtract(second, first), Subtract(third, first)) * 0.5f;

    private static float QuadArea(
        BossFragmentPoint p0,
        BossFragmentPoint p1,
        BossFragmentPoint p2,
        BossFragmentPoint p3) =>
        (Cross(p0, p1) + Cross(p1, p2) + Cross(p2, p3) + Cross(p3, p0)) * 0.5f;

    private void RemoveRigidVectorComponents(Span<BossFragmentPoint> vectors, BossFragmentPoint center)
    {
        BossFragmentPoint mean = default;
        for (int index = 0; index < vectors.Length; index++)
        {
            mean = Add(mean, vectors[index]);
        }

        mean = Multiply(mean, 1f / Math.Max(1, vectors.Length));
        float angularNumerator = 0f;
        float angularDenominator = 0f;
        for (int index = 0; index < vectors.Length; index++)
        {
            vectors[index] = Subtract(vectors[index], mean);
            BossFragmentPoint radius = Subtract(_particles[index].Position, center);
            angularNumerator += Cross(radius, vectors[index]);
            angularDenominator += LengthSquared(radius);
        }

        float angular = angularDenominator <= 0.001f
            ? 0f
            : angularNumerator / angularDenominator;
        for (int index = 0; index < vectors.Length; index++)
        {
            BossFragmentPoint radius = Subtract(_particles[index].Position, center);
            vectors[index] = Subtract(
                vectors[index],
                new BossFragmentPoint(-radius.Y * angular, radius.X * angular));
        }
    }

    private static BossFragmentPoint ClampLength(BossFragmentPoint point, float maximum)
    {
        float squared = LengthSquared(point);
        float maximumSquared = maximum * maximum;
        return squared <= maximumSquared || squared <= 0.0001f
            ? point
            : Multiply(point, maximum / MathF.Sqrt(squared));
    }

    private static BossFragmentPoint Add(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X + second.X, first.Y + second.Y);

    private static BossFragmentPoint Subtract(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X - second.X, first.Y - second.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float value) =>
        new(point.X * value, point.Y * value);

    private static BossFragmentPoint Lerp(
        BossFragmentPoint first,
        BossFragmentPoint second,
        float amount) =>
        new(
            first.X + (second.X - first.X) * amount,
            first.Y + (second.Y - first.Y) * amount);

    private static BossFragmentPoint Perpendicular(BossFragmentPoint point) => new(point.Y, -point.X);
    private static float Dot(BossFragmentPoint first, BossFragmentPoint second) =>
        first.X * second.X + first.Y * second.Y;
    private static float Cross(BossFragmentPoint first, BossFragmentPoint second) =>
        first.X * second.Y - first.Y * second.X;
    private static float Length(BossFragmentPoint point) => MathF.Sqrt(LengthSquared(point));
    private static float LengthSquared(BossFragmentPoint point) => point.X * point.X + point.Y * point.Y;
    private static bool IsFinitePoint(BossFragmentPoint point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y);

    private struct SoftParticle(
        BossFragmentPoint restPosition,
        BossFragmentPoint position,
        BossFragmentPoint previousPosition,
        BossFragmentPoint velocity,
        float inverseMass)
    {
        public BossFragmentPoint RestPosition = restPosition;
        public BossFragmentPoint Position = position;
        public BossFragmentPoint PreviousPosition = previousPosition;
        public BossFragmentPoint Velocity = velocity;
        public float InverseMass = inverseMass;
    }

    private struct DistanceConstraint(
        int first,
        int second,
        float restLength,
        float compliance,
        DistanceConstraintKind kind)
    {
        public int First = first;
        public int Second = second;
        public float RestLength = restLength;
        public float Compliance = compliance;
        public DistanceConstraintKind Kind = kind;
        public float Lambda;
    }

    private enum DistanceConstraintKind
    {
        Structural,
        Shear,
        Bend
    }

    private struct QuadAreaConstraint(
        int i0,
        int i1,
        int i2,
        int i3,
        float restArea)
    {
        public int I0 = i0;
        public int I1 = i1;
        public int I2 = i2;
        public int I3 = i3;
        public float RestArea = restArea;
        public float Lambda;
    }

    private struct TriangleBarrier(int i0, int i1, int i2, float restArea)
    {
        public int I0 = i0;
        public int I1 = i1;
        public int I2 = i2;
        public float RestArea = restArea;
        public float Lambda;
    }
}
