namespace NinjaSlayer.Code.Combat;

internal sealed class BossSoftBodySolver
{
    public const int MinimumSubsteps = 2;
    public const int MaximumSubsteps = 4;
    public const int ConstraintIterations = 6;
    public const float DefaultMaximumCenterSpeed = 620f;

    private readonly SoftCollisionBroadphase _broadphase = new();
    private readonly SoftContactManifoldBuilder _manifoldBuilder = new();
    private readonly SoftBoundaryContactSolver _boundarySolver = new();
    private readonly SoftBodyEnergyAudit _energyAudit = new();
    private readonly List<SoftContactManifold> _velocityManifolds = [];
    private readonly HashSet<(int First, int Second)> _velocityPairKeys = [];
    private readonly List<SoftBoundaryContactManifold> _boundaryVelocityManifolds = [];
    private readonly HashSet<(int Body, SoftBoundarySide Side)> _boundaryVelocityKeys = [];
    private readonly HashSet<SoftFragmentBody> _acceptedBodies = [];
    private readonly HashSet<SoftFragmentBody> _steppedBodies = [];
    private readonly Dictionary<SoftFragmentBody, float> _sweptFractions = [];
    private readonly Dictionary<(int First, int Second), SweptOrdering> _sweptOrderings = [];
    private readonly HashSet<SoftFragmentBody> _activeBodies = [];
    private readonly List<(int First, int Second)> _expiredSweptOrderings = [];

