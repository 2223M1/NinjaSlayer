using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public enum FinisherTargeting
{
    Single,
    All,
    Random
}

public sealed record FinisherAttackSpec(
    CardModel Card,
    CardPlay CardPlay,
    Func<Creature, decimal> Damage,
    ValueProp Props,
    int HitCount,
    FinisherTargeting Targeting)
{
    public static FinisherAttackSpec FromCard(
        CardModel card,
        CardPlay cardPlay,
        decimal? damageOverride = null,
        int? hitCountOverride = null,
        ValueProp? propsOverride = null)
    {
        Func<Creature, decimal> damage;
        ValueProp props;
        if (damageOverride.HasValue)
        {
            damage = _ => damageOverride.Value;
            props = propsOverride ?? ResolveProps(card);
        }
        else if (card.DynamicVars.TryGetValue(CalculatedDamageVar.defaultName, out DynamicVar? calculated)
            && calculated is CalculatedDamageVar calculatedDamage)
        {
            damage = target => calculatedDamage.Calculate(target);
            props = calculatedDamage.Props;
        }
        else
        {
            DamageVar damageVar = card.DynamicVars.Damage;
            damage = _ => damageVar.BaseValue;
            props = damageVar.Props;
        }

        FinisherTargeting targeting = card.TargetType switch
        {
            TargetType.AllEnemies => FinisherTargeting.All,
            TargetType.RandomEnemy => FinisherTargeting.Random,
            _ => FinisherTargeting.Single
        };
        int hitCount = hitCountOverride ?? KarateForecastCalculator.ResolveHitCount(card, cardPlay.Target);
        return new FinisherAttackSpec(card, cardPlay, damage, propsOverride ?? props, Math.Max(1, hitCount), targeting);
    }

    private static ValueProp ResolveProps(CardModel card)
    {
        return card.DynamicVars.TryGetValue(DamageVar.defaultName, out DynamicVar? damage)
            && damage is DamageVar damageVar
            ? damageVar.Props
            : ValueProp.Move;
    }
}

public static class NinjaSlayerFinisherCinematic
{
    private const float ImpactLeadSeconds = 0.04f;
    private const float HitStopSeconds = 0.1f;
    private const float ImpactRecoverySeconds = 0.1f;
    private const float FinisherSettleSeconds = 0.1f;
    private const float ReturnSeconds = 0.2f;
    private const float ApproachGap = 50f;
    private const float CameraPunchScaleMultiplier = 1.06f;
    private const float CameraPushPixels = 16f;
    private const float EnemyKnockbackPixels = 30f;
    private const float DeathNotificationTimeoutSeconds = 1f;

    private static FinisherSession? _active;

    public static bool IsMovementOwned(Creature creature) => _active?.Owner == creature;

    internal static void NotifyDeathAnimationStarted(NCreature creatureNode)
    {
        _active?.NotifyDeathAnimationStarted(creatureNode);
    }

    internal static bool TryProtectLethalDamage(Creature target, ref decimal amount)
    {
        return _active?.TryProtectLethalDamage(target, ref amount) == true;
    }

    internal static void SetSequenceHitIndex(int hitIndex, int totalHits)
    {
        _active?.SetHitIndex(hitIndex, totalHits);
    }

    public static async Task<AttackCommand> ExecuteWithFinisher(
        AttackCommand command,
        PlayerChoiceContext choiceContext,
        CardModel card,
        CardPlay cardPlay,
        decimal? damageOverride = null,
        int? hitCountOverride = null)
    {
        FinisherAttackSpec spec = FinisherAttackSpec.FromCard(
            card,
            cardPlay,
            damageOverride,
            hitCountOverride);
        if (!TryCreateSession(spec, command, out FinisherSession? session))
        {
            return await command.Execute(choiceContext);
        }

        ArgumentNullException.ThrowIfNull(session);
        await using (session)
        {
            await session.Begin();
            try
            {
                _active = session;
                command.BeforeDamage(() =>
                {
                    session.AdvanceHit();
                    return Task.CompletedTask;
                });
                AttackCommand result = await command.Execute(choiceContext);
                await session.CommitDeaths(choiceContext);
                return result;
            }
            finally
            {
                if (ReferenceEquals(_active, session))
                {
                    _active = null;
                }
            }
        }
    }

