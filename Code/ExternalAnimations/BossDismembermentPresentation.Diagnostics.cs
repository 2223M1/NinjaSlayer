using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;
public sealed partial class BossDismembermentPresentation
{
    private void RecordBodyDiagnostics(SoftFragmentBody body)
    {
        _wobbleCrossingsByFragment[body.Id] = body.WobbleZeroCrossings;
    }

    private void RecordJointDiagnostics()
    {
        for (int index = 0; index < _joints.Count; index++)
        {
            SoftRagdollLink joint = _joints[index];
            _maximumTetherAge = Math.Max(_maximumTetherAge, joint.AgeSeconds);
            if (joint.Broken && _recordedBrokenJoints.Add(joint))
            {
                _jointBreakTimes.Add(joint.BreakTimeSeconds);
            }
        }
    }

    private void CompleteAndFree()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        SetProcess(false);
        RecordSettlementIfDue(
            _burstElapsed,
            force: _fragments.Count == 0);
        double averageSolverMilliseconds = ResolveAverageMilliseconds(_solverTicks, _solverSteps);
        double longestSolverMilliseconds = ResolveMilliseconds(_longestSolverTicks);
        double averageRenderMilliseconds = ResolveAverageMilliseconds(_renderTicks, _renderFrames);
        double longestRenderMilliseconds = ResolveMilliseconds(_longestRenderTicks);
        for (int index = 0; index < _bodies.Count; index++)
        {
            RecordBodyDiagnostics(_bodies[index]);
        }

        RecordJointDiagnostics();
        string breakTimes = string.Join(",", _jointBreakTimes
            .Order()
            .Select(seconds => $"{seconds:F3}"));
        string wobbleCounts = string.Join(",", _wobbleCrossingsByFragment
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
        Entry.Logger.Info(
            $"Boss soft-body summary: boss={_monsterId}, motion_seed={_motionSeed}, "
            + $"fragments={_spawnedFragmentCount}, "
            + $"capture={_capturePixelWidth}x{_capturePixelHeight}, "
            + $"capture_bytes={_captureTextureBytes}, "
            + $"semantic_parts={_semanticPartCount}, "
            + $"merged_parts={_mergedPartCount}, "
            + $"local_splits={_splitFragmentCount}, "
            + $"atlas_density={_atlasDensity:F3}, "
            + $"baseline_scene_scale={_baselineSceneScale:F3}, "
            + $"spawn_scene_scale={_currentSceneScale:F3}, "
            + $"boss_screen_size="
            + $"({_originalBossScreenSize.X:F3},{_originalBossScreenSize.Y:F3}), "
            + $"fragment_union_size="
            + $"({_fragmentUnionScreenSize.X:F3},{_fragmentUnionScreenSize.Y:F3}), "
            + $"fragment_raw_width_ratio={_fragmentRawWidthRatio:F3}, "
            + $"fragment_raw_height_ratio={_fragmentRawHeightRatio:F3}, "
            + $"fragment_calibration_scale={_fragmentCalibrationScale:F3}, "
            + $"fragment_width_ratio={_fragmentWidthRatio:F3}, "
            + $"fragment_height_ratio={_fragmentHeightRatio:F3}, "
            + $"fragment_area_p50={_medianFragmentArea:F3}, "
            + $"fragment_area_max={_maximumFragmentArea:F3}, "
            + $"particles={_spawnedFragmentCount * SoftFragmentBody.ParticleCount}, "
            + $"constraints={_spawnedConstraintCount}, joints={_spawnedJointCount}, "
            + $"contact_manifolds={_contactCount}, "
            + $"contact_points={_contactPointCount}, "
            + $"contact_starts={_contactStartCount}, visible_bounces={_visibleBounceCount}, "
            + $"swept_contacts={_sweptContactCount}, "
            + $"left_wall_contacts={_leftWallContactCount}, "
            + $"right_wall_contacts={_rightWallContactCount}, "
            + $"wall_bounces={_wallBounceCount}, maximum_penetration={_maximumPenetration:F3}, "
            + $"broken_joints={_brokenLinkCount}, minimum_area_ratio={_minimumAreaRatio:F3}, "
            + $"maximum_stretch={_maximumStretch:F3}, maximum_residual={_maximumResidual:F3}, "
            + $"rms_residual_average={_residualStatistics.Average:F3}, "
            + $"rms_residual_p50={_residualStatistics.Percentile(0.5f):F3}, "
            + $"rms_residual_p90={_residualStatistics.Percentile(0.9f):F3}, "
            + $"rms_residual_maximum={_residualStatistics.Maximum:F3}, "
            + $"rms_visible_fraction={_residualStatistics.VisibleFraction:F3}, "
            + $"launch_max_speed={_maximumLaunchCenterSpeed:F3}, "
            + $"launch_observed_max_speed={_maximumObservedCenterSpeed:F3}, "
            + $"launch_speed_p50={_launchSpeedP50:F3}, "
            + $"launch_speed_p90={_launchSpeedP90:F3}, "
            + $"launch_horizontal_drift={_launchHorizontalDrift:F3}, "
            + $"launch_up={_upwardLaunchCount}, "
            + $"launch_horizontal={_horizontalLaunchCount}, "
            + $"launch_down={_downwardLaunchCount}, "
            + $"sector_coverage_035={_sectorCoverage035}, "
            + $"spatial_dispersion_035={_spatialDispersion035:F3}, "
            + $"fountain_gravity={_gravity:F3}, "
            + $"center_speed_limit={_centerSpeedLimit:F3}, "
            + $"settlement_bottom_band={_settlementBottomBandCount}, "
            + $"settlement_below_bottom={_settlementBelowBottomCount}, "
            + $"settlement_fraction={(_spawnedFragmentCount <= 0 ? 0f : (_settlementBottomBandCount + _settlementBelowBottomCount) / (float)_spawnedFragmentCount):F3}, "
            + $"settlement_remaining_descending={_settlementDescendingCount}, "
            + $"contact_energy_before={_contactEnergyBefore:F3}, "
            + $"contact_energy_after={_contactEnergyAfter:F3}, "
            + $"limited_contacts={_limitedContactCount}, "
            + $"limited_center_speeds={_limitedCenterSpeedCount}, "
            + $"first_fragment_contact_ms={(_firstFragmentContactSeconds < 0f ? -1f : _firstFragmentContactSeconds * 1000f):F1}, "
            + $"rollbacks={_rollbackCount}, inversions={_inversionCount}, "
            + $"safety_projections={_safetyProjectionCount}, "
            + $"wobble_zero_crossings={_wobbleZeroCrossings}, "
            + $"wobble_crossings_by_fragment=[{wobbleCounts}], "
            + $"joint_break_seconds=[{breakTimes}], "
            + $"maximum_tether_seconds={_maximumTetherAge:F3}, "
            + $"capture_setup_ms={ResolveMilliseconds(_captureSetupTicks):F3}, "
            + $"capture_total_ms={ResolveMilliseconds(_captureReadyElapsedTicks):F3}, "
            + $"capture_measurement_cpu_ms={ResolveMilliseconds(_captureMeasurementCpuTicks):F3}, "
            + $"capture_atlas_cpu_ms={ResolveMilliseconds(_captureAtlasCpuTicks):F3}, "
            + $"capture_fragment_prepare_cpu_ms={ResolveMilliseconds(_captureFragmentPreparationCpuTicks):F3}, "
            + $"capture_cpu_frame_max_ms={ResolveMilliseconds(_captureMaximumCpuFrameTicks):F3}, "
            + $"spawn_setup_ms={ResolveMilliseconds(_spawnSetupTicks):F3}, "
            + $"solver_average_ms={averageSolverMilliseconds:F3}, "
            + $"solver_longest_ms={longestSolverMilliseconds:F3}, "
            + $"render_average_ms={averageRenderMilliseconds:F3}, "
            + $"render_longest_ms={longestRenderMilliseconds:F3}, "
            + $"adaptive_substeps_max={_maximumAdaptiveSubsteps}, "
            + $"dropped_simulation_ms={_droppedSimulationSeconds * 1000f:F2}.");
        ClearFragments();
        _capture?.Dispose();
        _capture = null;
        _completion.TrySetResult();
        this.QueueFreeSafely();
    }