    public SoftBodyStepMetrics Step(
        IReadOnlyList<SoftFragmentBody> bodies,
        IReadOnlyList<SoftRagdollLink> links,
        float seconds,
        float gravity,
        float airDrag,
        float? floorY = null,
        float quadraticAirDrag = 0f,
        IReadOnlyList<SoftBodyLaunchActuator>? launchActuators = null,
        float centerSpeedLimit = DefaultMaximumCenterSpeed,
        SoftHorizontalBoundary? horizontalBoundary = null)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f)
        {
            return default;
        }

        int manifolds = 0;
        int contactPoints = 0;
        int contactStarts = 0;
        int visibleBounces = 0;
        int sweptContacts = 0;
        int leftWallContacts = 0;
        int rightWallContacts = 0;
        int wallBounces = 0;
        int brokenLinks = 0;
        int rollbacks = 0;
        int inversions = 0;
        int safetyProjections = 0;
        int wobbleZeroCrossings = 0;
        int limitedContacts = 0;
        int limitedCenterSpeeds = 0;
        float maximumPenetration = 0f;
        float maximumCenterSpeed = 0f;
        float maximumObservedCenterSpeed = 0f;
        float contactEnergyBefore = 0f;
        float contactEnergyAfter = 0f;
        PruneSweptOrderings(bodies);
        centerSpeedLimit = Math.Max(1f, centerSpeedLimit);
        int substeps = ResolveSubstepCount(
            bodies,
            seconds,
            centerSpeedLimit,
            launchActuators);
        float substepSeconds = seconds / substeps;
        for (int substep = 0; substep < substeps; substep++)
        {
            _steppedBodies.Clear();
            if (launchActuators != null)
            {
                for (int actuatorIndex = 0; actuatorIndex < launchActuators.Count; actuatorIndex++)
                {
                    launchActuators[actuatorIndex].Advance(substepSeconds);
                }
            }

            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                SoftFragmentBody body = bodies[bodyIndex];
                if (!body.HasFiniteState)
                {
                    continue;
                }

                body.CaptureSubstepState();
                _steppedBodies.Add(body);
                body.BeginSubstep();
                body.Predict(substepSeconds, gravity, airDrag, quadraticAirDrag);
            }

            for (int linkIndex = 0; linkIndex < links.Count; linkIndex++)
            {
                if (links[linkIndex].BeginSubstep(substepSeconds))
                {
                    brokenLinks++;
                }
            }

            _manifoldBuilder.BeginSubstep(links);
            _velocityManifolds.Clear();
            _velocityPairKeys.Clear();
            _boundaryVelocityManifolds.Clear();
            _boundaryVelocityKeys.Clear();
            IReadOnlyList<(SoftFragmentBody First, SoftFragmentBody Second)> pairs =
                _broadphase.BuildPairs(bodies);
            IReadOnlyList<SoftContactManifold> currentManifolds = [];
            for (int iteration = 0; iteration < ConstraintIterations; iteration++)
            {
                for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
                {
                    if (bodies[bodyIndex].HasFiniteState)
                    {
                        bodies[bodyIndex].SolveInternalConstraints(substepSeconds);
                    }
                }

                for (int linkIndex = 0; linkIndex < links.Count; linkIndex++)
                {
                    links[linkIndex].Solve(substepSeconds);
                }

                currentManifolds = _manifoldBuilder.Build(
                    pairs,
                    enableTemporalSampling: substeps == MaximumSubsteps && iteration == 0);
                if (iteration == 0)
                {
                    RewindSweptBodies(currentManifolds);
                }
                for (int manifoldIndex = 0; manifoldIndex < currentManifolds.Count; manifoldIndex++)
                {
                    SoftContactManifold manifold = currentManifolds[manifoldIndex];
                    (int First, int Second) key = PairKey(manifold.First, manifold.Second);
                    if (_velocityPairKeys.Add(key))
                    {
                        _velocityManifolds.Add(manifold);
                    }

                    maximumPenetration = Math.Max(
                        maximumPenetration,
                        manifold.MaximumPenetration);
                }

                for (int manifoldIndex = 0; manifoldIndex < currentManifolds.Count; manifoldIndex++)
                {
                    SoftContactSolver.SolvePositions(currentManifolds[manifoldIndex]);
                }

                ProjectSweptOrderings();

                if (horizontalBoundary is { IsValid: true } boundary)
                {
                    IReadOnlyList<SoftBoundaryContactManifold> boundaryManifolds =
                        _boundarySolver.Build(bodies, boundary);
                    for (int manifoldIndex = 0;
                        manifoldIndex < boundaryManifolds.Count;
                        manifoldIndex++)
                    {
                        SoftBoundaryContactManifold manifold = boundaryManifolds[manifoldIndex];
                        (int Body, SoftBoundarySide Side) key =
                            (manifold.Body.Id, manifold.Side);
                        if (_boundaryVelocityKeys.Add(key))
                        {
                            _boundaryVelocityManifolds.Add(manifold);
                        }

                        maximumPenetration = Math.Max(
                            maximumPenetration,
                            manifold.MaximumPenetration);
                        SoftBoundaryContactSolver.SolvePositions(manifold);
                    }
                }

                if (floorY.HasValue)
                {
                    for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
                    {
                        if (bodies[bodyIndex].HasFiniteState)
                        {
                            _ = bodies[bodyIndex].ConstrainToFloor(floorY.Value);
                        }
                    }
                }

            }

            manifolds += _velocityManifolds.Count;
            for (int manifoldIndex = 0; manifoldIndex < _velocityManifolds.Count; manifoldIndex++)
            {
                SoftContactManifold manifold = _velocityManifolds[manifoldIndex];
                contactPoints += manifold.PointCount;
                contactStarts += manifold.IsNewContact ? 1 : 0;
                sweptContacts += manifold.IsSwept ? 1 : 0;
            }

            for (int manifoldIndex = 0;
                manifoldIndex < _boundaryVelocityManifolds.Count;
                manifoldIndex++)
            {
                SoftBoundaryContactManifold manifold =
                    _boundaryVelocityManifolds[manifoldIndex];
                if (manifold.Side == SoftBoundarySide.Left)
                {
                    leftWallContacts++;
                }
                else
                {
                    rightWallContacts++;
                }
            }

            _acceptedBodies.Clear();
            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                SoftFragmentBody body = bodies[bodyIndex];
                if (!_steppedBodies.Contains(body))
                {
                    continue;
                }

                body.ProjectSafetyConstraints(substepSeconds);
                SoftBodyCommitResult commit = body.TryCommitSubstep(substepSeconds);
                if (commit.SafetyProjected)
                {
                    safetyProjections++;
                }
                if (commit.Accepted)
                {
                    if (floorY.HasValue)
                    {
                        _ = body.ConstrainToFloor(floorY.Value);
                        body.FinalizeVelocities(substepSeconds);
                    }

                    _acceptedBodies.Add(body);
                }
                else
                {
                    rollbacks++;
                    if (commit.Inverted)
                    {
                        inversions++;
                    }
                }
            }

            for (int manifoldIndex = 0; manifoldIndex < _velocityManifolds.Count; manifoldIndex++)
            {
                SoftContactManifold manifold = _velocityManifolds[manifoldIndex];
                if (!_acceptedBodies.Contains(manifold.First)
                    || !_acceptedBodies.Contains(manifold.Second))
                {
                    continue;
                }

                _energyAudit.Capture(manifold);
                SoftContactVelocityResult velocityResult =
                    SoftContactSolver.SolveVelocities(manifold);
                visibleBounces += velocityResult.Bounced ? 1 : 0;
                SoftBodyEnergyAuditResult energy = _energyAudit.LimitContactEnergy(manifold);
                contactEnergyBefore += energy.Before;
                contactEnergyAfter += energy.After;
                if (energy.Limited)
                {
                    limitedContacts++;
                }
            }

            for (int manifoldIndex = 0;
                manifoldIndex < _boundaryVelocityManifolds.Count;
                manifoldIndex++)
            {
                SoftBoundaryContactManifold manifold =
                    _boundaryVelocityManifolds[manifoldIndex];
                if (!_acceptedBodies.Contains(manifold.Body))
                {
                    continue;
                }

                _energyAudit.Capture(manifold.Body);
                SoftBoundaryVelocityResult velocityResult =
                    SoftBoundaryContactSolver.SolveVelocities(manifold);
                wallBounces += velocityResult.Bounced ? 1 : 0;
                SoftBodyEnergyAuditResult energy =
                    _energyAudit.LimitBoundaryEnergy(manifold.Body);
                contactEnergyBefore += energy.Before;
                contactEnergyAfter += energy.After;
                if (energy.Limited)
                {
                    limitedContacts++;
                }
            }

            _manifoldBuilder.EndSubstep();
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

            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                SoftFragmentBody body = bodies[bodyIndex];
                if (!body.HasFiniteState)
                {
                    continue;
                }

                maximumObservedCenterSpeed = Math.Max(
                    maximumObservedCenterSpeed,
                    body.ResolveCenterSpeed());
                if (body.ClampCenterSpeed(centerSpeedLimit))
                {
                    limitedCenterSpeeds++;
                }

                wobbleZeroCrossings += body.UpdateDeformationMetrics(substepSeconds);
                maximumCenterSpeed = Math.Max(maximumCenterSpeed, body.ResolveCenterSpeed());
            }
        }

        return new SoftBodyStepMetrics(
            manifolds,
            contactPoints,
            contactStarts,
            visibleBounces,
            sweptContacts,
            leftWallContacts,
            rightWallContacts,
            wallBounces,
            brokenLinks,
            rollbacks,
            inversions,
            safetyProjections,
            wobbleZeroCrossings,
            maximumCenterSpeed,
            maximumObservedCenterSpeed,
            contactEnergyBefore,
            contactEnergyAfter,
            limitedContacts,
            limitedCenterSpeeds,
            maximumPenetration,
            substeps);
    }

    private static int ResolveSubstepCount(
        IReadOnlyList<SoftFragmentBody> bodies,
        float seconds,
        float maximumCenterSpeed,
        IReadOnlyList<SoftBodyLaunchActuator>? launchActuators)
    {
        float shortest = float.PositiveInfinity;
        float fastest = 0f;
        for (int index = 0; index < bodies.Count; index++)
        {
            SoftFragmentBody body = bodies[index];
            if (!body.HasFiniteState)
            {
                continue;
            }

            shortest = Math.Min(shortest, body.ShortDimension);
            fastest = Math.Max(fastest, body.ResolveCenterSpeed());
        }

        if (launchActuators != null)
        {
            for (int index = 0; index < launchActuators.Count; index++)
            {
                if (launchActuators[index].IsComplete)
                {
                    continue;
                }

                BossFragmentPoint velocity = launchActuators[index].TargetVelocity;
                fastest = Math.Max(
                    fastest,
                    MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y));
            }
        }

        fastest = Math.Min(Math.Max(fastest, 0f), maximumCenterSpeed);
        if (!float.IsFinite(shortest) || shortest <= 0f || fastest <= 0f)
        {
            return MinimumSubsteps;
        }

        float maximumTravel = Math.Max(1f, shortest * 0.25f);
        int required = (int)MathF.Ceiling(fastest * seconds / maximumTravel);
        return Math.Clamp(required, MinimumSubsteps, MaximumSubsteps);
    }

    private void RewindSweptBodies(IReadOnlyList<SoftContactManifold> manifolds)
    {
        _sweptFractions.Clear();
        for (int index = 0; index < manifolds.Count; index++)
        {
            SoftContactManifold manifold = manifolds[index];
            if (!manifold.IsSwept)
            {
                continue;
            }

            AddEarliest(manifold.First, manifold.TimeOfImpact);
            AddEarliest(manifold.Second, manifold.TimeOfImpact);
        }

        foreach ((SoftFragmentBody body, float fraction) in _sweptFractions)
        {
            body.RewindToSubstepFraction(fraction);
        }

        for (int index = 0; index < manifolds.Count; index++)
        {
            SoftContactManifold manifold = manifolds[index];
            if (!manifold.IsSwept)
            {
                continue;
            }

            BossFragmentPoint delta = new(
                manifold.Second.Center.X - manifold.First.Center.X,
                manifold.Second.Center.Y - manifold.First.Center.Y);
            float separation = Math.Max(
                0.1f,
                delta.X * manifold.Normal.X + delta.Y * manifold.Normal.Y);
            (int First, int Second) key = PairKey(manifold.First, manifold.Second);
            _sweptOrderings.TryAdd(
                key,
                new SweptOrdering(
                    manifold.First,
                    manifold.Second,
                    manifold.Normal,
                    separation));
        }

        void AddEarliest(SoftFragmentBody body, float fraction)
        {
            if (!_sweptFractions.TryGetValue(body, out float current)
                || fraction < current)
            {
                _sweptFractions[body] = fraction;
            }
        }
    }

    private void ProjectSweptOrderings()
    {
        foreach (SweptOrdering ordering in _sweptOrderings.Values)
        {
            BossFragmentPoint delta = new(
                ordering.Second.Center.X - ordering.First.Center.X,
                ordering.Second.Center.Y - ordering.First.Center.Y);
            float signedSeparation = delta.X * ordering.Normal.X
                + delta.Y * ordering.Normal.Y;
            if (!float.IsFinite(signedSeparation)
                || signedSeparation >= ordering.MinimumSignedSeparation)
            {
                continue;
            }

            float firstInverseMass = 1f / ordering.First.Mass;
            float secondInverseMass = 1f / ordering.Second.Mass;
            float totalInverseMass = firstInverseMass + secondInverseMass;
            float correction = (ordering.MinimumSignedSeparation - signedSeparation)
                / Math.Max(0.0001f, totalInverseMass);
            ordering.First.TranslateForContinuousCollision(new BossFragmentPoint(
                -ordering.Normal.X * correction * firstInverseMass,
                -ordering.Normal.Y * correction * firstInverseMass));
            ordering.Second.TranslateForContinuousCollision(new BossFragmentPoint(
                ordering.Normal.X * correction * secondInverseMass,
                ordering.Normal.Y * correction * secondInverseMass));
        }
    }

    private void PruneSweptOrderings(IReadOnlyList<SoftFragmentBody> bodies)
    {
        _activeBodies.Clear();
        for (int index = 0; index < bodies.Count; index++)
        {
            _activeBodies.Add(bodies[index]);
        }

        _expiredSweptOrderings.Clear();
        foreach (((int First, int Second) key, SweptOrdering ordering) in _sweptOrderings)
        {
            if (!_activeBodies.Contains(ordering.First)
                || !_activeBodies.Contains(ordering.Second)
                || !ordering.First.HasFiniteState
                || !ordering.Second.HasFiniteState)
            {
                _expiredSweptOrderings.Add(key);
                continue;
            }

            ordering.First.ResolveDeformedProjection(
                ordering.Normal,
                out _,
                out float firstMaximum);
            ordering.Second.ResolveDeformedProjection(
                ordering.Normal,
                out float secondMinimum,
                out _);
            float releaseGap = Math.Min(
                ordering.First.ShortDimension,
                ordering.Second.ShortDimension) * 0.05f;
            BossFragmentPoint relativeVelocity = new(
                ordering.Second.CenterVelocity.X - ordering.First.CenterVelocity.X,
                ordering.Second.CenterVelocity.Y - ordering.First.CenterVelocity.Y);
            float separationSpeed = relativeVelocity.X * ordering.Normal.X
                + relativeVelocity.Y * ordering.Normal.Y;
            if (secondMinimum - firstMaximum >= releaseGap
                && separationSpeed >= 0f)
            {
                _expiredSweptOrderings.Add(key);
            }
        }

        for (int index = 0; index < _expiredSweptOrderings.Count; index++)
        {
            _sweptOrderings.Remove(_expiredSweptOrderings[index]);
        }
    }

    private static (int First, int Second) PairKey(
        SoftFragmentBody first,
        SoftFragmentBody second) =>
        first.Id < second.Id ? (first.Id, second.Id) : (second.Id, first.Id);

    private readonly record struct SweptOrdering(
        SoftFragmentBody First,
        SoftFragmentBody Second,
        BossFragmentPoint Normal,
        float MinimumSignedSeparation);
}
