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

internal sealed partial class SoftFragmentBody
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
}
