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
        BodyBaselineScreenBounds = capture.BodyBaselineScreenBounds;
        Seed = seed;
        MonsterId = monsterId;
    }

    public Rect2 BodyLocalBounds { get; }
    public Transform2D BodyToSceneContainer { get; }
    public Transform2D BaselineSceneToGlobal { get; }
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

    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<SoftFragmentRuntime> _fragments = [];
    private readonly List<SoftFragmentBody> _bodies = [];
    private readonly List<SoftBodyLaunchActuator> _launchActuators = [];
    private readonly BossSoftBodySolver _solver = new();
    private BossVisualCapture? _capture;
    private ArchitectSpineRagdoll? _architectRagdoll;
    private Node2D? _architectBody;
    private NCombatRoom _room = null!;
    private Transform2D _bodyToPresentation;
    private Transform2D _visualBodyToPresentation;
    private Transform2D _baselineSceneToGlobalInverse;
    private Transform2D _baselinePresentationGlobal;
    private Rect2 _bodyLocalBounds;
    private Rect2 _visibleSceneBounds;
    private Rect2 _sceneBounds;
    private Vector2 _burstOrigin;
    private float? _floorY;
    private float _elapsed;
    private float _burstElapsed;
    private float _physicsAccumulator;
    private float _gravity = BossFountainLaunchProfile.Gravity;
    private float _centerSpeedLimit = BossSoftBodySolver.DefaultMaximumCenterSpeed;
    private ulong _seed;
    private ulong _motionSeed;
    private PresentationMode _mode;
    private bool _burstTriggered;
    private bool _burstReleased;
    private bool _completed;
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

            FollowArchitectCamera();

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
                _physicsAccumulator = retained;
            }

            if (_burstReleased)
            {
                RemoveOffscreenFragments();
            }
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
        _baselinePresentationGlobal = GlobalTransform;
        _baselineSceneToGlobalInverse = baselineSceneToGlobal.AffineInverse();
        Transform2D globalToPresentation = _baselinePresentationGlobal.AffineInverse();
        _bodyToPresentation = globalToPresentation
            * baselineSceneToGlobal
            * bodyToSceneContainer;
        _visualBodyToPresentation = _bodyToPresentation;
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

    private void FollowArchitectCamera()
    {
        if (_mode != PresentationMode.ArchitectLead)
        {
            return;
        }

        GlobalTransform = _room.SceneContainer.GetGlobalTransform()
            * _baselineSceneToGlobalInverse
            * _baselinePresentationGlobal;
    }

    private void InitializeSoftBodies(
        NCreature creature,
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
                    out BossCapturedFragmentRenderSurface? surface))
            {
                prepared.Dispose();
                continue;
            }

            SoftFragmentBody body = surface.FragmentBody;
            body.SetMaterial(SoftBodyMaterialProfile.FountainJelly);
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

            surface.Anchor.Visible = mode != PresentationMode.ArchitectLead;

            var runtime = new SoftFragmentRuntime(
                surface,
                compressedScale,
                squashAmount,
                phase,
                rng.RandfRange(34f, 58f),
                compressionOrigin,
                restOffset);
            _fragments.Add(runtime);
            if (mode != PresentationMode.ArchitectLead)
            {
                _bodies.Add(body);
            }
        }

        if (_fragments.Count < 2)
        {
            throw new InvalidOperationException("fewer than two captured soft fragments initialized successfully");
        }

        AssignFountainLaunches();

        if (mode == PresentationMode.ArchitectLead)
        {
            InitializeArchitectLead(
                creature,
                partition,
                rng,
                architectFallDirection);
        }
        else
        {
            ApplyFountainPlan();
        }

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

        float rawWidthRatio = actual.Size.X / expected.Size.X;
        float rawHeightRatio = actual.Size.Y / expected.Size.Y;
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
                + $"({rawWidthRatio:F3}x{rawHeightRatio:F3})");
        }

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

        float widthRatio = actual.Size.X / expected.Size.X;
        float heightRatio = actual.Size.Y / expected.Size.Y;
        if (widthRatio is < 0.98f or > 1.02f
            || heightRatio is < 0.98f or > 1.02f)
        {
            throw new InvalidOperationException(
                $"uniformly calibrated semantic fragments still do not match their frozen visible source bounds "
                + $"({widthRatio:F3}x{heightRatio:F3})");
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

    private void InitializeArchitectLead(
        NCreature creature,
        BossFragmentPartition partition,
        RandomNumberGenerator rng,
        float fallDirection)
    {
        _architectBody = creature.Body;
        BossFragmentPoint velocity = BossDismembermentMath.ResolveArchitectLeadVelocity(
            fallDirection,
            rng.Randf());
        _architectRagdoll = ArchitectSpineRagdoll.TryCreate(
            this,
            creature,
            partition,
            _bodyToPresentation,
            _visualBodyToPresentation,
            velocity,
            out string failureReason);
        if (_architectRagdoll == null)
        {
            Entry.Logger.Warn(
                $"Architect Spine ragdoll unavailable for {_monsterId}: {failureReason}; "
                + "keeping the frozen death pose until Body Burst.");
            return;
        }

        _bodies.AddRange(_architectRagdoll.Bodies);
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
        if (_architectRagdoll != null)
        {
            BossFragmentPoint ragdollCenter = _architectRagdoll.ResolveBurstOrigin();
            _burstOrigin = new Vector2(ragdollCenter.X, ragdollCenter.Y);
        }

        _bodies.Clear();
        _launchActuators.Clear();
        for (int index = 0; index < _fragments.Count; index++)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            _bodies.Add(fragment.Body);
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

        ApplyFountainPlan();

        // Upload the compressed pose immediately so the first visible burst frame
        // cannot expose the lead pose.
        ApplyFragmentFrames();
        _architectRagdoll?.Dispose();
        _architectRagdoll = null;
        if (_architectBody != null && GodotObject.IsInstanceValid(_architectBody))
        {
            _architectBody.Visible = false;
        }

        foreach (SoftFragmentRuntime fragment in _fragments)
        {
            fragment.Surface.Anchor.Visible = true;
        }

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
        _burstElapsed += seconds;
    }

    private void SolveSoftBodies(float seconds, float? floorY)
    {
        bool architectLead = _mode == PresentationMode.ArchitectLead && !_burstTriggered;
        float flightSeconds = Math.Max(0f, _burstElapsed - CompressionSeconds);
        SoftHorizontalBoundary? horizontalBoundary = !architectLead
            && _burstReleased
            && flightSeconds >= 0.1f
            ? new SoftHorizontalBoundary(
                _visibleSceneBounds.Position.X,
                _visibleSceneBounds.End.X)
            : null;
        _solver.Step(
            _bodies,
            architectLead && _architectRagdoll != null
                ? _architectRagdoll.Links
                : [],
            seconds,
            architectLead ? 860f : _gravity,
            architectLead ? 0.08f : BossFountainLaunchProfile.LinearAirDrag,
            floorY,
            architectLead ? 0f : BossFountainLaunchProfile.QuadraticAirDrag,
            _burstReleased ? _launchActuators : null,
            _centerSpeedLimit,
            horizontalBoundary);
    }

    private void ApplyRenderFrame()
    {
        if (_mode == PresentationMode.ArchitectLead && !_burstTriggered)
        {
            _architectRagdoll?.ApplyVisualPose();
            return;
        }

        ApplyFragmentFrames();
    }

    private void ApplyFragmentFrames()
    {
        for (int index = _fragments.Count - 1; index >= 0; index--)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            if (!fragment.Surface.ApplyFrame())
            {
                RemoveFragmentAt(index);
            }
        }
    }

    private void RemoveOffscreenFragments()
    {
        for (int index = _fragments.Count - 1; index >= 0; index--)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            (BossFragmentPoint minimum, BossFragmentPoint maximum) =
                fragment.Body.ResolveDeformedBounds();
            if (!IsFullyOutsideScene(minimum, maximum))
            {
                continue;
            }

            RemoveFragmentAt(index);
        }
    }

    private void RemoveFragmentAt(int index)
    {
        SoftFragmentRuntime fragment = _fragments[index];
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

    private Vector2 ToLocalPoint(Vector2 globalPoint) => GlobalTransform.AffineInverse() * globalPoint;

    private void ClearFragments()
    {
        for (int index = _fragments.Count - 1; index >= 0; index--)
        {
            _fragments[index].Surface.Dispose();
        }

        _fragments.Clear();
        _bodies.Clear();
        _launchActuators.Clear();
        _architectRagdoll?.Dispose();
        _architectRagdoll = null;
        _architectBody = null;
    }

    private void CompleteAndFree()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        SetProcess(false);
        ClearFragments();
        _capture?.Dispose();
        _capture = null;
        _completion.TrySetResult();
        this.QueueFreeSafely();
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

    private static Rect2 ResolveBodyLocalBounds(
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

    private static Vector2[] RectCorners(Rect2 rect) =>
    [
        rect.Position,
        rect.Position + new Vector2(rect.Size.X, 0f),
        rect.End,
        rect.Position + new Vector2(0f, rect.Size.Y)
    ];

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
        float compressedScale,
        float squashAmount,
        float compressionPhase,
        float compressionSpeed,
        Vector2 compressionOrigin,
        BossFragmentPoint restCenterOffset)
    {
        public BossCapturedFragmentRenderSurface Surface { get; } = surface;
        public SoftFragmentBody Body => Surface.FragmentBody;
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