    public static async Task ExecuteSequenceWithFinisher(
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        Func<Task> sequence)
    {
        if (!TryCreateSession(spec, null, out FinisherSession? session))
        {
            await sequence();
            return;
        }

        ArgumentNullException.ThrowIfNull(session);
        await using (session)
        {
            await session.Begin();
            try
            {
                _active = session;
                await sequence();
                await session.CommitDeaths(choiceContext);
            }
            finally
            {
                if (ReferenceEquals(_active, session))
                {
                    _active = null;
                }
            }
        }
    }

    public static async Task ExecuteDirectWithFinisher(
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        Func<Task> damageAction)
    {
        if (!TryCreateSession(spec, null, out FinisherSession? session))
        {
            await damageAction();
            return;
        }

        ArgumentNullException.ThrowIfNull(session);
        await using (session)
        {
            await session.Begin();
            try
            {
                _active = session;
                session.SetHitIndex(0, 1);
                await damageAction();
                await session.CommitDeaths(choiceContext);
            }
            finally
            {
                if (ReferenceEquals(_active, session))
                {
                    _active = null;
                }
            }
        }
    }

    private static bool TryCreateSession(
        FinisherAttackSpec spec,
        AttackCommand? command,
        out FinisherSession? session)
    {
        session = null;
        if (_active != null
            || spec.Card is ShurikenCard or GiantShurikenCard
            || spec.Card.Owner?.Creature is not { } owner
            || owner.Player?.Character is not INinjaSlayerCharacter
            || owner.CombatState is not { } combatState
            || NCombatRoom.Instance is not { } room)
        {
            return false;
        }

        List<Creature> enemies = combatState.HittableEnemies.Where(enemy => enemy.IsAlive).ToList();
        if (enemies.Count == 0
            || !FinisherForecast.IsGuaranteedClear(owner, enemies, spec, command, out int resolvedHits))
        {
            return false;
        }

        NCreature? ownerNode = room.GetCreatureNode(owner);
        Creature? focus = enemies
            .Select(enemy => (Enemy: enemy, Node: room.GetCreatureNode(enemy)))
            .Where(pair => pair.Node != null)
            .OrderBy(pair => pair.Node!.GlobalPosition.X)
            .Select(pair => pair.Enemy)
            .FirstOrDefault();
        NCreature? focusNode = room.GetCreatureNode(focus);
        if (ownerNode == null || focus == null || focusNode == null
            || !CombatCinematicCameraLease.TryAcquire(room, "NinjaSlayer finisher", out CombatCinematicCameraLease? camera))
        {
            return false;
        }

        session = new FinisherSession(
            owner,
            ownerNode,
            focusNode,
            enemies,
            camera!,
            spec.Targeting == FinisherTargeting.Random,
            resolvedHits);
        return true;
    }

    private sealed class FinisherSession : IAsyncDisposable
    {
        private readonly NCreature _ownerNode;
        private readonly NCreature _focusNode;
        private readonly HashSet<Creature> _victims;
        private readonly HashSet<Creature> _deferredDeaths = [];
        private readonly List<NCreature> _deathNodes = [];
        private readonly CombatCinematicCameraLease _camera;
        private readonly NCombatRoom _room;
        private readonly Vector2 _ownerStartPosition;
        private readonly bool _commitAllGuaranteedVictims;
        private readonly TaskCompletionSource _deathStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ulong _lastFrameMsec;
        private ulong _lastDeltaFrame = ulong.MaxValue;
        private float _cachedFrameDelta;
        private bool _committing;
        private bool _disposed;
        private int _currentHitIndex = -1;
        private int _totalHits;

        public FinisherSession(
            Creature owner,
            NCreature ownerNode,
            NCreature focusNode,
            IEnumerable<Creature> victims,
            CombatCinematicCameraLease camera,
            bool commitAllGuaranteedVictims,
            int totalHits)
        {
            Owner = owner;
            _ownerNode = ownerNode;
            _focusNode = focusNode;
            _victims = victims.ToHashSet();
            _camera = camera;
            _commitAllGuaranteedVictims = commitAllGuaranteedVictims;
            _totalHits = Math.Max(1, totalHits);
            _room = NCombatRoom.Instance!;
            _ownerStartPosition = ownerNode.Position;
            _lastFrameMsec = Time.GetTicksMsec();
        }

