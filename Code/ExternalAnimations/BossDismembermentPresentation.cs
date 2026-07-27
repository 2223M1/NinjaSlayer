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
        bool canSplitSpine,
        ulong seed,
        string monsterId)
    {
        _capture = capture;
        BodyLocalBounds = bodyLocalBounds;
        BodyGlobalTransform = capture.BodyGlobalTransform;
        CanSplitSpine = canSplitSpine;
        Seed = seed;
        MonsterId = monsterId;
    }

    public Rect2 BodyLocalBounds { get; }
    public Transform2D BodyGlobalTransform { get; }
    public Vector2 BodyGlobalCenter => BodyGlobalTransform * BodyLocalBounds.GetCenter();
    public bool CanSplitSpine { get; }
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
    private const float Gravity = 860f;
    private const float CompressionSeconds = 0.1f;
    private const float PumpOpenSeconds = 0.06f;
    private const float CompressionSlideRadius = 2f;
    private const float AirDrag = 0.08f;
    private const int MaximumCatchUpSteps = 4;
    private const float SceneMargin = 128f;
    private const float MaximumFlightSeconds = 4f;

    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<SoftFragmentRuntime> _fragments = [];
    private readonly List<SoftFragmentBody> _bodies = [];
    private readonly List<SoftRagdollLink> _joints = [];
    private readonly BossSoftBodySolver _solver = new();
    private BossVisualCapture? _capture;
    private NCombatRoom _room = null!;
    private Transform2D _bodyToPresentation;
    private Rect2 _bodyLocalBounds;
    private Rect2 _sceneBounds;
    private Vector2 _burstOrigin;
    private float? _floorY;
    private float _elapsed;
    private float _burstElapsed;
    private float _physicsAccumulator;
    private float _droppedSimulationSeconds;
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
    private int _brokenLinkCount;
    private int _spawnedFragmentCount;
    private int _spawnedJointCount;
    private float _minimumAreaRatio = 1f;
    private float _maximumStretch = 1f;
    private float _maximumResidual;
    private long _captureSetupTicks;
    private long _captureReadyAgeTicks;
    private bool _completed;
    private bool _canSplitSpine;
    private string _monsterId = "unknown";

    public static IEnumerable<string> AssetPaths => [FragmentShaderPath];

    internal Task Completion => _completion.Task;

    internal static BossDismembermentSnapshot? TryCapture(
        NCombatRoom room,
        NCreature creature)
    {
        if (!GodotObject.IsInstanceValid(room)
            || !room.IsInsideTree()
            || !GodotObject.IsInstanceValid(creature)
            || !GodotObject.IsInstanceValid(creature.Body))
        {
            return null;
        }

        try
        {
            Node? presentationParent = creature.GetParent();
            if (presentationParent == null || !GodotObject.IsInstanceValid(presentationParent))
            {
                throw new InvalidOperationException(
                    "The creature has no persistent visual parent for dismemberment.");
            }

            Transform2D bodyToGlobal = creature.Body.GlobalTransform;
            Transform2D boundsToGlobal = creature.Visuals.Bounds.GetGlobalTransform();
            Transform2D globalToBody = bodyToGlobal.AffineInverse();
            Rect2 localBounds = BoundsOf(
                RectCorners(new Rect2(Vector2.Zero, creature.Visuals.Bounds.Size))
                    .Select(point => globalToBody * (boundsToGlobal * point)));
            BossVisualCapture? capture = BossVisualCapture.TryCreate(
                presentationParent,
                creature.Body,
                localBounds);
            if (capture == null)
            {
                return null;
            }

            return new BossDismembermentSnapshot(
                capture,
                localBounds,
                creature.HasSpineAnimation && !creature.Visuals.IsUsingPhobiaModeBody,
                CreateSeed(creature),
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
        Vector2 bodyExplosionCenter,
        string? detachedBoneName = null,
        Vector2? detachedExplosionCenter = null,
        int zIndex = BossBurstPresentationCoordinator.FragmentZIndex)
    {
        using BossDismembermentSnapshot? snapshot = TryCapture(room, creature);
        return TrySpawn(
            room,
            creature,
            snapshot,
            bodyExplosionCenter,
            detachedBoneName,
            detachedExplosionCenter,
            zIndex);
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

        if (!capture.IsReady || capture.Texture == null)
        {
            capture.Dispose();
            failureReason = "the GPU capture did not finish before the presentation began";
            return null;
        }

        Node? presentationParent = capture.PresentationParent;
        if (presentationParent == null || !GodotObject.IsInstanceValid(presentationParent))
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
            _canSplitSpine = snapshot.CanSplitSpine,
            _monsterId = snapshot.MonsterId,
            _mode = mode,
            _burstTriggered = mode == PresentationMode.CompressedBurst,
            _captureSetupTicks = capture.SetupElapsedTicks,
            _captureReadyAgeTicks = System.Diagnostics.Stopwatch.GetTimestamp()
                - capture.CaptureStartedTicks
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
            presentation.InitializeGeometry(snapshot.BodyGlobalTransform);
            presentation._burstOrigin = presentation.ToLocalPoint(bodyExplosionCenter);
            BossFragmentPartition partition = presentation.BuildFragmentCells(
                detachedBoneName,
                detachedExplosionCenter.HasValue);
            if (partition.Cells.Count < 2)
            {
                throw new InvalidOperationException("the captured body produced fewer than two cells");
            }

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

            Vector2I captureSize = capture.PixelSize;
            Entry.Logger.Info(
                $"Boss dismemberment spawned: boss={presentation._monsterId}, source=capture-mesh, "
                + $"capture={captureSize.X}x{captureSize.Y}, "
                + $"capture_bytes={capture.EstimatedTextureBytes}, "
                + $"fragments={presentation._fragments.Count}, "
                + $"particles={presentation._fragments.Count * SoftFragmentBody.ParticleCount}, "
                + $"constraints={presentation._bodies.Sum(body => body.ConstraintCount)}, "
                + $"joints={presentation._joints.Count}, mode={mode}, "
                + $"motion_seed={presentation._motionSeed}.");
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
        if (GodotObject.IsInstanceValid(creature)
            && GodotObject.IsInstanceValid(creature.Body))
        {
            creature.Body.Visible = false;
        }

        Entry.Logger.Warn(
            $"Boss dismemberment completed without fragments for "
            + $"{creature.Entity.Monster?.Id.Entry}: {reason}.");
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

    private void InitializeGeometry(Transform2D bodyGlobalTransform)
    {
        _bodyToPresentation = GlobalTransform.AffineInverse() * bodyGlobalTransform;
        _floorY = RectCorners(_bodyLocalBounds)
            .Select(point => (_bodyToPresentation * point).Y)
            .Max();
        Transform2D sceneToGlobal = _room.SceneContainer.GetGlobalTransform();
        Vector2 sceneSize = _room.SceneContainer.Size;
        Transform2D globalToPresentation = GlobalTransform.AffineInverse();
        Vector2[] sceneCorners =
        [
            globalToPresentation * (sceneToGlobal * Vector2.Zero),
            globalToPresentation * (sceneToGlobal * new Vector2(sceneSize.X, 0f)),
            globalToPresentation * (sceneToGlobal * sceneSize),
            globalToPresentation * (sceneToGlobal * new Vector2(0f, sceneSize.Y))
        ];
        _sceneBounds = BoundsOf(sceneCorners).Grow(SceneMargin);
    }

    private BossFragmentPartition BuildFragmentCells(
        string? detachedBoneName,
        bool hasDetachedBurst) =>
        BossFragmentPartitioner.Build(
            _capture?.Visual,
            _bodyLocalBounds,
            _canSplitSpine,
            _seed,
            detachedBoneName,
            hasDetachedBurst);

    private void InitializeSoftBodies(
        BossFragmentPartition partition,
        int zIndex,
        PresentationMode mode,
        float architectFallDirection,
        Vector2? detachedExplosionCenter)
    {
        IReadOnlyList<BossFragmentCell> cells = partition.Cells;
        Texture2D texture = _capture?.Texture
            ?? throw new InvalidOperationException("The captured boss texture expired before fragment creation.");
        Rect2 textureBounds = _capture.TextureBounds;
        float[] mappedAreas = cells.Select(ResolveMappedArea).ToArray();
        float averageArea = Math.Max(1f, mappedAreas.Average());
        _motionSeed = BossDismembermentMath.ResolveMotionSeed(
            _seed,
            Time.GetTicksUsec(),
            GetInstanceId());
        var rng = new RandomNumberGenerator { Seed = _motionSeed };
        int[] sectors = Enumerable.Range(0, cells.Count).ToArray();
        for (int index = sectors.Length - 1; index > 0; index--)
        {
            int swap = rng.RandiRange(0, index);
            (sectors[index], sectors[swap]) = (sectors[swap], sectors[index]);
        }

        BossFragmentPoint[] seeds = cells.Select(cell => cell.Seed).ToArray();
        Vector2 detachedOrigin = detachedExplosionCenter.HasValue
            ? ToLocalPoint(detachedExplosionCenter.Value)
            : _burstOrigin;
        float burstRotation = rng.Randf() * MathF.Tau;
        for (int index = 0; index < cells.Count; index++)
        {
            BossFragmentCell cell = cells[index];
            Vector2 compressionOrigin = partition.DetachedCellIndices.Contains(index)
                ? detachedOrigin
                : _burstOrigin;
            BossFragmentPoint direction = BossDismembermentMath.ResolveBurstDirection(
                sectors[index],
                cells.Count,
                burstRotation,
                rng.RandfRange(-1f, 1f));
            BossFragmentLaunch launch = BossDismembermentMath.ResolveLaunch(
                new BossFragmentPoint(
                    compressionOrigin.X + direction.X,
                    compressionOrigin.Y + direction.Y),
                new BossFragmentPoint(compressionOrigin.X, compressionOrigin.Y),
                mappedAreas[index] / averageArea,
                rng.Randf(),
                rng.Randf());
            float compressedArea = rng.RandfRange(0.45f, 0.6f);
            float compressedScale = MathF.Sqrt(compressedArea);
            float phase = rng.Randf() * MathF.Tau;
            BossFragmentPoint compressedCenter = new(
                compressionOrigin.X + MathF.Cos(phase) * CompressionSlideRadius,
                compressionOrigin.Y + MathF.Sin(phase) * CompressionSlideRadius);
            BossFragmentPoint? initialCenter = mode == PresentationMode.ArchitectLead
                ? null
                : compressedCenter;
            float initialScale = mode == PresentationMode.ArchitectLead ? 1f : compressedScale;
            if (!BossCapturedFragmentRenderSurface.TryCreate(
                    this,
                    index,
                    cell,
                    seeds,
                    textureBounds,
                    _bodyToPresentation,
                    texture,
                    _fragmentShader,
                    initialCenter,
                    initialScale,
                    Math.Clamp(mappedAreas[index] / averageArea, 0.25f, 3f),
                    BossDismembermentMath.ResolveCollisionPadding(mappedAreas[index]),
                    zIndex,
                    out BossCapturedFragmentRenderSurface? surface)
                || surface == null)
            {
                continue;
            }

            var runtime = new SoftFragmentRuntime(
                surface,
                new Vector2(launch.VelocityX, launch.VelocityY),
                launch.AngularVelocityDegrees,
                compressedScale,
                phase,
                rng.RandfRange(34f, 58f),
                compressionOrigin);
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

        BalanceHorizontalMomentum();
        BuildRagdollLinks(
            mode == PresentationMode.ArchitectLead ? _bodies.Count : 3,
            canBreak: mode != PresentationMode.ArchitectLead);
        _spawnedFragmentCount = _fragments.Count;
        _spawnedJointCount = _joints.Count;
    }

    private void BalanceHorizontalMomentum()
    {
        float totalMass = 0f;
        float horizontalMomentum = 0f;
        for (int index = 0; index < _fragments.Count; index++)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            totalMass += fragment.Body.Mass;
            horizontalMomentum += fragment.Body.Mass * fragment.LaunchVelocity.X;
        }

        float averageVelocity = totalMass <= 0.001f ? 0f : horizontalMomentum / totalMass;
        for (int index = 0; index < _fragments.Count; index++)
        {
            _fragments[index].LaunchVelocity -= new Vector2(averageVelocity, 0f);
        }
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
                Math.Clamp(particleRestLength, 0.5f, 72f))
            {
                CanBreak = canBreak
            };
            _joints.Add(joint);
        }
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
        BossFragmentPoint center = default;
        for (int index = 0; index < _bodies.Count; index++)
        {
            center = new BossFragmentPoint(
                center.X + _bodies[index].Center.X,
                center.Y + _bodies[index].Center.Y);
        }

        float inverseCount = 1f / Math.Max(1, _bodies.Count);
        _burstOrigin = new Vector2(center.X * inverseCount, center.Y * inverseCount);
        for (int index = 0; index < _fragments.Count; index++)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            fragment.CompressionOrigin = _burstOrigin;
            float phase = fragment.CompressionPhase;
            BossFragmentPoint compressedCenter = new(
                _burstOrigin.X + MathF.Cos(phase) * CompressionSlideRadius,
                _burstOrigin.Y + MathF.Sin(phase) * CompressionSlideRadius);
            fragment.Body.PinCompressed(
                compressedCenter,
                fragment.CompressedScale,
                phase,
                slideRadius: 0f);
        }

        _joints.Clear();
        BuildRagdollLinks(maximumClusterSize: 3, canBreak: true);
        _spawnedJointCount += _joints.Count;

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
                fragment.Body.PinCompressed(center, scale, phase, slideRadius: 0.8f);
            }

            _burstElapsed += seconds;
            return;
        }

        if (!_burstReleased)
        {
            _burstReleased = true;
            for (int index = 0; index < _fragments.Count; index++)
            {
                SoftFragmentRuntime fragment = _fragments[index];
                fragment.Body.Release(
                    new BossFragmentPoint(fragment.LaunchVelocity.X, fragment.LaunchVelocity.Y),
                    Mathf.DegToRad(fragment.LaunchAngularVelocityDegrees));
            }
        }

        float pumpProgress = Math.Clamp(
            (_burstElapsed - CompressionSeconds) / PumpOpenSeconds,
            0f,
            1f);
        pumpProgress = pumpProgress * pumpProgress * (3f - 2f * pumpProgress);
        for (int index = 0; index < _fragments.Count; index++)
        {
            SoftFragmentRuntime fragment = _fragments[index];
            fragment.Body.TargetLinearScale = Lerp(fragment.CompressedScale, 1f, pumpProgress);
            fragment.Body.SetCollisionEnvelope(
                hullScale: Lerp(0.06f, 1f, pumpProgress),
                marginScale: pumpProgress);
        }

        SolveSoftBodies(seconds, floorY: null);
        _burstElapsed += seconds;
    }

    private void SolveSoftBodies(float seconds, float? floorY)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        SoftBodyStepMetrics metrics = _solver.Step(
            _bodies,
            _joints,
            seconds,
            Gravity,
            AirDrag,
            floorY);
        long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        _solverTicks += elapsedTicks;
        _longestSolverTicks = Math.Max(_longestSolverTicks, elapsedTicks);
        _solverSteps++;
        _contactCount += metrics.Contacts;
        _brokenLinkCount += metrics.BrokenLinks;
        for (int index = 0; index < _bodies.Count; index++)
        {
            SoftFragmentBody body = _bodies[index];
            if (!body.HasFiniteState)
            {
                continue;
            }

            _minimumAreaRatio = Math.Min(_minimumAreaRatio, body.ResolveMinimumCellAreaRatio());
            _maximumStretch = Math.Max(_maximumStretch, body.ResolveMaximumStretch());
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
        }

        long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        _renderTicks += elapsedTicks;
        _longestRenderTicks = Math.Max(_longestRenderTicks, elapsedTicks);
        _renderFrames++;
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

            RemoveFragmentAt(index);
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

    private void RemoveFragmentAt(int index)
    {
        SoftFragmentRuntime fragment = _fragments[index];
        _joints.RemoveAll(joint =>
            ReferenceEquals(joint.First, fragment.Body)
            || ReferenceEquals(joint.Second, fragment.Body));
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
        for (int index = _fragments.Count - 1; index >= 0; index--)
        {
            _fragments[index].Surface.Dispose();
        }

        _fragments.Clear();
        _bodies.Clear();
        _joints.Clear();
    }

    private void CompleteAndFree()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        SetProcess(false);
        double averageSolverMilliseconds = ResolveAverageMilliseconds(_solverTicks, _solverSteps);
        double longestSolverMilliseconds = ResolveMilliseconds(_longestSolverTicks);
        double averageRenderMilliseconds = ResolveAverageMilliseconds(_renderTicks, _renderFrames);
        double longestRenderMilliseconds = ResolveMilliseconds(_longestRenderTicks);
        Entry.Logger.Info(
            $"Boss soft-body summary: boss={_monsterId}, motion_seed={_motionSeed}, "
            + $"fragments={_spawnedFragmentCount}, "
            + $"particles={_spawnedFragmentCount * SoftFragmentBody.ParticleCount}, "
            + $"joints={_spawnedJointCount}, contacts={_contactCount}, "
            + $"broken_joints={_brokenLinkCount}, minimum_area_ratio={_minimumAreaRatio:F3}, "
            + $"maximum_stretch={_maximumStretch:F3}, maximum_residual={_maximumResidual:F3}, "
            + $"capture_setup_ms={ResolveMilliseconds(_captureSetupTicks):F3}, "
            + $"capture_ready_age_ms={ResolveMilliseconds(_captureReadyAgeTicks):F3}, "
            + $"solver_average_ms={averageSolverMilliseconds:F3}, "
            + $"solver_longest_ms={longestSolverMilliseconds:F3}, "
            + $"render_average_ms={averageRenderMilliseconds:F3}, "
            + $"render_longest_ms={longestRenderMilliseconds:F3}, "
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

    private static float Lerp(float from, float to, float weight) =>
        from + (to - from) * Math.Clamp(weight, 0f, 1f);

    private static ulong CreateSeed(NCreature creature)
    {
        ulong combatId = unchecked((ulong)creature.Entity.CombatId.GetValueOrDefault());
        ulong modelHash = BossDismembermentMath.StableHash64(
            creature.Entity.Monster?.Id.Entry ?? creature.Name.ToString());
        return combatId * 0x9E3779B97F4A7C15UL ^ modelHash ^ 0x4E534255525354UL;
    }

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

    private sealed class SoftFragmentRuntime(
        BossCapturedFragmentRenderSurface surface,
        Vector2 launchVelocity,
        float launchAngularVelocityDegrees,
        float compressedScale,
        float compressionPhase,
        float compressionSpeed,
        Vector2 compressionOrigin)
    {
        public BossCapturedFragmentRenderSurface Surface { get; } = surface;
        public SoftFragmentBody Body => Surface.Body;
        public Vector2 LaunchVelocity { get; set; } = launchVelocity;
        public float LaunchAngularVelocityDegrees { get; } = launchAngularVelocityDegrees;
        public float CompressedScale { get; } = compressedScale;
        public float CompressionPhase { get; } = compressionPhase;
        public float CompressionSpeed { get; } = compressionSpeed;
        public Vector2 CompressionOrigin { get; set; } = compressionOrigin;
    }
}
