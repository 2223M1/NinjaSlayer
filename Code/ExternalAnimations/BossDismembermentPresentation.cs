using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal readonly record struct BossDismembermentSpawn(bool Spawned, Task Completion);

internal sealed class ArchitectBossSoftBodyLead : IDisposable
{
    private BossDismembermentPresentation? _presentation;
    private readonly Task _completion;

    internal ArchitectBossSoftBodyLead(BossDismembermentPresentation presentation)
    {
        _presentation = presentation;
        _completion = presentation.Completion;
    }

    public BossDismembermentSpawn TriggerBurst()
    {
        BossDismembermentPresentation? presentation =
            Interlocked.Exchange(ref _presentation, null);
        return presentation != null
            && GodotObject.IsInstanceValid(presentation)
            && presentation.IsInsideTree()
            && presentation.TriggerArchitectBurst()
            ? new BossDismembermentSpawn(true, _completion)
            : new BossDismembermentSpawn(false, _completion);
    }

    public void Dispose()
    {
        BossDismembermentPresentation? presentation =
            Interlocked.Exchange(ref _presentation, null);
        if (presentation != null && GodotObject.IsInstanceValid(presentation))
        {
            presentation.CancelPresentation();
        }
    }
}

internal sealed class BossDismembermentSnapshot : IDisposable
{
    private BossVisualCapture? _capture;

    public BossDismembermentSnapshot(
        BossVisualCapture capture,
        Rect2 bodyLocalBounds,
        ulong seed,
        string monsterId)
    {
        _capture = capture;
        BodyLocalBounds = bodyLocalBounds;
        BodyToSceneContainer = capture.BodyToSceneContainer;
        BaselineSceneToGlobal = capture.BaselineSceneToGlobal;
        BaselineSceneScale = capture.BaselineSceneScale;
        BodyBaselineScreenBounds = capture.BodyBaselineScreenBounds;
        Seed = seed;
        MonsterId = monsterId;
    }

    public Rect2 BodyLocalBounds { get; }
    public Transform2D BodyToSceneContainer { get; }
    public Transform2D BaselineSceneToGlobal { get; }
    public Vector2 BaselineSceneScale { get; }
    public Rect2 BodyBaselineScreenBounds { get; }
    public Transform2D BodyGlobalTransform =>
        BaselineSceneToGlobal * BodyToSceneContainer;
    public Vector2 BodyGlobalCenter => BodyGlobalTransform * BodyLocalBounds.GetCenter();
    public ulong Seed { get; }
    public string MonsterId { get; }

    internal BossVisualCapture? TakeCapture() => Interlocked.Exchange(ref _capture, null);

    public void Dispose() => Interlocked.Exchange(ref _capture, null)?.Dispose();
}

public sealed partial class BossDismembermentPresentation : Node2D
{
    private enum PresentationMode
    {
        CompressedBurst,
        ArchitectLead
    }

