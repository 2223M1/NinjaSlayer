using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
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
    private const float DoomPoseSeconds = 0.2f;
    private const float ImpactRecoverySeconds = 0.1f;
    private const float FinisherSettleSeconds = 0.1f;
    private const float ReturnSeconds = 0.2f;
    private const float ApproachGap = 50f;
    private const float CameraPunchScaleMultiplier = 1.06f;
    private const float CameraPushPixels = 16f;
    private const float EnemyKnockbackPixels = 30f;

    private static FinisherSession? _active;
    private static FinisherSession? _pendingAfterCardPlayed;

    public static bool IsMovementOwned(Creature creature) => _active?.Owner == creature;

    internal static bool TryProtectLethalDamage(Creature target, ref decimal amount)
    {
        return _active?.TryProtectLethalDamage(target, ref amount) == true;
    }

    internal static Task WrapAfterCardPlayed(Task original, CardPlay cardPlay) =>
        CompleteAfterCardPlayed(original, cardPlay);

    internal static Task WrapCardPlay(Task original, CardModel card) =>
        CleanupAfterCardPlay(original, card);

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
        bool transferred = false;
        try
        {
            await session.Begin();
            _active = session;
            try
            {
                AttackCommand result = await command.Execute(choiceContext);
                if (session.RequiresAfterCardPlayed)
                {
                    TransferToAfterCardPlayed(session);
                    transferred = true;
                }
                else
                {
                    await session.CommitDeaths();
                }

                return result;
            }
            catch
            {
                await session.CommitDeferredDeathsWithoutPose();
                throw;
            }
        }
        finally
        {
            if (!transferred)
            {
                ClearActive(session);
                await session.DisposeAsync();
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
        bool transferred = false;
        try
        {
            await session.Begin();
            _active = session;
            try
            {
                await sequence();
                if (session.RequiresAfterCardPlayed)
                {
                    TransferToAfterCardPlayed(session);
                    transferred = true;
                }
                else
                {
                    await session.CommitDeaths();
                }
            }
            catch
            {
                await session.CommitDeferredDeathsWithoutPose();
                throw;
            }
        }
        finally
        {
            if (!transferred)
            {
                ClearActive(session);
                await session.DisposeAsync();
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
        bool transferred = false;
        try
        {
            await session.Begin();
            _active = session;
            try
            {
                await damageAction();
                if (session.RequiresAfterCardPlayed)
                {
                    TransferToAfterCardPlayed(session);
                    transferred = true;
                }
                else
                {
                    await session.CommitDeaths();
                }
            }
            catch
            {
                await session.CommitDeferredDeathsWithoutPose();
                throw;
            }
        }
        finally
        {
            if (!transferred)
            {
                ClearActive(session);
                await session.DisposeAsync();
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
            || !FinisherForecast.IsGuaranteedClear(owner, enemies, spec, command, out FinisherForecastResult forecast))
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
            spec.CardPlay,
            forecast.RequiresAfterCardPlayed);
        return true;
    }

    private static void TransferToAfterCardPlayed(FinisherSession session)
    {
        if (_pendingAfterCardPlayed != null)
        {
            throw new InvalidOperationException("A NinjaSlayer finisher is already awaiting AfterCardPlayed.");
        }

        _pendingAfterCardPlayed = session;
    }

    private static async Task CompleteAfterCardPlayed(Task original, CardPlay cardPlay)
    {
        try
        {
            await original;
        }
        catch
        {
            await CleanupPending(cardPlay.Card, playPose: false);
            throw;
        }

        if (_pendingAfterCardPlayed?.CardPlay == cardPlay)
        {
            await CleanupPending(cardPlay.Card, playPose: true);
        }
    }

    private static async Task CleanupAfterCardPlay(Task original, CardModel card)
    {
        try
        {
            await original;
        }
        finally
        {
            if (_pendingAfterCardPlayed?.CardPlay.Card == card)
            {
                await CleanupPending(card, playPose: false);
            }
        }
    }

    private static async Task CleanupPending(CardModel card, bool playPose)
    {
        FinisherSession? session = _pendingAfterCardPlayed;
        if (session == null || session.CardPlay.Card != card)
        {
            return;
        }

        _pendingAfterCardPlayed = null;
        try
        {
            if (playPose)
            {
                await session.CommitDeaths();
            }
            else
            {
                await session.CommitDeferredDeathsWithoutPose();
            }
        }
        finally
        {
            ClearActive(session);
            await session.DisposeAsync();
        }
    }

    private static void ClearActive(FinisherSession session)
    {
        if (ReferenceEquals(_active, session))
        {
            _active = null;
        }
    }

    private sealed class FinisherSession : IAsyncDisposable
    {
        private readonly NCreature _ownerNode;
        private readonly NCreature _focusNode;
        private readonly HashSet<Creature> _victims;
        private readonly HashSet<Creature> _deferredDeaths = [];
        private readonly CombatCinematicCameraLease _camera;
        private readonly NCombatRoom _room;
        private readonly Vector2 _ownerStartPosition;
        private ulong _lastFrameMsec;
        private ulong _lastDeltaFrame = ulong.MaxValue;
        private float _cachedFrameDelta;
        private bool _committing;
        private bool _disposed;

        public FinisherSession(
            Creature owner,
            NCreature ownerNode,
            NCreature focusNode,
            IEnumerable<Creature> victims,
            CombatCinematicCameraLease camera,
            CardPlay cardPlay,
            bool requiresAfterCardPlayed)
        {
            Owner = owner;
            _ownerNode = ownerNode;
            _focusNode = focusNode;
            _victims = victims.ToHashSet();
            _camera = camera;
            _room = NCombatRoom.Instance!;
            _ownerStartPosition = ownerNode.Position;
            _lastFrameMsec = Time.GetTicksMsec();
            CardPlay = cardPlay;
            RequiresAfterCardPlayed = requiresAfterCardPlayed;
        }

        public Creature Owner { get; }
        public CardPlay CardPlay { get; }
        public bool RequiresAfterCardPlayed { get; }

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

        public async Task CommitDeaths()
        {
            _committing = true;
            bool guaranteedClearMatchedRuntime = _victims.All(
                victim => victim.IsDead
                    || _deferredDeaths.Contains(victim));
            List<Creature> toKill = _deferredDeaths.Where(creature => creature.IsAlive).ToList();
            if (!guaranteedClearMatchedRuntime)
            {
                Entry.Logger.Warn("Finisher forecast did not match runtime damage; committed deferred lethal damage without the finisher pose.");
                if (toKill.Count > 0)
                {
                    await CreatureCmd.Kill(toKill);
                }
                return;
            }

            List<NCreature> targetNodes = toKill
                .Select(creature => _room.GetCreatureNode(creature))
                .Where(node => node != null)
                .Cast<NCreature>()
                .ToList();
            if (targetNodes.Count > 0)
            {
                await PlayDoomPoseImpact(targetNodes);
            }

            if (toKill.Count > 0)
            {
                await CreatureCmd.Kill(toKill);
                await WaitSeconds(FinisherSettleSeconds);
            }
        }

        public async Task CommitDeferredDeathsWithoutPose()
        {
            _committing = true;
            List<Creature> toKill = _deferredDeaths.Where(creature => creature.IsAlive).ToList();
            if (toKill.Count > 0)
            {
                await CreatureCmd.Kill(toKill);
            }
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

        private async Task PlayDoomPoseImpact(IReadOnlyList<NCreature> targetNodes)
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
            CaptureImpactVisuals(targetNodes, impactVisuals);
            List<NCreature> frozenHurtTracks = [];
            foreach (NCreature targetNode in targetNodes)
            {
                if (SetDoomHurtPose(targetNode))
                {
                    frozenHurtTracks.Add(targetNode);
                }
            }
            ProcessModeSnapshot? ownerSnapshot = GodotObject.IsInstanceValid(_ownerNode)
                ? new ProcessModeSnapshot(_ownerNode, _ownerNode.ProcessMode)
                : null;

            try
            {
                if (ownerSnapshot is { } snapshot)
                {
                    snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
                }

                _camera.PlayScreenShake(ShakeStrength.Medium, ShakeDuration.Short);
                float elapsed = 0f;
                while (elapsed < ImpactLeadSeconds)
                {
                    elapsed += await NextFrame();
                    float progress = EaseOut(Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f));
                    ApplyEnemyFeedback(impactVisuals.Values, progress, flash: true);
                    _camera.SetTransform(
                        cameraStartPosition.Lerp(punchPosition, progress),
                        Mathf.Lerp(cameraStartScale, punchScale, progress));
                }

                RestoreEnemyFlash(impactVisuals.Values);
                float holdSeconds = DoomPoseSeconds - ImpactLeadSeconds - ImpactRecoverySeconds;
                if (holdSeconds > 0f)
                {
                    await WaitSeconds(holdSeconds);
                }

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
            }
            finally
            {
                if (ownerSnapshot is { } snapshot && GodotObject.IsInstanceValid(snapshot.Node))
                {
                    snapshot.Node.ProcessMode = snapshot.Mode;
                }

                ResumeDoomHurtPoses(frozenHurtTracks);
                RestoreImpactVisuals(impactVisuals.Values);
            }
        }

        private void CaptureImpactVisuals(
            IEnumerable<NCreature> targetNodes,
            Dictionary<Node2D, ImpactVisualSnapshot> snapshots)
        {
            foreach (NCreature creatureNode in targetNodes.Where(GodotObject.IsInstanceValid))
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

        private static bool SetDoomHurtPose(NCreature creatureNode)
        {
            if (!creatureNode.SpineAnimation.IsValid)
            {
                return false;
            }

            creatureNode.SetAnimationTrigger("Hit");
            using MegaTrackEntry? track = creatureNode.SpineAnimation.GetCurrentTrack();
            if (track?.GetAnimationName() != "hurt")
            {
                return false;
            }

            float trackTime = creatureNode.Entity.Monster?.HurtAnimationTrackOffsetForDoom ?? 0.1f;
            track.SetTrackTime(trackTime);
            track.SetTimeScale(0f);
            return true;
        }

        private static void ResumeDoomHurtPoses(IEnumerable<NCreature> creatureNodes)
        {
            foreach (NCreature creatureNode in creatureNodes.Where(GodotObject.IsInstanceValid))
            {
                using MegaTrackEntry? track = creatureNode.SpineAnimation.GetCurrentTrack();
                if (track?.GetAnimationName() == "hurt")
                {
                    track.SetTimeScale(1f);
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

internal readonly record struct FinisherForecastResult(bool RequiresAfterCardPlayed);

internal enum FinisherForecastEffectTargeting
{
    All,
    Random
}

internal sealed record FinisherForecastEffect(
    decimal Amount,
    ValueProp Props,
    Creature? Dealer,
    CardModel? CardSource,
    CardPlay? CardPlay,
    FinisherForecastEffectTargeting Targeting);

internal interface IFinisherForecastContributor
{
    bool TryCreateEffect(Creature owner, FinisherAttackSpec spec, out FinisherForecastEffect? effect);
}

internal sealed class KusarigamaFinisherForecastContributor : IFinisherForecastContributor
{
    public bool TryCreateEffect(Creature owner, FinisherAttackSpec spec, out FinisherForecastEffect? effect)
    {
        effect = null;
        Kusarigama? kusarigama = owner.Player?.GetRelic<Kusarigama>();
        if (kusarigama == null || spec.Card.Type != CardType.Attack)
        {
            return false;
        }

        int cardsPerTrigger = kusarigama.DynamicVars.Cards.IntValue;
        if (cardsPerTrigger <= 0 || kusarigama.DisplayAmount != cardsPerTrigger - 1)
        {
            return false;
        }

        effect = new FinisherForecastEffect(
            kusarigama.DynamicVars.Damage.BaseValue,
            kusarigama.DynamicVars.Damage.Props,
            owner,
            null,
            null,
            FinisherForecastEffectTargeting.Random);
        return true;
    }
}

internal static class FinisherForecast
{
    private static readonly IFinisherForecastContributor[] PostCardContributors =
    [
        new KusarigamaFinisherForecastContributor()
    ];

    public static bool IsGuaranteedClear(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        FinisherAttackSpec spec,
        AttackCommand? command,
        out FinisherForecastResult result)
    {
        result = default;
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

        List<FinisherForecastEffect> postCardEffects = [];
        foreach (IFinisherForecastContributor contributor in PostCardContributors)
        {
            if (contributor.TryCreateEffect(owner, spec, out FinisherForecastEffect? effect) && effect != null)
            {
                postCardEffects.Add(effect);
            }
        }

        result = new FinisherForecastResult(postCardEffects.Count > 0);
        var states = enemies.ToDictionary(enemy => enemy, enemy => new ForecastState(
            enemy.CurrentHp,
            enemy.Block,
            enemy.GetPowerAmount<KaratePower>()));
        bool Finish(Dictionary<Creature, ForecastState> finalStates) =>
            ApplyPostCardEffects(owner, finalStates, postCardEffects, 0);

        return spec.Targeting switch
        {
            FinisherTargeting.Single => spec.CardPlay.Target is { } target
                && enemies.Count == 1
                && SimulateFixed(owner, states, spec, hits, _ => [target], Finish),
            FinisherTargeting.All => SimulateFixed(
                owner,
                states,
                spec,
                hits,
                current => current.Keys.Where(enemy => current[enemy].Hp > 0).ToList(),
                Finish),
            FinisherTargeting.Random => SimulateRandom(owner, states, spec, 0, hits, Finish),
            _ => false
        };
    }

    private static bool SimulateFixed(
        Creature owner,
        Dictionary<Creature, ForecastState> states,
        FinisherAttackSpec spec,
        int hits,
        Func<Dictionary<Creature, ForecastState>, IReadOnlyList<Creature>> targets,
        Func<Dictionary<Creature, ForecastState>, bool> finish)
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

        return finish(states);
    }

    private static bool SimulateRandom(
        Creature owner,
        Dictionary<Creature, ForecastState> states,
        FinisherAttackSpec spec,
        int hitIndex,
        int hitsRemaining,
        Func<Dictionary<Creature, ForecastState>, bool> finish)
    {
        if (hitsRemaining == 0)
        {
            return finish(states);
        }

        List<Creature> alive = AliveTargets(states);
        if (alive.Count == 0)
        {
            return finish(states);
        }

        foreach (Creature target in alive)
        {
            Dictionary<Creature, ForecastState> branch = Clone(states);
            ApplyHit(owner, branch, spec, [target], hitIndex);
            if (!SimulateRandom(owner, branch, spec, hitIndex + 1, hitsRemaining - 1, finish))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ApplyPostCardEffects(
        Creature owner,
        Dictionary<Creature, ForecastState> states,
        IReadOnlyList<FinisherForecastEffect> effects,
        int effectIndex)
    {
        if (effectIndex >= effects.Count)
        {
            return states.Values.All(state => state.Hp <= 0);
        }

        FinisherForecastEffect effect = effects[effectIndex];
        List<Creature> alive = AliveTargets(states);
        if (alive.Count == 0)
        {
            return true;
        }

        if (effect.Targeting == FinisherForecastEffectTargeting.All)
        {
            foreach (Creature target in alive)
            {
                ApplyDamage(owner, states, target, effect.Amount, effect.Props, effect.Dealer, effect.CardSource, effect.CardPlay);
            }

            return ApplyPostCardEffects(owner, states, effects, effectIndex + 1);
        }

        foreach (Creature target in alive)
        {
            Dictionary<Creature, ForecastState> branch = Clone(states);
            ApplyDamage(owner, branch, target, effect.Amount, effect.Props, effect.Dealer, effect.CardSource, effect.CardPlay);
            if (!ApplyPostCardEffects(owner, branch, effects, effectIndex + 1))
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
            if (states[target].Hp <= 0)
            {
                continue;
            }

            decimal rawDamage = spec.Damage(target);
            decimal postHookMultiplier = spec.Card is TornadoFist && hitIndex > 0
                && target.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>() <= 0
                    ? 1.5m
                    : 1m;

            bool dealtDamage = ApplyDamage(
                owner,
                states,
                target,
                rawDamage,
                spec.Props,
                owner,
                spec.Card,
                spec.CardPlay,
                postHookMultiplier);
            damageResults.Add((target, dealtDamage));
        }

        foreach ((Creature target, bool triggerKarate) in damageResults)
        {
            ForecastState state = states[target];
            if (triggerKarate && state.Hp > 0 && state.Karate > 0 && spec.Props.IsPoweredAttack()
                && KarateTriggerRules.CanTriggerFromCardSource(spec.Card))
            {
                ApplyDamage(owner, states, target, state.Karate, ValueProp.Unpowered, owner, null, null);
                ForecastState afterKarate = states[target];
                if (afterKarate.Hp > 0)
                {
                    states[target] = afterKarate with { Karate = Math.Max(0, afterKarate.Karate - 1) };
                }
            }

            if (owner.GetPower<NarakuPower>() is { } naraku && spec.Props.IsPoweredAttack())
            {
                foreach (Creature enemy in AliveTargets(states))
                {
                    ApplyDamage(
                        owner,
                        states,
                        enemy,
                        naraku.DynamicVars.HpLoss.BaseValue,
                        ValueProp.Unblockable | ValueProp.Unpowered,
                        owner,
                        spec.Card,
                        null);
                }
            }
        }
    }

    private static bool ApplyDamage(
        Creature owner,
        Dictionary<Creature, ForecastState> states,
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        decimal postHookMultiplier = 1m)
    {
        ForecastState state = states[target];
        if (state.Hp <= 0)
        {
            return false;
        }

        decimal modified = Hook.ModifyDamage(
            owner.Player!.RunState,
            owner.CombatState,
            target,
            dealer,
            amount,
            props,
            cardSource,
            cardPlay,
            ModifyDamageHookType.All,
            CardPreviewMode.None,
            out _);
        modified *= postHookMultiplier;
        int blocked = props.HasFlag(ValueProp.Unblockable)
            ? 0
            : Math.Min(state.Block, Math.Max(0, (int)modified));
        decimal hpLoss = Hook.ModifyHpLost(
            owner.Player.RunState,
            owner.CombatState,
            target,
            Math.Max(modified - blocked, 0m),
            props,
            dealer,
            cardSource,
            HpLossHookPhase.BeforeOsty | HpLossHookPhase.AfterOsty,
            out _);
        states[target] = state with
        {
            Block = state.Block - blocked,
            Hp = state.Hp - Math.Max(0, (int)hpLoss)
        };
        return modified > 0m;
    }

    private static List<Creature> AliveTargets(Dictionary<Creature, ForecastState> states) =>
        states.Where(pair => pair.Value.Hp > 0).Select(pair => pair.Key).ToList();

    private static Dictionary<Creature, ForecastState> Clone(Dictionary<Creature, ForecastState> states) =>
        states.ToDictionary(pair => pair.Key, pair => pair.Value);

    private sealed record ForecastState(int Hp, int Block, int Karate);
}
