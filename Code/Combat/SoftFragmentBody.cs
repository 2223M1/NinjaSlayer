namespace NinjaSlayer.Code.Combat;

internal readonly record struct SoftBodyBounds(float X, float Y, float Width, float Height)
{
    public BossFragmentPoint Center => new(X + Width * 0.5f, Y + Height * 0.5f);
}

internal readonly record struct SoftBodyHullPoint(
    BossFragmentPoint RestPosition,
    float U,
    float V);

internal readonly record struct SoftBodyStepMetrics(int Contacts, int BrokenLinks);

internal sealed class SoftFragmentBody
{
    public const int GridSize = 4;
    public const int ParticleCount = GridSize * GridSize;

    private const float StructuralCompliance = 0.00035f;
    private const float ShearCompliance = 0.0011f;
    private const float BendCompliance = 0.0045f;
    private const float AreaCompliance = 0.00002f;
    private const float MinimumAreaFraction = 0.12f;

    private readonly SoftParticle[] _particles = new SoftParticle[ParticleCount];
    private readonly DistanceConstraint[] _distanceConstraints;
    private readonly TriangleAreaConstraint[] _areaConstraints;
    private readonly SoftBodyHullPoint[] _hull;
    private readonly BossFragmentPoint _restCenter;

    public SoftFragmentBody(
        int id,
        SoftBodyBounds restBounds,
        IReadOnlyList<BossFragmentPoint> restHull,
        BossFragmentPoint center,
        float compressedScale,
        float mass)
        : this(
            id,
            BuildGrid(restBounds),
            BuildHull(restBounds, restHull),
            center,
            compressedScale,
            mass,
            BossDismembermentMath.ResolveCollisionPadding(
                BossDismembermentMath.PolygonArea(restHull)))
    {
    }

    public SoftFragmentBody(
        int id,
        IReadOnlyList<BossFragmentPoint> restGrid,
        IReadOnlyList<SoftBodyHullPoint> restHull,
        BossFragmentPoint center,
        float compressedScale,
        float mass,
        float collisionMargin)
    {
        if (restGrid.Count != ParticleCount)
        {
            throw new ArgumentException($"A 4x4 soft body requires exactly {ParticleCount} rest points.", nameof(restGrid));
        }

        if (restHull.Count < 3)
        {
            throw new ArgumentException("A soft body collision hull requires at least three points.", nameof(restHull));
        }

        Id = id;
        Mass = Math.Max(0.1f, mass);
        CollisionMargin = Math.Max(0f, collisionMargin);
        _hull = restHull.ToArray();
        _restCenter = Average(restGrid);
        float inverseParticleMass = ParticleCount / Mass;
        for (int index = 0; index < ParticleCount; index++)
        {
            BossFragmentPoint rest = restGrid[index];
            BossFragmentPoint offset = Subtract(rest, _restCenter);
            BossFragmentPoint position = Add(center, Multiply(offset, compressedScale));
            _particles[index] = new SoftParticle(
                rest,
                position,
                position,
                default,
                inverseParticleMass);
        }

        _distanceConstraints = BuildDistanceConstraints();
        _areaConstraints = BuildAreaConstraints();
        TargetLinearScale = compressedScale;
        SetCollisionEnvelope(0f, 0f);
    }

    public int Id { get; }
    public float Mass { get; }
    public float CollisionMargin { get; }
    public float CollisionHullScale { get; private set; }
    public float CollisionMarginScale { get; private set; }
    public float TargetLinearScale { get; set; } = 1f;
    public bool Released { get; private set; }
    public int HullPointCount => _hull.Length;
    public int ConstraintCount => _distanceConstraints.Length + _areaConstraints.Length;
    public BossFragmentPoint RestCenter => _restCenter;

    public BossFragmentPoint Center
    {
        get
        {
            BossFragmentPoint sum = default;
            for (int index = 0; index < ParticleCount; index++)
            {
                sum = Add(sum, _particles[index].Position);
            }

            return Multiply(sum, 1f / ParticleCount);
        }
    }

    public BossFragmentPoint CenterVelocity
    {
        get
        {
            BossFragmentPoint sum = default;
            for (int index = 0; index < ParticleCount; index++)
            {
                sum = Add(sum, _particles[index].Velocity);
            }

            return Multiply(sum, 1f / ParticleCount);
        }
    }

