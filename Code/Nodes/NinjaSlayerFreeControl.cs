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
using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class NinjaSlayerFreeControl : Node
{
    private static RunSavedData<FreeControlRunData> _usedInRun = null!;

    internal static void RegisterSavedData(string modId) =>
        _usedInRun = RitsuLibFramework.GetRunSavedDataStore(modId).Register<FreeControlRunData>("free_control_used",
            options: new RunSavedDataOptions { WritePolicy = RunSavedDataWritePolicy.WhenNonDefault });

    internal static bool WasUsed(RunState run) => _usedInRun.Get(run).Used;

    internal NCreature Actor = null!;
    internal NinjaSlayerAimPose Pose = null!;
    internal bool Active { get; private set; }
    internal int Generation { get; private set; }
    private readonly FreeControlMotor _motor = new();
    private FreeControlPhysics _physics = null!;
    private double _physicsRemainder;
    private Transform2D _spaceToCanvas, _baselinePose;
    private Vector2 _baseCore;
    private Rect2 _arena;
    private Vector2[] _localHull = [];
    private Vector2[] _worldHull = [];
    private bool _paused, _left, _right, _down, _jumpHeld, _jumpPressed, _dashPressed;
    private bool _ragging => Active && _physics.Ragging;
    private bool _pressOnBody, _mouseDown, _tornadoTriggered;
    private Vector2 _pressPoint, _grabLocal;
    private float _held, _facing = 1f, _standUp, _standFrom, _time;
    private int _endedTurn = -1, _exclusiveDepth;
    private Sprite2D _body = null!;
    private FreeControlMotionBlur? _blur;

    internal static NinjaSlayerFreeControl? Get(Creature creature) => NinjaSlayerAimPose.Get(creature)?.FreeControl;
    private Transform2D PhysicsTransform => new(_physics.Rotation, ToGodot(_physics.Position));
    private Vector2 Velocity => ToGodot(_physics.Velocity);
    private static System.Numerics.Vector2 ToPhysics(Vector2 value) => new(value.X, value.Y);
    private static Vector2 ToGodot(System.Numerics.Vector2 value) => new(value.X, value.Y);
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
            _physicsRemainder = 0d;
            PauseAttacks(paused);
        }
        if (!paused && Engine.TimeScale > 0d)
        {
            float dt = (float)(delta / Engine.TimeScale);
            _time += dt;
            AdvanceAttacks(dt);
            if (_mouseDown && !_physics.Grabbing && (IsCardInputActive() || !CanUsePointer())) CancelMouseGesture();
            if (_mouseDown)
            {
                _held += dt;
                if (_pressOnBody && !_physics.Grabbing && (_held >= .15f || PointerWorld().DistanceTo(_pressPoint) > 4f)) Grab();
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
        _usedInRun.Modify((RunState)Actor.Entity.Player!.RunState, data => data.Used = true);
        Pose.SyncNow();
        _spaceToCanvas = Actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas();
        _baseCore = _spaceToCanvas.AffineInverse() * Pose.CoreCanvas;
        ReadHull();
        Rect2 visible = GetViewport().GetVisibleRect();
        Vector2 topLeft = _spaceToCanvas.AffineInverse() * visible.Position;
        Vector2 bottomRight = _spaceToCanvas.AffineInverse() * visible.End;
        float floor = _worldHull.Max(p => p.Y);
        _arena = new(topLeft, new Vector2(bottomRight.X - topLeft.X, floor - topLeft.Y));
        _physics = new(ToPhysics(_baseCore), ToPhysics(_arena.Position), ToPhysics(_arena.End), _localHull.Select(ToPhysics).ToArray());
        _physicsRemainder = 0d;
        _motor.Reset();
        _standUp = _standFrom = _held = 0f;
        _lastThrow = -10f;
        _tornadoTriggered = false;
        _paused = false;
        Active = true; Generation++;
        ConnectPointerSurfaces();
        SetPhysicsProcess(true);
        _blur = new FreeControlMotionBlur { Name = "FreeRotationExposure", Visible = false };
        Actor.Visuals.AddChild(_blur);
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
        if (!Active || _paused || _exclusiveDepth > 0 || Engine.TimeScale <= 0d || IsBlocked())
        { _physicsRemainder = 0d; return; }
        _physicsRemainder += delta / Engine.TimeScale;
        while (_physicsRemainder + 1e-9 >= FreeControlPhysics.StepSeconds)
        {
            _physicsRemainder -= FreeControlPhysics.StepSeconds;
            StepPhysics();
        }
    }

    private void StepPhysics()
    {
        const float dt = FreeControlPhysics.StepSeconds;
        if (_ragging)
        {
            if (!_physics.Grabbing && (_left || _right || _jumpPressed || _dashPressed)) ResumeWalking();
        }
        if (!_ragging)
        {
            float axis = (_right ? 1f : 0f) - (_left ? 1f : 0f);
            if (axis != 0f) _facing = axis;
            _motor.Velocity = _physics.Velocity;
            _motor.Step(dt, axis, _jumpPressed, _jumpHeld, _dashPressed, _down,
                _physics.Grounded, _physics.WallNormal, _facing);
            _physics.Velocity = _motor.Velocity;
            if (_standUp > 0f)
            {
                _standUp = Math.Max(0f, _standUp - dt);
                _physics.SetTransform(_physics.Position, _standFrom * Mathf.SmoothStep(0f, 1f, _standUp / .18f));
            }
            if (axis != 0f && !Pose.IsBusy) NinjaSlayerFacingState.SetFacing(Actor, axis < 0f);
        }
        _jumpPressed = _dashPressed = false;
        Pose.SyncNow();
        ReadHull();
        _physics.UpdateHull(_localHull.Select(ToPhysics).ToArray());
        UpdateEnemyBodies();
        _physics.Step(ToPhysics(PointerWorld()));
        Pose.SyncNow();
        ReadHull();
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
            _ragging ? _physics.AngularVelocity : 0f, _ragging);
    }

    internal Vector2 UntransformTarget(Vector2 canvas) => !Active || _exclusiveDepth > 0 ? canvas
        : _spaceToCanvas * (PhysicsTransform * new Transform2D(0f, -_baseCore)).AffineInverse()
            * (_spaceToCanvas.AffineInverse() * canvas);
    internal Vector2 UnrotateTravel(Vector2 travel) => Active && _exclusiveDepth == 0
        ? travel.Rotated(-PhysicsTransform.Rotation) : travel;
    internal float Altitude => Active && _exclusiveDepth == 0 ? Math.Max(0f, _baseCore.Y - PhysicsTransform.Origin.Y) : 0f;

    private void Grab()
    {
        _physics.Grab(ToPhysics(_grabLocal));
    }

    private void ReleaseGrip()
    {
        _physics.Release();
    }

    private void ResumeWalking()
    {
        _standFrom = Mathf.Wrap(_physics.Rotation, -Mathf.Pi, Mathf.Pi);
        _standUp = .18f;
        _physics.ResumeWalking();
        _motor.Reset(_physics.Velocity);
    }

    private void ClearInput()
    {
        _left = _right = _down = _jumpHeld = _jumpPressed = _dashPressed = false;
        CancelMouseGesture();
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
        _physicsRemainder = 0d;
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
        DisconnectPointerSurfaces();
        _physics.Dispose();
        _blur?.QueueFree(); _blur = null;
        SetPhysicsProcess(false);
        if (GodotObject.IsInstanceValid(Pose)) { Pose.Transform = _baselinePose; Pose.SyncNow(); }
    }

    public override void _ExitTree() => Stop();
}

public sealed class FreeControlRunData
{
    public bool Used { get; set; }
}
