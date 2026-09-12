using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class NinjaSlayerFreeControl
{
    private readonly Dictionary<Control, Control.GuiInputEventHandler> _pointerSurfaces = [];
    private Vector2 _pointerCanvas;
    private long _nativePointerFrame = -1;
    private ulong _lastMouseEvent;

    private Vector2 PointerWorld() => _spaceToCanvas.AffineInverse() * _pointerCanvas;

    private void ConnectPointerSurfaces()
    {
        _pointerCanvas = GetViewport().GetMousePosition();
        _nativePointerFrame = -1;
        if (NCombatRoom.Instance is { } room)
        {
            Connect(room);
            Connect(room.BackCombatVfxContainer);
            if (room.Ui != null) Connect(room.Ui);
        }
        if (Actor.Hitbox != null) Connect(Actor.Hitbox);
        void Connect(Control surface)
        {
            if (_pointerSurfaces.ContainsKey(surface)) return;
            Control.GuiInputEventHandler handler = input => HandleFreePointer(input, surface);
            _pointerSurfaces.Add(surface, handler);
            surface.GuiInput += handler;
        }
    }

    private void DisconnectPointerSurfaces()
    {
        foreach (var pair in _pointerSurfaces)
            if (GodotObject.IsInstanceValid(pair.Key)) pair.Key.GuiInput -= pair.Value;
        _pointerSurfaces.Clear();
    }

    public override void _Input(InputEvent input)
    {
        if (!Active) return;
        if (input is InputEventMouse mouse) _pointerCanvas = mouse.Position;
        if (IsCardInputActive()) _nativePointerFrame = GetTree().GetFrame();
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } && _mouseDown)
        {
            // Release even when a native UI control receives the mouse-up. Never consume its event.
            Callable.From(FinishMouseGesture).CallDeferred();
        }
        if (_paused || _exclusiveDepth > 0 || IsBlocked()) { ClearInput(); return; }
        if (input is not InputEventKey key) return;
        if (NCombatRoom.Instance?.Ui?.Hand?.IsInCardSelection == true
            || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
        {
            _left = _right = _down = _jumpHeld = _jumpPressed = _dashPressed = false;
            return;
        }
        Key code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        bool handled = true;
        switch (code)
        {
            case Key.A: _left = key.Pressed; break;
            case Key.D: _right = key.Pressed; break;
            case Key.S: _down = key.Pressed; break;
            case Key.W:
            case Key.Space: _jumpHeld = key.Pressed; _jumpPressed |= key.Pressed && !key.Echo; break;
            case Key.Shift: _dashPressed |= key.Pressed && !key.Echo; break;
            default: handled = false; break;
        }
        if (handled) GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent input) => HandleFreePointer(input, null);

    private bool IsCardInputActive() => Pose.HasCardDrag || NCombatRoom.Instance?.Ui?.Hand?.InCardPlay == true
        || NCombatRoom.Instance?.Ui?.Hand?.IsInCardSelection == true
        || NRun.Instance?.GlobalUi?.TargetManager?.IsInSelection == true;

    private bool NativePointerThisFrame() => IsCardInputActive() || _nativePointerFrame == GetTree().GetFrame()
        || NRun.Instance?.GlobalUi?.TargetManager?.LastTargetingFinishedFrame == GetTree().GetFrame();

    private bool CanUsePointer(Control? receiver = null)
    {
        if (!Active || _paused || _exclusiveDepth > 0 || IsBlocked() || NativePointerThisFrame()) return false;
        if (!_arena.HasPoint(PointerWorld())) return false;
        Control? hovered = GetViewport().GuiGetHoveredControl() ?? receiver;
        if (hovered == null) return true;
        for (Node? node = hovered; node != null; node = node.GetParent())
            if (node is NClickableControl or BaseButton or LineEdit or TextEdit) return false;
        return _pointerSurfaces.ContainsKey(hovered);
    }

    private bool PointerOverOtherCreature() => Actor.Entity.CombatState?.Creatures.Any(creature =>
        !ReferenceEquals(creature, Actor.Entity) && creature.GetCreatureNode() is { } node
        && node.Hitbox != null && new Rect2(Vector2.Zero, node.Hitbox.Size).HasPoint(
            node.Hitbox.GetGlobalTransformWithCanvas().AffineInverse() * _pointerCanvas)) == true;

    private void HandleFreePointer(InputEvent input, Control? receiver)
    {
        if (input is not InputEventMouseButton { Pressed: true } mouse || !CanUsePointer(receiver)
            || mouse.GetInstanceId() == _lastMouseEvent || PointerOverOtherCreature()) return;
        _lastMouseEvent = mouse.GetInstanceId();
        Pose.SyncNow(); ReadHull();
        bool onBody = Geometry2D.IsPointInPolygon(PointerWorld(), _worldHull);
        if (receiver == Actor.Hitbox && !onBody) return;
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            _pressPoint = PointerWorld(); _pressOnBody = onBody;
            _grabLocal = PhysicsTransform.AffineInverse() * _pressPoint;
            _mouseDown = true; _held = 0f; _tornadoTriggered = false;
        }
        else if (mouse.ButtonIndex == MouseButton.Right && !onBody) BeginThrow();
    }

    private void FinishMouseGesture()
    {
        if (!Active || !_mouseDown) return;
        if (!_pressOnBody && !_tornadoTriggered && CanUsePointer() && !PointerOverOtherCreature())
            BeginAttack(FreeControlMotor.AttackTier(_held));
        CancelMouseGesture();
    }

    private void CancelMouseGesture()
    {
        _mouseDown = false;
        _chargeMotion?.Dispose(); _chargeMotion = null;
        ReleaseGrip();
    }
}
