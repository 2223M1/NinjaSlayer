using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class ArchitectSpineRagdoll : IDisposable
{
    private static readonly StringName GetRotationMethod = new("get_rotation");
    private static readonly StringName SetRotationMethod = new("set_rotation");
    private static readonly StringName GetWorldXMethod = new("get_world_x");
    private static readonly StringName GetWorldYMethod = new("get_world_y");

    private readonly Node2D _presentation;
    private readonly MegaSprite _sprite;
    private readonly Node2D _body;
    private readonly CanvasItem _bodyParent;
    private readonly Transform2D _originalBodyTransform;
    private readonly Transform2D _visualBodyToPresentation;
    private readonly List<Segment> _segments;
    private readonly List<SoftFragmentBody> _bodies;
    private readonly List<SoftRagdollLink> _links;
    private readonly Segment _root;
    private readonly Callable _applyCallable;
    private Exception? _callbackFailure;
    private bool _connected;
    private bool _driving;
    private bool _disposed;

    private ArchitectSpineRagdoll(
        Node2D presentation,
        MegaSprite sprite,
        Node2D body,
        CanvasItem bodyParent,
        Transform2D visualBodyToPresentation,
        List<Segment> segments,
        List<SoftRagdollLink> links)
    {
        _presentation = presentation;
        _sprite = sprite;
        _body = body;
        _bodyParent = bodyParent;
        _originalBodyTransform = body.Transform;
        _visualBodyToPresentation = visualBodyToPresentation;
        _segments = segments;
        _bodies = segments.Select(segment => segment.Body).ToList();
        _links = links;
        _root = segments[0];
        _applyCallable = Callable.From<GodotObject>(_ => ApplyBoneOverrides());
    }

    public IReadOnlyList<SoftFragmentBody> Bodies => _bodies;
    public IReadOnlyList<SoftRagdollLink> Links => _links;

    public static ArchitectSpineRagdoll? TryCreate(
        Node2D presentation,
        NCreature architect,
        BossFragmentPartition partition,
        Transform2D physicsBodyToPresentation,
        Transform2D visualBodyToPresentation,
        BossFragmentPoint initialVelocity,
        out string failureReason)
    {
        failureReason = string.Empty;
        ArchitectSpineRagdoll? ragdoll = null;
        var segments = new List<Segment>();
        try
        {
            Node2D body = architect.Body;
            if (!GodotObject.IsInstanceValid(body)
                || body.GetParent() is not CanvasItem bodyParent
                || !GodotObject.IsInstanceValid(bodyParent))
            {
                throw new InvalidOperationException("the Architect Spine body is unavailable");
            }

            MegaSprite? sprite = architect.Visuals.SpineBody;
            MegaSkeleton? skeleton = sprite?.GetSkeleton();
            if (sprite == null || skeleton == null)
            {
                throw new InvalidOperationException("the Architect Spine skeleton is unavailable");
            }

            using IDisposable? skeletonLease = skeleton as IDisposable;
            SegmentDefinition[] definitions = partition.Fragments
                .GroupBy(fragment => fragment.Part.PrimaryBoneId)
                .Select(group => new SegmentDefinition(
                    group.First().Part,
                    group.Sum(fragment => Math.Max(0.0001f, fragment.BodyAreaRatio))))
                .ToArray();
            if (definitions.Length < 2)
            {
                throw new InvalidOperationException(
                    "the Architect death pose contains fewer than two visible bone segments");
            }

            IReadOnlyList<ArchitectRagdollTopologyNode> topology =
                BossDismembermentMath.ResolveArchitectRagdollTopology(
                    definitions.Select(definition => new ArchitectRagdollTopologyPart(
                        definition.Part.PrimaryBoneId,
                        definition.Weight,
                        definition.Part.DrawOrder,
                        definition.Part.AncestorBoneIds)).ToArray());
            Dictionary<ulong, SegmentDefinition> definitionsById = definitions
                .ToDictionary(definition => definition.Part.PrimaryBoneId);
            var segmentsById = new Dictionary<ulong, Segment>();
            foreach (ArchitectRagdollTopologyNode node in topology)
            {
                SegmentDefinition definition = definitionsById[node.BoneId];
                MegaBone bone = skeleton.FindBone(definition.Part.PrimaryBoneName)
                    ?? throw new InvalidOperationException(
                        $"Spine bone '{definition.Part.PrimaryBoneName}' was not found");
                try
                {
                    ValidateBoneMethods(bone, definition.Part.PrimaryBoneName);
                    Segment? parent = node.ParentBoneId.HasValue
                        ? segmentsById[node.ParentBoneId.Value]
                        : null;
                    var segment = new Segment(
                        CreateBody(
                            segments.Count,
                            definition.Part.SourceBounds,
                            physicsBodyToPresentation,
                            definition.Weight),
                        bone,
                        ReadBoneRotation(bone),
                        parent);
                    segments.Add(segment);
                    segmentsById.Add(node.BoneId, segment);
                }
                catch
                {
                    (bone as IDisposable)?.Dispose();
                    throw;
                }
            }

            var links = new List<SoftRagdollLink>(Math.Max(0, segments.Count - 1));
            foreach (Segment segment in segments)
            {
                segment.Body.Release(
                    segment.Parent == null ? initialVelocity : default,
                    angularVelocityRadians: 0f);
                if (segment.Parent == null)
                {
                    continue;
                }

                BossFragmentPoint pivot = ReadBoneWorldPosition(
                    segment.Bone,
                    physicsBodyToPresentation);
                int parentParticle = FindNearestParticle(segment.Parent.Body, pivot);
                int childParticle = FindNearestParticle(segment.Body, pivot);
                BossFragmentPoint parentPosition = segment.Parent.Body
                    .GetParticlePosition(parentParticle);
                BossFragmentPoint childPosition = segment.Body
                    .GetParticlePosition(childParticle);
                links.Add(new SoftRagdollLink(
                    segment.Parent.Body,
                    parentParticle,
                    segment.Body,
                    childParticle,
                    Distance(parentPosition, childPosition))
                {
                    CanBreak = false
                });
            }

            ragdoll = new ArchitectSpineRagdoll(
                presentation,
                sprite,
                body,
                bodyParent,
                visualBodyToPresentation,
                segments,
                links);
            ragdoll.Start();
            return ragdoll;
        }
        catch (Exception exception)
        {
            ragdoll?.Dispose();
            if (ragdoll == null)
            {
                foreach (Segment segment in segments)
                {
                    segment.Dispose();
                }
            }

            failureReason = exception.Message;
            return null;
        }
    }

    public void ApplyVisualPose()
    {
        if (_disposed || !_driving)
        {
            return;
        }

        if (_callbackFailure != null)
        {
            DisableVisualDrive(_callbackFailure);
            return;
        }

        if (!_segments.All(segment => segment.TryResolvePose()))
        {
            return;
        }

        try
        {
            ApplyBodyTransform();
            ApplyBoneOverridesCore();
        }
        catch (Exception exception)
        {
            DisableVisualDrive(exception);
        }
    }

    public BossFragmentPoint ResolveBurstOrigin()
    {
        float weightedX = 0f;
        float weightedY = 0f;
        float totalMass = 0f;
        foreach (Segment segment in _segments)
        {
            BossFragmentPoint center = segment.Body.Center;
            weightedX += center.X * segment.Body.Mass;
            weightedY += center.Y * segment.Body.Mass;
            totalMass += segment.Body.Mass;
        }

        return totalMass > 0f
            ? new BossFragmentPoint(weightedX / totalMass, weightedY / totalMass)
            : _root.Body.Center;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopDriving();
        foreach (Segment segment in _segments)
        {
            segment.Dispose();
        }
    }

    private void Start()
    {
        if (!_segments.All(segment => segment.TryResolvePose()))
        {
            throw new InvalidOperationException("the Architect ragdoll pose could not be resolved");
        }

        Error connection = _sprite.ConnectBeforeWorldTransformsChange(_applyCallable);
        if (connection != Error.Ok)
        {
            throw new InvalidOperationException(
                $"could not connect the Architect Spine ragdoll override: {connection}");
        }

        _connected = true;
        _driving = true;
        ApplyBodyTransform();
        ApplyBoneOverridesCore();
    }

    private void ApplyBodyTransform()
    {
        if (!GodotObject.IsInstanceValid(_presentation)
            || !GodotObject.IsInstanceValid(_body)
            || !GodotObject.IsInstanceValid(_bodyParent))
        {
            throw new InvalidOperationException("the Architect ragdoll scene nodes expired");
        }

        Vector2 currentCenter = new(_root.Pose.Position.X, _root.Pose.Position.Y);
        Vector2 restCenter = new(_root.Body.RestCenter.X, _root.Body.RestCenter.Y);
        Transform2D rigidDelta = new Transform2D(0f, currentCenter)
            * new Transform2D(_root.Pose.RotationRadians, Vector2.Zero)
            * new Transform2D(0f, -restCenter);
        Transform2D bodyGlobal = _presentation.GlobalTransform
            * rigidDelta
            * _visualBodyToPresentation;
        _body.Transform = _body.TopLevel
            ? bodyGlobal
            : _bodyParent.GetGlobalTransform().AffineInverse() * bodyGlobal;
    }

    private void ApplyBoneOverrides()
    {
        if (_disposed || !_driving)
        {
            return;
        }

        try
        {
            ApplyBoneOverridesCore();
        }
        catch (Exception exception)
        {
            _callbackFailure = exception;
        }
    }

    private void ApplyBoneOverridesCore()
    {
        for (int index = 1; index < _segments.Count; index++)
        {
            Segment segment = _segments[index];
            Segment parent = segment.Parent!;
            float rotation = BossDismembermentMath.ResolveArchitectLocalBoneRotation(
                segment.OriginalRotationDegrees,
                segment.Pose.RotationRadians,
                parent.Pose.RotationRadians);
            segment.Bone.BoundObject.Call(SetRotationMethod, rotation);
            GC.KeepAlive(segment.Bone);
        }
    }

    private void DisableVisualDrive(Exception exception)
    {
        Entry.Logger.Warn(
            $"Architect Spine ragdoll visual drive stopped; keeping the frozen death pose: {exception.Message}");
        StopDriving();
    }

    private void StopDriving()
    {
        _driving = false;
        if (_connected && GodotObject.IsInstanceValid(_sprite.BoundObject))
        {
            _sprite.DisconnectBeforeWorldTransformsChange(_applyCallable);
        }

        _connected = false;
        if (GodotObject.IsInstanceValid(_body))
        {
            _body.Transform = _originalBodyTransform;
        }

        foreach (Segment segment in _segments)
        {
            segment.RestoreRotation();
        }
    }

    private static SoftFragmentBody CreateBody(
        int index,
        Rect2 sourceBounds,
        Transform2D bodyToPresentation,
        float mass)
    {
        var restGrid = new BossFragmentPoint[SoftFragmentBody.ParticleCount];
        for (int row = 0; row < SoftFragmentBody.GridSize; row++)
        {
            for (int column = 0; column < SoftFragmentBody.GridSize; column++)
            {
                float u = column / (float)(SoftFragmentBody.GridSize - 1);
                float v = row / (float)(SoftFragmentBody.GridSize - 1);
                Vector2 mapped = bodyToPresentation
                    * (sourceBounds.Position + sourceBounds.Size * new Vector2(u, v));
                restGrid[row * SoftFragmentBody.GridSize + column] =
                    new BossFragmentPoint(mapped.X, mapped.Y);
            }
        }

        SoftBodyHullPoint[] restHull =
        [
            MapHull(sourceBounds.Position, 0f, 0f),
            MapHull(new Vector2(sourceBounds.End.X, sourceBounds.Position.Y), 1f, 0f),
            MapHull(sourceBounds.End, 1f, 1f),
            MapHull(new Vector2(sourceBounds.Position.X, sourceBounds.End.Y), 0f, 1f)
        ];
        BossFragmentPoint center = new(
            restGrid.Average(point => point.X),
            restGrid.Average(point => point.Y));
        var body = new SoftFragmentBody(
            id: -1 - index,
            restGrid,
            restHull,
            center,
            compressedScale: 1f,
            mass,
            collisionMargin: 0f);
        body.SetMaterial(SoftBodyMaterialProfile.ArchitectLead);
        body.SetCollisionEnvelope(hullScale: 1f, marginScale: 0f);
        return body;

        SoftBodyHullPoint MapHull(Vector2 point, float u, float v)
        {
            Vector2 mapped = bodyToPresentation * point;
            return new SoftBodyHullPoint(
                new BossFragmentPoint(mapped.X, mapped.Y),
                u,
                v);
        }
    }

    private static void ValidateBoneMethods(MegaBone bone, string boneName)
    {
        GodotObject native = bone.BoundObject;
        StringName[] methods =
        [
            GetRotationMethod,
            SetRotationMethod,
            GetWorldXMethod,
            GetWorldYMethod
        ];
        if (methods.Any(method => !native.HasMethod(method)))
        {
            throw new MissingMethodException(
                $"Spine bone '{boneName}' does not expose the required ragdoll methods");
        }
    }

    private static float ReadBoneRotation(MegaBone bone)
    {
        float rotation = bone.BoundObject.Call(GetRotationMethod).AsSingle();
        GC.KeepAlive(bone);
        return rotation;
    }

    private static BossFragmentPoint ReadBoneWorldPosition(
        MegaBone bone,
        Transform2D bodyToPresentation)
    {
        GodotObject native = bone.BoundObject;
        Vector2 mapped = bodyToPresentation * new Vector2(
            native.Call(GetWorldXMethod).AsSingle(),
            native.Call(GetWorldYMethod).AsSingle());
        GC.KeepAlive(bone);
        return new BossFragmentPoint(mapped.X, mapped.Y);
    }

    private static int FindNearestParticle(SoftFragmentBody body, BossFragmentPoint point)
    {
        int nearest = 0;
        float nearestDistanceSquared = float.PositiveInfinity;
        for (int index = 0; index < SoftFragmentBody.ParticleCount; index++)
        {
            BossFragmentPoint candidate = body.GetParticlePosition(index);
            float x = candidate.X - point.X;
            float y = candidate.Y - point.Y;
            float distanceSquared = x * x + y * y;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearest = index;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearest;
    }

    private static float Distance(BossFragmentPoint first, BossFragmentPoint second)
    {
        float x = second.X - first.X;
        float y = second.Y - first.Y;
        return MathF.Sqrt(x * x + y * y);
    }

    private sealed record SegmentDefinition(
        BossSemanticPartDefinition Part,
        float Weight);

    private sealed class Segment(
        SoftFragmentBody body,
        MegaBone bone,
        float originalRotationDegrees,
        Segment? parent) : IDisposable
    {
        private readonly BossFragmentPoint[] _residuals =
            new BossFragmentPoint[SoftFragmentBody.ParticleCount];
        private float _previousRotation;
        private bool _disposed;

        public SoftFragmentBody Body { get; } = body;
        public MegaBone Bone { get; } = bone;
        public float OriginalRotationDegrees { get; } = originalRotationDegrees;
        public Segment? Parent { get; } = parent;
        public SoftBodyRenderPose Pose { get; private set; }

        public bool TryResolvePose()
        {
            if (!SoftBodyRenderPoseResolver.TryResolve(
                    Body,
                    _previousRotation,
                    _residuals,
                    out SoftBodyRenderPose pose))
            {
                return false;
            }

            Pose = pose;
            _previousRotation = pose.RotationRadians;
            return true;
        }

        public void RestoreRotation()
        {
            if (GodotObject.IsInstanceValid(Bone.BoundObject))
            {
                Bone.BoundObject.Call(SetRotationMethod, OriginalRotationDegrees);
                GC.KeepAlive(Bone);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            (Bone as IDisposable)?.Dispose();
        }
    }
}
