using System.Reflection;
using Godot;
using HarmonyLib;
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
    Random,
    Fixed
}

internal enum FinisherPresentationMode
{
    Legacy,
    Enhanced
}

public sealed record FinisherAttackSpec(
    CardModel Card,
    CardPlay CardPlay,
    Func<Creature, decimal> Damage,
    ValueProp Props,
    int HitCount,
    FinisherTargeting Targeting,
    Creature? SingleTarget = null,
    IReadOnlyList<Creature>? FixedTargets = null)
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
        return new FinisherAttackSpec(
            card,
            cardPlay,
            damage,
            propsOverride ?? props,
            Math.Max(1, hitCount),
            targeting,
            cardPlay.Target);
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
    private const FinisherPresentationMode PresentationMode = FinisherPresentationMode.Enhanced;
    private const float ImpactLeadSeconds = 0.04f;
    private const float DoomPoseSeconds = 0.3f;
    private const float ImpactRecoverySeconds = 0.1f;
    private const float FinisherSettleSeconds = 0.1f;
    private const float ReturnSeconds = 0.2f;
    private const float SingleHitZoomSeconds = 0.1f;
    private const float MultiHitZoomSeconds = 0.2f;
    private const float FinalHitZoomSeconds = 0.1f;
    private const float MultiHitZoomMultiplier = 1.6f;
    private const float FinalHitZoomMultiplier = 2f;
    private const float ApproachGap = 50f;
    private const float CameraPunchScaleMultiplier = 1.06f;
    private const float CameraPushPixels = 16f;
    private const float EnemyKnockbackPixels = 30f;
    private const float EnhancedEnemyTiltDegrees = 3f;
    private const float EnhancedEnemyStretchX = 0.06f;
    private const float EnhancedEnemySquashY = 0.04f;
    private const float ImpactVfxTargetMargin = 160f;

    private static FinisherSession? _active;
    private static FinisherSession? _pendingAfterCardPlayed;
    private static readonly AsyncLocal<CommandBypassFrame?> CommandBypass = new();
    private static readonly AsyncLocal<int> DirectDamageBypassDepth = new();

    public static bool IsMovementOwned(Creature creature) => _active?.Owner == creature;

    internal static bool TryProtectLethalDamage(Creature target, ref decimal amount)
    {
        return _active?.TryProtectLethalDamage(target, ref amount) == true;
    }

    internal static void NotifyPrimaryAttackAnimation(Creature creature, string triggerName)
    {
        _active?.NotifyPrimaryAttackAnimation(creature, triggerName);
    }

    internal static void NotifyPrimaryDamage(Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        _active?.NotifyPrimaryDamage(dealer, cardSource, cardPlay);
    }

    internal static bool TryInterceptAttackCommand(
        AttackCommand command,
        PlayerChoiceContext? choiceContext,
        out Task<AttackCommand>? result)
    {
        result = null;
        if (_active != null
            || IsCommandBypassed(command)
            || !FinisherAttackCommandAdapter.TryCreateSpec(command, out FinisherAttackSpec? spec)
            || spec == null
            || IsExcludedAttackCard(spec.Card))
        {
            return false;
        }

        result = ExecuteCommandWithFinisher(
            command,
            choiceContext ?? new BlockingPlayerChoiceContext(),
            spec,
            "generic-command");
        return true;
    }

    internal static bool TryInterceptDirectDamage(
        PlayerChoiceContext choiceContext,
        IEnumerable<Creature>? targets,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        out Task<IEnumerable<DamageResult>>? result)
    {
        result = null;
        if (_active != null
            || DirectDamageBypassDepth.Value > 0
            || dealer?.Player?.Character is not INinjaSlayerCharacter
            || cardSource?.Type != CardType.Attack
            || cardPlay == null
            || cardSource.Owner?.Creature != dealer
            || IsExcludedAttackCard(cardSource))
        {
            return false;
        }

        List<Creature> targetList = targets?.Where(target => target.IsAlive).Distinct().ToList() ?? [];
        if (targetList.Count == 0)
        {
            return false;
        }

        var spec = new FinisherAttackSpec(
            cardSource,
            cardPlay,
            _ => amount,
            props,
            1,
            FinisherTargeting.Fixed,
            FixedTargets: targetList);
        result = ExecuteDirectDamageWithFinisher(
            choiceContext,
            spec,
            () => ExecuteOriginalDirectDamage(
                choiceContext,
                targetList,
                amount,
                props,
                dealer,
                cardSource,
                cardPlay));
        return true;
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
        return await ExecuteCommandWithFinisher(command, choiceContext, spec, "explicit-command");
    }

    private static async Task<AttackCommand> ExecuteCommandWithFinisher(
        AttackCommand command,
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        string entryPoint)
    {
        if (!TryCreateSession(spec, command, entryPoint, out FinisherSession? session))
        {
            return await ExecuteOriginalCommand(command, choiceContext);
        }

        ArgumentNullException.ThrowIfNull(session);
        bool transferred = false;
        try
        {
            await session.Begin();
            _active = session;
            try
            {
                AttackCommand result = await ExecuteOriginalCommand(command, choiceContext);
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
        if (!TryCreateSession(spec, null, "explicit-sequence", out FinisherSession? session))
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
        await ExecuteDirectWithFinisher(choiceContext, spec, damageAction, "explicit-direct");
    }

    private static async Task ExecuteDirectWithFinisher(
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        Func<Task> damageAction,
        string entryPoint)
    {
        if (!TryCreateSession(spec, null, entryPoint, out FinisherSession? session))
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

    private static async Task<IEnumerable<DamageResult>> ExecuteDirectDamageWithFinisher(
        PlayerChoiceContext choiceContext,
        FinisherAttackSpec spec,
        Func<Task<IEnumerable<DamageResult>>> damageAction)
    {
        IEnumerable<DamageResult> results = [];
        await ExecuteDirectWithFinisher(
            choiceContext,
            spec,
            async () => results = await damageAction(),
            "direct-damage");
        return results;
    }

    private static bool TryCreateSession(
        FinisherAttackSpec spec,
        AttackCommand? command,
        string entryPoint,
        out FinisherSession? session)
    {
        session = null;
        if (_active != null
            || IsExcludedAttackCard(spec.Card)
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
            forecast.RequiresAfterCardPlayed,
            forecast.ResolvedHits);
        Entry.Logger.Info(
            $"NinjaSlayer finisher session started: card={spec.Card.Id.Entry}, entry={entryPoint}, targeting={spec.Targeting}, hits={forecast.ResolvedHits}.");
        return true;
    }

    private static bool IsExcludedAttackCard(CardModel card) =>
        card is ShurikenCard or GiantShurikenCard
        || card.Tags.Contains(CardTag.Shiv)
        || card.Tags.Contains(NinjaSlayerCardTags.Shuriken);

    private static bool IsCommandBypassed(AttackCommand command)
    {
        for (CommandBypassFrame? frame = CommandBypass.Value; frame != null; frame = frame.Parent)
        {
            if (ReferenceEquals(frame.Command, command))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<AttackCommand> ExecuteOriginalCommand(
        AttackCommand command,
        PlayerChoiceContext choiceContext)
    {
        CommandBypassFrame? previous = CommandBypass.Value;
        CommandBypass.Value = new CommandBypassFrame(command, previous);
        try
        {
            return await command.Execute(choiceContext);
        }
        finally
        {
            CommandBypass.Value = previous;
        }
    }

    private static async Task<IEnumerable<DamageResult>> ExecuteOriginalDirectDamage(
        PlayerChoiceContext choiceContext,
        IEnumerable<Creature> targets,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        DirectDamageBypassDepth.Value++;
        try
        {
            return await CreatureCmd.Damage(
                choiceContext,
                targets,
                amount,
                props,
                dealer,
                cardSource,
                cardPlay);
        }
        finally
        {
            DirectDamageBypassDepth.Value--;
        }
    }

    private sealed record CommandBypassFrame(AttackCommand Command, CommandBypassFrame? Parent);

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
        private readonly HashSet<ulong> _vfxBaselineChildIds;
        private readonly CancellationTokenSource _impactCancellation = new();
        private ulong _lastFrameMsec;
        private ulong _lastDeltaFrame = ulong.MaxValue;
        private float _cachedFrameDelta;
        private Task _cameraTransitionTask = Task.CompletedTask;
        private Task _backdropTransitionTask = Task.CompletedTask;
        private Task _enhancedImpactTask = Task.CompletedTask;
        private int _cameraTransitionGeneration;
        private int _backdropTransitionGeneration;
        private int _primaryAnimationsStarted;
        private int _primaryDamageCalls;
        private float _backdropIntensity;
        private bool _finalZoomStarted;
        private bool _backdropDarkeningStarted;
        private bool _enhancedImpactScheduled;
        private bool _enhancedImpactFailed;
        private bool _committing;
        private bool _disposed;
        private NinjaSlayerHoverTipSuppression? _hoverTipSuppression;
        private FinisherImpactPresentation? _presentation;

        public FinisherSession(
            Creature owner,
            NCreature ownerNode,
            NCreature focusNode,
            IEnumerable<Creature> victims,
            CombatCinematicCameraLease camera,
            CardPlay cardPlay,
            bool requiresAfterCardPlayed,
            int resolvedHits)
        {
            Owner = owner;
            _ownerNode = ownerNode;
            _focusNode = focusNode;
            _victims = victims.ToHashSet();
            _camera = camera;
            _room = NCombatRoom.Instance!;
            _ownerStartPosition = ownerNode.Position;
            _vfxBaselineChildIds = _room.CombatVfxContainer.GetChildren()
                .Select(child => child.GetInstanceId())
                .ToHashSet();
            _lastFrameMsec = Time.GetTicksMsec();
            CardPlay = cardPlay;
            RequiresAfterCardPlayed = requiresAfterCardPlayed;
            ResolvedHits = Math.Max(1, resolvedHits);
        }

        public Creature Owner { get; }
        public CardPlay CardPlay { get; }
        public bool RequiresAfterCardPlayed { get; }
        public int ResolvedHits { get; }

        public Task Begin()
        {
            if (PresentationMode == FinisherPresentationMode.Enhanced)
            {
                _hoverTipSuppression = NinjaSlayerHoverTipSuppression.Acquire();
                try
                {
                    _presentation = FinisherImpactPresentation.Create(_room, _victims.Count);
                }
                catch (Exception ex)
                {
                    _enhancedImpactFailed = true;
                    Entry.Logger.Warn($"Could not create enhanced finisher presentation; legacy presentation will be used: {ex}");
                }
            }

            Vector2 destination = ResolveApproachPosition(_ownerNode, _focusNode);
            _ownerNode.Position = destination;
            _finalZoomStarted = ResolvedHits <= 1;
            StartCameraTransition(
                ResolvedHits > 1 ? MultiHitZoomMultiplier : FinalHitZoomMultiplier,
                ResolvedHits > 1 ? MultiHitZoomSeconds : SingleHitZoomSeconds);
            if (ResolvedHits <= 1)
            {
                StartBackdropDarkening();
            }

            return Task.CompletedTask;
        }

        public void NotifyPrimaryAttackAnimation(Creature creature, string triggerName)
        {
            if (ResolvedHits <= 1
                || creature != Owner
                || !IsPrimaryAttackTrigger(triggerName))
            {
                return;
            }

            _primaryAnimationsStarted++;
            if (_primaryAnimationsStarted >= ResolvedHits)
            {
                StartFinalZoom();
            }
        }

        public void NotifyPrimaryDamage(Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        {
            if (ResolvedHits <= 1
                || dealer != Owner
                || cardSource != CardPlay.Card
                || cardPlay != CardPlay)
            {
                return;
            }

            _primaryDamageCalls++;
            if (_primaryDamageCalls >= ResolvedHits)
            {
                StartFinalZoom();
            }

            TryScheduleEnhancedImpact();
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

            TryScheduleEnhancedImpact();

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
                if (PresentationMode == FinisherPresentationMode.Enhanced)
                {
                    TryScheduleEnhancedImpact();
                    await _enhancedImpactTask;
                }

                if (PresentationMode == FinisherPresentationMode.Legacy
                    || !_enhancedImpactScheduled
                    || _enhancedImpactFailed)
                {
                    if (PresentationMode == FinisherPresentationMode.Enhanced)
                    {
                        _finalZoomStarted = false;
                    }

                    StartFinalZoom();
                    await _cameraTransitionTask;
                    await PlayDoomPoseImpact(targetNodes);
                }
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
            _impactCancellation.Cancel();
            await _enhancedImpactTask;
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
                _impactCancellation.Cancel();
                await _enhancedImpactTask;
                _cameraTransitionGeneration++;
                _backdropTransitionGeneration++;
                await _cameraTransitionTask;
                await _backdropTransitionTask;
                await ReturnToBaseline();
            }
            finally
            {
                if (GodotObject.IsInstanceValid(_ownerNode))
                {
                    _ownerNode.Position = _ownerStartPosition;
                }

                _hoverTipSuppression?.Dispose();
                _hoverTipSuppression = null;
                DisposeEnhancedPresentation();
                _impactCancellation.Dispose();
                _camera.Dispose();
            }
        }

        private void TryScheduleEnhancedImpact()
        {
            if (PresentationMode != FinisherPresentationMode.Enhanced
                || _enhancedImpactScheduled
                || _enhancedImpactFailed
                || _disposed
                || !IsFinalPrimaryHitReady()
                || !_victims.All(victim => victim.IsDead || _deferredDeaths.Contains(victim)))
            {
                return;
            }

            _enhancedImpactScheduled = true;
            _enhancedImpactTask = RunEnhancedImpact();
        }

        private bool IsFinalPrimaryHitReady() =>
            ResolvedHits <= 1
            || _primaryAnimationsStarted >= ResolvedHits
            || _primaryDamageCalls >= ResolvedHits;

        private async Task RunEnhancedImpact()
        {
            try
            {
                await NextFrame();
                _impactCancellation.Token.ThrowIfCancellationRequested();
                List<NCreature> targetNodes = _deferredDeaths
                    .Where(creature => creature.IsAlive)
                    .Select(creature => _room.GetCreatureNode(creature))
                    .Where(node => node != null && GodotObject.IsInstanceValid(node))
                    .Cast<NCreature>()
                    .ToList();
                if (targetNodes.Count == 0)
                {
                    throw new InvalidOperationException("No living target nodes remained for the enhanced finisher impact.");
                }

                _cameraTransitionGeneration++;
                await PlayEnhancedDoomPoseImpact(targetNodes, _impactCancellation.Token);
            }
            catch (OperationCanceledException) when (_impactCancellation.IsCancellationRequested
                || _disposed
                || !GodotObject.IsInstanceValid(_room))
            {
            }
            catch (Exception ex)
            {
                _enhancedImpactFailed = true;
                DisposeEnhancedPresentation();
                Entry.Logger.Warn($"Enhanced finisher impact failed; legacy presentation will be used: {ex}");
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
            List<ProcessModeSnapshot> frozenImpactVfx = [];
            ProcessModeSnapshot? ownerSnapshot = GodotObject.IsInstanceValid(_ownerNode)
                ? new ProcessModeSnapshot(_ownerNode, _ownerNode.ProcessMode)
                : null;

            try
            {
                frozenImpactVfx.AddRange(FreezeImpactVfx(targetNodes));
                foreach (NCreature targetNode in targetNodes)
                {
                    if (SetDoomHurtPose(targetNode))
                    {
                        frozenHurtTracks.Add(targetNode);
                    }
                }

                if (ownerSnapshot is { } snapshot)
                {
                    snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
                }

                _camera.PlayScreenShake(ShakeStrength.Strong, ShakeDuration.Short);
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

                RestoreProcessModes(frozenImpactVfx);
                ResumeDoomHurtPoses(frozenHurtTracks);
                RestoreImpactVisuals(impactVisuals.Values);
            }
        }

        private async Task PlayEnhancedDoomPoseImpact(
            IReadOnlyList<NCreature> targetNodes,
            CancellationToken cancellationToken)
        {
            CanvasItem focus = NinjaSlayerVisualRig.GetCinematicFocus(_ownerNode.Visuals) is { } cinematicFocus
                ? cinematicFocus
                : _ownerNode.Visuals.Bounds;
            float impactDirection = ResolveImpactDirection(_ownerNode, _focusNode);
            Vector2 cameraStartPosition = _camera.CurrentPosition;
            float cameraStartScale = _camera.CurrentScale;
            float punchScale = _camera.BaselineScale.X * FinalHitZoomMultiplier * CameraPunchScaleMultiplier;
            float recoveryScale = _camera.BaselineScale.X * FinalHitZoomMultiplier;
            Vector2 punchCenter = _camera.ClampTarget(_camera.GetLocalCenter(focus), punchScale);
            Vector2 punchPosition = _camera.GetCameraPosition(
                punchCenter,
                punchScale,
                _camera.ViewportSize * 0.5f) + new Vector2(-impactDirection * CameraPushPixels, 0f);
            Vector2 recoveryCenter = _camera.ClampTarget(_camera.GetLocalCenter(focus), recoveryScale);
            Vector2 recoveryPosition = _camera.GetCameraPosition(
                recoveryCenter,
                recoveryScale,
                _camera.ViewportSize * 0.5f);
            var impactVisuals = new Dictionary<Node2D, ImpactVisualSnapshot>();
            CaptureImpactVisuals(targetNodes, impactVisuals);
            List<NCreature> frozenHurtTracks = [];
            List<ProcessModeSnapshot> frozenImpactVfx = [];
            ProcessModeSnapshot? ownerSnapshot = GodotObject.IsInstanceValid(_ownerNode)
                ? new ProcessModeSnapshot(_ownerNode, _ownerNode.ProcessMode)
                : null;
            FinisherImpactPresentation presentation = _presentation
                ?? throw new InvalidOperationException("The enhanced finisher presentation was not initialized.");

            try
            {
                frozenImpactVfx.AddRange(FreezeImpactVfx(targetNodes));
                foreach (NCreature targetNode in targetNodes)
                {
                    if (SetDoomHurtPose(targetNode))
                    {
                        frozenHurtTracks.Add(targetNode);
                    }
                }

                if (ownerSnapshot is { } snapshot)
                {
                    snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
                }

                _camera.PlayScreenShake(ShakeStrength.Strong, ShakeDuration.Short);
                float elapsed = 0f;
                while (elapsed < ImpactLeadSeconds)
                {
                    elapsed += await NextEnhancedFrame(cancellationToken);
                    float linearProgress = Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f);
                    float progress = EaseOut(linearProgress);
                    ApplyEnhancedEnemyFeedback(impactVisuals.Values, progress, flash: true);
                    presentation.SetImpactState(targetNodes, progress, Mathf.Sin(linearProgress * Mathf.Pi));
                    _camera.SetTransform(
                        cameraStartPosition.Lerp(punchPosition, progress),
                        Mathf.Lerp(cameraStartScale, punchScale, progress));
                }

                RestoreEnemyFlash(impactVisuals.Values);
                presentation.SetImpactState(targetNodes, 1f, 0f);
                float holdSeconds = DoomPoseSeconds - ImpactLeadSeconds - ImpactRecoverySeconds;
                if (holdSeconds > 0f)
                {
                    await WaitEnhancedSeconds(holdSeconds, cancellationToken);
                }

                elapsed = 0f;
                while (elapsed < ImpactRecoverySeconds)
                {
                    elapsed += await NextEnhancedFrame(cancellationToken);
                    float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / ImpactRecoverySeconds);
                    ApplyEnhancedEnemyFeedback(impactVisuals.Values, 1f - progress, flash: false);
                    presentation.SetImpactState(targetNodes, 1f - progress, 0f);
                    _camera.SetTransform(
                        punchPosition.Lerp(recoveryPosition, progress),
                        Mathf.Lerp(punchScale, recoveryScale, progress));
                }

                _camera.SetTransform(recoveryPosition, recoveryScale);
            }
            finally
            {
                presentation.SetImpactState([], 0f, 0f);
                if (ownerSnapshot is { } snapshot && GodotObject.IsInstanceValid(snapshot.Node))
                {
                    snapshot.Node.ProcessMode = snapshot.Mode;
                }

                RestoreProcessModes(frozenImpactVfx);
                ResumeDoomHurtPoses(frozenHurtTracks);
                RestoreImpactVisuals(impactVisuals.Values);
            }
        }

        private async Task<float> NextEnhancedFrame(CancellationToken cancellationToken)
        {
            float delta = await NextFrame();
            cancellationToken.ThrowIfCancellationRequested();
            return delta;
        }

        private async Task WaitEnhancedSeconds(float seconds, CancellationToken cancellationToken)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += await NextEnhancedFrame(cancellationToken);
            }
        }

        private void StartFinalZoom()
        {
            if (_finalZoomStarted)
            {
                return;
            }

            _finalZoomStarted = true;
            StartCameraTransition(FinalHitZoomMultiplier, FinalHitZoomSeconds);
            StartBackdropDarkening();
        }

        private void StartBackdropDarkening()
        {
            if (PresentationMode != FinisherPresentationMode.Enhanced
                || _enhancedImpactFailed
                || _presentation == null
                || _backdropDarkeningStarted)
            {
                return;
            }

            _backdropDarkeningStarted = true;
            int generation = ++_backdropTransitionGeneration;
            _backdropTransitionTask = RunBackdropTransition(generation, 1f, FinalHitZoomSeconds);
        }

        private async Task RunBackdropTransition(int generation, float targetIntensity, float duration)
        {
            try
            {
                float startIntensity = _backdropIntensity;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += await NextFrame();
                    if (_disposed
                        || generation != _backdropTransitionGeneration
                        || _presentation == null)
                    {
                        return;
                    }

                    float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / duration);
                    SetBackdropIntensity(Mathf.Lerp(startIntensity, targetIntensity, progress));
                }

                if (!_disposed
                    && generation == _backdropTransitionGeneration
                    && _presentation != null)
                {
                    SetBackdropIntensity(targetIntensity);
                }
            }
            catch (OperationCanceledException) when (_disposed || !GodotObject.IsInstanceValid(_room))
            {
            }
            catch (Exception ex)
            {
                _enhancedImpactFailed = true;
                DisposeEnhancedPresentation();
                Entry.Logger.Warn($"Finisher backdrop transition failed; legacy presentation will be used: {ex}");
            }
        }

        private void StartCameraTransition(float scaleMultiplier, float duration)
        {
            int generation = ++_cameraTransitionGeneration;
            _cameraTransitionTask = RunCameraTransition(generation, scaleMultiplier, duration);
        }

        private async Task RunCameraTransition(int generation, float scaleMultiplier, float duration)
        {
            try
            {
                CanvasItem focus = NinjaSlayerVisualRig.GetCinematicFocus(_ownerNode.Visuals) is { } cinematicFocus
                    ? cinematicFocus
                    : _ownerNode.Visuals.Bounds;
                Vector2 startPosition = _camera.CurrentPosition;
                float startScale = _camera.CurrentScale;
                float targetScale = _camera.BaselineScale.X * scaleMultiplier;
                Vector2 targetCenter = _camera.ClampTarget(_camera.GetLocalCenter(focus), targetScale);
                Vector2 targetPosition = _camera.GetCameraPosition(
                    targetCenter,
                    targetScale,
                    _camera.ViewportSize * 0.5f);
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += await NextFrame();
                    if (_disposed || generation != _cameraTransitionGeneration)
                    {
                        return;
                    }

                    float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / duration);
                    _camera.SetTransform(
                        startPosition.Lerp(targetPosition, progress),
                        Mathf.Lerp(startScale, targetScale, progress));
                }

                if (!_disposed && generation == _cameraTransitionGeneration)
                {
                    _camera.SetTransform(targetPosition, targetScale);
                }
            }
            catch (OperationCanceledException) when (_disposed || !GodotObject.IsInstanceValid(_room))
            {
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"Finisher camera transition failed: {ex}");
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
                        body.Scale,
                        body.Rotation,
                        body.SelfModulate,
                        ResolveImpactDirection(_ownerNode, creatureNode)));
                }
            }
        }

        private List<ProcessModeSnapshot> FreezeImpactVfx(IReadOnlyList<NCreature> targetNodes)
        {
            if (!GodotObject.IsInstanceValid(_room.CombatVfxContainer))
            {
                return [];
            }

            List<Rect2> targetRegions = targetNodes
                .Where(GodotObject.IsInstanceValid)
                .Select(target => target.Hitbox.GetGlobalRect().Grow(ImpactVfxTargetMargin))
                .ToList();
            if (targetRegions.Count == 0)
            {
                return [];
            }

            List<ProcessModeSnapshot> snapshots = [];
            foreach (Node vfxRoot in _room.CombatVfxContainer.GetChildren())
            {
                if (_vfxBaselineChildIds.Contains(vfxRoot.GetInstanceId())
                    || !IsNodeActive(vfxRoot)
                    || !IsVfxNearTargets(vfxRoot, targetRegions))
                {
                    continue;
                }

                CaptureProcessModes(vfxRoot, snapshots);
            }

            foreach (ProcessModeSnapshot snapshot in snapshots)
            {
                if (IsNodeActive(snapshot.Node))
                {
                    snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
                }
            }

            return snapshots;
        }

        private static bool IsVfxNearTargets(Node vfxRoot, IReadOnlyList<Rect2> targetRegions)
        {
            Vector2? position = vfxRoot switch
            {
                Control control => control.GetGlobalRect().GetCenter(),
                Node2D node => node.GlobalPosition,
                _ => null
            };
            return position.HasValue && targetRegions.Any(region => region.HasPoint(position.Value));
        }

        private static void CaptureProcessModes(Node node, ICollection<ProcessModeSnapshot> snapshots)
        {
            if (!IsNodeActive(node))
            {
                return;
            }

            snapshots.Add(new ProcessModeSnapshot(node, node.ProcessMode));
            foreach (Node child in node.GetChildren())
            {
                CaptureProcessModes(child, snapshots);
            }
        }

        private static void RestoreProcessModes(IEnumerable<ProcessModeSnapshot> snapshots)
        {
            foreach (ProcessModeSnapshot snapshot in snapshots)
            {
                if (IsNodeActive(snapshot.Node))
                {
                    snapshot.Node.ProcessMode = snapshot.Mode;
                }
            }
        }

        private static bool IsNodeActive(Node node) =>
            GodotObject.IsInstanceValid(node)
            && node.IsInsideTree()
            && !node.IsQueuedForDeletion();

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

        private static void ApplyEnhancedEnemyFeedback(
            IEnumerable<ImpactVisualSnapshot> snapshots,
            float amount,
            bool flash)
        {
            foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
            {
                snapshot.Body.Position = snapshot.Position
                    + Vector2.Right * snapshot.Direction * EnemyKnockbackPixels * amount;
                snapshot.Body.Scale = snapshot.Scale * new Vector2(
                    1f + EnhancedEnemyStretchX * amount,
                    1f - EnhancedEnemySquashY * amount);
                snapshot.Body.Rotation = snapshot.Rotation
                    + Mathf.DegToRad(EnhancedEnemyTiltDegrees * snapshot.Direction * amount);
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
                snapshot.Body.Scale = snapshot.Scale;
                snapshot.Body.Rotation = snapshot.Rotation;
                snapshot.Body.SelfModulate = snapshot.SelfModulate;
            }
        }

        private async Task ReturnToBaseline()
        {
            if (!GodotObject.IsInstanceValid(_ownerNode))
            {
                SetBackdropIntensity(0f);
                _camera.ResetToBaseline();
                return;
            }

            Vector2 ownerFrom = _ownerNode.Position;
            Vector2 cameraFrom = _camera.CurrentPosition;
            float scaleFrom = _camera.CurrentScale;
            float backdropFrom = _backdropIntensity;
            float elapsed = 0f;
            while (elapsed < ReturnSeconds)
            {
                elapsed += await NextFrame();
                float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / ReturnSeconds);
                _ownerNode.Position = ownerFrom.Lerp(_ownerStartPosition, progress);
                _camera.SetTransform(
                    cameraFrom.Lerp(_camera.BaselinePosition, progress),
                    Mathf.Lerp(scaleFrom, _camera.BaselineScale.X, progress));
                SetBackdropIntensity(Mathf.Lerp(backdropFrom, 0f, progress));
            }

            SetBackdropIntensity(0f);
        }

        private void SetBackdropIntensity(float intensity)
        {
            _backdropIntensity = Mathf.Clamp(intensity, 0f, 1f);
            _presentation?.SetBackdropIntensity(_backdropIntensity);
        }

        private void DisposeEnhancedPresentation()
        {
            _backdropTransitionGeneration++;
            _presentation?.Dispose();
            _presentation = null;
            _backdropIntensity = 0f;
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

        private static bool IsPrimaryAttackTrigger(string triggerName) =>
            triggerName is "Attack" or "SlowAttack" or "XAttack"
            || triggerName == TornadoFistSpinAnimation.TriggerName;

        private readonly record struct ProcessModeSnapshot(Node Node, Node.ProcessModeEnum Mode);
        private readonly record struct ImpactVisualSnapshot(
            Node2D Body,
            Vector2 Position,
            Vector2 Scale,
            float Rotation,
            Color SelfModulate,
            float Direction);
    }

    private static float EaseOut(float value) => 1f - (1f - value) * (1f - value);
}

