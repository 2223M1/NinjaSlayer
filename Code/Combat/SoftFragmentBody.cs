namespace NinjaSlayer.Code.Combat;

internal readonly record struct SoftBodyBounds(float X, float Y, float Width, float Height)
{
    public BossFragmentPoint Center => new(X + Width * 0.5f, Y + Height * 0.5f);
}

internal readonly record struct SoftBodyHullPoint(
    BossFragmentPoint RestPosition,
    float U,
    float V);

internal readonly record struct SoftBodyStepMetrics(
    int Contacts,
    int ContactPoints,
    int ContactStarts,
    int VisibleBounces,
    int SweptContacts,
    int LeftWallContacts,
    int RightWallContacts,
    int WallBounces,
    int BrokenLinks,
    int Rollbacks,
    int Inversions,
    int SafetyProjections,
    int WobbleZeroCrossings,
    float MaximumCenterSpeed,
    float MaximumObservedCenterSpeed,
    float ContactEnergyBefore,
    float ContactEnergyAfter,
    int LimitedContacts,
    int LimitedCenterSpeeds,
    float MaximumPenetration,
    int Substeps);

internal readonly record struct SoftBodyCommitResult(
    bool Accepted,
    bool Inverted,
    bool SafetyProjected);

internal sealed class SoftFragmentBody
{
    public const int GridSize = 4;
    public const int ParticleCount = GridSize * GridSize;

    private const float MaximumPredictedParticleSpeed = 8_000f;