    private const float PhysicsStep = 1f / 60f;
    private const float CompressionSeconds = 0.1f;
    private const float PumpOpenSeconds = 0.06f;
    private const float CompressionSlideRadius = 2f;
    private const int MaximumCatchUpSteps = 4;
    private const float SceneMargin = 128f;
    private const float MaximumFlightSeconds = 4f;
    private const float SettlementBottomFraction = 0.85f;

    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<SoftFragmentRuntime> _fragments = [];
    private readonly List<SoftFragmentBody> _bodies = [];
    private readonly List<SoftRagdollLink> _joints = [];
    private readonly List<SoftBodyLaunchActuator> _launchActuators = [];
    private readonly Dictionary<int, int> _wobbleCrossingsByFragment = [];
    private readonly List<float> _jointBreakTimes = [];
    private readonly HashSet<SoftRagdollLink> _recordedBrokenJoints = [];
    private readonly BossSoftBodySolver _solver = new();
    private readonly SoftBodyResidualStatistics _residualStatistics = new();
    private BossVisualCapture? _capture;
    private NCombatRoom _room = null!;
    private Transform2D _bodyToPresentation;
    private Rect2 _bodyLocalBounds;
    private Rect2 _visibleSceneBounds;
    private Rect2 _sceneBounds;
    private Vector2 _burstOrigin;
    private float? _floorY;
    private float _elapsed;
    private float _burstElapsed;
    private float _physicsAccumulator;
    private float _droppedSimulationSeconds;
    private float _gravity = BossFountainLaunchProfile.Gravity;
    private float _centerSpeedLimit = BossSoftBodySolver.DefaultMaximumCenterSpeed;
    private ulong _seed;
    private ulong _motionSeed;
    private PresentationMode _mode;
    private bool _burstTriggered;
    private bool _burstReleased;
    private long _solverTicks;
    private long _longestSolverTicks;
    private long _renderTicks;
    private long _longestRenderTicks;
    private int _solverSteps;
    private int _renderFrames;
    private int _contactCount;
    private int _contactPointCount;
    private int _contactStartCount;
    private int _visibleBounceCount;
    private int _sweptContactCount;
    private int _leftWallContactCount;
    private int _rightWallContactCount;
    private int _wallBounceCount;
    private int _limitedContactCount;
    private int _limitedCenterSpeedCount;
    private int _brokenLinkCount;
    private int _spawnedFragmentCount;
    private int _spawnedJointCount;
    private int _spawnedConstraintCount;
    private float _minimumAreaRatio = 1f;
    private float _maximumStretch = 1f;
    private float _maximumResidual;
    private double _contactEnergyBefore;
    private double _contactEnergyAfter;
    private int _rollbackCount;
    private int _inversionCount;
    private int _safetyProjectionCount;
    private int _wobbleZeroCrossings;
    private float _maximumLaunchCenterSpeed;
    private float _maximumObservedCenterSpeed;
    private float _maximumPenetration;
    private int _maximumAdaptiveSubsteps;
    private int _removedBelowBottomCount;
    private int _settlementBottomBandCount;
    private int _settlementBelowBottomCount;
    private int _settlementDescendingCount;
    private bool _settlementRecorded;
    private float _maximumTetherAge;
    private float _firstFragmentContactSeconds = -1f;
    private int _upwardLaunchCount;
    private int _horizontalLaunchCount;
    private int _downwardLaunchCount;
    private long _captureSetupTicks;
    private long _captureReadyElapsedTicks;
    private long _captureMeasurementCpuTicks;
    private long _captureAtlasCpuTicks;
    private long _captureFragmentPreparationCpuTicks;
    private long _captureMaximumCpuFrameTicks;
    private long _spawnSetupTicks;
    private bool _completed;
    private float _fragmentRawWidthRatio = 1f;
    private float _fragmentRawHeightRatio = 1f;
    private float _fragmentCalibrationScale = 1f;
    private float _fragmentWidthRatio = 1f;
    private float _fragmentHeightRatio = 1f;
    private Vector2 _originalBossScreenSize;
    private Vector2 _semanticSourceScreenSize;
    private Vector2 _fragmentUnionScreenSize;
    private float _medianFragmentArea;
    private float _maximumFragmentArea;
    private float _launchSpeedP50;
    private float _launchSpeedP90;
    private float _launchHorizontalDrift;
    private bool _dispersionRecorded;
    private int _sectorCoverage035;
    private float _spatialDispersion035;
    private int _capturePixelWidth;
    private int _capturePixelHeight;
    private long _captureTextureBytes;
    private int _semanticPartCount;
    private int _mergedPartCount;
    private int _splitFragmentCount;
    private float _atlasDensity;
    private float _baselineSceneScale;
    private float _currentSceneScale;
    private string _monsterId = "unknown";

    public static IEnumerable<string> AssetPaths =>
        [BossCapturedFragmentRenderSurface.ShaderPath];

    internal Task Completion => _completion.Task;

    public override void _Ready() => SetProcess(false);

