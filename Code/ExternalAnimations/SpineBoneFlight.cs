using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class SpineBoneFlight : IDisposable
{
    // These methods run on every Spine world-transform update, so marshal their names once.
    private static readonly StringName SetXMethod = new("set_x");
    private static readonly StringName SetYMethod = new("set_y");
    private static readonly StringName SetRotationMethod = new("set_rotation");
    private static readonly StringName GetWorldXMethod = new("get_world_x");
    private static readonly StringName GetWorldYMethod = new("get_world_y");

    private readonly MegaSprite _sprite;
    private readonly MegaBone _bone;
    private readonly Node2D _body;
    private readonly Callable _applyCallable;
    private readonly float _originalX;
    private readonly float _originalY;
    private readonly float _originalRotation;
    private readonly float _originalScaleX;
    private readonly float _originalScaleY;
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
        float scaleY)
    {
        OwnerId = ownerId;
        BoneName = boneName;
        _sprite = sprite;
        _bone = bone;
        _boneSupportsWorldPosition = bone.BoundObject.HasMethod(GetWorldXMethod)
            && bone.BoundObject.HasMethod(GetWorldYMethod);
        _body = body;
        _x = _originalX = x;
        _y = _originalY = y;
        _rotation = _originalRotation = rotation;
        _originalScaleX = scaleX;
        _originalScaleY = scaleY;
        _applyCallable = Callable.From<GodotObject>(_ => Apply());
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
                scaleY);
        }
        catch
        {
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

}