        public Creature Owner { get; }

        public Task Begin()
        {
            Vector2 destination = ResolveApproachPosition(_ownerNode, _focusNode);
            CanvasItem focus = NinjaSlayerVisualRig.GetCinematicFocus(_ownerNode.Visuals) is { } cinematicFocus
                ? cinematicFocus
                : _ownerNode.Visuals.Bounds;
            float targetScale = _camera.BaselineScale.X * 2f;
            _ownerNode.Position = destination;
            _camera.FrameOn(focus, targetScale, clamp: true);
            return Task.CompletedTask;
        }

        public bool TryProtectLethalDamage(Creature target, ref decimal amount)
        {
            if (_committing
                || _currentHitIndex >= _totalHits - 1
                || !_victims.Contains(target)
                || amount < target.CurrentHp
                || target.CurrentHp <= 0)
            {
                return false;
            }

            _deferredDeaths.Add(target);
            if (target.CurrentHp == 1)
            {
                if (target.MaxHp > 1)
                {
                    target.SetCurrentHpInternal(2);
                    amount = 1m;
                }
                else
                {
                    amount = 0m;
                }
            }
            else
            {
                amount = target.CurrentHp - 1;
            }

            return true;
        }

        public void NotifyDeathAnimationStarted(NCreature creatureNode)
        {
            if (!_victims.Contains(creatureNode.Entity))
            {
                return;
            }

            if (!_deathNodes.Contains(creatureNode))
            {
                _deathNodes.Add(creatureNode);
            }

            _deathStarted.TrySetResult();
        }

        public void AdvanceHit()
        {
            _currentHitIndex++;
        }

        public void SetHitIndex(int hitIndex, int totalHits)
        {
            _currentHitIndex = hitIndex;
            _totalHits = Math.Max(1, totalHits);
        }

        public async Task CommitDeaths(PlayerChoiceContext choiceContext)
        {
            _committing = true;
            bool guaranteedClearMatchedRuntime = _victims.All(
                victim => victim.IsDead
                    || _deferredDeaths.Contains(victim)
                    || _commitAllGuaranteedVictims);
            IEnumerable<Creature> commitSet = _commitAllGuaranteedVictims ? _victims : _deferredDeaths;
            List<Creature> toKill = commitSet.Where(creature => creature.IsAlive).ToList();
            Task killTask = toKill.Count > 0 ? CreatureCmd.Kill(toKill) : Task.CompletedTask;
            if (!guaranteedClearMatchedRuntime)
            {
                Entry.Logger.Warn("Finisher forecast did not match runtime damage; committed real lethal damage without the final freeze.");
                await killTask;
                return;
            }

            await Task.WhenAny(_deathStarted.Task, WaitSeconds(DeathNotificationTimeoutSeconds));
            if (_deathStarted.Task.IsCompleted)
            {
                await PlayDeathFreeze();
            }

            await killTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                await ReturnToBaseline();
            }
            finally
            {
                if (GodotObject.IsInstanceValid(_ownerNode))
                {
                    _ownerNode.Position = _ownerStartPosition;
                }

                _camera.Dispose();
            }
        }