internal static class FinisherAttackCommandAdapter
{
    private static readonly FieldInfo? DamagePerHitField = AccessTools.Field(typeof(AttackCommand), "_damagePerHit");
    private static readonly FieldInfo? CalculatedDamageField = AccessTools.Field(typeof(AttackCommand), "_calculatedDamageVar");
    private static readonly FieldInfo? HitCountField = AccessTools.Field(typeof(AttackCommand), "_hitCount");
    private static readonly FieldInfo? SingleTargetField = AccessTools.Field(typeof(AttackCommand), "_singleTarget");

    public static bool TryCreateSpec(AttackCommand command, out FinisherAttackSpec? spec)
    {
        spec = null;
        if (DamagePerHitField == null
            || CalculatedDamageField == null
            || HitCountField == null
            || SingleTargetField == null
            || command.ModelSource is not CardModel { Type: CardType.Attack } card
            || command.CardPlay is not { } cardPlay
            || command.Attacker == null
            || card.Owner?.Creature != command.Attacker)
        {
            return false;
        }

        var calculatedDamage = CalculatedDamageField.GetValue(command) as CalculatedDamageVar;
        decimal damagePerHit = (decimal)(DamagePerHitField.GetValue(command) ?? 0m);
        int hitCount = (int)(HitCountField.GetValue(command) ?? 1);
        var singleTarget = SingleTargetField.GetValue(command) as Creature;
        FinisherTargeting? targeting = command.IsRandomlyTargeted
            ? FinisherTargeting.Random
            : command.IsSingleTargeted
                ? FinisherTargeting.Single
                : command.IsMultiTargeted
                    ? FinisherTargeting.All
                    : null;
        if (targeting == null || targeting == FinisherTargeting.Single && singleTarget == null)
        {
            return false;
        }

        Func<Creature, decimal> damage = calculatedDamage switch
        {
            null => _ => damagePerHit,
            _ when command.IsMultiTargeted && !command.IsRandomlyTargeted => _ => calculatedDamage.Calculate(null),
            _ => target => calculatedDamage.Calculate(target)
        };
        spec = new FinisherAttackSpec(
            card,
            cardPlay,
            damage,
            command.DamageProps,
            Math.Max(1, hitCount),
            targeting.Value,
            singleTarget);
        return true;
    }
}

internal readonly record struct FinisherForecastResult(int ResolvedHits, bool RequiresAfterCardPlayed);

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

        result = new FinisherForecastResult(hits, postCardEffects.Count > 0);
        var states = enemies.ToDictionary(enemy => enemy, enemy => new ForecastState(
            enemy.CurrentHp,
            enemy.Block,
            enemy.GetPowerAmount<KaratePower>()));
        bool Finish(Dictionary<Creature, ForecastState> finalStates) =>
            ApplyPostCardEffects(owner, finalStates, postCardEffects, 0);

        return spec.Targeting switch
        {
            FinisherTargeting.Single => (spec.SingleTarget ?? spec.CardPlay.Target) is { } target
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
            FinisherTargeting.Fixed => spec.FixedTargets is { Count: > 0 } fixedTargets
                && fixedTargets.All(states.ContainsKey)
                && SimulateFixed(
                    owner,
                    states,
                    spec,
                    hits,
                    current => fixedTargets.Where(target => current[target].Hp > 0).ToList(),
                    Finish),
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