    public bool HasFiniteState
    {
        get
        {
            if (!float.IsFinite(TargetLinearScale)
                || !float.IsFinite(CollisionHullScale)
                || !float.IsFinite(CollisionMarginScale))
            {
                return false;
            }

            for (int index = 0; index < ParticleCount; index++)
            {
                SoftParticle particle = _particles[index];
                if (!IsFinitePoint(particle.Position)
                    || !IsFinitePoint(particle.PreviousPosition)
                    || !IsFinitePoint(particle.Velocity)
                    || !float.IsFinite(particle.InverseMass))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void SetCollisionEnvelope(float hullScale, float marginScale)
    {
        CollisionHullScale = Math.Clamp(hullScale, 0f, 1f);
        CollisionMarginScale = Math.Clamp(marginScale, 0f, 1f);
    }

    public void PinCompressed(
        BossFragmentPoint center,
        float scale,
        float phase,
        float slideRadius)
    {
        Released = false;
        TargetLinearScale = scale;
        SetCollisionEnvelope(0f, 0f);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            BossFragmentPoint restOffset = Subtract(particle.RestPosition, _restCenter);
            float localPhase = phase + index * 0.73f;
            BossFragmentPoint slide = new(
                MathF.Cos(localPhase) * slideRadius,
                MathF.Sin(localPhase * 1.31f) * slideRadius * 0.7f);
            BossFragmentPoint position = Add(center, Add(Multiply(restOffset, scale), slide));
            particle.Position = position;
            particle.PreviousPosition = position;
            particle.Velocity = default;
            _particles[index] = particle;
        }

        BossFragmentPoint recenter = Subtract(center, Center);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.Position = Add(particle.Position, recenter);
            particle.PreviousPosition = particle.Position;
            _particles[index] = particle;
        }
    }

    public void Release(BossFragmentPoint linearVelocity, float angularVelocityRadians)
    {
        BossFragmentPoint center = Center;
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            BossFragmentPoint radius = Subtract(particle.Position, center);
            BossFragmentPoint angularVelocity = new(
                -radius.Y * angularVelocityRadians,
                radius.X * angularVelocityRadians);
            particle.Velocity = Add(linearVelocity, angularVelocity);
            particle.PreviousPosition = particle.Position;
            _particles[index] = particle;
        }

        Released = true;
    }