    private readonly SoftParticle[] _particles = new SoftParticle[ParticleCount];
    private readonly SoftParticle[] _substepSnapshot = new SoftParticle[ParticleCount];
    private readonly SoftParticle[] _safetyCandidate = new SoftParticle[ParticleCount];
    private readonly BossFragmentPoint[] _launchDeltas = new BossFragmentPoint[ParticleCount];
    private readonly BossFragmentPoint[] _workVectors = new BossFragmentPoint[ParticleCount];
    private readonly DistanceConstraint[] _distanceConstraints;
    private readonly QuadAreaConstraint[] _areaConstraints;
    private readonly TriangleBarrier[] _triangleBarriers;
    private readonly SoftBodyHullPoint[] _hull;
    private readonly BossFragmentPoint _restCenter;
    private SoftBodyMaterialProfile _material;
    private SoftBodyDeformationExciter _deformationExciter;
    private BossFragmentPoint _safetyPositionTranslation;
    private BossFragmentPoint _safetyPreviousTranslation;
    private float _targetLinearScale = 1f;
    private int _collisionCooldownSubsteps;

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
        ResolveRestDimensions(restGrid, out float width, out float height);
        RestBounds = new SoftBodyBounds(
            _restCenter.X - width * 0.5f,
            _restCenter.Y - height * 0.5f,
            width,
            height);
        ShortDimension = Math.Max(1f, Math.Min(width, height));
        CharacteristicLength = Math.Max(1f, MathF.Sqrt(width * width + height * height));
        _material = SoftBodyMaterialProfile.FountainJelly;
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
        _triangleBarriers = BuildTriangleBarriers();
        _deformationExciter = new SoftBodyDeformationExciter(
            restGrid,
            _restCenter,
            ShortDimension,
            unchecked((ulong)(uint)id * 0x9E3779B97F4A7C15UL));
        TargetLinearScale = compressedScale;
        SetCollisionEnvelope(0f, 0f);
    }

    public int Id { get; }
    public float Mass { get; }
    public float CollisionMargin { get; }
    public float CollisionHullScale { get; private set; }
    public float CollisionMarginScale { get; private set; }
    public float TargetLinearScale
    {
        get => _targetLinearScale;
        set
        {
            if (float.IsFinite(value)
                && float.IsFinite(_targetLinearScale)
                && _targetLinearScale > 0.001f
                && value > _targetLinearScale + 0.001f)
            {
                ApplyUniformScaleExpansion(value / _targetLinearScale);
            }

            _targetLinearScale = value;
        }
    }
    public bool Released { get; private set; }
    public bool CanCollide => HasFiniteState
        && CollisionHullScale > 0f
        && _collisionCooldownSubsteps <= 0;
    public int HullPointCount => _hull.Length;
    public int ConstraintCount => _distanceConstraints.Length + _areaConstraints.Length;
    public BossFragmentPoint RestCenter => _restCenter;
    public SoftBodyBounds RestBounds { get; }
    public float ShortDimension { get; }
    public float CharacteristicLength { get; }
    public int RollbackCount { get; private set; }
    public int InversionCount { get; private set; }
    public int SafetyProjectionCount { get; private set; }
    public int WobbleZeroCrossings => _deformationExciter.ZeroCrossings;
    public float LastRejectedAreaRatio { get; private set; } = 1f;
    public float LastRejectedParticleSpeed { get; private set; }

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

    public void SetMaterial(SoftBodyMaterialProfile material)
    {
        _material = material;
        for (int index = 0; index < _distanceConstraints.Length; index++)
        {
            DistanceConstraint constraint = _distanceConstraints[index];
            constraint.Compliance = ResolveCompliance(constraint.Kind);
            _distanceConstraints[index] = constraint;
        }
    }

    public void ConfigureDeformation(ulong seed)
    {
        BossFragmentPoint[] restPoints = new BossFragmentPoint[ParticleCount];
        for (int index = 0; index < ParticleCount; index++)
        {
            restPoints[index] = _particles[index].RestPosition;
        }

        _deformationExciter = new SoftBodyDeformationExciter(
            restPoints,
            _restCenter,
            ShortDimension,
            seed);
    }

    public void PinCompressed(
        BossFragmentPoint center,
        float scale,
        float phase,
        float slideRadius,
        float squashAmount = 0f)
    {
        Released = false;
        TargetLinearScale = scale;
        SetCollisionEnvelope(0f, 0f);
        BossFragmentPoint axis = new(MathF.Cos(phase), MathF.Sin(phase));
        BossFragmentPoint perpendicular = new(-axis.Y, axis.X);
        squashAmount = Math.Clamp(squashAmount, -0.3f, 0.3f);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            BossFragmentPoint restOffset = Subtract(particle.RestPosition, _restCenter);
            float axisDistance = Dot(restOffset, axis) * (1f - squashAmount);
            float perpendicularDistance = Dot(restOffset, perpendicular)
                * (1f + squashAmount * 0.72f);
            BossFragmentPoint compressedOffset = Add(
                Multiply(axis, axisDistance),
                Multiply(perpendicular, perpendicularDistance));
            float localPhase = phase + index * 0.73f;
            BossFragmentPoint slide = new(
                MathF.Cos(localPhase) * slideRadius,
                MathF.Sin(localPhase * 1.31f) * slideRadius * 0.7f);
            BossFragmentPoint position = Add(
                center,
                Add(Multiply(compressedOffset, scale), slide));
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

    public void BeginStagedRelease()
    {
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.Velocity = default;
            particle.PreviousPosition = particle.Position;
            _particles[index] = particle;
        }

        Released = true;
    }

    public void ApplyLaunchVelocityDelta(
        BossFragmentPoint linearVelocityDelta,
        float angularVelocityDelta,
        float differentialFraction)
    {
        ApplyLaunchVelocityDelta(
            linearVelocityDelta,
            linearVelocityDelta,
            angularVelocityDelta,
            differentialFraction);
    }

    public void ApplyLaunchVelocityDelta(
        BossFragmentPoint linearVelocityDelta,
        BossFragmentPoint deformationVelocityDelta,
        float angularVelocityDelta,
        float differentialFraction)
    {
        BossFragmentPoint center = Center;
        _deformationExciter.AddLaunchExcitation(
            deformationVelocityDelta,
            angularVelocityDelta,
            ResolveBestFitRotation(),
            differentialFraction,
            _launchDeltas);
        RemoveRigidVectorComponents(_launchDeltas, center);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            BossFragmentPoint radius = Subtract(particle.Position, center);
            BossFragmentPoint requestedAngularDelta = new(
                -radius.Y * angularVelocityDelta,
                radius.X * angularVelocityDelta);
            particle.Velocity = Add(
                particle.Velocity,
                Add(
                    linearVelocityDelta,
                    Add(_launchDeltas[index], requestedAngularDelta)));
            _particles[index] = particle;
        }
    }

    public void Predict(
        float seconds,
        float gravity,
        float airDrag,
        float quadraticAirDrag = 0f)
    {
        ApplyShapeMemoryForces(seconds);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.PreviousPosition = particle.Position;
            float speed = Length(particle.Velocity);
            float drag = MathF.Exp(-(
                Math.Max(0f, airDrag)
                + Math.Max(0f, quadraticAirDrag) * speed) * seconds);
            particle.Velocity = Multiply(particle.Velocity, drag);
            particle.Velocity = new BossFragmentPoint(
                particle.Velocity.X,
                particle.Velocity.Y + gravity * seconds);
            particle.Position = Add(particle.Position, Multiply(particle.Velocity, seconds));
            _particles[index] = particle;
        }
    }

    public void CaptureSubstepState() => Array.Copy(_particles, _substepSnapshot, ParticleCount);

    public void RewindToSubstepFraction(float amount)
    {
        if (!float.IsFinite(amount))
        {
            return;
        }

        amount = Math.Clamp(amount, 0f, 1f);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            SoftParticle baseline = _substepSnapshot[index];
            particle.Position = Lerp(baseline.Position, particle.Position, amount);
            particle.PreviousPosition = baseline.Position;
            _particles[index] = particle;
        }
    }

    public void TranslateForContinuousCollision(BossFragmentPoint correction)
    {
        if (!IsFinitePoint(correction))
        {
            return;
        }

        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.Position = Add(particle.Position, correction);
            particle.PreviousPosition = Add(particle.PreviousPosition, correction);
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

    public void SolveInternalConstraints(float seconds)
    {
        for (int index = 0; index < _distanceConstraints.Length; index++)
        {
            SolveDistance(ref _distanceConstraints[index], seconds);
        }

        for (int index = 0; index < _areaConstraints.Length; index++)
        {
            SolveArea(ref _areaConstraints[index], seconds);
        }
    }

    public void ProjectSafetyConstraints(float seconds, int iterations = 18)
    {
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            bool corrected = false;
            for (int index = 0; index < _distanceConstraints.Length; index++)
            {
                corrected |= ProjectDistanceSafety(_distanceConstraints[index]);
            }

            for (int index = 0; index < _triangleBarriers.Length; index++)
            {
                TriangleBarrier constraint = _triangleBarriers[index];
                float orientation = constraint.RestArea < 0f ? -1f : 1f;
                float minimum = MathF.Abs(constraint.RestArea)
                    * TargetLinearScale
                    * TargetLinearScale
                    * _material.MinimumAreaFraction;
                float current = TriangleArea(
                    _particles[constraint.I0].Position,
                    _particles[constraint.I1].Position,
                    _particles[constraint.I2].Position) * orientation;
                if (current < minimum)
                {
                    SolveAreaValue(
                        constraint.I0,
                        constraint.I1,
                        constraint.I2,
                        minimum,
                        orientation,
                        compliance: 0f,
                        seconds,
                        ref constraint.Lambda,
                        accumulate: false);
                    corrected = true;
                }
            }

            if (!corrected)
            {
                break;
            }
        }

    }

    public SoftBodyCommitResult TryCommitSubstep(float seconds)
    {
        float minimumArea = ResolveMinimumCellAreaRatio();
        bool inverted = minimumArea <= 0f;
        float minimumAllowedArea = TargetLinearScale
            * TargetLinearScale
            * _material.MinimumAreaFraction
            * 0.95f;
        float maximumPredictedSpeed = ResolveMaximumPredictedParticleSpeed(seconds);
        float residualRmsRatio = ResolveRmsResidualRatio();
        bool invalid = !HasFiniteState
            || minimumArea < minimumAllowedArea
            || residualRmsRatio > _material.MaximumResidualRmsRatio
            || maximumPredictedSpeed > MaximumPredictedParticleSpeed;
        if (!invalid)
        {
            FinalizeVelocities(seconds);
            return new SoftBodyCommitResult(true, false, false);
        }

        if (HasFiniteState
            && TryProjectCandidateToSafety(
                minimumAllowedArea,
                seconds,
                MaximumPredictedParticleSpeed,
                _material.MaximumResidualRmsRatio,
                out float acceptedFraction))
        {
            SafetyProjectionCount++;
            FinalizeVelocities(seconds);
            if (acceptedFraction < 0.999f)
            {
                ApplySafetyBoundaryRestitution();
            }
            return new SoftBodyCommitResult(true, false, true);
        }

        if (inverted)
        {
            InversionCount++;
        }

        RollbackCount++;
        LastRejectedAreaRatio = minimumArea;
        LastRejectedParticleSpeed = maximumPredictedSpeed;
        Array.Copy(_substepSnapshot, _particles, ParticleCount);
        DampNonRigidVelocity(0.45f);
        _collisionCooldownSubsteps = Math.Max(_collisionCooldownSubsteps, 2);
        return new SoftBodyCommitResult(false, inverted, false);
    }

    private bool TryProjectCandidateToSafety(
        float minimumArea,
        float seconds,
        float maximumParticleSpeed,
        float maximumResidualRmsRatio,
        out float acceptedFraction)
    {
        acceptedFraction = 0f;
        Array.Copy(_particles, _safetyCandidate, ParticleCount);
        BossFragmentPoint snapshotCenter = AverageParticlePositions(
            _substepSnapshot,
            usePreviousPosition: false);
        _safetyPositionTranslation = Subtract(
            AverageParticlePositions(_safetyCandidate, usePreviousPosition: false),
            snapshotCenter);
        _safetyPreviousTranslation = Subtract(
            AverageParticlePositions(_safetyCandidate, usePreviousPosition: true),
            snapshotCenter);
        float low = 0f;
        float high = 1f;
        for (int iteration = 0; iteration < 16; iteration++)
        {
            float amount = (low + high) * 0.5f;
            InterpolateSafetyCandidate(amount);
            bool valid = HasFiniteState
                && ResolveMinimumCellAreaRatio() >= minimumArea
                && ResolveRmsResidualRatio() <= maximumResidualRmsRatio
                && ResolveMaximumPredictedParticleSpeed(seconds) <= maximumParticleSpeed;
            if (valid)
            {
                low = amount;
            }
            else
            {
                high = amount;
            }
        }

        InterpolateSafetyCandidate(low);
        bool accepted = HasFiniteState
            && ResolveMinimumCellAreaRatio() >= minimumArea * 0.999f
            && ResolveRmsResidualRatio() <= maximumResidualRmsRatio * 1.001f
            && ResolveMaximumPredictedParticleSpeed(seconds) <= maximumParticleSpeed;
        acceptedFraction = accepted ? low : 0f;
        return accepted;
    }

    private void InterpolateSafetyCandidate(float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle baseline = _substepSnapshot[index];
            SoftParticle candidate = _safetyCandidate[index];
            SoftParticle particle = _particles[index];
            particle.Position = Lerp(
                Add(baseline.Position, _safetyPositionTranslation),
                candidate.Position,
                amount);
            particle.PreviousPosition = Lerp(
                Add(baseline.Position, _safetyPreviousTranslation),
                candidate.PreviousPosition,
                amount);
            particle.Velocity = candidate.Velocity;
            _particles[index] = particle;
        }
    }

    private static BossFragmentPoint AverageParticlePositions(
        IReadOnlyList<SoftParticle> particles,
        bool usePreviousPosition)
    {
        BossFragmentPoint sum = default;
        for (int index = 0; index < particles.Count; index++)
        {
            sum = Add(
                sum,
                usePreviousPosition
                    ? particles[index].PreviousPosition
                    : particles[index].Position);
        }

        return Multiply(sum, 1f / Math.Max(1, particles.Count));
    }

    private void ApplySafetyBoundaryRestitution()
    {
        BossFragmentPoint center = Center;
        BossFragmentPoint centerVelocity = CenterVelocity;
        float rotation = ResolveBestFitRotation();
        float angularVelocity = ResolveAngularVelocity();
        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        float covarianceMagnitude = 0f;
        float restVariance = 0f;
        float covarianceA = 0f;
        float covarianceB = 0f;
        for (int index = 0; index < ParticleCount; index++)
        {
            BossFragmentPoint rest = Subtract(_particles[index].RestPosition, _restCenter);
            BossFragmentPoint current = Subtract(_particles[index].Position, center);
            covarianceA += rest.X * current.X + rest.Y * current.Y;
            covarianceB += rest.X * current.Y - rest.Y * current.X;
            restVariance += LengthSquared(rest);
        }

        covarianceMagnitude = MathF.Sqrt(
            covarianceA * covarianceA + covarianceB * covarianceB);
        float scale = covarianceMagnitude / Math.Max(restVariance, 0.001f);
        if (!float.IsFinite(scale) || scale <= 0.001f)
        {
            return;
        }

        double outwardNumerator = 0d;
        double outwardDenominator = 0d;
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            BossFragmentPoint rest = Multiply(
                Subtract(particle.RestPosition, _restCenter),
                scale);
            BossFragmentPoint goal = new(
                center.X + rest.X * cosine - rest.Y * sine,
                center.Y + rest.X * sine + rest.Y * cosine);
            BossFragmentPoint radius = Subtract(particle.Position, center);
            BossFragmentPoint rigidVelocity = Add(
                centerVelocity,
                new BossFragmentPoint(
                    -radius.Y * angularVelocity,
                    radius.X * angularVelocity));
            _launchDeltas[index] = Subtract(particle.Position, goal);
            _workVectors[index] = Subtract(particle.Velocity, rigidVelocity);
            outwardNumerator += Dot(_workVectors[index], _launchDeltas[index]);
            outwardDenominator += LengthSquared(_launchDeltas[index]);
        }

        float outwardRate = outwardDenominator <= 0.001d
            ? 0f
            : (float)(outwardNumerator / outwardDenominator);
        if (outwardRate > 0f)
        {
            const float strainRestitution = 0.9f;
            float reflectedRate = outwardRate * (1f + strainRestitution);
            for (int index = 0; index < ParticleCount; index++)
            {
                _workVectors[index] = Subtract(
                    _workVectors[index],
                    Multiply(_launchDeltas[index], reflectedRate));
            }
        }

        RemoveRigidVectorComponents(_workVectors, center);
        float maximumRmsVelocity = ShortDimension
            * MathF.Tau
            * _material.ShapeMemoryFrequencyHz
            * _material.MaximumResidualRmsRatio
            * 2.5f;
        double squaredVelocity = 0d;
        for (int index = 0; index < ParticleCount; index++)
        {
            squaredVelocity += LengthSquared(_workVectors[index]);
        }

        float rmsVelocity = (float)Math.Sqrt(squaredVelocity / ParticleCount);
        float velocityScale = rmsVelocity <= maximumRmsVelocity || rmsVelocity <= 0.001f
            ? 1f
            : maximumRmsVelocity / rmsVelocity;
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            BossFragmentPoint radius = Subtract(particle.Position, center);
            BossFragmentPoint rigidVelocity = Add(
                centerVelocity,
                new BossFragmentPoint(
                    -radius.Y * angularVelocity,
                    radius.X * angularVelocity));
            particle.Velocity = Add(
                rigidVelocity,
                Multiply(_workVectors[index], velocityScale));
            _particles[index] = particle;
        }
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

    public int UpdateDeformationMetrics(float seconds)
    {
        _ = seconds;
        BossFragmentPoint center = Center;
        float rotation = ResolveBestFitRotation();
        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        float inverseScale = 1f / Math.Max(TargetLinearScale, 0.05f);
        for (int index = 0; index < ParticleCount; index++)
        {
            BossFragmentPoint current = Subtract(_particles[index].Position, center);
            BossFragmentPoint local = new(
                (current.X * cosine + current.Y * sine) * inverseScale,
                (-current.X * sine + current.Y * cosine) * inverseScale);
            _workVectors[index] = Subtract(
                local,
                Subtract(_particles[index].RestPosition, _restCenter));
        }

        int crossings = _deformationExciter.ObserveLocalResiduals(_workVectors);
        if (_collisionCooldownSubsteps > 0)
        {
            _collisionCooldownSubsteps--;
        }

        return crossings;
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

    public void CopyParticleVelocities(Span<BossFragmentPoint> destination)
    {
        if (destination.Length < ParticleCount)
        {
            throw new ArgumentException("The destination cannot hold all particle velocities.", nameof(destination));
        }

        for (int index = 0; index < ParticleCount; index++)
        {
            destination[index] = _particles[index].Velocity;
        }
    }

    public void BlendParticleVelocities(
        IReadOnlyList<BossFragmentPoint> baseline,
        float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.Velocity = new BossFragmentPoint(
                baseline[index].X + (particle.Velocity.X - baseline[index].X) * amount,
                baseline[index].Y + (particle.Velocity.Y - baseline[index].Y) * amount);
            _particles[index] = particle;
        }
    }

    public void RestoreParticleVelocities(IReadOnlyList<BossFragmentPoint> velocities)
    {
        if (velocities.Count < ParticleCount)
        {
            throw new ArgumentException(
                "The source must contain every particle velocity.",
                nameof(velocities));
        }

        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.Velocity = velocities[index];
            _particles[index] = particle;
        }
    }

    public float ResolveKineticEnergy()
    {
        double squaredVelocity = 0d;
        for (int index = 0; index < ParticleCount; index++)
        {
            squaredVelocity += LengthSquared(_particles[index].Velocity);
        }

        double energy = 0.5d * (Mass / ParticleCount) * squaredVelocity;
        return double.IsFinite(energy)
            ? (float)Math.Min(energy, float.MaxValue)
            : float.PositiveInfinity;
    }

    public bool ClampCenterSpeed(float maximumSpeed)
    {
        maximumSpeed = Math.Max(0f, maximumSpeed);
        BossFragmentPoint velocity = CenterVelocity;
        float speed = Length(velocity);
        if (speed <= maximumSpeed || speed <= 0.001f)
        {
            return false;
        }

        float boundedSpeed = Math.Max(0f, maximumSpeed - 0.001f);
        BossFragmentPoint correction = Subtract(
            Multiply(velocity, boundedSpeed / speed),
            velocity);
        for (int index = 0; index < ParticleCount; index++)
        {
            SoftParticle particle = _particles[index];
            particle.Velocity = Add(particle.Velocity, correction);
            _particles[index] = particle;
        }

        return true;
    }

    public float GetParticleInverseMass(int index) =>
        _particles[Math.Clamp(index, 0, ParticleCount - 1)].InverseMass;

    public BossFragmentPoint GetRestParticlePosition(int index) =>
        _particles[Math.Clamp(index, 0, ParticleCount - 1)].RestPosition;

    public BossFragmentPoint MapUv(float u, float v)
    {
        return MapUv(_particles, u, v);
    }

    private static BossFragmentPoint MapUv(
        IReadOnlyList<SoftParticle> particles,
        float u,
        float v)
    {
        ResolveWeights(u, v, out int i00, out int i10, out int i01, out int i11,
            out float w00, out float w10, out float w01, out float w11);
        BossFragmentPoint result = default;
        result = Add(result, Multiply(particles[i00].Position, w00));
        result = Add(result, Multiply(particles[i10].Position, w10));
        result = Add(result, Multiply(particles[i01].Position, w01));
        result = Add(result, Multiply(particles[i11].Position, w11));
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

    public void ApplyParticleCorrection(int index, BossFragmentPoint correction)
    {
        SoftParticle particle = _particles[Math.Clamp(index, 0, ParticleCount - 1)];
        particle.Position = Add(particle.Position, correction);
        _particles[Math.Clamp(index, 0, ParticleCount - 1)] = particle;
    }

    public void CopyCollisionHull(SoftCollisionVertex[] destination)
    {
        CopyHull(destination, _particles, CollisionHullScale);
    }

    public void CopyPreviousCollisionHull(SoftCollisionVertex[] destination)
    {
        CopyHull(destination, _substepSnapshot, CollisionHullScale);
    }

    public void CopyDeformedHull(SoftCollisionVertex[] destination)
    {
        CopyHull(destination, _particles, scale: 1f);
    }

    private void CopyHull(
        SoftCollisionVertex[] destination,
        IReadOnlyList<SoftParticle> particles,
        float scale)
    {
        if (destination.Length < _hull.Length)
        {
            throw new ArgumentException("The destination cannot hold the deformed hull.", nameof(destination));
        }

        BossFragmentPoint center = AverageParticlePositions(
            particles,
            usePreviousPosition: false);
        for (int index = 0; index < _hull.Length; index++)
        {
            SoftBodyHullPoint hullPoint = _hull[index];
            BossFragmentPoint mapped = MapUv(particles, hullPoint.U, hullPoint.V);
            destination[index] = new SoftCollisionVertex(
                Add(center, Multiply(Subtract(mapped, center), scale)),
                hullPoint.U,
                hullPoint.V);
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

    public void ResolveDeformedProjection(
        BossFragmentPoint axis,
        out float minimum,
        out float maximum)
    {
        BossFragmentPoint first = MapUv(_hull[0].U, _hull[0].V);
        minimum = first.X * axis.X + first.Y * axis.Y;
        maximum = minimum;
        for (int index = 1; index < _hull.Length; index++)
        {
            BossFragmentPoint point = MapUv(_hull[index].U, _hull[index].V);
            float projection = point.X * axis.X + point.Y * axis.Y;
            minimum = Math.Min(minimum, projection);
            maximum = Math.Max(maximum, projection);
        }
    }

    public SoftBodyHullPoint GetHullPoint(int index) =>
        _hull[Math.Clamp(index, 0, _hull.Length - 1)];

    public (BossFragmentPoint Minimum, BossFragmentPoint Maximum) ResolveCollisionAabb()
    {
        return ResolveCollisionAabb(_particles);
    }

    public (BossFragmentPoint Minimum, BossFragmentPoint Maximum) ResolvePreviousCollisionAabb()
    {
        return ResolveCollisionAabb(_substepSnapshot);
    }

    private (BossFragmentPoint Minimum, BossFragmentPoint Maximum) ResolveCollisionAabb(
        IReadOnlyList<SoftParticle> particles)
    {
        BossFragmentPoint center = AverageParticlePositions(
            particles,
            usePreviousPosition: false);
        float scale = CollisionHullScale;
        float margin = CollisionMargin * CollisionMarginScale;
        BossFragmentPoint first = Add(
            center,
            Multiply(Subtract(
                MapUv(particles, _hull[0].U, _hull[0].V),
                center), scale));
        float minX = first.X;
        float minY = first.Y;
        float maxX = first.X;
        float maxY = first.Y;
        for (int index = 1; index < _hull.Length; index++)
        {
            BossFragmentPoint mapped = MapUv(
                particles,
                _hull[index].U,
                _hull[index].V);
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
            QuadAreaConstraint constraint = _areaConstraints[index];
            currentArea += MathF.Abs(QuadArea(
                _particles[constraint.I0].Position,
                _particles[constraint.I1].Position,
                _particles[constraint.I2].Position,
                _particles[constraint.I3].Position));
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
        for (int index = 0; index < _triangleBarriers.Length; index++)
        {
            TriangleBarrier constraint = _triangleBarriers[index];
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

    public float ResolveRmsResidualRatio()
    {
        BossFragmentPoint center = Center;
        float covarianceA = 0f;
        float covarianceB = 0f;
        float restVariance = 0f;
        for (int index = 0; index < ParticleCount; index++)
        {
            BossFragmentPoint rest = Subtract(_particles[index].RestPosition, _restCenter);
            BossFragmentPoint current = Subtract(_particles[index].Position, center);
            covarianceA += rest.X * current.X + rest.Y * current.Y;
            covarianceB += rest.X * current.Y - rest.Y * current.X;
            restVariance += LengthSquared(rest);
        }

        float covarianceMagnitude = MathF.Sqrt(
            covarianceA * covarianceA + covarianceB * covarianceB);
        float scale = covarianceMagnitude / Math.Max(restVariance, 0.001f);
        if (!float.IsFinite(scale) || scale <= 0.001f)
        {
            return float.PositiveInfinity;
        }

        float rotation = MathF.Atan2(covarianceB, covarianceA);
        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        double squaredResidual = 0d;
        for (int index = 0; index < ParticleCount; index++)
        {
            BossFragmentPoint rest = Subtract(_particles[index].RestPosition, _restCenter);
            BossFragmentPoint current = Subtract(_particles[index].Position, center);
            BossFragmentPoint unrotated = new(
                (current.X * cosine + current.Y * sine) / scale,
                (-current.X * sine + current.Y * cosine) / scale);
            BossFragmentPoint residual = Subtract(unrotated, rest);
            squaredResidual += LengthSquared(residual);
        }

        double rms = Math.Sqrt(squaredResidual / ParticleCount) / ShortDimension;
        return double.IsFinite(rms) ? (float)rms : float.PositiveInfinity;
    }

    public float ResolveCenterSpeed() => Length(CenterVelocity);

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