    private static double ResolveAverageMilliseconds(long ticks, int count) =>
        count <= 0 ? 0d : ResolveMilliseconds(ticks) / count;

    private static double ResolveMilliseconds(long ticks) =>
        ticks * 1000d / System.Diagnostics.Stopwatch.Frequency;

    private static float Percentile(float[] sortedValues, float percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0f;
        }

        int index = Math.Clamp(
            (int)MathF.Round((sortedValues.Length - 1) * Math.Clamp(percentile, 0f, 1f)),
            0,
            sortedValues.Length - 1);
        return sortedValues[index];
    }

    private static float Lerp(float from, float to, float weight) =>
        from + (to - from) * Math.Clamp(weight, 0f, 1f);

    private static ulong CreateSeed(NCreature creature)
    {
        ulong combatId = unchecked((ulong)creature.Entity.CombatId.GetValueOrDefault());
        ulong modelHash = BossDismembermentMath.StableHash64(
            creature.Entity.Monster?.Id.Entry ?? creature.Name.ToString());
        return combatId * 0x9E3779B97F4A7C15UL ^ modelHash ^ 0x4E534255525354UL;
    }

    private static Rect2 ResolveFallbackCaptureBounds(
        NCreature creature,
        Node2D sourceBody)
    {
        Transform2D bodyToGlobal = sourceBody.GlobalTransform;
        Transform2D boundsToGlobal = creature.Visuals.Bounds.GetGlobalTransform();
        Transform2D globalToBody = bodyToGlobal.AffineInverse();
        Rect2 localBounds = BoundsOf(
            RectCorners(new Rect2(Vector2.Zero, creature.Visuals.Bounds.Size))
                .Select(point => globalToBody * (boundsToGlobal * point)));
        if (!IsValidBounds(localBounds))
        {
            throw new InvalidOperationException(
                "The boss visual bounds are unavailable for dismemberment capture.");
        }

        return localBounds;
    }

    private static bool IsValidBounds(Rect2 bounds) =>
        float.IsFinite(bounds.Position.X)
        && float.IsFinite(bounds.Position.Y)
        && float.IsFinite(bounds.Size.X)
        && float.IsFinite(bounds.Size.Y)
        && bounds.Size.X > 1f
        && bounds.Size.Y > 1f;

    private static Rect2 BoundsOf(IEnumerable<Vector2> points)
    {
        Vector2[] values = points.ToArray();
        if (values.Length == 0)
        {
            return default;
        }

        float minX = values.Min(point => point.X);
        float minY = values.Min(point => point.Y);
        float maxX = values.Max(point => point.X);
        float maxY = values.Max(point => point.Y);
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private static BossFragmentRect MergeBounds(IEnumerable<BossFragmentRect> bounds)
    {
        BossFragmentRect[] values = bounds.ToArray();
        if (values.Length == 0)
        {
            return default;
        }

        float minimumX = values.Min(value => value.X);
        float minimumY = values.Min(value => value.Y);
        float maximumX = values.Max(value => value.X + value.Width);
        float maximumY = values.Max(value => value.Y + value.Height);
        return new BossFragmentRect(
            minimumX,
            minimumY,
            maximumX - minimumX,
            maximumY - minimumY);
    }

    private static Vector2[] RectCorners(Rect2 rect) =>
    [
        rect.Position,
        rect.Position + new Vector2(rect.Size.X, 0f),
        rect.End,
        rect.Position + new Vector2(0f, rect.Size.Y)
    ];

    private static float PolygonArea(Vector2[] polygon)
    {
        double twiceArea = 0d;
        for (int index = 0; index < polygon.Length; index++)
        {
            Vector2 current = polygon[index];
            Vector2 next = polygon[(index + 1) % polygon.Length];
            twiceArea += current.X * next.Y - next.X * current.Y;
        }

        return (float)Math.Abs(twiceArea * 0.5d);
    }

    private static Vector2 ToVector2(BossFragmentPoint point) => new(point.X, point.Y);

    private static BossFragmentPoint ToBossFragmentPoint(Vector2 point) => new(point.X, point.Y);

    private BossFragmentPoint ResolveDescriptorRestCenter(
        BossCapturedFragmentDescriptor descriptor)
    {
        BossFragmentRect bounds = BossDismembermentMath.BoundsOf(descriptor.Cell.Vertices);
        Vector2 center = _bodyToPresentation
            * new Vector2(
                bounds.X + bounds.Width * 0.5f,
                bounds.Y + bounds.Height * 0.5f);
        return ToBossFragmentPoint(center);
    }

    private BossFragmentPoint ResolveDomainRestCenter(
        IReadOnlyList<BossCapturedFragmentDescriptor> descriptors,
        float[] massWeights,
        bool belongsToDetachedPart,
        BossFragmentPoint fallback)
    {
        float weightedX = 0f;
        float weightedY = 0f;
        float totalWeight = 0f;
        for (int index = 0; index < descriptors.Count; index++)
        {
            if (descriptors[index].Part.BelongsToDetachedPart != belongsToDetachedPart)
            {
                continue;
            }

            BossFragmentPoint center = ResolveDescriptorRestCenter(descriptors[index]);
            float weight = Math.Max(0.0001f, massWeights[index]);
            weightedX += center.X * weight;
            weightedY += center.Y * weight;
            totalWeight += weight;
        }

        return totalWeight > 0.0001f
            ? new BossFragmentPoint(weightedX / totalWeight, weightedY / totalWeight)
            : fallback;
    }

    private sealed class SoftFragmentRuntime(
        BossCapturedFragmentRenderSurface surface,
        float mappedArea,
        float compressedScale,
        float squashAmount,
        float compressionPhase,
        float compressionSpeed,
        Vector2 compressionOrigin,
        BossFragmentPoint restCenterOffset)
    {
        public BossCapturedFragmentRenderSurface Surface { get; } = surface;
        public SoftFragmentBody Body => Surface.Body;
        public float MappedArea { get; } = mappedArea;
        public Vector2 LaunchVelocity { get; set; }
        public float LaunchAngularVelocityDegrees { get; set; }
        public BossFountainLaunchLane LaunchLane { get; set; }
        public float CompressedScale { get; } = compressedScale;
        public float SquashAmount { get; } = squashAmount;
        public float CompressionPhase { get; } = compressionPhase;
        public float CompressionSpeed { get; } = compressionSpeed;
        public Vector2 CompressionOrigin { get; set; } = compressionOrigin;
        public BossFragmentPoint RestCenterOffset { get; } = restCenterOffset;
    }
}
