using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class BossFountainSoftBodyTests
{
    [Fact]
    public void LaunchAppliesFullRigidMomentumImmediatelyThenExcitesLocalParticles()
    {
        SoftFragmentBody body = CreateBody(1, default, 0.7f);
        var target = new BossFragmentPoint(220f, -360f);
        var actuator = new SoftBodyLaunchActuator(body, target, 0f);

        actuator.Begin();
        Assert.InRange(Length(Subtract(body.CenterVelocity, target)), 0f, 0.01f);
        actuator.Advance(1f / 120f);
        Assert.False(actuator.IsComplete);
        Assert.InRange(Length(Subtract(body.CenterVelocity, target)), 0f, 0.01f);
        for (int step = 1; step < 20; step++)
        {
            actuator.Advance(1f / 120f);
        }

        Assert.True(actuator.IsComplete);
        Assert.InRange(MathF.Abs(body.CenterVelocity.X - target.X), 0f, 0.01f);
        Assert.InRange(MathF.Abs(body.CenterVelocity.Y - target.Y), 0f, 0.01f);
        BossFragmentPoint averageResidual = default;
        float maximumResidual = 0f;
        for (int index = 0; index < SoftFragmentBody.ParticleCount; index++)
        {
            BossFragmentPoint residual = Subtract(body.GetParticleVelocity(index), target);
            averageResidual = Add(averageResidual, residual);
            maximumResidual = Math.Max(maximumResidual, Length(residual));
        }

        averageResidual = Multiply(averageResidual, 1f / SoftFragmentBody.ParticleCount);
        Assert.InRange(Length(averageResidual), 0f, 0.01f);
        Assert.True(maximumResidual > 20f);
    }

    [Fact]
    public void BodyBurstLaunchPlanIsFixedNaturalAndIndependentOfSceneOrigin()
    {
        float[] masses = Enumerable.Range(0, BossDismembermentMath.MaximumPieces)
            .Select(index => 0.65f + index % 5 * 0.17f)
            .ToArray();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            IReadOnlyList<BossFountainLaunch> natural =
                BossFountainLaunchProfile.Create(masses, seed);
            BossFountainLaunchPlan plan = BossFountainLaunchProfile.CreatePlan(natural);
            Assert.Equal(natural, plan.Launches);
            Assert.Equal(BossFountainLaunchProfile.Gravity, plan.Gravity);
            Assert.Equal(BossFountainLaunchProfile.MaximumCenterSpeed, plan.MaximumCenterSpeed);
            Assert.Equal(12, plan.Launches.Count(launch =>
                launch.Lane == BossFountainLaunchLane.Upward));
            Assert.Equal(2, plan.Launches.Count(launch =>
                launch.Lane == BossFountainLaunchLane.Horizontal));
            Assert.Equal(2, plan.Launches.Count(launch =>
                launch.Lane == BossFountainLaunchLane.Downward));
            Assert.All(plan.Launches, launch => Assert.InRange(
                Length(launch.Velocity),
                BossFountainLaunchProfile.MinimumLaunchSpeed - 0.01f,
                BossFountainLaunchProfile.MaximumLaunchSpeed + 0.01f));

            float totalMass = 0f;
            float horizontalMomentum = 0f;
            for (int index = 0; index < natural.Count; index++)
            {
                totalMass += masses[index];
                horizontalMomentum += masses[index] * natural[index].Velocity.X;
            }

            Assert.InRange(
                MathF.Abs(horizontalMomentum / totalMass),
                0f,
                BossFountainLaunchProfile.MaximumHorizontalDrift + 0.01f);
        }
    }

    [Theory]
    [InlineData(2, 1, 0, 1)]
    [InlineData(3, 1, 1, 1)]
    [InlineData(4, 2, 1, 1)]
    [InlineData(7, 5, 1, 1)]
    [InlineData(8, 6, 1, 1)]
    [InlineData(16, 12, 2, 2)]
    public void EveryMultiFragmentBurstKeepsAtLeastOneDownwardLane(
        int count,
        int expectedUpward,
        int expectedHorizontal,
        int expectedDownward)
    {
        float[] masses = Enumerable.Repeat(1f, count).ToArray();
        IReadOnlyList<BossFountainLaunch> launches =
            BossFountainLaunchProfile.Create(masses, seed: 0xD0A1UL);

        Assert.Equal(expectedUpward, launches.Count(launch =>
            launch.Lane == BossFountainLaunchLane.Upward));
        Assert.Equal(expectedHorizontal, launches.Count(launch =>
            launch.Lane == BossFountainLaunchLane.Horizontal));
        Assert.Equal(expectedDownward, launches.Count(launch =>
            launch.Lane == BossFountainLaunchLane.Downward));
        Assert.All(
            launches.Where(launch => launch.Lane == BossFountainLaunchLane.Downward),
            launch => Assert.True(launch.Velocity.Y > 0f));
    }

    [Fact]
    public void BodyBurstLaunchProfileKeepsSpeedAndDriftContractsForEveryFragmentCount()
    {
        for (int count = 2; count <= BossDismembermentMath.MaximumPieces; count++)
        {
            float[] masses = Enumerable.Range(0, count)
                .Select(index => 0.3f + index % 7 * 0.41f)
                .ToArray();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                IReadOnlyList<BossFountainLaunch> launches =
                    BossFountainLaunchProfile.Create(masses, seed);
                Assert.Equal(count, launches.Count);
                Assert.All(launches, launch => Assert.InRange(
                    Length(launch.Velocity),
                    ResolveMinimumSpeed(launch.Lane) - 0.01f,
                    ResolveMaximumSpeed(launch.Lane) + 0.01f));

                float totalMass = masses.Sum();
                float horizontalDrift = launches.Select((launch, index) =>
                    launch.Velocity.X * masses[index]).Sum() / totalMass;
                Assert.InRange(
                    MathF.Abs(horizontalDrift),
                    0f,
                    BossFountainLaunchProfile.MaximumHorizontalDrift + 0.01f);
            }
        }
    }

    [Fact]
    public void FasterRigidLaunchKeepsDeformationExcitationAtTheReferenceSpeed()
    {
        var requested = new BossFragmentPoint(
            0f,
            -BossFountainLaunchProfile.MaximumLaunchSpeed);
        float requestedSpeed = Length(requested);
        BossFragmentPoint reference = Multiply(
            requested,
            BossFountainLaunchProfile.MaximumDeformationSpeed / requestedSpeed);
        SoftFragmentBody fast = CreateBody(70, default, 0.7f);
        SoftFragmentBody referenceBody = CreateBody(71, default, 0.7f);
        fast.ConfigureDeformation(0xA11CEUL);
        referenceBody.ConfigureDeformation(0xA11CEUL);
        var fastActuator = new SoftBodyLaunchActuator(fast, requested, 0f);
        var referenceActuator = new SoftBodyLaunchActuator(referenceBody, reference, 0f);

        fastActuator.Begin();
        referenceActuator.Begin();
        fastActuator.Advance(BossFountainLaunchProfile.LaunchActuatorSeconds);
        referenceActuator.Advance(BossFountainLaunchProfile.LaunchActuatorSeconds);

        Assert.InRange(Length(Subtract(fast.CenterVelocity, requested)), 0f, 0.01f);
        Assert.InRange(Length(Subtract(referenceBody.CenterVelocity, reference)), 0f, 0.01f);
        for (int index = 0; index < SoftFragmentBody.ParticleCount; index++)
        {
            BossFragmentPoint fastResidual = Subtract(
                fast.GetParticleVelocity(index),
                fast.CenterVelocity);
            BossFragmentPoint referenceResidual = Subtract(
                referenceBody.GetParticleVelocity(index),
                referenceBody.CenterVelocity);
            Assert.InRange(
                Length(Subtract(fastResidual, referenceResidual)),
                0f,
                0.01f);
        }
    }

    [Fact]
    public void LaunchWobbleDecaysWithoutGravityOrDragReExcitation()
    {
        SoftFragmentBody body = CreateBody(2, default, 0.7f);
        body.ConfigureDeformation(0x12345678UL);
        body.PinCompressed(
            default,
            0.7f,
            phase: 0.7f,
            slideRadius: 0f,
            squashAmount: 0.22f);
        var actuator = new SoftBodyLaunchActuator(
            body,
            new BossFragmentPoint(180f, -420f),
            targetAngularVelocityRadians: 1.8f);
        actuator.Begin();
        var solver = new BossSoftBodySolver();
        var residuals = new BossFragmentPoint[SoftFragmentBody.ParticleCount];
        float previousRotation = 0f;
        float launchPeak = 0f;
        float residualAtSixTenths = float.NaN;
        float residualAtTwelveTenths = float.NaN;
        for (int frame = 0; frame < 108; frame++)
        {
            float elapsed = frame / 60f;
            float pump = SmoothStep(0f, 0.06f, elapsed);
            body.TargetLinearScale = 0.7f + 0.3f * pump;
            solver.Step(
                [body],
                [],
                1f / 60f,
                BossFountainLaunchProfile.Gravity,
                BossFountainLaunchProfile.LinearAirDrag,
                quadraticAirDrag: BossFountainLaunchProfile.QuadraticAirDrag,
                launchActuators: [actuator]);
            Assert.True(body.ResolveMinimumCellAreaRatio() > 0f);
            Assert.True(SoftBodyRenderPoseResolver.TryResolve(
                body,
                previousRotation,
                residuals,
                out SoftBodyRenderPose pose));
            previousRotation = pose.RotationRadians;
            float rms = MathF.Sqrt(residuals.Sum(point => point.X * point.X + point.Y * point.Y)
                / residuals.Length);
            float ratio = rms / body.ShortDimension;
            if (elapsed <= 0.3f)
            {
                launchPeak = Math.Max(launchPeak, ratio);
            }
            if (frame == 36)
            {
                residualAtSixTenths = ratio;
            }
            if (frame == 72)
            {
                residualAtTwelveTenths = ratio;
            }
        }

        string diagnostics = $"peak={launchPeak:F3}, at_06={residualAtSixTenths:F3}, "
            + $"at_12={residualAtTwelveTenths:F3}, crossings={body.WobbleZeroCrossings}";
        Assert.True(launchPeak >= 0.1f, diagnostics);
        Assert.True(residualAtSixTenths < 0.1f, diagnostics);
        Assert.True(residualAtTwelveTenths < 0.05f, diagnostics);
        Assert.Equal(0, body.InversionCount);
    }

    [Fact]
    public void ConnectedFragmentsDoNotGenerateCollisionManifoldsUntilTheLinkBreaks()
    {
        SoftFragmentBody first = CreateBody(30, default, 1f);
        SoftFragmentBody second = CreateBody(31, default, 1f);
        first.SetCollisionEnvelope(1f, 1f);
        second.SetCollisionEnvelope(1f, 1f);
        var link = new SoftRagdollLink(first, 0, second, 0, restLength: 0.5f)
        {
            CanBreak = true,
            MinimumBreakAgeSeconds = 0.8f,
            BreakDeadlineSeconds = 1f
        };
        var builder = new SoftContactManifoldBuilder();
        IReadOnlyList<(SoftFragmentBody First, SoftFragmentBody Second)> pairs =
            new SoftCollisionBroadphase().BuildPairs([first, second]);

        builder.BeginSubstep([link]);
        Assert.Empty(builder.Build(pairs, enableTemporalSampling: false));
        Assert.False(link.BeginSubstep(0.8f));
        Assert.True(link.BeginSubstep(0.21f));

        builder.BeginSubstep([link]);
        Assert.NotEmpty(builder.Build(pairs, enableTemporalSampling: false));
        Assert.InRange(link.BreakTimeSeconds, 1f, 1.6f);
    }

    [Fact]
    public void RightBoundaryCreatesALocalBounceWithoutAddingVerticalWalls()
    {
        SoftFragmentBody body = CreateBody(32, new BossFragmentPoint(40f, -80f), 1f);
        body.ConfigureDeformation(32UL);
        body.SetCollisionEnvelope(1f, 1f);
        body.Release(new BossFragmentPoint(480f, -300f), 0.8f);
        var solver = new BossSoftBodySolver();
        var boundary = new SoftHorizontalBoundary(-100f, 100f);
        int leftContacts = 0;
        int rightContacts = 0;
        int bounces = 0;
        float energyBefore = 0f;
        float energyAfter = 0f;
        float maximumResidual = 0f;
        for (int frame = 0; frame < 18; frame++)
        {
            SoftBodyStepMetrics metrics = solver.Step(
                [body],
                [],
                1f / 60f,
                BossFountainLaunchProfile.Gravity,
                BossFountainLaunchProfile.LinearAirDrag,
                quadraticAirDrag: BossFountainLaunchProfile.QuadraticAirDrag,
                centerSpeedLimit: 1_500f,
                horizontalBoundary: boundary);
            leftContacts += metrics.LeftWallContacts;
            rightContacts += metrics.RightWallContacts;
            bounces += metrics.WallBounces;
            energyBefore += metrics.ContactEnergyBefore;
            energyAfter += metrics.ContactEnergyAfter;
            maximumResidual = Math.Max(maximumResidual, body.ResolveRmsResidualRatio());
        }

        (BossFragmentPoint minimum, _) = body.ResolveDeformedBounds();
        string diagnostics = $"left={leftContacts}, right={rightContacts}, "
            + $"bounces={bounces}, velocity=({body.CenterVelocity.X:F1},"
            + $"{body.CenterVelocity.Y:F1}), residual={maximumResidual:F3}, "
            + $"energy={energyBefore:F1}->{energyAfter:F1}";
        Assert.Equal(0, leftContacts);
        Assert.True(rightContacts > 0, diagnostics);
        Assert.InRange(bounces, 1, 2);
        Assert.True(body.CenterVelocity.X < 0f, diagnostics);
        Assert.True(maximumResidual >= 0.05f, diagnostics);
        Assert.True(minimum.Y < -100f, diagnostics);
        Assert.True(energyAfter <= energyBefore * 1.011f + 1f, diagnostics);
        Assert.True(body.HasFiniteState);
        Assert.True(body.ResolveMinimumCellAreaRatio() > 0f);
    }

    [Fact]
    public void MinimumFragmentsUseTemporalSatAtTheFourSubstepSpeedCap()
    {
        SoftFragmentBody first = CreateSizedBody(
            33,
            new BossFragmentPoint(-7.5f, 0f),
            size: 10f);
        SoftFragmentBody second = CreateSizedBody(
            34,
            new BossFragmentPoint(7.5f, 0f),
            size: 10f);
        first.SetCollisionEnvelope(1f, 0f);
        second.SetCollisionEnvelope(1f, 0f);
        first.Release(new BossFragmentPoint(4_400f, 0f), 0f);
        second.Release(new BossFragmentPoint(-4_400f, 0f), 0f);

        var solver = new BossSoftBodySolver();
        int sweptContacts = 0;
        int inversions = 0;
        int rollbacks = 0;
        float energyBefore = 0f;
        float energyAfter = 0f;
        int openingSubsteps = 0;
        for (int frame = 0; frame < 240; frame++)
        {
            SoftBodyStepMetrics metrics = solver.Step(
                [first, second],
                [],
                1f / 60f,
                gravity: 0f,
                airDrag: 0.1f,
                centerSpeedLimit: 4_405f);
            if (frame == 0)
            {
                openingSubsteps = metrics.Substeps;
            }

            sweptContacts += metrics.SweptContacts;
            inversions += metrics.Inversions;
            rollbacks += metrics.Rollbacks;
            energyBefore += metrics.ContactEnergyBefore;
            energyAfter += metrics.ContactEnergyAfter;
            Assert.True(
                first.Center.X < second.Center.X,
                $"frame={frame}, first={first.Center.X:F3}, second={second.Center.X:F3}, "
                    + $"swept={sweptContacts}, inversions={inversions}, rollbacks={rollbacks}");
        }

        string diagnostics = $"substeps={openingSubsteps}, swept={sweptContacts}, "
            + $"positions={first.Center.X:F2}/{second.Center.X:F2}, "
            + $"inversions={inversions}, rollbacks={rollbacks}, "
            + $"energy={energyBefore:F1}->{energyAfter:F1}";
        Assert.Equal(BossSoftBodySolver.MaximumSubsteps, openingSubsteps);
        Assert.True(sweptContacts > 0, diagnostics);
        Assert.True(first.Center.X < second.Center.X, diagnostics);
        Assert.True(second.Center.X - first.Center.X >= 9.8f, diagnostics);
        Assert.Equal(0, inversions);
        Assert.True(first.HasFiniteState);
        Assert.True(second.HasFiniteState);
        Assert.True(
            energyAfter <= energyBefore * 1.011f + 1f,
            diagnostics);
    }

    [Fact]
    public void NonFiniteContactResponseRestoresTheCapturedParticleVelocities()
    {
        SoftFragmentBody first = CreateBody(40, default, 1f);
        SoftFragmentBody second = CreateBody(41, new BossFragmentPoint(80f, 0f), 1f);
        first.Release(new BossFragmentPoint(120f, 0f), 0f);
        second.Release(new BossFragmentPoint(-120f, 0f), 0f);
        var manifold = new SoftContactManifold(
            first,
            second,
            new BossFragmentPoint(1f, 0f),
            isNewContact: true);
        var audit = new SoftBodyEnergyAudit();
        audit.Capture(manifold);
        first.ApplyVelocityImpulse(0f, 0f, new BossFragmentPoint(1f, 0f), float.NaN);

        SoftBodyEnergyAuditResult result = audit.LimitContactEnergy(manifold);

        Assert.True(result.Limited);
        Assert.True(first.HasFiniteState);
        Assert.True(second.HasFiniteState);
        Assert.InRange(first.CenterVelocity.X, 119.99f, 120.01f);
        Assert.InRange(second.CenterVelocity.X, -120.01f, -119.99f);
    }

    [Fact]
    public void UniformGravityAndDragDoNotInjectNonRigidMotion()
    {
        SoftFragmentBody body = CreateBody(50, default, 1f);
        body.ConfigureDeformation(0xBADC0FFEEUL);
        body.Release(new BossFragmentPoint(120f, -40f), 0f);

        float residualBefore = ResolveVelocityResidualRms(body);
        body.Predict(
            1f / 120f,
            BossFountainLaunchProfile.Gravity,
            BossFountainLaunchProfile.LinearAirDrag,
            BossFountainLaunchProfile.QuadraticAirDrag);

        float residualAfter = ResolveVelocityResidualRms(body);
        Assert.InRange(residualBefore, 0f, 0.001f);
        Assert.InRange(residualAfter, 0f, 0.001f);
        Assert.True(body.CenterVelocity.X > 0f);
        Assert.True(body.CenterVelocity.Y > -40f);
    }

    [Fact]
    public void CenterSpeedLimitLeavesTheNonRigidJellyMotionIntact()
    {
        SoftFragmentBody body = CreateBody(51, default, 1f);
        body.ConfigureDeformation(123UL);
        body.BeginStagedRelease();
        body.ApplyLaunchVelocityDelta(
            new BossFragmentPoint(1_200f, -1_200f),
            angularVelocityDelta: 1.5f,
            differentialFraction: 0.8f);
        float residualBefore = ResolveVelocityResidualRms(body);

        SoftBodyStepMetrics metrics = new BossSoftBodySolver().Step(
            [body],
            [],
            1f / 60f,
            gravity: 0f,
            airDrag: 0f);
        float residualAfter = ResolveVelocityResidualRms(body);

        Assert.True(metrics.MaximumObservedCenterSpeed > BossSoftBodySolver.DefaultMaximumCenterSpeed);
        Assert.InRange(
            metrics.MaximumCenterSpeed,
            0f,
            BossSoftBodySolver.DefaultMaximumCenterSpeed);
        Assert.InRange(
            body.ResolveCenterSpeed(),
            0f,
            BossSoftBodySolver.DefaultMaximumCenterSpeed);
        Assert.True(residualAfter >= residualBefore * 0.2f);
    }

    [Fact]
    public void RagdollEndpointTransfersVisibleBendingToItsNeighbor()
    {
        SoftFragmentBody first = CreateBody(52, new BossFragmentPoint(-60f, 0f), 1f);
        SoftFragmentBody second = CreateBody(53, new BossFragmentPoint(60f, 0f), 1f);
        first.ConfigureDeformation(52UL);
        second.ConfigureDeformation(53UL);
        var actuator = new SoftBodyLaunchActuator(
            first,
            new BossFragmentPoint(-180f, -320f),
            targetAngularVelocityRadians: 1.2f);
        actuator.Begin();
        second.BeginStagedRelease();
        var link = new SoftRagdollLink(first, 7, second, 4, restLength: 20f)
        {
            CanBreak = false
        };
        var solver = new BossSoftBodySolver();
        for (int frame = 0; frame < 24; frame++)
        {
            solver.Step(
                [first, second],
                [link],
                1f / 60f,
                gravity: 0f,
                airDrag: 0.05f,
                launchActuators: [actuator]);
        }

        Assert.True(second.ResolveRmsResidualRatio() > 0.05f);
        Assert.False(link.Broken);
        Assert.True(second.HasFiniteState);
    }

    [Fact]
    public void TwentyFourPieceFountainKeepsReadableDeformationWithoutInvalidBodies()
    {
        const ulong seed = 0xC0FFEEUL;
        IReadOnlyList<BossFountainLaunch> launches = BossFountainLaunchProfile.Create(
            Enumerable.Repeat(1f, 24).ToArray(),
            seed);
        var bodies = new List<SoftFragmentBody>(24);
        var actuators = new List<SoftBodyLaunchActuator>(24);
        for (int index = 0; index < launches.Count; index++)
        {
            SoftFragmentBody body = CreateBody(index, default, 0.7f);
            body.ConfigureDeformation(seed + (ulong)index);
            body.PinCompressed(
                default,
                0.7f,
                phase: index * 0.41f,
                slideRadius: 0f,
                squashAmount: 0.22f);
            var actuator = new SoftBodyLaunchActuator(
                body,
                launches[index].Velocity,
                launches[index].AngularVelocityDegrees * MathF.PI / 180f);
            actuator.Begin();
            bodies.Add(body);
            actuators.Add(actuator);
        }

        var solver = new BossSoftBodySolver();
        var launchStatistics = new SoftBodyResidualStatistics();
        var settledStatistics = new SoftBodyResidualStatistics();
        var residuals = new BossFragmentPoint[SoftFragmentBody.ParticleCount];
        var previousRotations = new float[bodies.Count];
        int inversions = 0;
        int invalidBodies = 0;
        float maximumCenterSpeed = 0f;
        for (int frame = 0; frame < 108; frame++)
        {
            float elapsed = frame / 60f;
            float pump = SmoothStep(0f, 0.06f, elapsed);
            for (int index = 0; index < bodies.Count; index++)
            {
                SoftFragmentBody body = bodies[index];
                body.TargetLinearScale = 0.7f + 0.3f * pump;
                body.SetCollisionEnvelope(0f, 0f);
            }

            SoftBodyStepMetrics metrics = solver.Step(
                bodies,
                [],
                1f / 60f,
                BossFountainLaunchProfile.Gravity,
                BossFountainLaunchProfile.LinearAirDrag,
                quadraticAirDrag: BossFountainLaunchProfile.QuadraticAirDrag,
                launchActuators: actuators);
            inversions += metrics.Inversions;
            maximumCenterSpeed = Math.Max(maximumCenterSpeed, metrics.MaximumCenterSpeed);
            for (int index = 0; index < bodies.Count; index++)
            {
                SoftFragmentBody body = bodies[index];
                if (!body.HasFiniteState
                    || !SoftBodyRenderPoseResolver.TryResolve(
                        body,
                        previousRotations[index],
                        residuals,
                        out SoftBodyRenderPose pose))
                {
                    invalidBodies++;
                    continue;
                }

                previousRotations[index] = pose.RotationRadians;
                float residualRatio = ResolveResidualRmsRatio(residuals, body.ShortDimension);
                if (elapsed is >= 0.1f and <= 0.3f)
                {
                    launchStatistics.Add(residualRatio);
                }
                else if (elapsed >= 1.2f)
                {
                    settledStatistics.Add(residualRatio);
                }
            }
        }

        string diagnostics = $"launch_p50={launchStatistics.Percentile(0.5f):F3}, "
            + $"launch_max={launchStatistics.Maximum:F3}, "
            + $"settled_p90={settledStatistics.Percentile(0.9f):F3}, "
            + $"settled_max={settledStatistics.Maximum:F3}, inversions={inversions}, "
            + $"invalid={invalidBodies}, speed={maximumCenterSpeed:F3}";
        Assert.True(
            launchStatistics.Percentile(0.5f) >= 0.1f,
            diagnostics);
        Assert.True(
            settledStatistics.Percentile(0.9f) < 0.08f,
            diagnostics);
        Assert.True(launchStatistics.Maximum <= 0.45f, diagnostics);
        Assert.Equal(0, inversions);
        Assert.Equal(0, invalidBodies);
        Assert.InRange(
            maximumCenterSpeed,
            0f,
            BossSoftBodySolver.DefaultMaximumCenterSpeed);
    }

    [Fact]
    public void OriginalBoneTetherBreaksOnlyAfterMinimumAgeAndSustainedStretch()
    {
        SoftFragmentBody first = CreateBody(20, default, 1f);
        SoftFragmentBody second = CreateBody(21, new BossFragmentPoint(240f, 0f), 1f);
        var tether = new SoftRagdollLink(first, 3, second, 0, restLength: 20f)
        {
            MinimumBreakAgeSeconds = 0.25f,
            BreakStretchRatio = 1.45f,
            BreakPadding = 0f,
            FatigueThresholdSeconds = 0.05f,
            BreakDeadlineSeconds = 0.85f
        };
        for (int step = 0; step < 14; step++)
        {
            Assert.False(tether.BeginSubstep(1f / 60f));
        }

        Assert.False(tether.Broken);
        bool broke = tether.BeginSubstep(1f / 60f);

        Assert.True(broke);
        Assert.True(tether.AgeSeconds >= 0.25f);
    }

    [Fact]
    public void CollisionMarginRampsOnlyAfterTheOpeningSeparationWindow()
    {
        Assert.Equal(0f, BossFountainLaunchProfile.ResolveCollisionMarginScale(0.29f, 1f));
        Assert.InRange(
            BossFountainLaunchProfile.ResolveCollisionMarginScale(0.4f, 1f),
            0.49f,
            0.51f);
        Assert.InRange(
            BossFountainLaunchProfile.ResolveCollisionMarginScale(0.5f, 1f),
            0.99f,
            1f);
        Assert.Equal(0f, BossFountainLaunchProfile.ResolveCollisionMarginScale(1f, 0f));
    }

    [Fact]
    public void TwentyFourPieceFountainRemainsFiniteAndNeverInvertsForFourSeconds()
    {
        BossFragmentPoint sceneMinimum = default;
        var sceneMaximum = new BossFragmentPoint(1_920f, 1_080f);
        var boundary = new SoftHorizontalBoundary(sceneMinimum.X, sceneMaximum.X);
        float[] masses = Enumerable.Repeat(1f, 24).ToArray();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            float originFraction = (seed % 3) switch
            {
                0 => 0.2f,
                1 => 0.5f,
                _ => 0.8f
            };
            float originY = (seed % 3) switch
            {
                0 => 380f,
                1 => 540f,
                _ => 700f
            };
            var origin = new BossFragmentPoint(sceneMaximum.X * originFraction, originY);
            IReadOnlyList<BossFountainLaunch> natural =
                BossFountainLaunchProfile.Create(masses, seed);
            BossFountainLaunchPlan plan = BossFountainLaunchProfile.CreatePlan(natural);
            var bodies = new List<SoftFragmentBody>(24);
            var actuators = new List<SoftBodyLaunchActuator>(24);
            for (int index = 0; index < plan.Launches.Count; index++)
            {
                SoftFragmentBody body = CreateBody(index, origin, 0.7f);
                body.ConfigureDeformation(seed * 31UL + (ulong)index);
                body.PinCompressed(
                    origin,
                    0.7f,
                    phase: index * 0.39f,
                    slideRadius: 0f,
                    squashAmount: 0.22f);
                var actuator = new SoftBodyLaunchActuator(
                    body,
                    plan.Launches[index].Velocity,
                    plan.Launches[index].AngularVelocityDegrees * MathF.PI / 180f);
                actuator.Begin();
                bodies.Add(body);
                actuators.Add(actuator);
            }

            IReadOnlyList<SoftRagdollLink> links = CreateFountainTestLinks(bodies);
            var solver = new BossSoftBodySolver();
            int rollbacks = 0;
            int inversions = 0;
            float contactEnergyBefore = 0f;
            float contactEnergyAfter = 0f;
            float maximumCenterSpeed = 0f;
            for (int frame = 0; frame < 240; frame++)
            {
                float elapsed = frame / 60f;
                float pump = SmoothStep(0f, 0.06f, elapsed);
                for (int index = 0; index < bodies.Count; index++)
                {
                    SoftFragmentBody body = bodies[index];
                    body.TargetLinearScale = 0.7f + 0.3f * pump;
                    float hull = 1f;
                    body.SetCollisionEnvelope(
                        hull,
                        BossFountainLaunchProfile.ResolveCollisionMarginScale(elapsed, hull));
                }

                SoftBodyStepMetrics metrics = solver.Step(
                    bodies,
                    links,
                    1f / 60f,
                    plan.Gravity,
                    BossFountainLaunchProfile.LinearAirDrag,
                    quadraticAirDrag: BossFountainLaunchProfile.QuadraticAirDrag,
                    launchActuators: actuators,
                    centerSpeedLimit: plan.MaximumCenterSpeed,
                    horizontalBoundary: elapsed >= 0.1f ? boundary : null);
                rollbacks += metrics.Rollbacks;
                inversions += metrics.Inversions;
                contactEnergyBefore += metrics.ContactEnergyBefore;
                contactEnergyAfter += metrics.ContactEnergyAfter;
                maximumCenterSpeed = Math.Max(maximumCenterSpeed, metrics.MaximumCenterSpeed);
                foreach (SoftFragmentBody body in bodies)
                {
                    Assert.True(body.HasFiniteState);
                    Assert.True(body.ResolveMinimumCellAreaRatio() > 0f);
                }
            }

            string diagnostics = $"seed={seed}, origin=({originFraction:F1},{originY:F0}), "
                + $"gravity={plan.Gravity:F1}, "
                + $"inversions={inversions}, rollbacks={rollbacks}, "
                + $"speed={maximumCenterSpeed:F1}/{plan.MaximumCenterSpeed:F1}, "
                + $"energy={contactEnergyBefore:F1}->{contactEnergyAfter:F1}";
            Assert.True(inversions == 0, diagnostics);
            Assert.True(rollbacks <= 8, diagnostics);
            Assert.True(maximumCenterSpeed <= plan.MaximumCenterSpeed + 0.1f, diagnostics);
            Assert.True(
                contactEnergyAfter <= contactEnergyBefore * 1.011f + 1f,
                diagnostics);
        }
    }

    private static IReadOnlyList<SoftRagdollLink> CreateFountainTestLinks(
        IReadOnlyList<SoftFragmentBody> bodies)
    {
        if (bodies.Count < 6)
        {
            return [];
        }

        (int First, int Second)[] pairs = [(0, 1), (3, 4)];
        var links = new List<SoftRagdollLink>(pairs.Length);
        foreach ((int firstIndex, int secondIndex) in pairs)
        {
            const int firstParticle = 7;
            const int secondParticle = 4;
            BossFragmentPoint firstPoint = bodies[firstIndex].GetParticlePosition(firstParticle);
            BossFragmentPoint secondPoint = bodies[secondIndex].GetParticlePosition(secondParticle);
            float restLength = Length(Subtract(secondPoint, firstPoint));
            links.Add(new SoftRagdollLink(
                bodies[firstIndex],
                firstParticle,
                bodies[secondIndex],
                secondParticle,
                restLength)
            {
                CanBreak = true,
                MinimumBreakAgeSeconds = 0.25f,
                BreakStretchRatio = 1.45f,
                BreakPadding = 2f,
                FatigueThresholdSeconds = 0.05f,
                BreakDeadlineSeconds = 0.7f
            });
        }

        return links;
    }

    private static SoftFragmentBody CreateBody(
        int id,
        BossFragmentPoint center,
        float compressedScale)
    {
        BossFragmentPoint[] hull =
        [
            new(-50f, -50f),
            new(50f, -50f),
            new(50f, 50f),
            new(-50f, 50f)
        ];
        return new SoftFragmentBody(
            id,
            new SoftBodyBounds(-50f, -50f, 100f, 100f),
            hull,
            center,
            compressedScale,
            mass: 1f);
    }

    private static SoftFragmentBody CreateSizedBody(
        int id,
        BossFragmentPoint center,
        float size)
    {
        float half = size * 0.5f;
        BossFragmentPoint[] hull =
        [
            new(-half, -half),
            new(half, -half),
            new(half, half),
            new(-half, half)
        ];
        return new SoftFragmentBody(
            id,
            new SoftBodyBounds(-half, -half, size, size),
            hull,
            center,
            compressedScale: 1f,
            mass: 1f);
    }

    private static float SmoothStep(float start, float end, float value)
    {
        float progress = Math.Clamp((value - start) / Math.Max(0.0001f, end - start), 0f, 1f);
        return progress * progress * (3f - 2f * progress);
    }

    private static float ResolveResidualRmsRatio(
        IReadOnlyList<BossFragmentPoint> residuals,
        float shortDimension)
    {
        double squared = 0d;
        for (int index = 0; index < residuals.Count; index++)
        {
            squared += residuals[index].X * residuals[index].X
                + residuals[index].Y * residuals[index].Y;
        }

        return (float)Math.Sqrt(squared / Math.Max(1, residuals.Count))
            / Math.Max(1f, shortDimension);
    }

    private static float ResolveVelocityResidualRms(SoftFragmentBody body)
    {
        BossFragmentPoint centerVelocity = body.CenterVelocity;
        double squared = 0d;
        for (int index = 0; index < SoftFragmentBody.ParticleCount; index++)
        {
            BossFragmentPoint residual = Subtract(
                body.GetParticleVelocity(index),
                centerVelocity);
            squared += residual.X * residual.X + residual.Y * residual.Y;
        }

        return (float)Math.Sqrt(squared / SoftFragmentBody.ParticleCount);
    }

    private static BossFragmentPoint Add(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X + second.X, first.Y + second.Y);

    private static BossFragmentPoint Subtract(BossFragmentPoint first, BossFragmentPoint second) =>
        new(first.X - second.X, first.Y - second.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float scalar) =>
        new(point.X * scalar, point.Y * scalar);

    private static float Length(BossFragmentPoint point) =>
        MathF.Sqrt(point.X * point.X + point.Y * point.Y);

    private static float ResolveMinimumSpeed(BossFountainLaunchLane lane) =>
        lane == BossFountainLaunchLane.Upward
            ? BossFountainLaunchProfile.UpwardMinimumLaunchSpeed
            : BossFountainLaunchProfile.MinimumLaunchSpeed;

    private static float ResolveMaximumSpeed(BossFountainLaunchLane lane) => lane switch
    {
        BossFountainLaunchLane.Horizontal =>
            BossFountainLaunchProfile.HorizontalMaximumLaunchSpeed,
        BossFountainLaunchLane.Downward =>
            BossFountainLaunchProfile.DownwardMaximumLaunchSpeed,
        _ => BossFountainLaunchProfile.MaximumLaunchSpeed
    };
}