    public void Predict(float seconds, float gravity, float airDrag)
    {
        float drag = MathF.Exp(-Math.Max(0f, airDrag) * seconds);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.PreviousPosition = particle.Position;
            particle.Velocity = new BossFragmentPoint(
                particle.Velocity.X * drag,
                particle.Velocity.Y * drag + gravity * seconds);
            particle.Position = Add(particle.Position, Multiply(particle.Velocity, seconds));
            _particles[index] = particle;
        }
    }

    public void BeginSubstep()
    {
        for (int index = 0; index < _distanceConstraints.Length; index++)
        {
            _distanceConstraints[index].Lambda = 0f;
        }

        for (int index = 0; index < _areaConstraints.Length; index++)
        {
            _areaConstraints[index].Lambda = 0f;
        }
    }

    public void SolveInternalConstraints(float seconds, float shapeMatchingStrength)
    {
        for (int index = 0; index < _distanceConstraints.Length; index++)
        {
            SolveDistance(ref _distanceConstraints[index], seconds);
        }

        for (int index = 0; index < _areaConstraints.Length; index++)
        {
            SolveArea(ref _areaConstraints[index], seconds);
        }

        SolveCorotationalShape(shapeMatchingStrength);
    }

    public void FinalizeVelocities(float seconds)
    {
        float inverseSeconds = 1f / Math.Max(seconds, 0.0001f);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.Velocity = Multiply(
                Subtract(particle.Position, particle.PreviousPosition),
                inverseSeconds);
            _particles[index] = particle;
        }
    }

    public int ConstrainToFloor(float floorY)
    {
        int contacts = 0;
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            if (particle.Position.Y <= floorY)
            {
                continue;
            }

            particle.Position = new BossFragmentPoint(particle.Position.X, floorY);
            _particles[index] = particle;
            contacts++;
        }

        return contacts;
    }

    public void ApplyFloorVelocity(float floorY)
    {
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            if (particle.Position.Y < floorY - 0.01f || particle.Velocity.Y <= 0f)
            {
                continue;
            }

            particle.Velocity = new BossFragmentPoint(
                particle.Velocity.X * 0.76f,
                -particle.Velocity.Y * 0.34f);
            _particles[index] = particle;
        }
    }

    public BossFragmentPoint GetParticlePosition(int index) =>
        _particles[Math.Clamp(index, 0, ParticleCount - 1)].Position;

    public BossFragmentPoint GetParticleVelocity(int index) =>
        _particles[Math.Clamp(index, 0, ParticleCount - 1)].Velocity;

    public float GetParticleInverseMass(int index) =>
        _particles[Math.Clamp(index, 0, ParticleCount - 1)].InverseMass;

    public BossFragmentPoint GetRestParticlePosition(int index) =>
        _particles[Math.Clamp(index, 0, ParticleCount - 1)].RestPosition;

    public BossFragmentPoint MapUv(float u, float v)
    {
        ResolveWeights(u, v, out int i00, out int i10, out int i01, out int i11,
            out float w00, out float w10, out float w01, out float w11);
        BossFragmentPoint result = default;
        result = Add(result, Multiply(_particles[i00].Position, w00));
        result = Add(result, Multiply(_particles[i10].Position, w10));
        result = Add(result, Multiply(_particles[i01].Position, w01));
        result = Add(result, Multiply(_particles[i11].Position, w11));
        return result;
    }

    public BossFragmentPoint GetVelocityAt(float u, float v)
    {
        ResolveWeights(u, v, out int i00, out int i10, out int i01, out int i11,
            out float w00, out float w10, out float w01, out float w11);
        BossFragmentPoint result = default;
        result = Add(result, Multiply(_particles[i00].Velocity, w00));
        result = Add(result, Multiply(_particles[i10].Velocity, w10));
        result = Add(result, Multiply(_particles[i01].Velocity, w01));
        result = Add(result, Multiply(_particles[i11].Velocity, w11));
        return result;
    }

    public float GetEffectiveInverseMass(float u, float v)
    {
        ResolveWeights(u, v, out int i00, out int i10, out int i01, out int i11,
            out float w00, out float w10, out float w01, out float w11);
        return _particles[i00].InverseMass * w00 * w00
            + _particles[i10].InverseMass * w10 * w10
            + _particles[i01].InverseMass * w01 * w01
            + _particles[i11].InverseMass * w11 * w11;
    }

    public void ApplyContactPositionImpulse(
        float u,
        float v,
        BossFragmentPoint direction,
        float lambda)
    {
        ResolveWeights(u, v, out int i00, out int i10, out int i01, out int i11,
            out float w00, out float w10, out float w01, out float w11);
        ApplyPositionImpulse(i00, w00, direction, lambda, preserveVelocity: true);
        ApplyPositionImpulse(i10, w10, direction, lambda, preserveVelocity: true);
        ApplyPositionImpulse(i01, w01, direction, lambda, preserveVelocity: true);
        ApplyPositionImpulse(i11, w11, direction, lambda, preserveVelocity: true);
    }

    public void ApplyVelocityImpulse(float u, float v, BossFragmentPoint direction, float impulse)
    {
        ResolveWeights(u, v, out int i00, out int i10, out int i01, out int i11,
            out float w00, out float w10, out float w01, out float w11);
        ApplyVelocityImpulse(i00, w00, direction, impulse);
        ApplyVelocityImpulse(i10, w10, direction, impulse);
        ApplyVelocityImpulse(i01, w01, direction, impulse);
        ApplyVelocityImpulse(i11, w11, direction, impulse);
    }

    public void ApplyCenterVelocityImpulse(BossFragmentPoint direction, float impulse)
    {
        BossFragmentPoint delta = Multiply(direction, impulse / Mass);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.Velocity = Add(particle.Velocity, delta);
            _particles[index] = particle;
        }
    }

    public void ApplyParticleCorrection(int index, BossFragmentPoint correction)
    {
        SoftParticle particle = _particles[Math.Clamp(index, 0, ParticleCount - 1)];
        particle.Position = Add(particle.Position, correction);
        _particles[Math.Clamp(index, 0, ParticleCount - 1)] = particle;
    }

    public void CopyCollisionHull(BossFragmentPoint[] destination)
    {
        if (destination.Length < _hull.Length)
        {
            throw new ArgumentException("The destination cannot hold the deformed hull.", nameof(destination));
        }

        BossFragmentPoint center = Center;
        float scale = CollisionHullScale;
        for (int index = 0; index < _hull.Length; index++)
        {
            BossFragmentPoint mapped = MapUv(_hull[index].U, _hull[index].V);
            destination[index] = Add(center, Multiply(Subtract(mapped, center), scale));
        }
    }

    public (BossFragmentPoint Minimum, BossFragmentPoint Maximum) ResolveDeformedBounds()
    {
        BossFragmentPoint first = MapUv(_hull[0].U, _hull[0].V);
        float minX = first.X;
        float minY = first.Y;
        float maxX = first.X;
        float maxY = first.Y;
        for (int index = 1; index < _hull.Length; index++)
        {
            BossFragmentPoint mapped = MapUv(_hull[index].U, _hull[index].V);
            minX = Math.Min(minX, mapped.X);
            minY = Math.Min(minY, mapped.Y);
            maxX = Math.Max(maxX, mapped.X);
            maxY = Math.Max(maxY, mapped.Y);
        }

        return (
            new BossFragmentPoint(minX, minY),
            new BossFragmentPoint(maxX, maxY));
    }

    public SoftBodyHullPoint GetHullPoint(int index) =>
        _hull[Math.Clamp(index, 0, _hull.Length - 1)];

    public (BossFragmentPoint Minimum, BossFragmentPoint Maximum) ResolveCollisionAabb()
    {
        BossFragmentPoint center = Center;
        float scale = CollisionHullScale;
        float margin = CollisionMargin * CollisionMarginScale;
        BossFragmentPoint first = Add(
            center,
            Multiply(Subtract(MapUv(_hull[0].U, _hull[0].V), center), scale));
        float minX = first.X;
        float minY = first.Y;
        float maxX = first.X;
        float maxY = first.Y;
        for (int index = 1; index < _hull.Length; index++)
        {
            BossFragmentPoint mapped = MapUv(_hull[index].U, _hull[index].V);
            mapped = Add(center, Multiply(Subtract(mapped, center), scale));
            minX = Math.Min(minX, mapped.X);
            minY = Math.Min(minY, mapped.Y);
            maxX = Math.Max(maxX, mapped.X);
            maxY = Math.Max(maxY, mapped.Y);
        }

        return (
            new BossFragmentPoint(minX - margin, minY - margin),
            new BossFragmentPoint(maxX + margin, maxY + margin));
    }

    public float ResolveAreaRatio()
    {
        float currentArea = 0f;
        float restArea = 0f;
        for (int index = 0; index < _areaConstraints.Length; index++)
        {
            TriangleAreaConstraint constraint = _areaConstraints[index];
            currentArea += MathF.Abs(TriangleArea(
                _particles[constraint.I0].Position,
                _particles[constraint.I1].Position,
                _particles[constraint.I2].Position));
            restArea += MathF.Abs(constraint.RestArea);
        }

        return restArea <= 0.001f ? 1f : currentArea / restArea;
    }

    public float ResolveMaximumStretch()
    {
        float maximum = 1f;
        for (int index = 0; index < _distanceConstraints.Length; index++)
        {
            DistanceConstraint constraint = _distanceConstraints[index];
            float rest = Math.Max(0.001f, constraint.RestLength);
            float current = Length(Subtract(
                _particles[constraint.Second].Position,
                _particles[constraint.First].Position));
            maximum = Math.Max(maximum, current / rest);
        }

        return maximum;
    }

    public float ResolveMinimumCellAreaRatio()
    {
        float minimum = float.PositiveInfinity;
        for (int index = 0; index < _areaConstraints.Length; index++)
        {
            TriangleAreaConstraint constraint = _areaConstraints[index];
            float currentArea = TriangleArea(
                _particles[constraint.I0].Position,
                _particles[constraint.I1].Position,
                _particles[constraint.I2].Position);
            float orientation = constraint.RestArea < 0f ? -1f : 1f;
            minimum = Math.Min(
                minimum,
                currentArea * orientation / Math.Max(MathF.Abs(constraint.RestArea), 0.001f));
        }

        return float.IsPositiveInfinity(minimum) ? 1f : minimum;
    }

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
        float alpha = constraint.Compliance / Math.Max(seconds * seconds, 0.000001f);
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

    private void SolveArea(ref TriangleAreaConstraint constraint, float seconds)
    {
        float orientation = constraint.RestArea < 0f ? -1f : 1f;
        float restArea = MathF.Abs(constraint.RestArea);
        float target = restArea * TargetLinearScale * TargetLinearScale;
        SolveAreaValue(
            ref constraint,
            target,
            orientation,
            AreaCompliance,
            seconds,
            accumulate: true);

        float current = TriangleArea(
            _particles[constraint.I0].Position,
            _particles[constraint.I1].Position,
            _particles[constraint.I2].Position) * orientation;
        float minimum = restArea
            * TargetLinearScale
            * TargetLinearScale
            * MinimumAreaFraction;
        if (current < minimum)
        {
            SolveAreaValue(
                ref constraint,
                minimum,
                orientation,
                0f,
                seconds,
                accumulate: false);
        }
    }

    private void SolveAreaValue(
        ref TriangleAreaConstraint constraint,
        float target,
        float orientation,
        float compliance,
        float seconds,
        bool accumulate)
    {
        SoftParticle p0 = _particles[constraint.I0];
        SoftParticle p1 = _particles[constraint.I1];
        SoftParticle p2 = _particles[constraint.I2];
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
        float previousLambda = accumulate ? constraint.Lambda : 0f;
        float deltaLambda = (-(area - target) - alpha * previousLambda) / Math.Max(denominator + alpha, 0.0001f);
        if (accumulate)
        {
            constraint.Lambda += deltaLambda;
        }

        p0.Position = Add(p0.Position, Multiply(gradient0, p0.InverseMass * deltaLambda));
        p1.Position = Add(p1.Position, Multiply(gradient1, p1.InverseMass * deltaLambda));
        p2.Position = Add(p2.Position, Multiply(gradient2, p2.InverseMass * deltaLambda));
        _particles[constraint.I0] = p0;
        _particles[constraint.I1] = p1;
        _particles[constraint.I2] = p2;
    }

    private void SolveCorotationalShape(float strength)
    {
        if (strength <= 0f)
        {
            return;
        }

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

        float magnitude = MathF.Sqrt(covarianceA * covarianceA + covarianceB * covarianceB);
        if (magnitude <= 0.0001f)
        {
            return;
        }

        float cosine = covarianceA / magnitude;
        float sine = covarianceB / magnitude;
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            BossFragmentPoint rest = Multiply(
                Subtract(particle.RestPosition, _restCenter),
                TargetLinearScale);
            BossFragmentPoint goal = new(
                center.X + rest.X * cosine - rest.Y * sine,
                center.Y + rest.X * sine + rest.Y * cosine);
            particle.Position = Add(
                particle.Position,
                Multiply(Subtract(goal, particle.Position), strength));
            _particles[index] = particle;
        }
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
        var constraints = new List<DistanceConstraint>(58);
        for (int row = 0; row < GridSize; row++)
        {
            for (int column = 0; column < GridSize; column++)
            {
                if (column + 1 < GridSize)
                {
                    AddDistance(constraints, column, row, column + 1, row, StructuralCompliance);
                }

                if (row + 1 < GridSize)
                {
                    AddDistance(constraints, column, row, column, row + 1, StructuralCompliance);
                }

                if (column + 1 < GridSize && row + 1 < GridSize)
                {
                    AddDistance(constraints, column, row, column + 1, row + 1, ShearCompliance);
                    AddDistance(constraints, column + 1, row, column, row + 1, ShearCompliance);
                }

                if (column + 2 < GridSize)
                {
                    AddDistance(constraints, column, row, column + 2, row, BendCompliance);
                }

                if (row + 2 < GridSize)
                {
                    AddDistance(constraints, column, row, column, row + 2, BendCompliance);
                }
            }
        }

        return constraints.ToArray();
    }

    private TriangleAreaConstraint[] BuildAreaConstraints()
    {
        var constraints = new List<TriangleAreaConstraint>(18);
        for (int row = 0; row < GridSize - 1; row++)
        {
            for (int column = 0; column < GridSize - 1; column++)
            {
                int i0 = Index(column, row);
                int i1 = Index(column + 1, row);
                int i2 = Index(column + 1, row + 1);
                int i3 = Index(column, row + 1);
                constraints.Add(CreateAreaConstraint(i0, i1, i2));
                constraints.Add(CreateAreaConstraint(i0, i2, i3));
            }
        }

        return constraints.ToArray();
    }

    private TriangleAreaConstraint CreateAreaConstraint(int i0, int i1, int i2) => new(
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
        float compliance)
    {
        int first = Index(firstColumn, firstRow);
        int second = Index(secondColumn, secondRow);
        constraints.Add(new DistanceConstraint(
            first,
            second,
            Length(Subtract(
                _particles[second].RestPosition,
                _particles[first].RestPosition)),
            compliance));
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

    private static BossFragmentPoint Add(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X + second.X, first.Y + second.Y);

    private static BossFragmentPoint Subtract(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X - second.X, first.Y - second.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float value) =>
        new(point.X * value, point.Y * value);

    private static BossFragmentPoint Perpendicular(BossFragmentPoint point) => new(point.Y, -point.X);
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
        float compliance)
    {
        public int First = first;
        public int Second = second;
        public float RestLength = restLength;
        public float Compliance = compliance;
        public float Lambda;
    }

    private struct TriangleAreaConstraint(
        int i0,
        int i1,
        int i2,
        float restArea)
    {
        public int I0 = i0;
        public int I1 = i1;
        public int I2 = i2;
        public float RestArea = restArea;
        public float Lambda;
    }
}
