using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class NinjaSlayerFreeControl
{
    private readonly List<FreeStrike> _strikes = [];
    private readonly List<FreeProjectile> _projectiles = [];
    private readonly Dictionary<Creature, float> _hitTimes = [];
    private readonly Dictionary<Creature, Vector2> _enemyPositions = [];
    private readonly List<Node2D> _effects = [];
    private readonly List<NinjaSlayerAimPose.VisualMotion> _throwMotions = [];
    private NinjaSlayerAimPose.VisualMotion? _chargeMotion;
    private VerticalAxisSpinProjection? _freeSpin;
    private float _spinTime, _freeSpinDegrees, _lastThrow = -10f;
    private Transform2D _previousPhysics;
    private bool _hasPreviousPhysics;

    private sealed class FreeStrike(int tier, NinjaSlayerAimPose.VisualMotion motion, Vector2 direction)
    {
        internal readonly int Tier = tier;
        internal readonly NinjaSlayerAimPose.VisualMotion Motion = motion;
        internal readonly Vector2 Direction = direction;
        internal readonly HashSet<Creature> Hit = [];
        internal float Elapsed;
    }

    private sealed class FreeProjectile(Vector2 aimPoint)
    {
        internal readonly Vector2 AimPoint = aimPoint;
        internal Vector2 Direction;
        internal Vector2 Origin, End, Previous;
        internal float Elapsed;
        internal bool Released;
        internal FreeShurikenEffect? Effect;
    }

    private Vector2 ToMotionOffset(Vector2 offset) => offset.Rotated(-PhysicsTransform.Rotation);

    private void BeginAttack(int tier)
    {
        _chargeMotion?.Dispose(); _chargeMotion = null;
        Vector2 direction = PointerWorld() - _spaceToCanvas.AffineInverse() * Pose.CoreCanvas;
        direction = direction.LengthSquared() > 1f ? direction.Normalized() : new(_facing, 0f);
        _facing = direction.X < 0f ? -1f : 1f;
        if (!Pose.IsBusy) NinjaSlayerFacingState.SetFacing(Actor, _facing < 0f);
        float duration = tier == 2 ? .8f : (tier == 0 ? .15f : .2f) + .2f;
        var motion = Pose.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Offset, duration);
        if (motion == null) return;
        motion.Unscaled = true;
        _strikes.Add(new(tier, motion, direction));
        if (tier == 2)
        {
            _spinTime = .8f;
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiLongjuanquanEvent);
        }
        else NinjaSlayerCombatAudioSet.Play(tier == 0 ? NinjaSlayerAudio.NinjaSlayerFastAttackEvent
            : NinjaSlayerAudio.NinjaSlayerSlowAttackEvent);
    }

    private void BeginThrow()
    {
        if (_time - _lastThrow < .167f) return;
        _lastThrow = _time;
        if (Pose.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Throw, .167f) is { } motion)
        {
            motion.Unscaled = true;
            _throwMotions.Add(motion);
        }
        _projectiles.Add(new(PointerWorld()));
    }

    private void AdvanceAttacks(float dt)
    {
        if (_mouseDown && !_pressOnBody && !_tornadoTriggered && !Pose.IsExclusive)
        {
            _chargeMotion ??= Pose.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Offset, float.MaxValue);
            if (_chargeMotion != null)
            {
                var charge = Pose.ChargeProfile.Sample(_held);
                _chargeMotion.Offset = ToMotionOffset(new(-_facing * charge.Back, 0f));
                _chargeMotion.Scale = new(charge.Scale.X, charge.Scale.Y);
                _chargeMotion.Unscaled = true;
            }
        }
        else { _chargeMotion?.Dispose(); _chargeMotion = null; }
        foreach (FreeStrike strike in _strikes.ToArray())
        {
            strike.Elapsed += dt;
            if (!strike.Motion.Active || strike.Elapsed >= strike.Motion.Duration)
            {
                strike.Motion.Dispose(); _strikes.Remove(strike); continue;
            }
            if (strike.Tier == 2) continue;
            float peak = strike.Tier == 0 ? .15f : .2f;
            float p = strike.Elapsed / peak;
            float envelope = p < 1f ? (strike.Tier == 0 ? FinisherActionTrajectory.FastProgress(p)
                : FinisherActionTrajectory.SlowProgress(p))
                : 1f - Mathf.SmoothStep(0f, 1f, (strike.Elapsed - peak) / .2f);
            strike.Motion.Offset = ToMotionOffset(strike.Direction * (strike.Tier == 0 ? 90f : 120f) * envelope);
            strike.Motion.Radians = Mathf.Wrap(strike.Direction.Angle() - (_facing < 0f ? Mathf.Pi : 0f), -Mathf.Pi, Mathf.Pi) * envelope;
        }
        if (_spinTime > 0f)
        {
            _spinTime = Math.Max(0f, _spinTime - dt);
            float before = _freeSpinDegrees;
            float age = .8f - _spinTime, braking = Math.Max(0f, age - .7f);
            _freeSpinDegrees = 12000f * (age - 5f * braking * braking);
            if (!Pose.OwnsSpin && !SoarSpinAnimation.IsVerticalSpinActive(Actor.Entity))
            {
                Sprite2D body = NinjaSlayerVisualRig.GetBodySprite(Actor.Visuals)!;
                _freeSpin ??= VerticalAxisSpinProjection.CaptureCurrent(body, Pose.CoreCanvas.X, Pose.CoreCanvas);
                _freeSpin.ApplyDegrees(_freeSpinDegrees, age => before + Math.Max(0f, dt - (float)age) * 12000f);
            }
        }
        if (_spinTime <= 0f || Pose.OwnsSpin || SoarSpinAnimation.IsVerticalSpinActive(Actor.Entity))
        {
            ReleaseSpinProjection();
        }
        foreach (FreeProjectile shot in _projectiles.ToArray())
        {
            shot.Elapsed += dt;
            if (!shot.Released && shot.Elapsed >= .083f) ReleaseProjectile(shot);
        }
        _effects.RemoveAll(effect => !GodotObject.IsInstanceValid(effect) || effect.IsQueuedForDeletion());
        _throwMotions.RemoveAll(motion => !motion.Active);
    }

    private void ReleaseProjectile(FreeProjectile shot)
    {
        Pose.SyncNow();
        Vector2 hand = ShurikenOrbVisual.TryGetHandCanvasPosition(Actor, out Vector2 position) ? position : Pose.CoreCanvas;
        shot.Origin = shot.Previous = _spaceToCanvas.AffineInverse() * hand;
        shot.Direction = (shot.AimPoint - shot.Origin).Normalized();
        if (shot.Direction.IsZeroApprox()) shot.Direction = Vector2.Right * _facing;
        shot.End = shot.Origin + shot.Direction * 4000f;
        // A ray fixes only the flight endpoint, never bends the shot toward an enemy.
        foreach (Vector2[] polygon in EnemyPolygons().Select(pair => pair.Polygon)
                     .Append([_arena.Position, new(_arena.End.X, _arena.Position.Y), _arena.End, new(_arena.Position.X, _arena.End.Y)]))
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                Variant point = Geometry2D.SegmentIntersectsSegment(shot.Origin, shot.End, polygon[i], polygon[(i + 1) % polygon.Length]);
                if (point.VariantType == Variant.Type.Vector2 && shot.Origin.DistanceSquaredTo(point.AsVector2()) < shot.Origin.DistanceSquaredTo(shot.End))
                    shot.End = point.AsVector2() + shot.Direction * 1f;
            }
        }
        shot.Released = true;
        NDebugAudioManager.Instance?.Play(TmpSfx.daggerThrow);
        Transform2D toVfx = Actor.GetCanvasTransform().AffineInverse() * _spaceToCanvas;
        Vector2 origin = toVfx * shot.Origin, end = toVfx * shot.End;
        NShivThrowVfx? authored = ShurikenCombat.CreateFreeThrowVfx(origin, end);
        if (authored != null && NCombatRoom.Instance is { } room)
        {
            FreeShurikenEffect effect = FreeShurikenEffect.TakeParticles(authored, origin.DistanceTo(end));
            room.CombatVfxContainer.AddChild(effect);
            shot.Effect = effect; _effects.Add(effect);
        }
    }

    private IEnumerable<(Creature Creature, Vector2[] Polygon)> EnemyPolygons()
    {
        foreach (Creature enemy in Actor.Entity.CombatState?.HittableEnemies ?? [])
        {
            if (!enemy.IsAlive || !enemy.IsHittable || enemy.GetCreatureNode() is not { } node) continue;
            Transform2D transform = _spaceToCanvas.AffineInverse() * node.Hitbox.GetGlobalTransformWithCanvas();
            Vector2 size = node.Hitbox.Size;
            yield return (enemy, [transform * Vector2.Zero, transform * new Vector2(size.X, 0f),
                transform * size, transform * new Vector2(0f, size.Y)]);
        }
    }

    private void UpdateEnemyBodies()
    {
        _physics.UpdateEnemies(EnemyPolygons().Select(pair =>
            (pair.Creature.GetCreatureNode()!.GetInstanceId(), pair.Polygon.Select(ToPhysics).ToArray())));
    }

    private void DetectHits(float dt)
    {
        var hits = new Dictionary<Creature, int>();
        Transform2D current = PhysicsTransform;
        float angular = _ragging ? _physics.AngularVelocity : 0f;
        int samples = Math.Clamp((int)MathF.Ceiling(Math.Max(Math.Abs(angular * dt) / Mathf.DegToRad(10f),
            Velocity.Length() * dt / 16f)), 1, 32);
        foreach ((Creature enemy, Vector2[] polygon) in EnemyPolygons())
        {
            Vector2 enemyCenter = (polygon[0] + polygon[2]) * .5f;
            Vector2 enemyVelocity = _enemyPositions.TryGetValue(enemy, out Vector2 previous) ? (enemyCenter - previous) / dt : Vector2.Zero;
            _enemyPositions[enemy] = enemyCenter;
            (System.Numerics.Vector2 Point, System.Numerics.Vector2 Velocity) physicalContact = default;
            bool rigidContact = _ragging && _physics.Contacts.TryGetValue(enemy.GetCreatureNode()!.GetInstanceId(), out physicalContact);
            bool contact = rigidContact;
            Vector2 contactPoint = rigidContact ? ToGodot(physicalContact.Point) : enemyCenter;
            for (int sample = 0; !rigidContact && sample <= samples; sample++)
            {
                float p = (float)sample / samples;
                Transform2D frame = new(current.Rotation - angular * dt * (1f - p),
                    _hasPreviousPhysics ? _previousPhysics.Origin.Lerp(current.Origin, p) : current.Origin);
                Vector2[] shape = _localHull.Select(point => frame * point).ToArray();
                var overlaps = Geometry2D.IntersectPolygons(shape, polygon);
                if (overlaps.Count == 0 || overlaps[0].Length == 0) continue;
                contact = true;
                contactPoint = overlaps[0].Aggregate(Vector2.Zero, (sum, point) => sum + point) / overlaps[0].Length;
                break;
            }
            if (contact && (!_hitTimes.TryGetValue(enemy, out float last) || _time - last >= .15f))
            {
                Vector2 radius = contactPoint - current.Origin;
                Vector2 velocity = (rigidContact ? ToGodot(physicalContact.Velocity)
                    : Velocity + new Vector2(-radius.Y, radius.X) * angular) - enemyVelocity;
                int damage = FreeControlMotor.CollisionDamage(velocity.Length());
                foreach (FreeStrike strike in _strikes)
                {
                    if (strike.Tier != 2 && strike.Hit.Contains(enemy)) continue;
                    damage = Math.Max(damage, strike.Tier == 0 ? 6 : strike.Tier == 1 ? 12 : 4);
                    strike.Hit.Add(enemy);
                }
                if (damage > 0) { hits[enemy] = damage; _hitTimes[enemy] = _time; }
            }
        }
        foreach (FreeProjectile shot in _projectiles.ToArray())
        {
            if (!shot.Released) continue;
            Vector2 next = shot.Origin.Lerp(shot.End, Mathf.Clamp((shot.Elapsed - .083f) / .15f, 0f, 1f));
            Creature? first = null;
            Vector2 impact = next;
            float nearest = float.PositiveInfinity;
            foreach ((Creature enemy, Vector2[] polygon) in EnemyPolygons())
            {
                Vector2? point = Geometry2D.IsPointInPolygon(shot.Previous, polygon) ? shot.Previous : null;
                for (int i = 0; i < polygon.Length; i++)
                {
                    Variant crossing = Geometry2D.SegmentIntersectsSegment(shot.Previous, next, polygon[i], polygon[(i + 1) % polygon.Length]);
                    if (crossing.VariantType == Variant.Type.Vector2 && (point == null
                        || shot.Previous.DistanceSquaredTo(crossing.AsVector2()) < shot.Previous.DistanceSquaredTo(point.Value)))
                        point = crossing.AsVector2();
                }
                if (point == null || shot.Previous.DistanceSquaredTo(point.Value) >= nearest) continue;
                first = enemy; impact = point.Value; nearest = shot.Previous.DistanceSquaredTo(impact);
            }
            if (first != null)
            {
                hits[first] = hits.GetValueOrDefault(first) + 6;
                shot.Effect?.Hit(Actor.GetCanvasTransform().AffineInverse() * (_spaceToCanvas * impact));
                _projectiles.Remove(shot);
            }
            shot.Previous = next;
            if (shot.Elapsed >= .233f) _projectiles.Remove(shot);
        }
        _previousPhysics = current; _hasPreviousPhysics = true;
        if (hits.Count > 0 && RunManager.Instance.NetService?.Type == NetGameType.Singleplayer)
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new FreeControlImpactAction(this, Generation, hits));
    }

    private void ClearAttacks()
    {
        _chargeMotion?.Dispose(); _chargeMotion = null;
        foreach (FreeStrike strike in _strikes) strike.Motion.Dispose();
        _strikes.Clear(); _projectiles.Clear(); _hitTimes.Clear(); _enemyPositions.Clear();
        foreach (var motion in _throwMotions) motion.Dispose();
        _throwMotions.Clear();
        _hasPreviousPhysics = false;
        _freeSpin?.Restore(); _freeSpin = null; _spinTime = 0f;
        foreach (Node2D effect in _effects) if (GodotObject.IsInstanceValid(effect)) effect.QueueFree();
        _effects.Clear();
    }

    private void PauseAttacks(bool paused)
    {
        foreach (FreeStrike strike in _strikes) strike.Motion.Paused = paused;
        foreach (var motion in _throwMotions) motion.Paused = paused;
        foreach (Node2D effect in _effects)
            if (GodotObject.IsInstanceValid(effect)) effect.ProcessMode = paused ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
    }

    internal void ReleaseSpinProjection() { _freeSpin?.Restore(); _freeSpin = null; }

    internal float AdditionalYaw(VerticalAxisSpinProjection projection) => Active && _spinTime > 0f
        && !ReferenceEquals(projection, _freeSpin) ? _freeSpinDegrees : 0f;

    private sealed class FreeControlImpactAction(NinjaSlayerFreeControl controller, int generation,
        Dictionary<Creature, int> hits) : GameAction
    {
        public override ulong OwnerId => controller.Actor.Entity.Player!.NetId;
        public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;
        public override bool RecordableToReplay => false;
        public override INetAction ToNetAction() => throw new InvalidOperationException("Free control is single-player only.");
        protected override async Task ExecuteAction()
        {
            var context = new GameActionPlayerChoiceContext(this);
            using var pacing = CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.ComboDamage);
            foreach ((Creature target, int damage) in hits)
            {
                if (!GodotObject.IsInstanceValid(controller) || !controller.Active || controller.Generation != generation
                    || !controller.InPlayPhase || !target.IsAlive || !target.IsHittable
                    || !ReferenceEquals(target.CombatState, controller.Actor.Entity.CombatState)) continue;
                await CreatureCmd.Damage(context, [target], damage, ValueProp.Move | ValueProp.Unpowered,
                    controller.Actor.Entity, null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                    , null
#endif
                );
            }
        }
    }
}