        private async Task PlayDeathFreeze()
        {
            CanvasItem focus = NinjaSlayerVisualRig.GetCinematicFocus(_ownerNode.Visuals) is { } cinematicFocus
                ? cinematicFocus
                : _ownerNode.Visuals.Bounds;
            float impactDirection = ResolveImpactDirection(_ownerNode, _focusNode);
            Vector2 cameraStartPosition = _camera.CurrentPosition;
            float cameraStartScale = _camera.CurrentScale;
            float punchScale = cameraStartScale * CameraPunchScaleMultiplier;
            Vector2 punchCenter = _camera.ClampTarget(_camera.GetLocalCenter(focus), punchScale);
            Vector2 punchPosition = _camera.GetCameraPosition(
                punchCenter,
                punchScale,
                _camera.ViewportSize * 0.5f) + new Vector2(-impactDirection * CameraPushPixels, 0f);
            var impactVisuals = new Dictionary<Node2D, ImpactVisualSnapshot>();
            List<ProcessModeSnapshot> snapshots = [];

            try
            {
                _camera.PlayScreenShake(ShakeStrength.Medium, ShakeDuration.Short);
                float elapsed = 0f;
                while (elapsed < ImpactLeadSeconds)
                {
                    elapsed += await NextFrame();
                    CaptureImpactVisuals(impactVisuals);
                    float progress = EaseOut(Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f));
                    ApplyEnemyFeedback(impactVisuals.Values, progress, flash: true);
                    _camera.SetTransform(
                        cameraStartPosition.Lerp(punchPosition, progress),
                        Mathf.Lerp(cameraStartScale, punchScale, progress));
                }

                RestoreEnemyFlash(impactVisuals.Values);
                if (GodotObject.IsInstanceValid(_ownerNode))
                {
                    snapshots.Add(new ProcessModeSnapshot(_ownerNode, _ownerNode.ProcessMode));
                }

                snapshots.AddRange(_deathNodes
                    .Where(GodotObject.IsInstanceValid)
                    .Select(node => new ProcessModeSnapshot(node, node.ProcessMode)));
                foreach (ProcessModeSnapshot snapshot in snapshots)
                {
                    snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
                }

                await WaitSeconds(HitStopSeconds);
                RestoreProcessModes(snapshots);

                elapsed = 0f;
                while (elapsed < ImpactRecoverySeconds)
                {
                    elapsed += await NextFrame();
                    float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / ImpactRecoverySeconds);
                    ApplyEnemyFeedback(impactVisuals.Values, 1f - progress, flash: false);
                    _camera.SetTransform(
                        punchPosition.Lerp(cameraStartPosition, progress),
                        Mathf.Lerp(punchScale, cameraStartScale, progress));
                }

