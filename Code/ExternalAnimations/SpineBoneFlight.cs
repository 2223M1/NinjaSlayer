using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class SpineBoneFlight : IDisposable
{
    // The setters run on every Spine world-transform update and the parent-transform accessors run
    // on every frame of the head flight, so the names are marshalled once rather than per call.
    private static readonly StringName SetXMethod = new("set_x");
    private static readonly StringName SetYMethod = new("set_y");
    private static readonly StringName SetRotationMethod = new("set_rotation");
    private static readonly StringName GetAMethod = new("get_a");
    private static readonly StringName GetBMethod = new("get_b");
    private static readonly StringName GetCMethod = new("get_c");
    private static readonly StringName GetDMethod = new("get_d");
    private static readonly StringName GetWorldXMethod = new("get_world_x");
    private static readonly StringName GetWorldYMethod = new("get_world_y");

    private static readonly StringName[] ParentTransformMethods =
        [GetAMethod, GetBMethod, GetCMethod, GetDMethod, GetWorldXMethod, GetWorldYMethod];

    private readonly MegaSprite _sprite;
    private readonly MegaBone _bone;
    private readonly GodotObject? _parentBone;
    private readonly Node2D _body;
    private readonly Callable _applyCallable;
    private readonly float _originalX;
    private readonly float _originalY;
    private readonly float _originalRotation;
    private readonly float _originalScaleX;
    private readonly float _originalScaleY;
    private readonly Vector2 _originalWorldPosition;
    private readonly bool _parentSupportsWorldTransform;
    private readonly bool _boneSupportsWorldPosition;
    private float _x;
    private float _y;
    private float _rotation;
    private bool _hidden;
    private bool _disposed;

    private SpineBoneFlight(
        string ownerId,
        string boneName,
        MegaSprite sprite,
        MegaBone bone,
        Node2D body,
        float x,
        float y,
        float rotation,
        float scaleX,
        float scaleY,
        GodotObject? parentBone,
        Vector2 worldPosition)
    {
        OwnerId = ownerId;
        BoneName = boneName;
        _sprite = sprite;
        _bone = bone;
        _parentBone = parentBone;
        _parentSupportsWorldTransform = parentBone != null
            && Array.TrueForAll(ParentTransformMethods, parentBone.HasMethod);
        _boneSupportsWorldPosition = bone.BoundObject.HasMethod(GetWorldXMethod)
            && bone.BoundObject.HasMethod(GetWorldYMethod);
        _body = body;
        _x = _originalX = x;
        _y = _originalY = y;
        _rotation = _originalRotation = rotation;
        _originalScaleX = scaleX;
        _originalScaleY = scaleY;
        _originalWorldPosition = worldPosition;
        _applyCallable = Callable.From(Apply);
        Error connection = _sprite.ConnectBeforeWorldTransformsChange(_applyCallable);
        if (connection != Error.Ok)
        {
            throw new InvalidOperationException(
                $"Could not connect Spine bone flight override for {ownerId}/{boneName}: {connection}.");
        }
    }

    public string OwnerId { get; }
    public string BoneName { get; }

    public Vector2 GlobalCenter
    {
        get
        {
            GodotObject native = _bone.BoundObject;
            if (_boneSupportsWorldPosition)
            {
                float worldX = native.Call(GetWorldXMethod).AsSingle();
                float worldY = native.Call(GetWorldYMethod).AsSingle();
                GC.KeepAlive(_bone);
                return _body.ToGlobal(new Vector2(worldX, worldY));
            }

            return _body.ToGlobal(new Vector2(_x, _y));
        }
    }

    public static SpineBoneFlight? TryCreate(NCreature creature, string boneName, string ownerId)
    {
        MegaSprite? sprite = creature.Visuals.SpineBody;
        MegaSkeleton? skeleton = sprite?.GetSkeleton();
        using IDisposable skeletonLease = GameCompatibility.NativeHandles.Lease(skeleton);
        MegaBone? bone = skeleton?.FindBone(boneName);
        if (sprite == null || bone == null)
        {
            Entry.Logger.Warn($"Spine bone flight skipped: bone '{boneName}' was not found on {ownerId}.");
            GameCompatibility.NativeHandles.Dispose(bone);
            return null;
        }

        GodotObject native = bone.BoundObject;
        string[] methods =
        [
            "get_x", "get_y", "get_rotation", "get_scale_x", "get_scale_y",
            "set_x", "set_y", "set_rotation", "set_scale_x", "set_scale_y"
        ];
        if (methods.Any(method => !native.HasMethod(method)))
        {
            Entry.Logger.Warn($"Spine bone methods are unavailable for {ownerId}/{boneName}.");
            GameCompatibility.NativeHandles.Dispose(bone);
            return null;
        }

        float x = native.Call("get_x").AsSingle();
        float y = native.Call("get_y").AsSingle();
        float rotation = native.Call("get_rotation").AsSingle();
        float scaleX = native.Call("get_scale_x").AsSingle();
        float scaleY = native.Call("get_scale_y").AsSingle();
        Vector2 worldPosition = ReadWorldPosition(native, new Vector2(x, y));
        GodotObject? parentBone = null;
        if (native.HasMethod("get_parent"))
        {
            Variant parent = native.Call("get_parent");
            if (parent.VariantType == Variant.Type.Object)
            {
                parentBone = parent.AsGodotObject();
            }
        }

        GC.KeepAlive(bone);
        try
        {
            return new SpineBoneFlight(
                ownerId,
                boneName,
                sprite,
                bone,
                creature.Body,
                x,
                y,
                rotation,
                scaleX,
                scaleY,
                parentBone,
                worldPosition);
        }
        catch
        {
            parentBone?.Dispose();
            GameCompatibility.NativeHandles.Dispose(bone);
            throw;
        }
    }

    public void Advance(Vector2 offset, float rotationDegrees)
    {
        _x += offset.X;
        _y += offset.Y;
        _rotation += rotationDegrees;
        Apply();
    }

    public void SetRelativeTransform(Vector2 offset, float rotationDegrees)
    {
        _x = _originalX + offset.X;
        _y = _originalY + offset.Y;
        _rotation = _originalRotation + rotationDegrees;
        Apply();
    }

    public Vector2 GetScenePosition(CanvasItem sceneRoot)
    {
        Vector2 bodyPoint = ReadWorldPosition(_bone.BoundObject, _originalWorldPosition);
        Vector2 globalPoint = _body.GetGlobalTransform() * bodyPoint;
        return sceneRoot.GetGlobalTransform().AffineInverse() * globalPoint;
    }

    public bool SetSceneTransform(
        CanvasItem sceneRoot,
        Vector2 scenePosition,
        float rotationDegrees)
    {
        Vector2 globalPoint = sceneRoot.GetGlobalTransform() * scenePosition;
        Vector2 bodyPoint = _body.GetGlobalTransform().AffineInverse() * globalPoint;
        if (!TryConvertWorldToParentLocal(bodyPoint, out Vector2 localPoint))
        {
            Vector2 worldOffset = bodyPoint - _originalWorldPosition;
            localPoint = new Vector2(_originalX, _originalY) + worldOffset;
        }

        _x = localPoint.X;
        _y = localPoint.Y;
        _rotation = _originalRotation + rotationDegrees;
        Apply();
        return _parentBone != null;
    }

    public void MarkDisappeared()
    {
        _hidden = true;
        Apply();
    }

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

        if (GodotObject.IsInstanceValid(_bone.BoundObject))
        {
            SetNative(_originalX, _originalY, _originalRotation);
            _bone.SetScaleX(_originalScaleX);
            _bone.SetScaleY(_originalScaleY);
        }

        _parentBone?.Dispose();
        GameCompatibility.NativeHandles.Dispose(_bone);
    }

    private void Apply()
    {
        if (_disposed || !GodotObject.IsInstanceValid(_bone.BoundObject))
        {
            return;
        }

        SetNative(_x, _y, _rotation);
        if (_hidden)
        {
            _bone.Hide();
        }
    }

    private void SetNative(float x, float y, float rotation)
    {
        GodotObject native = _bone.BoundObject;
        native.Call(SetXMethod, x);
        native.Call(SetYMethod, y);
        native.Call(SetRotationMethod, rotation);
        GC.KeepAlive(_bone);
    }

    private bool TryConvertWorldToParentLocal(Vector2 worldPoint, out Vector2 localPoint)
    {
        localPoint = default;
        if (_parentBone == null || !GodotObject.IsInstanceValid(_parentBone))
        {
            return false;
        }

        // A bound Spine object's class is fixed once the skeleton exists, so the capability is
        // probed once in TryCreate instead of six HasMethod lookups on every frame.
        if (!_parentSupportsWorldTransform)
        {
            return false;
        }

        float a = _parentBone.Call(GetAMethod).AsSingle();
        float b = _parentBone.Call(GetBMethod).AsSingle();
        float c = _parentBone.Call(GetCMethod).AsSingle();
        float d = _parentBone.Call(GetDMethod).AsSingle();
        float worldX = _parentBone.Call(GetWorldXMethod).AsSingle();
        float worldY = _parentBone.Call(GetWorldYMethod).AsSingle();
        float determinant = a * d - b * c;
        if (Mathf.IsZeroApprox(determinant))
        {
            return false;
        }

        Vector2 delta = worldPoint - new Vector2(worldX, worldY);
        localPoint = new Vector2(
            (d * delta.X - b * delta.Y) / determinant,
            (-c * delta.X + a * delta.Y) / determinant);
        return true;
    }

    private static Vector2 ReadWorldPosition(GodotObject native, Vector2 fallback)
    {
        if (!native.HasMethod(GetWorldXMethod) || !native.HasMethod(GetWorldYMethod))
        {
            return fallback;
        }

        return new Vector2(
            native.Call(GetWorldXMethod).AsSingle(),
            native.Call(GetWorldYMethod).AsSingle());
    }
}
