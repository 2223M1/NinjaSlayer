using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class NinjaSlayerFreeControl : Node
{
    internal NCreature Actor = null!;
    internal NinjaSlayerAimPose Pose = null!;
    internal bool Active { get; private set; }
    internal int Generation { get; private set; }
    private readonly FreeControlMotor _motor = new();
    private SubViewport? _world;
    private CharacterBody2D _walker = null!;
    private FreeControlRigidBody _ragdoll = null!;
    private CollisionShape2D _walkShape = null!, _ragShape = null!;
    private StaticBody2D _mouseBody = null!;
    private PinJoint2D? _joint;
    private Transform2D _spaceToCanvas, _baselinePose;
    private Vector2 _baseCore;
    private Rect2 _arena;
    private Vector2[] _localHull = [];
    private Vector2[] _worldHull = [];
    private bool _ragging, _paused, _left, _right, _down, _jumpHeld, _jumpPressed, _dashPressed;
    private bool _pressOnBody, _mouseDown, _tornadoTriggered;
    private Vector2 _pressPoint, _grabLocal;
    private float _held, _facing = 1f, _standUp, _standFrom, _time;
    private int _endedTurn = -1, _exclusiveDepth;
    private Sprite2D _body = null!;
    private FreeControlMotionBlur? _blur;
    private Vector2 _savedRagdollVelocity;
    private float _savedRagdollSpin;
    private readonly Dictionary<Creature, StaticBody2D> _enemyBodies = [];

    internal static NinjaSlayerFreeControl? Get(Creature creature) => NinjaSlayerAimPose.Get(creature)?.FreeControl;
    private Transform2D PhysicsTransform => _ragging ? _ragdoll.Transform : _walker.Transform;
    private Vector2 Velocity => _ragging ? _ragdoll.RealVelocity : _walker.Velocity;
    private bool InPlayPhase => Actor.Entity.IsAlive && Actor.Entity.Player?.PlayerCombatState?.Phase == PlayerTurnPhase.Play;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = 110;
        SetPhysicsProcess(false);
    }

    public override void _Process(double delta)
    {
        bool allowed = NinjaSlayerSettings.FreeControlEnabled && InPlayPhase
            && Actor.Entity.Player!.PlayerCombatState!.TurnNumber != _endedTurn
            && RunManager.Instance.NetService?.Type == NetGameType.Singleplayer
            && NCombatRoom.Instance != null && !CombatManager.Instance.IsOverOrEnding;
        if (!allowed) { if (Active) Stop(); return; }
        if (!Active && _exclusiveDepth == 0) Start();
        if (!Active) return;
        bool paused = IsBlocked() || _exclusiveDepth > 0;
        if (paused != _paused)
        {
            _paused = paused;
            ClearInput();
            if (_ragging)
            {
                if (paused) { _savedRagdollVelocity = _ragdoll.RealVelocity; _savedRagdollSpin = _ragdoll.RealSpin; }
                else { _ragdoll.RealVelocity = _savedRagdollVelocity; _ragdoll.RealSpin = _savedRagdollSpin; }
                _ragdoll.Freeze = paused;
            }
            PauseAttacks(paused);
        }
        if (!paused && Engine.TimeScale > 0d)
        {
            float dt = (float)(delta / Engine.TimeScale);
            _time += dt;
            AdvanceAttacks(dt);
            if (_mouseDown)
            {
                _held += dt;
                if (_pressOnBody && _joint == null && (_held >= .15f || PointerWorld().DistanceTo(_pressPoint) > 4f)) Grab();
                if (!_pressOnBody && !_tornadoTriggered && _held >= .5f)
                {
                    _tornadoTriggered = true;
                    BeginAttack(2);
                }
            }
        }
    }

    private bool IsBlocked()
    {
        if (GetTree().Paused || !GetWindow().HasFocus() || !Actor.CanProcess() || CombatManager.Instance.IsPaused
            || NinjaSlayerFinisherCinematic.IsMovementOwned(Actor.Entity)) return true;
        var ui = NRun.Instance?.GlobalUi;
        return ui?.Overlays.ScreenCount > 0 || ui?.CapstoneContainer.InUse == true || ui?.MapScreen.IsOpen == true
            || NModalContainer.Instance?.OpenModal != null;
    }

    internal void Start()
    {
        if (Active) return;
        Pose.SyncNow();
        _spaceToCanvas = Actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas();
        _baseCore = _spaceToCanvas.AffineInverse() * Pose.CoreCanvas;
        ReadHull();
        Rect2 visible = GetViewport().GetVisibleRect();
        Vector2 topLeft = _spaceToCanvas.AffineInverse() * visible.Position;
        Vector2 bottomRight = _spaceToCanvas.AffineInverse() * visible.End;
        float floor = _worldHull.Max(p => p.Y);
        _arena = new(topLeft, new Vector2(bottomRight.X - topLeft.X, floor - topLeft.Y));
        _world = new SubViewport { Name = "FreeControlPhysics", World2D = new World2D(),
            Disable3D = true, GuiDisableInput = true, Size = Vector2I.One * 2,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, ProcessMode = ProcessModeEnum.Always };
        AddChild(_world);
        _walker = new CharacterBody2D { Name = "Walker", Position = _baseCore,
            CollisionLayer = 2, CollisionMask = 1, FloorSnapLength = 5f, SafeMargin = .2f };
        _ragdoll = new FreeControlRigidBody { Name = "ThrownBody", Position = _baseCore,
            CollisionLayer = 0, CollisionMask = 5, Freeze = true, Mass = 1f, CustomIntegrator = true,
            ContactMonitor = true, MaxContactsReported = 16,
            GravityScale = FreeControlMotor.Gravity / ProjectSettings.GetSetting("physics/2d/default_gravity", 980f).AsSingle(),
            LinearDamp = .3f, AngularDamp = .25f, ContinuousCd = RigidBody2D.CcdMode.CastShape,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = .65f, Bounce = .25f } };
        _walkShape = new CollisionShape2D { Shape = new ConvexPolygonShape2D { Points = _localHull } };
        _ragShape = new CollisionShape2D { Shape = new ConvexPolygonShape2D { Points = _localHull }, Disabled = true };
        _walker.AddChild(_walkShape); _ragdoll.AddChild(_ragShape);
        _world.AddChild(_walker); _world.AddChild(_ragdoll);
        AddBoundary("Floor", new(_arena.GetCenter().X, floor + 50f), new(_arena.Size.X + 200f, 100f));
        AddBoundary("Ceiling", new(_arena.GetCenter().X, _arena.Position.Y - 50f), new(_arena.Size.X + 200f, 100f));
        AddBoundary("LeftWall", new(_arena.Position.X - 50f, _arena.GetCenter().Y), new(100f, _arena.Size.Y + 200f));
        AddBoundary("RightWall", new(_arena.End.X + 50f, _arena.GetCenter().Y), new(100f, _arena.Size.Y + 200f));
        _mouseBody = new StaticBody2D { Name = "MouseAnchor", CollisionLayer = 0, CollisionMask = 0 };
        _world.AddChild(_mouseBody);
        _motor.Reset();
        _standUp = _standFrom = _held = 0f;
        _lastThrow = -10f;
        _tornadoTriggered = false;
        _ragging = _paused = false;
        Active = true; Generation++;
        SetPhysicsProcess(true);
        _blur = new FreeControlMotionBlur { Name = "FreeRotationExposure", Visible = false };
        Actor.Visuals.AddChild(_blur);
    }

    private void AddBoundary(string name, Vector2 position, Vector2 size)
    {
        var wall = new StaticBody2D { Name = name, Position = position, CollisionLayer = 1, CollisionMask = 2 };
        wall.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });
        _world!.AddChild(wall);
    }

    private void ReadHull()
    {
        Sprite2D source = NinjaSlayerVisualRig.GetBodySprite(Actor.Visuals)!;
        var overlay = Pose.GetNode<NarakuVisualOverlay>("NarakuVisualOverlay");
        _body = overlay.Visible ? overlay : source;
        bool full = NinjaSlayerFormState.GetPresentation(Actor.Entity).Kind == NinjaSlayerFormKind.FullyReleasedNaraku;
        var contour = full ? CombatBodyContours.FullyReleasedNaraku : CombatBodyContours.NinjaSlayer;
        Transform2D bodyWorld = _spaceToCanvas.AffineInverse() * _body.GetGlobalTransformWithCanvas();
        Transform2D inverse = Active ? PhysicsTransform.AffineInverse() : new Transform2D(0f, -_baseCore);
        _worldHull = new Vector2[contour.Length];
        _localHull = new Vector2[contour.Length];
        for (int i = 0; i < contour.Length; i++)
        {
            Vector2 pixel = new(contour[i].X * (_body.FlipH ? -1f : 1f), contour[i].Y * (_body.FlipV ? -1f : 1f));
            if (!_body.Centered) pixel += _body.Texture.GetSize() * .5f;
            _worldHull[i] = bodyWorld * (pixel + _body.Offset);
            _localHull[i] = inverse * _worldHull[i];
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active || _paused || _exclusiveDepth > 0 || Engine.TimeScale <= 0d) return;
        float dt = (float)(delta / Engine.TimeScale);
        if (_ragging)
        {
            if (_joint != null) _mouseBody.Position = _mouseBody.Position.MoveToward(PointerWorld(), 2400f * dt);
            else if (_left || _right || _jumpPressed || _dashPressed) ResumeWalking();
        }
        if (!_ragging)
        {
            float axis = (_right ? 1f : 0f) - (_left ? 1f : 0f);
            if (axis != 0f) _facing = axis;
            _motor.Velocity = new(_walker.Velocity.X, _walker.Velocity.Y);
            _motor.Step(dt, axis, _jumpPressed, _jumpHeld, _dashPressed, _down,
                _walker.IsOnFloor(), _walker.IsOnWall() ? _walker.GetWallNormal().X : 0f, _facing);
            _walker.Velocity = new(_motor.Velocity.X, _motor.Velocity.Y);
            if (_standUp > 0f)
            {
                _standUp = Math.Max(0f, _standUp - dt);
                _walker.Rotation = _standFrom * Mathf.SmoothStep(0f, 1f, _standUp / .18f);
            }
            _walker.Velocity /= (float)Engine.TimeScale;
            _walker.MoveAndSlide();
            _walker.Velocity *= (float)Engine.TimeScale;
            if (axis != 0f && !Pose.IsBusy) NinjaSlayerFacingState.SetFacing(Actor, axis < 0f);
        }
        _jumpPressed = _dashPressed = false;
        Pose.SyncNow();
        ReadHull();
        ((ConvexPolygonShape2D)_walkShape.Shape).Points = _localHull;
        ((ConvexPolygonShape2D)_ragShape.Shape).Points = _localHull;
        UpdateEnemyBodies();
        DetectHits(dt);
    }

    internal void Compose(ref Vector2 coreCanvas)
    {
        if (!Active || _exclusiveDepth > 0) return;
        _spaceToCanvas = Actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas();
        _baselinePose = Pose.Transform;
        Transform2D free = PhysicsTransform * new Transform2D(0f, -_baseCore);
        Transform2D freeCanvas = _spaceToCanvas * free * _spaceToCanvas.AffineInverse();
        Transform2D parent = Pose.GetParent<CanvasItem>().GetGlobalTransformWithCanvas();
        Pose.Transform = parent.AffineInverse() * freeCanvas * parent * Pose.Transform;
        coreCanvas = freeCanvas * coreCanvas;
        _blur?.Record(_body, PhysicsTransform.Origin, _spaceToCanvas, _time, Velocity,
            _ragging ? _ragdoll.RealSpin : 0f, _ragging);
    }

    internal Vector2 UntransformTarget(Vector2 canvas) => !Active || _exclusiveDepth > 0 ? canvas
        : _spaceToCanvas * (PhysicsTransform * new Transform2D(0f, -_baseCore)).AffineInverse()
            * (_spaceToCanvas.AffineInverse() * canvas);
    internal Vector2 UnrotateTravel(Vector2 travel) => Active && _exclusiveDepth == 0
        ? travel.Rotated(-PhysicsTransform.Rotation) : travel;
    internal float Altitude => Active && _exclusiveDepth == 0 ? Math.Max(0f, _baseCore.Y - PhysicsTransform.Origin.Y) : 0f;

    private void Grab()
    {
        if (_joint != null) return;
        if (!_ragging)
        {
            _ragdoll.Transform = _walker.Transform;
            _ragdoll.RealVelocity = _walker.Velocity;
            _ragdoll.RealSpin = 0f;
            _walkShape.Disabled = true;
            _ragShape.Disabled = false;
            _ragdoll.CollisionLayer = 2;
            _ragdoll.Freeze = false;
            _ragging = true;
        }
        _mouseBody.Position = PhysicsTransform * _grabLocal;
        _joint = new PinJoint2D { Name = "HandGrip", Position = _mouseBody.Position, Softness = 0f };
        _world!.AddChild(_joint);
        _joint.NodeA = _mouseBody.GetPath();
        _joint.NodeB = _ragdoll.GetPath();
    }

    private void ReleaseGrip()
    {
        _joint?.QueueFree();
        _joint = null;
    }

    private void ResumeWalking()
    {
        _walker.Transform = _ragdoll.Transform;
        _walker.Velocity = _ragdoll.RealVelocity;
        _standFrom = Mathf.Wrap(_walker.Rotation, -Mathf.Pi, Mathf.Pi);
        _standUp = .18f;
        _ragdoll.Freeze = true;
        _ragdoll.CollisionLayer = 0;
        _ragShape.Disabled = true;
        _walkShape.Disabled = false;
        _ragging = false;
        _motor.Reset(new(_walker.Velocity.X, _walker.Velocity.Y));
    }

    private Vector2 PointerWorld() => _spaceToCanvas.AffineInverse() * GetViewport().GetMousePosition();

    public override void _Input(InputEvent input)
    {
        if (!Active || _paused || _exclusiveDepth > 0 || IsBlocked()) return;
        bool cardInput = IsCardInputActive();
        if (input is InputEventKey key && GetViewport().GuiGetFocusOwner() is not LineEdit and not TextEdit)
        {
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
        if (input is not InputEventMouseButton mouse) return;
        if (mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed && _mouseDown)
        {
            if (!_pressOnBody && !_tornadoTriggered && !cardInput) BeginAttack(FreeControlMotor.AttackTier(_held));
            _mouseDown = false;
            ReleaseGrip();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!mouse.Pressed || cardInput || IsPointerOverUi()) return;
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            ReadHull();
            _pressPoint = PointerWorld();
            _pressOnBody = Geometry2D.IsPointInPolygon(_pressPoint, _worldHull);
            _grabLocal = PhysicsTransform.AffineInverse() * _pressPoint;
            if (!_pressOnBody && IsPointerOverEnemy()) return;
            _mouseDown = true; _held = 0f; _tornadoTriggered = false;
            GetViewport().SetInputAsHandled();
        }
        else if (mouse.ButtonIndex == MouseButton.Right)
        {
            BeginThrow();
            GetViewport().SetInputAsHandled();
        }
    }

    private bool IsPointerOverEnemy() => Actor.Entity.CombatState?.HittableEnemies.Any(enemy =>
        enemy.GetCreatureNode() is { } node && new Rect2(Vector2.Zero, node.Hitbox.Size).HasPoint(
            node.Hitbox.GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition())) == true;

    private bool IsPointerOverUi()
    {
        for (Node? n = GetViewport().GuiGetHoveredControl(); n != null; n = n.GetParent())
        {
            if (n is NCreature) return false;
            if (n is NCombatRoom) return false;
            if (n is BaseButton || n.GetType().Namespace?.Contains("Cards", StringComparison.Ordinal) == true
                || n.GetType().Name.Contains("Potion", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private bool IsCardInputActive() => Pose.HasCardDrag || NCombatRoom.Instance?.Ui?.Hand?.InCardPlay == true
        || NCombatRoom.Instance?.Ui?.Hand?.IsInCardSelection == true;

    private void ClearInput()
    {
        _left = _right = _down = _jumpHeld = _jumpPressed = _dashPressed = _mouseDown = false;
        _chargeMotion?.Dispose(); _chargeMotion = null;
        ReleaseGrip();
    }

    internal void EndTurn()
    {
        _endedTurn = Actor.Entity.Player?.PlayerCombatState?.TurnNumber ?? -1;
        Stop();
    }

    internal CinematicLease? SuspendForCinematic(Vector2 authoredRoot)
    {
        if (!Active || _exclusiveDepth > 0) return null;
        Pose.SyncNow();
        Vector2 translation = PhysicsTransform.Origin - _baseCore;
        _exclusiveDepth++;
        if (_blur != null) _blur.Visible = false;
        ClearInput();
        ClearAttacks();
        _savedRagdollVelocity = _ragdoll.RealVelocity;
        _savedRagdollSpin = _ragdoll.RealSpin;
        _ragdoll.Freeze = true;
        Actor.Position += translation;
        Pose.SyncNow();
        return new CinematicLease(this, authoredRoot, translation);
    }

    internal sealed class CinematicLease(NinjaSlayerFreeControl owner, Vector2 authored, Vector2 translation) : IDisposable
    {
        internal Vector2 Baseline => authored + translation;
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed || !GodotObject.IsInstanceValid(owner)) return;
            _disposed = true;
            owner._exclusiveDepth--;
            if (GodotObject.IsInstanceValid(owner.Actor)) owner.Actor.Position = authored;
            if (owner.Active)
            {
                owner._ragdoll.RealVelocity = owner._savedRagdollVelocity;
                owner._ragdoll.RealSpin = owner._savedRagdollSpin;
                owner._ragdoll.Freeze = !owner._ragging || owner._paused;
                owner.Pose.SyncNow();
            }
        }
    }

    internal void Stop()
    {
        if (!Active) return;
        Active = false; Generation++;
        ClearInput();
        ClearAttacks();
        _world?.QueueFree(); _world = null;
        _enemyBodies.Clear();
        _blur?.QueueFree(); _blur = null;
        SetPhysicsProcess(false);
        if (GodotObject.IsInstanceValid(Pose)) { Pose.Transform = _baselinePose; Pose.SyncNow(); }
    }

    public override void _ExitTree() => Stop();
}

internal sealed partial class FreeControlRigidBody : RigidBody2D
{
    private static float TimeScale => Math.Max(.001f, (float)Engine.TimeScale);
    internal Vector2 RealVelocity { get => LinearVelocity * TimeScale; set { _previousScale = TimeScale; LinearVelocity = value / TimeScale; } }
    internal float RealSpin { get => AngularVelocity * TimeScale; set { _previousScale = TimeScale; AngularVelocity = value / TimeScale; } }
    private float _previousScale = 1f;
    internal Vector2 TravelVelocity { get; private set; }
    internal float TravelSpin { get; private set; }
    internal readonly Dictionary<ulong, (Vector2 Point, Vector2 Velocity)> Contacts = [];
    public override void _IntegrateForces(PhysicsDirectBodyState2D state)
    {
        Contacts.Clear();
        Vector2 massCenter = state.Transform * state.CenterOfMassLocal;
        for (int i = 0; i < state.GetContactCount(); i++)
        {
            Vector2 point = state.GetContactLocalPosition(i), radius = point - massCenter;
            Contacts[state.GetContactColliderId(i)] = (point,
                TravelVelocity + new Vector2(-radius.Y, radius.X) * TravelSpin);
        }
        float scale = TimeScale, dt = state.Step / scale;
        Vector2 velocity = state.LinearVelocity * _previousScale;
        velocity.Y += FreeControlMotor.Gravity * dt;
        state.LinearVelocity = (velocity * MathF.Exp(-.3f * dt)).LimitLength(2400f) / scale;
        state.AngularVelocity = Mathf.Clamp(state.AngularVelocity * _previousScale * MathF.Exp(-.25f * dt),
            -Mathf.Tau * 20f, Mathf.Tau * 20f) / scale;
        TravelVelocity = state.LinearVelocity * scale;
        TravelSpin = state.AngularVelocity * scale;
        _previousScale = scale;
    }
}