                await WaitSeconds(FinisherSettleSeconds);
            }
            finally
            {
                RestoreProcessModes(snapshots);
                RestoreImpactVisuals(impactVisuals.Values);
            }
        }

        private void CaptureImpactVisuals(Dictionary<Node2D, ImpactVisualSnapshot> snapshots)
        {
            foreach (NCreature creatureNode in _deathNodes.Where(GodotObject.IsInstanceValid))
            {
                Node2D body = creatureNode.Visuals.GetCurrentBody();
                if (!snapshots.ContainsKey(body))
                {
                    snapshots.Add(body, new ImpactVisualSnapshot(
                        body,
                        body.Position,
                        body.SelfModulate,
                        ResolveImpactDirection(_ownerNode, creatureNode)));
                }
            }
        }

        private static void ApplyEnemyFeedback(
            IEnumerable<ImpactVisualSnapshot> snapshots,
            float amount,
            bool flash)
        {
            foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
            {
                snapshot.Body.Position = snapshot.Position
                    + Vector2.Right * snapshot.Direction * EnemyKnockbackPixels * amount;
                snapshot.Body.SelfModulate = flash
                    ? snapshot.SelfModulate.Lerp(
                        new Color(1.8f, 1.8f, 1.8f, snapshot.SelfModulate.A),
                        amount)
                    : snapshot.SelfModulate;
            }
        }

        private static void RestoreEnemyFlash(IEnumerable<ImpactVisualSnapshot> snapshots)
        {
            foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
            {
                snapshot.Body.SelfModulate = snapshot.SelfModulate;
            }
        }

        private static void RestoreImpactVisuals(IEnumerable<ImpactVisualSnapshot> snapshots)
        {
            foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
            {
                snapshot.Body.Position = snapshot.Position;
                snapshot.Body.SelfModulate = snapshot.SelfModulate;
            }
        }

        private static void RestoreProcessModes(IEnumerable<ProcessModeSnapshot> snapshots)
        {
            foreach (ProcessModeSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Node)))
            {
                snapshot.Node.ProcessMode = snapshot.Mode;
            }
        }

        private async Task ReturnToBaseline()
        {
            if (!GodotObject.IsInstanceValid(_ownerNode))
            {
                _camera.ResetToBaseline();
                return;
            }

            Vector2 ownerFrom = _ownerNode.Position;
            Vector2 cameraFrom = _camera.CurrentPosition;
            float scaleFrom = _camera.CurrentScale;
            float elapsed = 0f;
            while (elapsed < ReturnSeconds)
            {
                elapsed += await NextFrame();
                float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / ReturnSeconds);
                _ownerNode.Position = ownerFrom.Lerp(_ownerStartPosition, progress);
                _camera.SetTransform(
                    cameraFrom.Lerp(_camera.BaselinePosition, progress),
                    Mathf.Lerp(scaleFrom, _camera.BaselineScale.X, progress));
            }
        }

        private async Task<float> NextFrame()
        {
            if (!GodotObject.IsInstanceValid(_room) || !_room.IsInsideTree())
            {
                throw new OperationCanceledException("Combat room was unloaded during the finisher.");
            }

            await _room.ToSignal(_room.GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong processFrame = Engine.GetProcessFrames();
            if (processFrame != _lastDeltaFrame)
            {
                ulong now = Time.GetTicksMsec();
                _cachedFrameDelta = _room.ProcessMode == Node.ProcessModeEnum.Disabled
                    ? 0f
                    : Math.Min((now - _lastFrameMsec) / 1000f, 0.05f);
                _lastFrameMsec = now;
                _lastDeltaFrame = processFrame;
                _camera.Advance(_cachedFrameDelta);
            }

            return _cachedFrameDelta;
        }

        private async Task WaitSeconds(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += await NextFrame();
            }
        }

        private static Vector2 ResolveApproachPosition(NCreature owner, NCreature target)
        {
            float direction = Mathf.Sign(target.Position.X - owner.Position.X);
            if (Mathf.IsZeroApprox(direction))
            {
                direction = 1f;
            }

            float targetHalfWidth = target.Visuals.Bounds.Size.X * Mathf.Abs(target.Visuals.Scale.X) * 0.5f;
            return new Vector2(
                target.Position.X - direction * (targetHalfWidth + ApproachGap),
                owner.Position.Y);
        }

        private static float ResolveImpactDirection(NCreature owner, NCreature target)
        {
            float direction = Mathf.Sign(target.GlobalPosition.X - owner.GlobalPosition.X);
            return Mathf.IsZeroApprox(direction) ? 1f : direction;
        }

        private readonly record struct ProcessModeSnapshot(Node Node, Node.ProcessModeEnum Mode);
        private readonly record struct ImpactVisualSnapshot(
            Node2D Body,
            Vector2 Position,
            Color SelfModulate,
            float Direction);
    }

    private static float EaseOut(float value) => 1f - (1f - value) * (1f - value);
}

internal static class FinisherForecast
{
    public static bool IsGuaranteedClear(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        FinisherAttackSpec spec,
        AttackCommand? command,
        out int resolvedHits)
    {
        resolvedHits = 0;
        ICombatState? combatState = owner.CombatState;
        if (combatState == null || enemies.Any(enemy => !Hook.ShouldDie(owner.Player!.RunState, combatState, enemy, out _)))
        {
            return false;
        }

        int hits = spec.HitCount;
        if (command != null)
        {
            hits = (int)Math.Ceiling(Math.Max(0m, Hook.ModifyAttackHitCount(combatState, command, hits)));
        }

        if (hits <= 0)
        {
            return false;
        }

        resolvedHits = hits;

        var states = enemies.ToDictionary(enemy => enemy, enemy => new ForecastState(
            enemy.CurrentHp,
            enemy.Block,
            enemy.GetPowerAmount<KaratePower>()));
        return spec.Targeting switch
        {
            FinisherTargeting.Single => spec.CardPlay.Target is { } target
                && enemies.Count == 1
                && SimulateFixed(owner, states, spec, hits, _ => [target]),
            FinisherTargeting.All => SimulateFixed(owner, states, spec, hits, current => current.Keys.Where(e => current[e].Hp > 0).ToList()),
            FinisherTargeting.Random => SimulateRandom(owner, states, spec, hits),
            _ => false
        };
    }

