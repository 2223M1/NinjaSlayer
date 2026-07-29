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

    private const string FragmentShaderPath =
        "res://NinjaSlayer/shaders/vfx/boss_dismemberment_clip.gdshader";
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
    private Shader _fragmentShader = null!;
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
    private bool _completed;
    private float _fragmentRawWidthRatio = 1f;
    private float _fragmentRawHeightRatio = 1f;
    private float _fragmentCalibrationScale = 1f;
    private float _fragmentWidthRatio = 1f;
    private float _fragmentHeightRatio = 1f;
    private Vector2 _originalBossScreenSize;
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

    public static IEnumerable<string> AssetPaths => [FragmentShaderPath];

    internal Task Completion => _completion.Task;

    internal static BossDismembermentSnapshot? TryCapture(
        NCombatRoom room,
        NCreature creature,
        string? detachedBoneName = null)
    {
        if (!GodotObject.IsInstanceValid(room)
            || !room.IsInsideTree()
            || !GodotObject.IsInstanceValid(creature))
        {
            return null;
        }

        try
        {
            Node2D sourceBody = creature.Body;
            if (!GodotObject.IsInstanceValid(sourceBody))
            {
                return null;
            }

            Node presentationParent = room.CombatVfxContainer;
            if (!GodotObject.IsInstanceValid(presentationParent)
                || !presentationParent.IsInsideTree())
            {
                throw new InvalidOperationException(
                    "The combat VFX container is unavailable for dismemberment.");
            }

            if (!CombatCinematicCameraLease.TryResolveBaseline(
                    room,
                    out CombatSceneBaseline baseline))
            {
                throw new InvalidOperationException(
                    "The complete-battle camera baseline is unavailable.");
            }

            Transform2D bodyToSceneContainer = room.SceneContainer
                .GetGlobalTransform()
                .AffineInverse()
                * sourceBody.GlobalTransform;
            Rect2 fallbackBounds = ResolveFallbackCaptureBounds(creature, sourceBody);
            ulong seed = CreateSeed(creature);
            bool canSplitSpine = creature.HasSpineAnimation
                && !creature.Visuals.IsUsingPhobiaModeBody;
            BossVisualCapture? capture = BossVisualCapture.TryCreate(
                presentationParent,
                sourceBody,
                fallbackBounds,
                bodyToSceneContainer,
                baseline,
                canSplitSpine,
                seed,
                detachedBoneName);
            if (capture == null)
            {
                return null;
            }

            return new BossDismembermentSnapshot(
                capture,
                capture.BodyLocalBounds,
                seed,
                creature.Entity.Monster?.Id.Entry ?? creature.Name.ToString());
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Boss dismemberment snapshot capture failed for "
                + $"{creature.Entity.Monster?.Id.Entry}: {exception}");
            return null;
        }
    }

    internal static BossDismembermentSpawn TrySpawn(
        NCombatRoom room,
        NCreature creature,
        BossDismembermentSnapshot? snapshot,
        Vector2 bodyExplosionCenter,
        string? detachedBoneName = null,
        Vector2? detachedExplosionCenter = null,
        int zIndex = BossBurstPresentationCoordinator.FragmentZIndex)
    {
        BossDismembermentPresentation? presentation = TryCreatePresentation(
            room,
            creature,
            snapshot,
            bodyExplosionCenter,
            detachedBoneName,
            detachedExplosionCenter,
            zIndex,
            PresentationMode.CompressedBurst,
            architectFallDirection: 0f,
            out string failureReason);
        return presentation == null
            ? CompleteWithoutFragments(creature, failureReason)
            : new BossDismembermentSpawn(true, presentation.Completion);
    }

    internal static ArchitectBossSoftBodyLead? TrySpawnArchitectLead(
        NCombatRoom room,
        NCreature creature,
        BossDismembermentSnapshot? snapshot,
        float fallDirection,
        int zIndex = BossBurstPresentationCoordinator.FragmentZIndex)
    {
        Vector2 burstOrigin = snapshot?.BodyGlobalCenter ?? Vector2.Zero;
        BossDismembermentPresentation? presentation = TryCreatePresentation(
            room,
            creature,
            snapshot,
            burstOrigin,
            detachedBoneName: null,
            detachedExplosionCenter: null,
            zIndex,
            PresentationMode.ArchitectLead,
            fallDirection,
            out string failureReason);
        if (presentation != null)
        {
            return new ArchitectBossSoftBodyLead(presentation);
        }

        CompleteWithoutFragments(creature, failureReason);
        return null;
    }

    private static BossDismembermentPresentation? TryCreatePresentation(
        NCombatRoom room,
        NCreature creature,
        BossDismembermentSnapshot? snapshot,
        Vector2 bodyExplosionCenter,
        string? detachedBoneName,
        Vector2? detachedExplosionCenter,
        int zIndex,
        PresentationMode mode,
        float architectFallDirection,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!GodotObject.IsInstanceValid(room)
            || !GodotObject.IsInstanceValid(creature)
            || !room.IsInsideTree())
        {
            failureReason = "the combat room or creature is no longer available";
            return null;
        }

        if (snapshot == null)
        {
            failureReason = "the pre-death capture is unavailable";
            return null;
        }

        BossVisualCapture? capture = snapshot.TakeCapture();
        if (capture == null)
        {
            failureReason = "the pre-death capture was already consumed";
            return null;
        }

        if (!capture.IsReady
            || capture.Texture == null
            || capture.Partition == null)
        {
            string reason = string.IsNullOrWhiteSpace(capture.FailureReason)
                ? "the GPU capture did not finish before the presentation began"
                : capture.FailureReason;
            capture.Dispose();
            failureReason = reason;
            return null;
        }

        Node presentationParent = room.CombatVfxContainer;
        if (!GodotObject.IsInstanceValid(presentationParent)
            || !presentationParent.IsInsideTree())
        {
            capture.Dispose();
            failureReason = "the capture parent is unavailable";
            return null;
        }

        var presentation = new BossDismembermentPresentation
        {
            Name = "NinjaSlayerBossDismemberment",
            ProcessMode = ProcessModeEnum.Always,
            ZAsRelative = false,
            ZIndex = zIndex,
            _capture = capture,
            _room = room,
            _bodyLocalBounds = snapshot.BodyLocalBounds,
            _seed = snapshot.Seed,
            _monsterId = snapshot.MonsterId,
            _mode = mode,
            _burstTriggered = mode == PresentationMode.CompressedBurst,
            _captureSetupTicks = capture.SetupElapsedTicks,
            _captureReadyElapsedTicks = capture.ReadyElapsedTicks
        };
        try
        {
            presentationParent.AddChildSafely(presentation);
            if (!GodotObject.IsInstanceValid(presentation) || !presentation.IsInsideTree())
            {
                throw new InvalidOperationException(
                    "the fragment presentation could not enter the scene tree");
            }

            presentation._fragmentShader = ResourceLoader.Load<Shader>(FragmentShaderPath)
                ?? throw new InvalidOperationException("The captured fragment shader is unavailable.");
            presentation.InitializeGeometry(
                snapshot.BodyToSceneContainer,
                snapshot.BaselineSceneToGlobal);
            BossFragmentPartition partition = capture.Partition;
            if (partition.Fragments.Count < 2)
            {
                throw new InvalidOperationException(
                    "the captured body produced fewer than two semantic fragments");
            }

            Vector2I captureSize = capture.PixelSize;
            presentation._capturePixelWidth = captureSize.X;
            presentation._capturePixelHeight = captureSize.Y;
            presentation._captureTextureBytes = capture.EstimatedTextureBytes;
            presentation._semanticPartCount = partition.SemanticPartCount;
            presentation._mergedPartCount = partition.MergedPartCount;
            presentation._splitFragmentCount = partition.SplitFragmentCount;
            presentation._atlasDensity = capture.AtlasDensity;
            presentation._baselineSceneScale = snapshot.BaselineSceneScale.X;
            presentation._currentSceneScale = room.SceneContainer.Scale.X;
            presentation.ValidateBaselineFragmentGeometry(
                partition,
                snapshot.BodyBaselineScreenBounds);
            presentation._burstOrigin = presentation.ToLocalPoint(bodyExplosionCenter);
            presentation.InitializeSoftBodies(
                partition,
                zIndex,
                mode,
                architectFallDirection,
                detachedExplosionCenter);
            if (GodotObject.IsInstanceValid(creature.Body))
            {
                creature.Body.Visible = false;
            }
            presentation.SetProcess(true);
            return presentation;
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Boss dismemberment fragment generation failed for "
                + $"{creature.Entity.Monster?.Id.Entry}: {exception}");
            failureReason = exception.Message;
            if (GodotObject.IsInstanceValid(presentation))
            {
                presentation.CompleteAndFree();
            }

            capture.Dispose();
            return null;
        }
    }

    internal static BossDismembermentSpawn CompleteWithoutFragments(
        NCreature creature,
        string reason)
    {
        Entry.Logger.Warn(
            $"Boss dismemberment completed without fragments for "
            + $"{creature.Entity.Monster?.Id.Entry}: {reason}; "
            + "keeping the original death pose visible.");
        return new BossDismembermentSpawn(false, Task.CompletedTask);
    }

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

            ApplyRenderFrame();
            RemoveOffscreenFragments();
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
        int zIndex,
        PresentationMode mode,
        float architectFallDirection,
        Vector2? detachedExplosionCenter)
    {
        IReadOnlyList<BossCapturedFragmentDescriptor> descriptors = partition.Fragments;
        Texture2D texture = _capture?.Texture
            ?? throw new InvalidOperationException("The captured boss texture expired before fragment creation.");
        float[] mappedAreas = descriptors
            .Select(descriptor => ResolveMappedArea(descriptor.Cell))
            .ToArray();
        float[] massWeights = descriptors
            .Select(descriptor => Math.Max(0.0001f, descriptor.BodyAreaRatio))
            .ToArray();
        float averageMassWeight = Math.Max(0.0001f, massWeights.Average());
        _motionSeed = BossDismembermentMath.ResolveMotionSeed(
            _seed,
            Time.GetTicksUsec(),
            GetInstanceId());
        var rng = new RandomNumberGenerator { Seed = _motionSeed };
        float[] massRatios = massWeights
            .Select(weight => Math.Clamp(weight / averageMassWeight, 0.25f, 3f))
            .ToArray();

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
            BossFragmentPoint? initialCenter = mode == PresentationMode.ArchitectLead
                ? null
                : compressedCenter;
            float initialScale = mode == PresentationMode.ArchitectLead ? 1f : compressedScale;
            if (!BossCapturedFragmentRenderSurface.TryCreate(
                    this,
                    descriptor,
                    _bodyToPresentation,
                    texture,
                    _fragmentShader,
                    initialCenter,
                    initialScale,
                    massRatios[index],
                    BossDismembermentMath.ResolveCollisionPadding(mappedAreas[index]),
                    zIndex,
                    out BossCapturedFragmentRenderSurface? surface)
                || surface == null)
            {
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
                surface.ApplyFrame();
            }

            var runtime = new SoftFragmentRuntime(
                surface,
                mappedAreas[index],
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
        Rect2 expected = BoundsOf(RectCorners(originalBossScreenBounds)
            .Select(point => globalToPresentation * point));
        Rect2 actual = BoundsOf(partition.Fragments
            .SelectMany(fragment => fragment.Cell.Vertices)
            .Select(point => _bodyToPresentation * ToVector2(point)));
        if (!IsValidBounds(expected) || !IsValidBounds(actual))
        {
            throw new InvalidOperationException(
                "the battle-view fragment bounds are invalid");
        }

        _originalBossScreenSize = expected.Size;
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
                $"semantic fragments cannot be uniformly calibrated to the frozen battle-view boss size "
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
                $"uniformly calibrated semantic fragments still do not match the frozen battle-view boss size "
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
            if (!IsFullyOutsideScene(fragment.Body)
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

    private bool IsFullyOutsideScene(SoftFragmentBody body)
    {
        (BossFragmentPoint minimum, BossFragmentPoint maximum) = body.ResolveDeformedBounds();
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

    private static float Percentile(IReadOnlyList<float> sortedValues, float percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0f;
        }

        int index = Math.Clamp(
            (int)MathF.Round((sortedValues.Count - 1) * Math.Clamp(percentile, 0f, 1f)),
            0,
            sortedValues.Count - 1);
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

    private static float PolygonArea(IReadOnlyList<Vector2> polygon)
    {
        double twiceArea = 0d;
        for (int index = 0; index < polygon.Count; index++)
        {
            Vector2 current = polygon[index];
            Vector2 next = polygon[(index + 1) % polygon.Count];
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
        IReadOnlyList<float> massWeights,
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
