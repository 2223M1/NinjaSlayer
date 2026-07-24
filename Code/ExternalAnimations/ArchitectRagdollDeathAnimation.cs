using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class ArchitectRagdollDeathAnimation : IDisposable
{
    public const float FallSeconds = 1f;

    private const float BodyTravel = 42f;
    private const float BodyLift = 20f;
    private const float BodyRotationDegrees = 70f;

    private static readonly BoneMotionSpec[] BoneSpecs =
    [
        new("shoulder_b", -6f, 5f, 3f, -58f),
        new("shoulder front", 8f, 8f, 5f, 76f),
        new("leg_base_f", 6f, 4f, 7f, 34f),
        new("leg_base_b", -5f, 3f, 6f, -30f),
        new("pen", -12f, 10f, 8f, -115f)
    ];

    private readonly MegaSprite _sprite;
    private readonly Node2D _body;
    private readonly Callable _applyCallable;
    private readonly List<BoneMotion> _bones;
    private readonly Vector2 _bodyPosition;
    private readonly Vector2 _bodyScale;
    private readonly float _bodyRotation;
    private readonly Vector2[] _boundsBodyLocal;
    private readonly float _originalFloorParentY;
    private float _progress;
    private float _direction;
    private bool _restore = true;
    private bool _disposed;

    private ArchitectRagdollDeathAnimation(
        MegaSprite sprite,
        Node2D body,
        List<BoneMotion> bones,
        NCreature architect)
    {
        _sprite = sprite;
        _body = body;
        _bones = bones;
        _bodyPosition = body.Position;
        _bodyScale = body.Scale;
        _bodyRotation = body.RotationDegrees;
        (_boundsBodyLocal, _originalFloorParentY) = CaptureFloorReference(architect);
        _applyCallable = Callable.From(Apply);
        Error connection = _sprite.ConnectBeforeWorldTransformsChange(_applyCallable);
        if (connection != Error.Ok)
        {
            throw new InvalidOperationException(
                $"Could not connect Architect ragdoll pose override: {connection}.");
        }
    }

    public static ArchitectRagdollDeathAnimation? TryCreate(NCreature architect)
    {
        MegaSprite? sprite = architect.Visuals.SpineBody;
        using MegaSkeleton? skeleton = sprite?.GetSkeleton();
        if (sprite == null || skeleton == null)
        {
            Entry.Logger.Warn("Architect ragdoll skipped because its Spine skeleton is unavailable.");
            return null;
        }

        var bones = new List<BoneMotion>();
        try
        {
            foreach (BoneMotionSpec spec in BoneSpecs)
            {
                MegaBone? bone = skeleton.FindBone(spec.BoneName);
                if (bone == null || !BoneMotion.TryCreate(bone, spec, out BoneMotion? motion))
                {
                    Entry.Logger.Warn($"Architect ragdoll bone '{spec.BoneName}' is unavailable.");
                    bone?.Dispose();
                    continue;
                }

                bones.Add(motion!);
            }

            return new ArchitectRagdollDeathAnimation(sprite, architect.Body, bones, architect);
        }
        catch
        {
            foreach (BoneMotion bone in bones)
            {
                bone.Dispose(restore: true);
            }

            throw;
        }
    }

    public void SetProgress(float progress, float direction)
    {
        _progress = Mathf.Clamp(progress, 0f, 1f);
        _direction = Mathf.IsZeroApprox(direction) ? 1f : Mathf.Sign(direction);
        Apply();
    }

    public void CommitDisappearance() => _restore = false;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (GodotObject.IsInstanceValid(_sprite.BoundObject))
        {
            _sprite.DisconnectBeforeWorldTransformsChange(_applyCallable);
        }

        if (_restore && GodotObject.IsInstanceValid(_body))
        {
            _body.Position = _bodyPosition;
            _body.RotationDegrees = _bodyRotation;
        }

        foreach (BoneMotion bone in _bones)
        {
            bone.Dispose(_restore);
        }
    }

    private void Apply()
    {
        if (_disposed || !GodotObject.IsInstanceValid(_body))
        {
            return;
        }

        float eased = ArchitectRagdollMath.EaseOutCubic(_progress);
        float arcY = ArchitectRagdollMath.ParabolicOffset(_progress, BodyLift, 0f);
        float targetRotation = _bodyRotation + _direction * BodyRotationDegrees * eased;
        float landingCompensation = ResolveLandingCompensation(_direction) * eased;
        _body.Position = _bodyPosition + new Vector2(
            _direction * BodyTravel * eased,
            arcY + landingCompensation);
        _body.RotationDegrees = targetRotation;
        foreach (BoneMotion bone in _bones)
        {
            bone.Apply(_progress, eased, _direction);
        }
    }

    private float ResolveLandingCompensation(float direction)
    {
        float finalRotation = _bodyRotation + direction * BodyRotationDegrees;
        float finalLowestY = _boundsBodyLocal.Max(point =>
            _bodyPosition.Y + ArchitectRagdollMath.RotatedScaledY(
                point.X,
                point.Y,
                _bodyScale.X,
                _bodyScale.Y,
                finalRotation));
        return _originalFloorParentY - finalLowestY;
    }

    private static (Vector2[] BodyLocal, float FloorParentY) CaptureFloorReference(
        NCreature architect)
    {
        Node2D body = architect.Body;
        CanvasItem parent = body.GetParent<CanvasItem>();
        Rect2 bounds = architect.Visuals.Bounds.GetGlobalRect();
        Vector2[] canvasCorners =
        [
            bounds.Position,
            bounds.Position + new Vector2(bounds.Size.X, 0f),
            bounds.End,
            bounds.Position + new Vector2(0f, bounds.Size.Y)
        ];
        Transform2D bodyInverse = body.GetGlobalTransformWithCanvas().AffineInverse();
        Transform2D parentInverse = parent.GetGlobalTransformWithCanvas().AffineInverse();
        Vector2[] bodyLocal = canvasCorners.Select(point => bodyInverse * point).ToArray();
        float floorParentY = canvasCorners.Max(point => (parentInverse * point).Y);
        return (bodyLocal, floorParentY);
    }

    private sealed class BoneMotion : IDisposable
    {
        private readonly MegaBone _bone;
        private readonly BoneMotionSpec _spec;
        private readonly float _x;
        private readonly float _y;
        private readonly float _rotation;
        private bool _disposed;

        private BoneMotion(
            MegaBone bone,
            BoneMotionSpec spec,
            float x,
            float y,
            float rotation)
        {
            _bone = bone;
            _spec = spec;
            _x = x;
            _y = y;
            _rotation = rotation;
        }

        public static bool TryCreate(
            MegaBone bone,
            BoneMotionSpec spec,
            out BoneMotion? motion)
        {
            GodotObject native = bone.BoundObject;
            string[] methods = ["get_x", "get_y", "get_rotation", "set_x", "set_y", "set_rotation"];
            if (methods.Any(method => !native.HasMethod(method)))
            {
                motion = null;
                return false;
            }

            motion = new BoneMotion(
                bone,
                spec,
                native.Call("get_x").AsSingle(),
                native.Call("get_y").AsSingle(),
                native.Call("get_rotation").AsSingle());
            GC.KeepAlive(bone);
            return true;
        }

        public void Apply(float progress, float eased, float direction)
        {
            if (_disposed || !GodotObject.IsInstanceValid(_bone.BoundObject))
            {
                return;
            }

            GodotObject native = _bone.BoundObject;
            native.Call("set_x", _x + direction * _spec.OffsetX * eased);
            native.Call(
                "set_y",
                _y + ArchitectRagdollMath.ParabolicOffset(
                    progress,
                    _spec.Lift,
                    _spec.DropY));
            native.Call("set_rotation", _rotation + direction * _spec.RotationDegrees * eased);
            GC.KeepAlive(_bone);
        }

        public void Dispose(bool restore)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (restore && GodotObject.IsInstanceValid(_bone.BoundObject))
            {
                GodotObject native = _bone.BoundObject;
                native.Call("set_x", _x);
                native.Call("set_y", _y);
                native.Call("set_rotation", _rotation);
                GC.KeepAlive(_bone);
            }

            _bone.Dispose();
        }

        void IDisposable.Dispose() => Dispose(restore: true);
    }

    private sealed record BoneMotionSpec(
        string BoneName,
        float OffsetX,
        float Lift,
        float DropY,
        float RotationDegrees);
}