    private static bool SimulateFixed(
        Creature owner,
        Dictionary<Creature, ForecastState> states,
        FinisherAttackSpec spec,
        int hits,
        Func<Dictionary<Creature, ForecastState>, IReadOnlyList<Creature>> targets)
    {
        for (int hit = 0; hit < hits; hit++)
        {
            IReadOnlyList<Creature> hitTargets = targets(states);
            if (hitTargets.Count == 0)
            {
                break;
            }

            ApplyHit(owner, states, spec, hitTargets, hit);
        }

        return states.Values.All(state => state.Hp <= 0);
    }

    private static bool SimulateRandom(
        Creature owner,
        Dictionary<Creature, ForecastState> states,
        FinisherAttackSpec spec,
        int hits)
    {
        if (hits == 0)
        {
            return states.Values.All(state => state.Hp <= 0);
        }

        List<Creature> alive = states.Where(pair => pair.Value.Hp > 0).Select(pair => pair.Key).ToList();
        if (alive.Count == 0)
        {
            return true;
        }

        foreach (Creature target in alive)
        {
            Dictionary<Creature, ForecastState> branch = states.ToDictionary(pair => pair.Key, pair => pair.Value);
            ApplyHit(owner, branch, spec, [target], spec.HitCount - hits);
            if (!SimulateRandom(owner, branch, spec, hits - 1))
            {
                return false;
            }
        }

        return true;
    }

    private static void ApplyHit(
        Creature owner,
        Dictionary<Creature, ForecastState> states,
        FinisherAttackSpec spec,
        IReadOnlyList<Creature> targets,
        int hitIndex)
    {
        List<(Creature Target, bool TriggerKarate)> damageResults = [];
        foreach (Creature target in targets)
        {
            ForecastState state = states[target];
            if (state.Hp <= 0)
            {
                continue;
            }

            decimal rawDamage = spec.Damage(target);
            decimal modified = Hook.ModifyDamage(
                owner.Player!.RunState,
                owner.CombatState,
                target,
                owner,
                rawDamage,
                spec.Props,
                spec.Card,
                spec.CardPlay,
                ModifyDamageHookType.All,
                CardPreviewMode.None,
                out _);
            if (spec.Card is TornadoFist && hitIndex > 0 && target.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>() <= 0)
            {
                modified *= 1.5m;
            }

            int blocked = spec.Props.HasFlag(ValueProp.Unblockable) ? 0 : Math.Min(state.Block, (int)modified);
            state = state with { Block = state.Block - blocked };
            decimal hpLoss = Hook.ModifyHpLost(
                owner.Player.RunState,
                owner.CombatState,
                target,
                Math.Max(modified - blocked, 0m),
                spec.Props,
                owner,
                spec.Card,
                HpLossHookPhase.BeforeOsty | HpLossHookPhase.AfterOsty,
                out _);
            int lost = Math.Max(0, (int)hpLoss);
            state = state with { Hp = state.Hp - lost };
            states[target] = state;
            damageResults.Add((target, modified > 0m));
        }

        foreach ((Creature target, bool triggerKarate) in damageResults)
        {
            ForecastState state = states[target];
            if (triggerKarate && state.Hp > 0 && state.Karate > 0 && spec.Props.IsPoweredAttack()
                && KarateTriggerRules.CanTriggerFromCardSource(spec.Card))
            {
                int blocked = Math.Min(state.Block, state.Karate);
                state = state with
                {
                    Block = state.Block - blocked,
                    Hp = state.Hp - Math.Max(0, state.Karate - blocked),
                    Karate = state.Karate - 1
                };
                states[target] = state;
            }

            if (owner.HasPower<NarakuPower>() && spec.Props.IsPoweredAttack())
            {
                int narakuDamage = owner.GetPower<NarakuPower>()!.DynamicVars.HpLoss.IntValue;
                foreach (Creature enemy in states.Keys.ToList())
                {
                    ForecastState enemyState = states[enemy];
                    if (enemyState.Hp > 0)
                    {
                        states[enemy] = enemyState with { Hp = enemyState.Hp - narakuDamage };
                    }
                }
            }
        }
    }

    private sealed record ForecastState(int Hp, int Block, int Karate);
}