    public override void _Process(double delta)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(_room)
                || !_room.IsInsideTree()
                || !ReferenceEquals(NCombatRoom.Instance, _room))
            {
                CompleteAndFree();
                return;
            }

            if (BossBurstPresentationCoordinator.IsPresentationPaused(_room))
            {
                return;
            }

            float seconds = Math.Min((float)delta, 0.1f);
            if (seconds <= 0f)
            {
                return;
            }

            _elapsed += seconds;
            _physicsAccumulator += seconds;
            int catchUpSteps = 0;
            while (_physicsAccumulator >= PhysicsStep && catchUpSteps < MaximumCatchUpSteps)
            {
                StepPhysics(PhysicsStep);
                _physicsAccumulator -= PhysicsStep;
                catchUpSteps++;
            }

            if (catchUpSteps == MaximumCatchUpSteps && _physicsAccumulator >= PhysicsStep)
            {
                float retained = _physicsAccumulator % PhysicsStep;
                _droppedSimulationSeconds += _physicsAccumulator - retained;
                _physicsAccumulator = retained;
            }

            RemoveOffscreenFragments();
            ApplyRenderFrame();
            if (_elapsed >= MaximumFlightSeconds)
            {
                ClearFragments();
            }

            if (_fragments.Count == 0)
            {
                CompleteAndFree();
            }
        }
        catch (Exception exception)
        {
            Entry.Logger.Error(
                $"Boss soft-body presentation failed for {_monsterId}; ending the fragment effect: {exception}");
            CompleteAndFree();
        }
    }

    public override void _ExitTree()
    {
        _completed = true;
        SetProcess(false);
        ClearFragments();
        _capture?.Dispose();
        _capture = null;
        _completion.TrySetResult();
    }

    private void InitializeGeometry(
        Transform2D bodyToSceneContainer,
        Transform2D baselineSceneToGlobal)
    {
        Transform2D globalToPresentation = GlobalTransform.AffineInverse();
        _bodyToPresentation = globalToPresentation
            * baselineSceneToGlobal
            * bodyToSceneContainer;
        Transform2D vfxToGlobal = _room.CombatVfxContainer.GetGlobalTransform();
        Vector2 sceneSize = _room.CombatVfxContainer.Size;
        Vector2[] sceneCorners =
        [
            globalToPresentation * (vfxToGlobal * Vector2.Zero),
            globalToPresentation * (vfxToGlobal * new Vector2(sceneSize.X, 0f)),
            globalToPresentation * (vfxToGlobal * sceneSize),
            globalToPresentation * (vfxToGlobal * new Vector2(0f, sceneSize.Y))
        ];
        _visibleSceneBounds = BoundsOf(sceneCorners);
        _sceneBounds = _visibleSceneBounds.Grow(SceneMargin);
    }

    private void InitializeSoftBodies(
        BossFragmentPartition partition,
        IReadOnlyList<BossCapturedFragmentRenderSurface.PreparedResource> preparedFragments,
        int zIndex,
        PresentationMode mode,
        float architectFallDirection,
        Vector2? detachedExplosionCenter)
    {
        IReadOnlyList<BossCapturedFragmentDescriptor> descriptors = partition.Fragments;
        Texture2D texture = _capture?.Texture
            ?? throw new InvalidOperationException("The captured boss texture expired before fragment creation.");
        float[] massWeights = descriptors
            .Select(descriptor => Math.Max(0.0001f, descriptor.BodyAreaRatio))
            .ToArray();
        _motionSeed = BossDismembermentMath.ResolveMotionSeed(
            _seed,
            Time.GetTicksUsec(),
            GetInstanceId());
        var rng = new RandomNumberGenerator { Seed = _motionSeed };

        Vector2 detachedOrigin = detachedExplosionCenter.HasValue
            ? ToLocalPoint(detachedExplosionCenter.Value)
            : _burstOrigin;
        BossFragmentPoint bodyDomainRestCenter = ToBossFragmentPoint(
            _bodyToPresentation * _bodyLocalBounds.GetCenter());
        BossFragmentPoint detachedDomainRestCenter = ResolveDomainRestCenter(
            descriptors,
            massWeights,
            belongsToDetachedPart: true,
            bodyDomainRestCenter);
        for (int index = 0; index < descriptors.Count; index++)
        {
            BossCapturedFragmentDescriptor descriptor = descriptors[index];
            BossCapturedFragmentRenderSurface.PreparedResource prepared =
                preparedFragments[index];
            if (!ReferenceEquals(prepared.Descriptor, descriptor))
            {
                throw new InvalidOperationException(
                    $"prepared fragment {index} does not match the semantic partition");
            }
            Vector2 domainBurstOrigin = descriptor.Part.BelongsToDetachedPart
                ? detachedOrigin
                : _burstOrigin;
            BossFragmentPoint restCenter = ResolveDescriptorRestCenter(descriptor);
            BossFragmentPoint domainRestCenter = descriptor.Part.BelongsToDetachedPart
                ? detachedDomainRestCenter
                : bodyDomainRestCenter;
            BossFragmentPoint restOffset = new(
                restCenter.X - domainRestCenter.X,
                restCenter.Y - domainRestCenter.Y);
            BossFragmentPoint packedOrigin = BossBurstCompressionLayout.ResolvePackedOrigin(
                ToBossFragmentPoint(domainBurstOrigin),
                restOffset);
            Vector2 compressionOrigin = ToVector2(packedOrigin);
            float compressedArea = rng.RandfRange(0.45f, 0.6f);
            float compressedScale = MathF.Sqrt(compressedArea);
            float phase = rng.Randf() * MathF.Tau;
            float squashAmount = rng.RandfRange(0.18f, 0.26f);
            BossFragmentPoint compressedCenter = new(
                compressionOrigin.X + MathF.Cos(phase) * CompressionSlideRadius,
                compressionOrigin.Y + MathF.Sin(phase) * CompressionSlideRadius);
            if (!BossCapturedFragmentRenderSurface.TryInstantiate(
                    this,
                    prepared,
                    texture,
                    zIndex,
                    out BossCapturedFragmentRenderSurface? surface)
                || surface == null)
            {
                prepared.Dispose();
                continue;
            }

            SoftFragmentBody body = surface.Body;
            body.SetMaterial(mode == PresentationMode.ArchitectLead
                ? SoftBodyMaterialProfile.ArchitectLead
                : SoftBodyMaterialProfile.FountainJelly);
            body.ConfigureDeformation(
                _motionSeed ^ unchecked((ulong)(index + 1) * 0x9E3779B97F4A7C15UL));
            if (mode != PresentationMode.ArchitectLead)
            {
                body.PinCompressed(
                    compressedCenter,
                    compressedScale,
                    phase,
                    slideRadius: 0f,
                    squashAmount: squashAmount);
            }

            if (!surface.ApplyFrame())
            {
                surface.Dispose();
                continue;
            }

            surface.Anchor.Visible = true;

            var runtime = new SoftFragmentRuntime(
                surface,
                prepared.MappedArea,
                compressedScale,
                squashAmount,
                phase,
                rng.RandfRange(34f, 58f),
                compressionOrigin,
                restOffset);
            _fragments.Add(runtime);
            _bodies.Add(surface.Body);
            if (mode == PresentationMode.ArchitectLead)
            {
                float fallSign = Mathf.IsZeroApprox(architectFallDirection)
                    ? 1f
                    : Mathf.Sign(architectFallDirection);
                surface.Body.TargetLinearScale = 1f;
                // Keep the actual Voronoi outline during the fall; only the exaggerated
                // jelly margin is disabled until the burst.
                surface.Body.SetCollisionEnvelope(hullScale: 1f, marginScale: 0f);
                surface.Body.Release(
                    new BossFragmentPoint(
                        fallSign * rng.RandfRange(76f, 108f),
                        rng.RandfRange(-165f, -120f)),
                    Mathf.DegToRad(rng.RandfRange(-38f, 38f)));
            }
        }

        if (_fragments.Count < 2)
        {
            throw new InvalidOperationException("fewer than two captured soft fragments initialized successfully");
        }

        AssignFountainLaunches();

        if (mode == PresentationMode.ArchitectLead)
        {
            BuildRagdollLinks(_bodies.Count, canBreak: false);
        }
        else
        {
            ApplyFountainPlan();
        }

        RecordSpawnDiagnostics();
        CountLaunchLanes();
        _spawnedFragmentCount = _fragments.Count;
        _spawnedJointCount = _joints.Count;
        _spawnedConstraintCount = _bodies.Sum(body => body.ConstraintCount);
    }

    private void ValidateBaselineFragmentGeometry(
        BossFragmentPartition partition,
        Rect2 originalBossScreenBounds)
    {
        Transform2D globalToPresentation = GlobalTransform.AffineInverse();
        Rect2 fullBossBounds = BoundsOf(RectCorners(originalBossScreenBounds)
            .Select(point => globalToPresentation * point));
        var semanticSourceBounds = new Rect2(
            partition.SourceBounds.X,
            partition.SourceBounds.Y,
            partition.SourceBounds.Width,
            partition.SourceBounds.Height);
        Rect2 expected = BoundsOf(RectCorners(semanticSourceBounds)
            .Select(point => _bodyToPresentation * point));
        Rect2 actual = BoundsOf(partition.Fragments
            .SelectMany(fragment => fragment.Cell.Vertices)
            .Select(point => _bodyToPresentation * ToVector2(point)));
        if (!IsValidBounds(fullBossBounds)
            || !IsValidBounds(expected)
            || !IsValidBounds(actual))
        {
            throw new InvalidOperationException(
                "the battle-view fragment bounds are invalid");
        }

        _originalBossScreenSize = fullBossBounds.Size;
        _semanticSourceScreenSize = expected.Size;
        _fragmentUnionScreenSize = actual.Size;
        _fragmentRawWidthRatio = actual.Size.X / expected.Size.X;
        _fragmentRawHeightRatio = actual.Size.Y / expected.Size.Y;
        _fragmentWidthRatio = _fragmentRawWidthRatio;
        _fragmentHeightRatio = _fragmentRawHeightRatio;
        if (!BossDismembermentMath.TryResolveUniformBoundsCalibration(
                new BossFragmentRect(
                    expected.Position.X,
                    expected.Position.Y,
                    expected.Size.X,
                    expected.Size.Y),
                new BossFragmentRect(
                    actual.Position.X,
                    actual.Position.Y,
                    actual.Size.X,
                    actual.Size.Y),
                out BossFragmentBoundsCalibration calibration))
        {
            throw new InvalidOperationException(
                $"semantic fragments cannot be uniformly calibrated to their frozen visible source bounds "
                + $"({_fragmentRawWidthRatio:F3}x{_fragmentRawHeightRatio:F3})");
        }

        _fragmentCalibrationScale = calibration.UniformScale;
        if (!calibration.IsIdentity)
        {
            var correction = new Transform2D(
                new Vector2(calibration.UniformScale, 0f),
                new Vector2(0f, calibration.UniformScale),
                new Vector2(calibration.TranslationX, calibration.TranslationY));
            _bodyToPresentation = correction * _bodyToPresentation;
            actual = BoundsOf(partition.Fragments
                .SelectMany(fragment => fragment.Cell.Vertices)
                .Select(point => _bodyToPresentation * ToVector2(point)));
        }

        _fragmentUnionScreenSize = actual.Size;
        _fragmentWidthRatio = actual.Size.X / expected.Size.X;
        _fragmentHeightRatio = actual.Size.Y / expected.Size.Y;
        if (_fragmentWidthRatio is < 0.98f or > 1.02f
            || _fragmentHeightRatio is < 0.98f or > 1.02f)
        {
            throw new InvalidOperationException(
                $"uniformly calibrated semantic fragments still do not match their frozen visible source bounds "
                + $"({_fragmentWidthRatio:F3}x{_fragmentHeightRatio:F3})");
        }

        _floorY = RectCorners(_bodyLocalBounds)
            .Select(point => (_bodyToPresentation * point).Y)
            .Max();
    }

    private void AssignFountainLaunches()
    {
        IReadOnlyList<BossFountainLaunch> launches = BossFountainLaunchProfile.Create(
            _fragments.Select(fragment => fragment.Body.Mass).ToArray(),
            _motionSeed ^ 0x464F554E5441494EUL);
        if (launches.Count != _fragments.Count)
        {
            throw new InvalidOperationException(
                "the fountain launch plan does not match the initialized fragment count");
        }

        for (int index = 0; index < _fragments.Count; index++)
        {
            BossFountainLaunch launch = launches[index];
            SoftFragmentRuntime fragment = _fragments[index];
            fragment.LaunchVelocity = new Vector2(
                launch.Velocity.X,
                launch.Velocity.Y);
            fragment.LaunchAngularVelocityDegrees = launch.AngularVelocityDegrees;
            fragment.LaunchLane = launch.Lane;
        }
    }

    private void RecordSpawnDiagnostics()
    {
        float[] areas = _fragments
            .Select(fragment => fragment.MappedArea)
            .Order()
            .ToArray();
        _medianFragmentArea = Percentile(areas, 0.5f);
        _maximumFragmentArea = areas.Length == 0 ? 0f : areas[^1];

        float[] launchSpeeds = _fragments
            .Select(fragment => fragment.LaunchVelocity.Length())
            .Order()
            .ToArray();
        _launchSpeedP50 = Percentile(launchSpeeds, 0.5f);
        _launchSpeedP90 = Percentile(launchSpeeds, 0.9f);
        float totalMass = _fragments.Sum(fragment => fragment.Body.Mass);
        _launchHorizontalDrift = totalMass <= 0.001f
            ? 0f
            : _fragments.Sum(fragment =>
                fragment.LaunchVelocity.X * fragment.Body.Mass) / totalMass;
    }

    private void ApplyFountainPlan()
    {
        BossFountainLaunch[] launches = _fragments
            .Select(fragment => new BossFountainLaunch(
                new BossFragmentPoint(
                    fragment.LaunchVelocity.X,
                    fragment.LaunchVelocity.Y),
                fragment.LaunchAngularVelocityDegrees,
                fragment.LaunchLane))
            .ToArray();
        BossFountainLaunchPlan plan = BossFountainLaunchProfile.CreatePlan(launches);
        int count = Math.Min(_fragments.Count, plan.Launches.Count);
        for (int index = 0; index < count; index++)
        {
            BossFountainLaunch launch = plan.Launches[index];
            _fragments[index].LaunchVelocity = new Vector2(
                launch.Velocity.X,
                launch.Velocity.Y);
        }

        _gravity = plan.Gravity;
        _centerSpeedLimit = plan.MaximumCenterSpeed;
    }

    private void BuildRagdollLinks(int maximumClusterSize, bool canBreak)
    {
        IReadOnlyList<BossFragmentLink> links = BossDismembermentMath.BuildRagdollLinks(
            _bodies.Select(body => body.RestCenter).ToArray(),
            maximumClusterSize);
        for (int index = 0; index < links.Count; index++)
        {
            BossFragmentLink link = links[index];
            SoftFragmentBody first = _bodies[link.FirstIndex];
            SoftFragmentBody second = _bodies[link.SecondIndex];
            (int firstParticle, int secondParticle) = ResolveNearestParticlePair(first, second);
            BossFragmentPoint firstRest = first.GetRestParticlePosition(firstParticle);
            BossFragmentPoint secondRest = second.GetRestParticlePosition(secondParticle);
            float restDx = secondRest.X - firstRest.X;
            float restDy = secondRest.Y - firstRest.Y;
            float particleRestLength = MathF.Sqrt(restDx * restDx + restDy * restDy);
            var joint = new SoftRagdollLink(
                first,
                firstParticle,
                second,
                secondParticle,
                Math.Max(particleRestLength, 0.5f))
            {
                CanBreak = canBreak
            };
            _joints.Add(joint);
        }
    }

    private void CountLaunchLanes()
    {
        _upwardLaunchCount = _fragments.Count(
            fragment => fragment.LaunchLane == BossFountainLaunchLane.Upward);
        _horizontalLaunchCount = _fragments.Count(
            fragment => fragment.LaunchLane == BossFountainLaunchLane.Horizontal);
        _downwardLaunchCount = _fragments.Count(
            fragment => fragment.LaunchLane == BossFountainLaunchLane.Downward);
    }

    internal bool TriggerArchitectBurst()
    {
        if (_completed
            || _mode != PresentationMode.ArchitectLead
            || _burstTriggered
            || _fragments.Count < 2)
        {
            return false;
        }

        _burstTriggered = true;
        _burstReleased = false;
        _burstElapsed = 0f;
        _elapsed = 0f;
        _physicsAccumulator = 0f;
        _floorY = null;
        _removedBelowBottomCount = 0;
        _settlementBottomBandCount = 0;
        _settlementBelowBottomCount = 0;
        _settlementDescendingCount = 0;
        _settlementRecorded = false;
        BossFragmentPoint weightedCenter = default;
        float totalMass = 0f;
        for (int index = 0; index < _bodies.Count; index++)
        {
            SoftFragmentBody body = _bodies[index];
            weightedCenter = new BossFragmentPoint(
                weightedCenter.X + body.Center.X * body.Mass,
                weightedCenter.Y + body.Center.Y * body.Mass);
            totalMass += body.Mass;
        }

        float inverseMass = 1f / Math.Max(0.001f, totalMass);
        _burstOrigin = new Vector2(
            weightedCenter.X * inverseMass,
            weightedCenter.Y * inverseMass);
        for (int index = 0; index < _fragments.Count; index++)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            fragment.Body.SetMaterial(SoftBodyMaterialProfile.FountainJelly);
            fragment.Body.ConfigureDeformation(
                _motionSeed ^ unchecked((ulong)(index + 1) * 0x9E3779B97F4A7C15UL));
            fragment.CompressionOrigin = ToVector2(
                BossBurstCompressionLayout.ResolvePackedOrigin(
                    ToBossFragmentPoint(_burstOrigin),
                    fragment.RestCenterOffset));
            float phase = fragment.CompressionPhase;
            Vector2 compressionOrigin = fragment.CompressionOrigin;
            BossFragmentPoint compressedCenter = new(
                compressionOrigin.X + MathF.Cos(phase) * CompressionSlideRadius,
                compressionOrigin.Y + MathF.Sin(phase) * CompressionSlideRadius);
            fragment.Body.PinCompressed(
                compressedCenter,
                fragment.CompressedScale,
                phase,
                slideRadius: 0f,
                squashAmount: fragment.SquashAmount);
        }

        RecordJointDiagnostics();
        _joints.Clear();
        ApplyFountainPlan();

        // Upload the compressed pose immediately so the first visible burst frame
        // cannot expose the settled ragdoll positions.
        ApplyRenderFrame();

        return true;
    }

    internal void CancelPresentation() => CompleteAndFree();

    private void StepPhysics(float seconds)
    {
        if (!_burstTriggered)
        {
            SolveSoftBodies(seconds, _floorY);
            return;
        }

        if (!_burstReleased && _burstElapsed < CompressionSeconds)
        {
            float progress = Math.Clamp(_burstElapsed / CompressionSeconds, 0f, 1f);
            for (int index = 0; index < _fragments.Count; index++)
            {
                SoftFragmentRuntime fragment = _fragments[index];
                float phase = fragment.CompressionPhase + _burstElapsed * fragment.CompressionSpeed;
                float pulse = 0.92f + MathF.Sin(phase * 1.71f) * 0.08f;
                float scale = fragment.CompressedScale * pulse;
                Vector2 origin = fragment.CompressionOrigin;
                BossFragmentPoint center = new(
                    origin.X + MathF.Cos(phase) * CompressionSlideRadius * progress,
                    origin.Y + MathF.Sin(phase * 1.37f) * CompressionSlideRadius * progress * 0.7f);
                fragment.Body.PinCompressed(
                    center,
                    scale,
                    phase,
                    slideRadius: 0.8f,
                    squashAmount: fragment.SquashAmount);
            }

            _burstElapsed += seconds;
            return;
        }

        if (!_burstReleased)
        {
            _burstReleased = true;
            _launchActuators.Clear();
            for (int index = 0; index < _fragments.Count; index++)
            {
                SoftFragmentRuntime fragment = _fragments[index];
                var actuator = new SoftBodyLaunchActuator(
                    fragment.Body,
                    new BossFragmentPoint(fragment.LaunchVelocity.X, fragment.LaunchVelocity.Y),
                    Mathf.DegToRad(fragment.LaunchAngularVelocityDegrees));
                actuator.Begin();
                _launchActuators.Add(actuator);
            }
        }

        float flightSeconds = Math.Max(0f, _burstElapsed - CompressionSeconds);
        float pumpProgress = Math.Clamp(
            flightSeconds / PumpOpenSeconds,
            0f,
            1f);
        pumpProgress = pumpProgress * pumpProgress * (3f - 2f * pumpProgress);
        for (int index = 0; index < _fragments.Count; index++)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            fragment.Body.TargetLinearScale = Lerp(fragment.CompressedScale, 1f, pumpProgress);
            float hullScale = 1f;
            float marginScale = BossFountainLaunchProfile.ResolveCollisionMarginScale(
                flightSeconds,
                hullScale);
            fragment.Body.SetCollisionEnvelope(
                hullScale,
                marginScale);
        }

        SolveSoftBodies(seconds, floorY: null);
        RecordDispersionIfDue(flightSeconds + seconds);
        _burstElapsed += seconds;
        RecordSettlementIfDue(_burstElapsed);
    }

    private void SolveSoftBodies(float seconds, float? floorY)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool architectLead = _mode == PresentationMode.ArchitectLead && !_burstTriggered;
        float flightSeconds = Math.Max(0f, _burstElapsed - CompressionSeconds);
        SoftHorizontalBoundary? horizontalBoundary = !architectLead
            && _burstReleased
            && flightSeconds >= 0.1f
            ? new SoftHorizontalBoundary(
                _visibleSceneBounds.Position.X,
                _visibleSceneBounds.End.X)
            : null;
        SoftBodyStepMetrics metrics = _solver.Step(
            _bodies,
            _joints,
            seconds,
            architectLead ? 860f : _gravity,
            architectLead ? 0.08f : BossFountainLaunchProfile.LinearAirDrag,
            floorY,
            architectLead ? 0f : BossFountainLaunchProfile.QuadraticAirDrag,
            _burstReleased ? _launchActuators : null,
            _centerSpeedLimit,
            horizontalBoundary);
        long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        _solverTicks += elapsedTicks;
        _longestSolverTicks = Math.Max(_longestSolverTicks, elapsedTicks);
        _solverSteps++;
        _contactCount += metrics.Contacts;
        _contactPointCount += metrics.ContactPoints;
        _contactStartCount += metrics.ContactStarts;
        _visibleBounceCount += metrics.VisibleBounces;
        _sweptContactCount += metrics.SweptContacts;
        _leftWallContactCount += metrics.LeftWallContacts;
        _rightWallContactCount += metrics.RightWallContacts;
        _wallBounceCount += metrics.WallBounces;
        _contactEnergyBefore += metrics.ContactEnergyBefore;
        _contactEnergyAfter += metrics.ContactEnergyAfter;
        _limitedContactCount += metrics.LimitedContacts;
        _limitedCenterSpeedCount += metrics.LimitedCenterSpeeds;
        _maximumPenetration = Math.Max(_maximumPenetration, metrics.MaximumPenetration);
        _maximumAdaptiveSubsteps = Math.Max(_maximumAdaptiveSubsteps, metrics.Substeps);
        _brokenLinkCount += metrics.BrokenLinks;
        _rollbackCount += metrics.Rollbacks;
        _inversionCount += metrics.Inversions;
        _safetyProjectionCount += metrics.SafetyProjections;
        _wobbleZeroCrossings += metrics.WobbleZeroCrossings;
        if (_burstReleased
            && _firstFragmentContactSeconds < 0f
            && metrics.ContactStarts > 0)
        {
            _firstFragmentContactSeconds = flightSeconds;
        }
        if (_burstReleased && flightSeconds <= 0.7f)
        {
            _maximumLaunchCenterSpeed = Math.Max(
                _maximumLaunchCenterSpeed,
                metrics.MaximumCenterSpeed);
            _maximumObservedCenterSpeed = Math.Max(
                _maximumObservedCenterSpeed,
                metrics.MaximumObservedCenterSpeed);
        }
        RecordJointDiagnostics();
        for (int index = 0; index < _bodies.Count; index++)
        {
            SoftFragmentBody body = _bodies[index];
            if (!body.HasFiniteState)
            {
                continue;
            }

            _minimumAreaRatio = Math.Min(_minimumAreaRatio, body.ResolveMinimumCellAreaRatio());
            _maximumStretch = Math.Max(_maximumStretch, body.ResolveMaximumStretch());
            RecordBodyDiagnostics(body);
        }

    }

    private void ApplyRenderFrame()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int index = _fragments.Count - 1; index >= 0; index--)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            if (!fragment.Surface.ApplyFrame())
            {
                RemoveFragmentAt(index);
                continue;
            }

            _maximumResidual = Math.Max(_maximumResidual, fragment.Surface.MaximumResidual);
            float residualRatio = fragment.Surface.RmsResidualRatio;
            float flightSeconds = Math.Max(0f, _burstElapsed - CompressionSeconds);
            if (_burstReleased && flightSeconds is >= 0.2f and <= 1.8f)
            {
                _residualStatistics.Add(residualRatio);
            }
        }

        long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        _renderTicks += elapsedTicks;
        _longestRenderTicks = Math.Max(_longestRenderTicks, elapsedTicks);
        _renderFrames++;
    }

    private void RecordSettlementIfDue(float burstTimelineSeconds, bool force = false)
    {
        if (_settlementRecorded
            || !force && burstTimelineSeconds < BossBurstTimeline.VideoSeconds
            || _visibleSceneBounds.Size.Y <= 1f)
        {
            return;
        }

        _settlementRecorded = true;
        float bottomBand = _visibleSceneBounds.Position.Y
            + _visibleSceneBounds.Size.Y * SettlementBottomFraction;
        float visibleBottom = _visibleSceneBounds.End.Y;
        int bottomBandCount = 0;
        int belowBottomCount = _removedBelowBottomCount;
        int descendingCount = 0;
        for (int index = 0; index < _fragments.Count; index++)
        {
            SoftFragmentBody body = _fragments[index].Body;
            BossFragmentPoint center = body.Center;
            if (center.Y >= visibleBottom)
            {
                belowBottomCount++;
            }
            else if (center.Y >= bottomBand)
            {
                bottomBandCount++;
            }
            else if (body.CenterVelocity.Y > 0f)
            {
                descendingCount++;
            }
        }

        _settlementBottomBandCount = bottomBandCount;
        _settlementBelowBottomCount = belowBottomCount;
        _settlementDescendingCount = descendingCount;
    }

    private void RecordDispersionIfDue(float flightSeconds)
    {
        if (_dispersionRecorded || flightSeconds < 0.35f || _bodies.Count == 0)
        {
            return;
        }

        _dispersionRecorded = true;
        var sectors = new HashSet<int>();
        double squaredDistance = 0d;
        for (int index = 0; index < _bodies.Count; index++)
        {
            BossFragmentPoint center = _bodies[index].Center;
            float x = center.X - _burstOrigin.X;
            float y = center.Y - _burstOrigin.Y;
            float angle = MathF.Atan2(y, x) + MathF.PI;
            int sector = Math.Clamp(
                (int)MathF.Floor(angle / MathF.Tau * 12f),
                0,
                11);
            sectors.Add(sector);
            squaredDistance += x * x + y * y;
        }

        _sectorCoverage035 = sectors.Count;
        _spatialDispersion035 = (float)Math.Sqrt(squaredDistance / _bodies.Count);
    }

    private void RemoveOffscreenFragments()
    {
        for (int index = _fragments.Count - 1; index >= 0; index--)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            (BossFragmentPoint minimum, BossFragmentPoint maximum) =
                fragment.Body.ResolveDeformedBounds();
            if (!IsFullyOutsideScene(minimum, maximum)
                || HasLiveJoint(fragment.Body))
            {
                continue;
            }

            bool exitedBelow = fragment.Body.Center.Y >= _visibleSceneBounds.End.Y;
            RemoveFragmentAt(index, exitedBelow);
        }
    }

    private bool HasLiveJoint(SoftFragmentBody body)
    {
        for (int index = 0; index < _joints.Count; index++)
        {
            SoftRagdollLink joint = _joints[index];
            if (!joint.Broken
                && (ReferenceEquals(joint.First, body)
                    || ReferenceEquals(joint.Second, body)))
            {
                return true;
            }
        }

        return false;
    }

    private void RemoveFragmentAt(int index, bool exitedBelow = false)
    {
        SoftFragmentRuntime fragment = _fragments[index];
        if (exitedBelow)
        {
            _removedBelowBottomCount++;
        }
        RecordBodyDiagnostics(fragment.Body);
        RecordJointDiagnostics();
        _joints.RemoveAll(joint =>
            ReferenceEquals(joint.First, fragment.Body)
            || ReferenceEquals(joint.Second, fragment.Body));
        _launchActuators.RemoveAll(actuator => ReferenceEquals(actuator.Body, fragment.Body));
        _bodies.Remove(fragment.Body);
        _fragments.RemoveAt(index);
        fragment.Surface.Dispose();
    }

    private bool IsFullyOutsideScene(
        BossFragmentPoint minimum,
        BossFragmentPoint maximum)
    {
        Rect2 bounds = new(
            minimum.X,
            minimum.Y,
            maximum.X - minimum.X,
            maximum.Y - minimum.Y);
        return !_sceneBounds.Intersects(bounds, includeBorders: true);
    }

    private static (int First, int Second) ResolveNearestParticlePair(
        SoftFragmentBody first,
        SoftFragmentBody second)
    {
        int[] perimeter = [0, 1, 2, 3, 4, 7, 8, 11, 12, 13, 14, 15];
        (int First, int Second) result = default;
        float bestDistance = float.PositiveInfinity;
        foreach (int firstIndex in perimeter)
        {
            foreach (int secondIndex in perimeter)
            {
                BossFragmentPoint firstPoint = first.GetRestParticlePosition(firstIndex);
                BossFragmentPoint secondPoint = second.GetRestParticlePosition(secondIndex);
                float dx = firstPoint.X - secondPoint.X;
                float dy = firstPoint.Y - secondPoint.Y;
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    result = (firstIndex, secondIndex);
                }
            }
        }

        return result;
    }

    private float ResolveMappedArea(BossFragmentCell cell)
    {
        Vector2[] mapped = cell.Vertices
            .Select(point => _bodyToPresentation * ToVector2(point))
            .ToArray();
        return Math.Max(1f, PolygonArea(mapped));
    }

    private Vector2 ToLocalPoint(Vector2 globalPoint) => GlobalTransform.AffineInverse() * globalPoint;

    private void ClearFragments()
    {
        for (int index = 0; index < _bodies.Count; index++)
        {
            RecordBodyDiagnostics(_bodies[index]);
        }

        RecordJointDiagnostics();
        for (int index = _fragments.Count - 1; index >= 0; index--)
        {
            _fragments[index].Surface.Dispose();
        }

        _fragments.Clear();
        _bodies.Clear();
        _joints.Clear();
        _launchActuators.Clear();
    }
}
