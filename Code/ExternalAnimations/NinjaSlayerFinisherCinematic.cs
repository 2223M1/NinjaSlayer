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
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Scripts;
using static NinjaSlayer.Code.ExternalAnimations.FinisherTimeline;

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
        int hitCount = hitCountOverride
            ?? (HitPreviewResolver.TryResolve(card, cardPlay.Target, out int resolvedHits) ? resolvedHits : 0);
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

    private static readonly object SessionRegistrySync = new();
    private static FinisherSession? _active;
    private static FinisherSession? _pendingAfterCardPlayed;
    private static ICombatState? _epochCombatState;
    private static NCombatRoom? _epochRoom;
    private static long _nextSessionId;
    private static long _combatEpoch;
    private static long _registryGeneration;
    private static int _compatibilityWarningLogged;
    private static readonly AsyncLocal<CommandBypassFrame?> CommandBypass = new();
    private static readonly AsyncLocal<int> DirectDamageBypassDepth = new();

    public static bool IsMovementOwned(Creature creature) => GetActiveSession()?.Owner == creature;

    internal static void TryProtectLethalDamage(
        Creature target,
        ref decimal amount,
        out FinisherProtectionToken? token)
    {
        token = null;
        if (NinjaSlayerPatchCapabilities.FinisherEnabled)
        {
            GetActiveSession()?.TryProtectLethalDamage(target, ref amount, out token);
        }
    }

    internal static void ConfirmProtectedDamageResult(
        DamageResult? result,
        bool originalRan,
        FinisherProtectionToken? token)
    {
        if (token == null)
        {
            return;
        }

        try
        {
            if (result != null)
            {
                token.Ledger.Confirm(token, result, originalRan);
            }
        }
        catch (Exception ex)
        {
            LogErrorSafely($"Could not confirm NinjaSlayer finisher lethal protection: {ex}");
        }
        finally
        {
            if (token.IsConfirmed
                && GetActiveSession() is { } session
                && session.SessionId == token.SessionId
                && session.CombatEpoch == token.CombatEpoch)
            {
                session.NotifyProtectedDamageConfirmed();
            }
        }
    }

    internal static void FinalizeLethalProtection(FinisherProtectionToken? token)
    {
        token?.Ledger.FinalizeProtection(token);
    }

    internal static bool TryTakeDamageDisplayOverride(DamageResult result, out int displayDamage)
    {
        if (GetActiveSession() is { } session)
        {
            return session.TryTakeDamageDisplayOverride(result, out displayDamage);
        }

        displayDamage = 0;
        return false;
    }

    internal static void NotifyPrimaryAttackAnimation(Creature creature, string triggerName)
    {
        GetActiveSession()?.NotifyPrimaryAttackAnimation(creature, triggerName);
    }

    internal static void NotifyPrimaryDamage(Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        GetActiveSession()?.NotifyPrimaryDamage(dealer, cardSource, cardPlay);
    }

    internal static bool TryInterceptAttackCommand(
        AttackCommand command,
        PlayerChoiceContext? choiceContext,
        out Task<AttackCommand>? result)
    {
        result = null;
        if (!NinjaSlayerPatchCapabilities.FinisherEnabled
            || HasRegisteredSession()
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
        if (!NinjaSlayerPatchCapabilities.FinisherEnabled
            || HasRegisteredSession()
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
            AttackCommand result = await ExecuteOriginalCommand(command, choiceContext);
            if (session.RequiresAfterCardPlayed)
            {
                TransferToAfterCardPlayed(session);
                transferred = true;
            }
            else
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Succeeded,
                    FinisherCompletionMode.PlayPose);
            }

            return result;
        }
        catch (Exception ex)
        {
            await session.CompleteAsync(
                FinisherCompletionStatus.Faulted,
                FinisherCompletionMode.CommitWithoutPose,
                ex.Message);
            throw;
        }
        finally
        {
            if (!transferred)
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Cancelled,
                    FinisherCompletionMode.CommitWithoutPose,
                    "Command wrapper exited before normal completion.");
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
            await sequence();
            if (session.RequiresAfterCardPlayed)
            {
                TransferToAfterCardPlayed(session);
                transferred = true;
            }
            else
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Succeeded,
                    FinisherCompletionMode.PlayPose);
            }
        }
        catch (Exception ex)
        {
            await session.CompleteAsync(
                FinisherCompletionStatus.Faulted,
                FinisherCompletionMode.CommitWithoutPose,
                ex.Message);
            throw;
        }
        finally
        {
            if (!transferred)
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Cancelled,
                    FinisherCompletionMode.CommitWithoutPose,
                    "Sequence wrapper exited before normal completion.");
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
            await damageAction();
            if (session.RequiresAfterCardPlayed)
            {
                TransferToAfterCardPlayed(session);
                transferred = true;
            }
            else
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Succeeded,
                    FinisherCompletionMode.PlayPose);
            }
        }
        catch (Exception ex)
        {
            await session.CompleteAsync(
                FinisherCompletionStatus.Faulted,
                FinisherCompletionMode.CommitWithoutPose,
                ex.Message);
            throw;
        }
        finally
        {
            if (!transferred)
            {
                await session.CompleteAsync(
                    FinisherCompletionStatus.Cancelled,
                    FinisherCompletionMode.CommitWithoutPose,
                    "Direct-damage wrapper exited before normal completion.");
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
        if (!NinjaSlayerPatchCapabilities.FinisherEnabled
            || HasRegisteredSession()
            || IsExcludedAttackCard(spec.Card)
            || spec.Card.Owner?.Creature is not { } owner
            || owner.Player?.Character is not INinjaSlayerCharacter
            || owner.CombatState is not { } combatState
            || NCombatRoom.Instance is not { } room)
        {
            return false;
        }

        if (!GameCompatibility.Finisher.CanProtectLethalDamage(out string compatibilityReason))
        {
            if (Interlocked.Exchange(ref _compatibilityWarningLogged, 1) == 0)
            {
                LogWarningSafely(
                    $"NinjaSlayer enhanced finisher disabled for this process: {compatibilityReason} "
                    + $"supportedGame={GameCompatibility.SupportedGameVersion}.");
            }

            return false;
        }

        List<Creature> enemies = combatState.HittableEnemies.Where(enemy => enemy.IsAlive).ToList();
        if (enemies.Count == 0
            || FinisherForecast.Evaluate(owner, enemies, spec, command, out FinisherForecastResult forecast)
                != FinisherForecastOutcome.Guaranteed)
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

        if (!TryRegisterSession(
                owner,
                ownerNode,
                focusNode,
                enemies,
                camera!,
                spec.CardPlay,
                forecast.RequiresAfterCardPlayed,
                forecast.ResolvedHits,
                combatState,
                room,
                out session))
        {
            camera!.Dispose();
            return false;
        }

        LogInfoSafely(
            $"NinjaSlayer finisher session {session!.SessionId} started: card={spec.Card.Id.Entry}, entry={entryPoint}, targeting={spec.Targeting}, hits={forecast.ResolvedHits}.");
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
        lock (SessionRegistrySync)
        {
            if (!ReferenceEquals(_active, session)
                || _pendingAfterCardPlayed != null
                || !session.TryAwaitPostCard())
            {
                throw new InvalidOperationException("A NinjaSlayer finisher is already awaiting AfterCardPlayed.");
            }

            _pendingAfterCardPlayed = session;
        }
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

        if (GetPendingSession(cardPlay) != null)
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
            if (GetPendingSession(card) != null)
            {
                await CleanupPending(card, playPose: false);
            }
        }
    }

    private static async Task CleanupPending(CardModel card, bool playPose)
    {
        FinisherSession? session = GetPendingSession(card);
        if (session == null)
        {
            return;
        }

        await session.CompleteAsync(
            playPose ? FinisherCompletionStatus.Succeeded : FinisherCompletionStatus.Degraded,
            playPose ? FinisherCompletionMode.PlayPose : FinisherCompletionMode.CommitWithoutPose,
            playPose ? null : "Card resolution ended before AfterCardPlayed completed.");
    }

    private static FinisherSession? GetActiveSession()
    {
        lock (SessionRegistrySync)
        {
            return _active;
        }
    }

    private static bool HasRegisteredSession()
    {
        lock (SessionRegistrySync)
        {
            return _active != null || _pendingAfterCardPlayed != null;
        }
    }

    private static FinisherSession? GetPendingSession(CardPlay cardPlay)
    {
        lock (SessionRegistrySync)
        {
            return _pendingAfterCardPlayed?.CardPlay == cardPlay ? _pendingAfterCardPlayed : null;
        }
    }

    private static FinisherSession? GetPendingSession(CardModel card)
    {
        lock (SessionRegistrySync)
        {
            return _pendingAfterCardPlayed?.CardPlay.Card == card ? _pendingAfterCardPlayed : null;
        }
    }

    private static bool TryRegisterSession(
        Creature owner,
        NCreature ownerNode,
        NCreature focusNode,
        IEnumerable<Creature> victims,
        CombatCinematicCameraLease camera,
        CardPlay cardPlay,
        bool requiresAfterCardPlayed,
        int resolvedHits,
        ICombatState combatState,
        NCombatRoom room,
        out FinisherSession? session)
    {
        lock (SessionRegistrySync)
        {
            if (_active != null || _pendingAfterCardPlayed != null)
            {
                session = null;
                return false;
            }

            if (!ReferenceEquals(_epochCombatState, combatState) || !ReferenceEquals(_epochRoom, room))
            {
                _epochCombatState = combatState;
                _epochRoom = room;
                _combatEpoch++;
            }

            long sessionId = ++_nextSessionId;
            long registryGeneration = ++_registryGeneration;
            try
            {
                session = new FinisherSession(
                    sessionId,
                    _combatEpoch,
                    registryGeneration,
                    combatState,
                    room,
                    owner,
                    ownerNode,
                    focusNode,
                    victims,
                    camera,
                    cardPlay,
                    requiresAfterCardPlayed,
                    resolvedHits);
            }
            catch (Exception ex)
            {
                session = null;
                LogWarningSafely($"Could not create NinjaSlayer finisher session {sessionId}: {ex}");
                return false;
            }

            _active = session;
            return true;
        }
    }

    private static bool IsSessionCurrent(FinisherSession session)
    {
        lock (SessionRegistrySync)
        {
            return session.RegistryGeneration == _registryGeneration
                && (ReferenceEquals(_active, session) || ReferenceEquals(_pendingAfterCardPlayed, session));
        }
    }

    private static void MarkSessionCompleting(FinisherSession session)
    {
        lock (SessionRegistrySync)
        {
            if (ReferenceEquals(_pendingAfterCardPlayed, session))
            {
                _pendingAfterCardPlayed = null;
            }
        }
    }

    private static void UnregisterSession(FinisherSession session)
    {
        lock (SessionRegistrySync)
        {
            bool changed = false;
            if (ReferenceEquals(_active, session))
            {
                _active = null;
                changed = true;
            }

            if (ReferenceEquals(_pendingAfterCardPlayed, session))
            {
                _pendingAfterCardPlayed = null;
                changed = true;
            }

            if (changed)
            {
                _registryGeneration++;
            }
        }
    }

    private static void LogErrorSafely(string message)
    {
        try
        {
            Entry.Logger.Error(message);
        }
        catch
        {
        }
    }

    private static void LogInfoSafely(string message)
    {
        try
        {
            Entry.Logger.Info(message);
        }
        catch
        {
        }
    }

    private static void LogWarningSafely(string message)
    {
        try
        {
            Entry.Logger.Warn(message);
        }
        catch
        {
        }
    }

    private sealed class FinisherSession : IAsyncDisposable
    {
        private readonly ICombatState _combatState;
        private readonly NCreature _ownerNode;
        private readonly NCreature _focusNode;
        private readonly FinisherDamageLedger _ledger;
        private readonly Dictionary<Node2D, Vector2> _deathSquashOriginalScales = [];
        private readonly CombatCinematicCameraLease _camera;
        private readonly NCombatRoom _room;
        private readonly Vector2 _ownerStartPosition;
        private readonly HashSet<ulong> _vfxBaselineChildIds;
        private readonly bool _usesJumpDeathSquash;
        private readonly FinisherCompletionProtocol _completionProtocol;
        private FinisherCameraFrame _cameraFrame = new([], false);
        private readonly CinematicSessionLifetime _impactCancellation = new();
        private readonly CinematicSessionLifetime _watchdogCancellation = new();
        private ulong _lastFrameMsec;
        private ulong _lastDeltaFrame = ulong.MaxValue;
        private float _cachedFrameDelta;
        private Task _cameraTransitionTask = Task.CompletedTask;
        private Task _backdropTransitionTask = Task.CompletedTask;
        private Task _enhancedImpactTask = Task.CompletedTask;
        private Task _cameraShakePumpTask = Task.CompletedTask;
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
        private bool _deathCommitStarted;
        private bool _disposed;
        private NinjaSlayerHoverTipSuppression? _hoverTipSuppression;
        private FinisherCardVisualSuppression? _cardVisualSuppression;
        private FinisherImpactPresentation? _presentation;

        public FinisherSession(
            long sessionId,
            long combatEpoch,
            long registryGeneration,
            ICombatState combatState,
            NCombatRoom room,
            Creature owner,
            NCreature ownerNode,
            NCreature focusNode,
            IEnumerable<Creature> victims,
            CombatCinematicCameraLease camera,
            CardPlay cardPlay,
            bool requiresAfterCardPlayed,
            int resolvedHits)
        {
            SessionId = sessionId;
            CombatEpoch = combatEpoch;
            RegistryGeneration = registryGeneration;
            _combatState = combatState;
            _room = room;
            Owner = owner;
            _ownerNode = ownerNode;
            _focusNode = focusNode;
            _camera = camera;
            _completionProtocol = new FinisherCompletionProtocol(sessionId);
            _ledger = new FinisherDamageLedger(
                victims,
                sessionId,
                combatEpoch,
                combatState,
                IsCurrentCombatContext);
            _ownerStartPosition = ownerNode.Position;
            _vfxBaselineChildIds = _room.CombatVfxContainer.GetChildren()
                .Select(child => child.GetInstanceId())
                .ToHashSet();
            _room.TreeExiting += OnRoomTreeExiting;
            _lastFrameMsec = Time.GetTicksMsec();
            _usesJumpDeathSquash = JumpAnimation.IsActive(owner);
            CardPlay = cardPlay;
            RequiresAfterCardPlayed = requiresAfterCardPlayed;
            ResolvedHits = Math.Max(1, resolvedHits);
        }

        public long SessionId { get; }
        public long CombatEpoch { get; }
        public long RegistryGeneration { get; }
        public Creature Owner { get; }
        public CardPlay CardPlay { get; }
        public bool RequiresAfterCardPlayed { get; }
        public int ResolvedHits { get; }
        public Task<FinisherCompletionResult> Completion => _completionProtocol.Completion;

        public Task Begin()
        {
            if (!_completionProtocol.TryStart())
            {
                throw new InvalidOperationException(
                    $"Finisher session {SessionId} cannot begin from phase {_completionProtocol.Phase}.");
            }

            _ = RunWatchdog();
            NinjaSlayerFacingState.SyncForTarget(Owner, _focusNode.Entity);
            if (PresentationMode == FinisherPresentationMode.Enhanced)
            {
                _hoverTipSuppression = NinjaSlayerHoverTipSuppression.Acquire();
                _cardVisualSuppression = FinisherCardVisualSuppression.Acquire(_room, CardPlay);
                try
                {
                    _presentation = FinisherImpactPresentation.Create(_room, _ledger.Victims.Count);
                }
                catch (Exception ex)
                {
                    _enhancedImpactFailed = true;
                    LogWarningSafely($"Could not create enhanced finisher presentation; legacy presentation will be used: {ex}");
                }
            }

            Vector2 destination = ResolveApproachPosition(_ownerNode, _focusNode);
            _ownerNode.Position = destination;
            CanvasItem cameraFocus = GetCameraFocus();
            List<NCreature> framingCandidates = _ledger.Victims
                .Select(victim => _room.GetCreatureNode(victim))
                .Where(node => node != null)
                .Cast<NCreature>()
                .ToList();
            float maximumScale = _camera.BaselineScale.X
                * FinalHitZoomMultiplier
                * CameraPunchScaleMultiplier;
            _cameraFrame = FinisherCameraFraming.SelectTargets(
                _camera,
                cameraFocus,
                framingCandidates,
                maximumScale);
            _cameraShakePumpTask = RunCameraShakePump();
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

        public bool TryAwaitPostCard() => _completionProtocol.TryAwaitPostCard();

        public void NotifyPrimaryAttackAnimation(Creature creature, string triggerName)
        {
            if (_disposed
                || _committing
                || ResolvedHits <= 1
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
            if (_disposed
                || _committing
                || dealer != Owner
                || cardSource != CardPlay.Card
                || cardPlay != CardPlay)
            {
                return;
            }

            _primaryDamageCalls++;
            bool isFinalHit = _primaryDamageCalls >= ResolvedHits;
            _camera.PlayScreenShake(
                isFinalHit ? ShakeStrength.TooMuch : ShakeStrength.Medium,
                ShakeDuration.Short,
                rejectWeakerReplacement: true);
            if (ResolvedHits > 1 && isFinalHit)
            {
                StartFinalZoom();
            }

            TryScheduleEnhancedImpact();
        }

        public bool TryProtectLethalDamage(
            Creature target,
            ref decimal amount,
            out FinisherProtectionToken? token)
        {
            token = null;
            if (_disposed
                || !IsCurrentCombatContext()
                || !_ledger.TryProtect(target, _committing, ref amount, out token))
            {
                return false;
            }

            return true;
        }

        public void NotifyProtectedDamageConfirmed()
        {
            if (!_disposed && !_committing)
            {
                TryScheduleEnhancedImpact();
            }
        }

        public bool TryTakeDamageDisplayOverride(DamageResult result, out int displayDamage) =>
            _ledger.TryTakeDamageDisplayOverride(result, out displayDamage);

        public async Task<FinisherCompletionResult> CompleteAsync(
            FinisherCompletionStatus requestedStatus,
            FinisherCompletionMode requestedMode,
            string? diagnostic = null)
        {
            if (!_completionProtocol.TryBeginCompletion())
            {
                return await Completion;
            }

            MarkSessionCompleting(this);
            FinisherCompletionStatus status = requestedStatus;
            FinisherCompletionMode mode = requestedMode;
            string? finalDiagnostic = diagnostic;
            bool currentCombat = IsCurrentCombatContext();
            if (!currentCombat)
            {
                status = FinisherCompletionStatus.Cancelled;
                mode = FinisherCompletionMode.ReleaseOnly;
                finalDiagnostic = AppendDiagnostic(finalDiagnostic, "Combat or room generation changed before completion.");
            }

            try
            {
                if (mode != FinisherCompletionMode.ReleaseOnly)
                {
                    if (!_completionProtocol.TryTransition(FinisherSessionPhase.Committing))
                    {
                        throw new InvalidOperationException(
                            $"Finisher session {SessionId} cannot commit from phase {_completionProtocol.Phase}.");
                    }

                    if (mode == FinisherCompletionMode.PlayPose)
                    {
                        bool posePlayed = await CommitDeathsWithPoseCore();
                        if (!posePlayed)
                        {
                            status = FinisherCompletionStatus.Degraded;
                            mode = FinisherCompletionMode.CommitWithoutPose;
                            finalDiagnostic = AppendDiagnostic(
                                finalDiagnostic,
                                "Runtime damage did not satisfy the forecast or target visuals were unavailable.");
                        }
                    }
                    else
                    {
                        await CommitDeferredDeathsWithoutPoseCore();
                    }
                }
            }
            catch (Exception ex)
            {
                status = FinisherCompletionStatus.Faulted;
                finalDiagnostic = AppendDiagnostic(finalDiagnostic, ex.Message);
                LogErrorSafely($"NinjaSlayer finisher session {SessionId} completion failed: {ex}");
                if (IsCurrentCombatContext() && mode != FinisherCompletionMode.ReleaseOnly)
                {
                    try
                    {
                        mode = FinisherCompletionMode.CommitWithoutPose;
                        await CommitConfirmedDeathsEmergencyCore();
                    }
                    catch (Exception fallbackEx)
                    {
                        finalDiagnostic = AppendDiagnostic(finalDiagnostic, $"Fallback commit failed: {fallbackEx.Message}");
                        LogErrorSafely(
                            $"NinjaSlayer finisher session {SessionId} fallback death commit failed: {fallbackEx}");
                    }
                }
            }
            finally
            {
                _completionProtocol.TryTransition(FinisherSessionPhase.Restoring);
                bool mayRestoreCurrentCombat = mode != FinisherCompletionMode.ReleaseOnly
                    && IsCurrentCombatContext();
                if (!mayRestoreCurrentCombat)
                {
                    mode = FinisherCompletionMode.ReleaseOnly;
                    if (status != FinisherCompletionStatus.Faulted)
                    {
                        status = FinisherCompletionStatus.Cancelled;
                    }
                }

                try
                {
                    await RestoreResourcesCore(mayRestoreCurrentCombat);
                }
                catch (Exception ex)
                {
                    status = FinisherCompletionStatus.Faulted;
                    finalDiagnostic = AppendDiagnostic(finalDiagnostic, $"Resource restoration failed: {ex.Message}");
                    LogErrorSafely($"NinjaSlayer finisher session {SessionId} restoration failed: {ex}");
                }
                finally
                {
                    UnregisterSession(this);
                    _completionProtocol.TryTransition(FinisherSessionPhase.Finished);
                    _completionProtocol.Finish(new FinisherCompletionResult(
                        SessionId,
                        status,
                        mode,
                        finalDiagnostic));
                }
            }

            return await Completion;
        }

        public async ValueTask DisposeAsync()
        {
            bool currentCombat = IsCurrentCombatContext();
            await CompleteAsync(
                FinisherCompletionStatus.Cancelled,
                currentCombat ? FinisherCompletionMode.CommitWithoutPose : FinisherCompletionMode.ReleaseOnly,
                "Finisher session was disposed before normal completion.");
        }

        private async Task<bool> CommitDeathsWithPoseCore()
        {
            _committing = true;
            _ledger.ReleasePendingProtections(mayRestoreCurrentCombat: true);
            bool guaranteedClearMatchedRuntime = _ledger.GuaranteedClearMatchedRuntime();
            List<Creature> toKill = _ledger.LivingDeferredDeaths();
            if (!guaranteedClearMatchedRuntime)
            {
                LogWarningSafely(
                    $"Finisher session {SessionId} forecast did not match runtime damage; committing confirmed deaths without the pose.");
                await KillDeferredDeathsOnce(toKill);
                return false;
            }

            List<NCreature> targetNodes = toKill
                .Select(creature => _room.GetCreatureNode(creature))
                .Where(node => node != null && GodotObject.IsInstanceValid(node))
                .Cast<NCreature>()
                .ToList();
            if (toKill.Count > 0 && targetNodes.Count == 0)
            {
                await KillDeferredDeathsOnce(toKill);
                return false;
            }

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

            if (await KillDeferredDeathsOnce(toKill))
            {
                await WaitSeconds(FinisherSettleSeconds);
            }

            return true;
        }

        private async Task CommitDeferredDeathsWithoutPoseCore()
        {
            _committing = true;
            _impactCancellation.Cancel();
            await _enhancedImpactTask;
            _ledger.ReleasePendingProtections(mayRestoreCurrentCombat: true);
            await KillDeferredDeathsOnce(_ledger.LivingDeferredDeaths());
        }

        private async Task CommitConfirmedDeathsEmergencyCore()
        {
            _committing = true;
            try
            {
                _impactCancellation.Cancel();
            }
            catch (Exception ex)
            {
                LogWarningSafely(
                    $"Finisher session {SessionId} could not cancel its impact during fallback commit: {ex}");
            }

            try
            {
                _ledger.ReleasePendingProtections(mayRestoreCurrentCombat: true);
            }
            catch (Exception ex)
            {
                LogWarningSafely(
                    $"Finisher session {SessionId} could not release every pending protection during fallback commit: {ex}");
            }

            await KillDeferredDeathsOnce(_ledger.LivingDeferredDeaths());
        }

        private async Task<bool> KillDeferredDeathsOnce(IEnumerable<Creature> deferredDeaths)
        {
            if (_deathCommitStarted || !IsCurrentCombatContext())
            {
                return false;
            }

            List<Creature> toKill = deferredDeaths.Where(creature => creature.IsAlive).Distinct().ToList();
            if (toKill.Count == 0)
            {
                _deathCommitStarted = true;
                return false;
            }

            try
            {
                RestoreDeathSquashes();
            }
            catch (Exception ex)
            {
                LogWarningSafely(
                    $"Finisher session {SessionId} could not restore a death squash before committing deaths: {ex}");
            }

            _deathCommitStarted = true;
            await CreatureCmd.Kill(toKill);
            return true;
        }

        private async Task RestoreResourcesCore(bool mayRestoreCurrentCombat)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var cleanup = new FinisherCleanupAccumulator();
            cleanup.Capture(_watchdogCancellation.Cancel);
            cleanup.Capture(_impactCancellation.Cancel);
            await cleanup.CaptureAsync(() => _enhancedImpactTask);
            _cameraTransitionGeneration++;
            _backdropTransitionGeneration++;
            await cleanup.CaptureAsync(() => _cameraTransitionTask);
            await cleanup.CaptureAsync(() => _backdropTransitionTask);
            if (mayRestoreCurrentCombat)
            {
                await cleanup.CaptureAsync(ReturnToBaseline);
            }
            await cleanup.CaptureAsync(() => _cameraShakePumpTask);

            if (mayRestoreCurrentCombat && GodotObject.IsInstanceValid(_ownerNode))
            {
                cleanup.Capture(() => _ownerNode.Position = _ownerStartPosition);
            }

            cleanup.Capture(() => _hoverTipSuppression?.Dispose());
            _hoverTipSuppression = null;
            cleanup.Capture(() => _cardVisualSuppression?.Dispose());
            _cardVisualSuppression = null;
            cleanup.Capture(() => _ledger.Clear(mayRestoreCurrentCombat));
            cleanup.Capture(RestoreDeathSquashes);
            cleanup.Capture(DisposeEnhancedPresentation);
            if (GodotObject.IsInstanceValid(_room))
            {
                cleanup.Capture(() => _room.TreeExiting -= OnRoomTreeExiting);
            }
            cleanup.Capture(_impactCancellation.Dispose);
            cleanup.Capture(_watchdogCancellation.Dispose);
            cleanup.Capture(_camera.Dispose);
            cleanup.ThrowIfAny(
                $"Finisher session {SessionId} encountered {cleanup.FailureCount} resource-restoration failure(s).");
        }

        private async Task RunWatchdog()
        {
            try
            {
                float elapsed = 0f;
                while (elapsed < WatchdogSeconds)
                {
                    _watchdogCancellation.Token.ThrowIfCancellationRequested();
                    if (!IsCurrentCombatContext())
                    {
                        await CompleteAsync(
                            FinisherCompletionStatus.Cancelled,
                            FinisherCompletionMode.ReleaseOnly,
                            "Combat room changed while the finisher was active.");
                        return;
                    }

                    elapsed += await NextFrame();
                }

                if (_disposed)
                {
                    return;
                }

                LogErrorSafely(
                    $"NinjaSlayer finisher session {SessionId} exceeded 90 active seconds; committing confirmed deaths and restoring state.");
                await CompleteAsync(
                    FinisherCompletionStatus.Degraded,
                    FinisherCompletionMode.CommitWithoutPose,
                    "Finisher watchdog expired.");
            }
            catch (OperationCanceledException) when (_watchdogCancellation.IsCancellationRequested || _disposed)
            {
            }
            catch (OperationCanceledException ex)
            {
                await CompleteAsync(
                    FinisherCompletionStatus.Cancelled,
                    FinisherCompletionMode.ReleaseOnly,
                    ex.Message);
            }
            catch (Exception ex)
            {
                LogErrorSafely($"NinjaSlayer finisher session {SessionId} watchdog failed: {ex}");
                await CompleteAsync(
                    FinisherCompletionStatus.Faulted,
                    IsCurrentCombatContext()
                        ? FinisherCompletionMode.CommitWithoutPose
                        : FinisherCompletionMode.ReleaseOnly,
                    ex.Message);
            }
        }

        private void OnRoomTreeExiting()
        {
            _ = CompleteAfterRoomExit();
        }

        private async Task CompleteAfterRoomExit()
        {
            try
            {
                await CompleteAsync(
                    FinisherCompletionStatus.Cancelled,
                    FinisherCompletionMode.ReleaseOnly,
                    "Combat room exited the scene tree.");
            }
            catch (Exception ex)
            {
                LogErrorSafely($"NinjaSlayer finisher session {SessionId} room-exit cleanup failed: {ex}");
            }
        }

        private bool IsCurrentCombatContext() =>
            IsSessionCurrent(this)
            && ReferenceEquals(Owner.CombatState, _combatState)
            && ReferenceEquals(NCombatRoom.Instance, _room)
            && GodotObject.IsInstanceValid(_room)
            && _room.IsInsideTree();

        private static string AppendDiagnostic(string? current, string next) =>
            string.IsNullOrWhiteSpace(current) ? next : $"{current} {next}";

        private void TryScheduleEnhancedImpact()
        {
            if (PresentationMode != FinisherPresentationMode.Enhanced
                || _enhancedImpactScheduled
                || _enhancedImpactFailed
                || _disposed
                || !IsFinalPrimaryHitReady()
                || !_ledger.GuaranteedClearMatchedRuntime())
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
                List<NCreature> targetNodes = _ledger.DeferredDeaths
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
                LogWarningSafely($"Enhanced finisher impact failed; legacy presentation will be used: {ex}");
            }
        }

        private async Task PlayDoomPoseImpact(IReadOnlyList<NCreature> targetNodes)
        {
            float impactDirection = ResolveImpactDirection(_ownerNode, _focusNode);
            Vector2 cameraStartPosition = _camera.CurrentPosition;
            float cameraStartScale = _camera.CurrentScale;
            float punchScale = cameraStartScale * CameraPunchScaleMultiplier;
            Vector2 punchPosition = GetFramedCameraPosition(
                punchScale,
                impactDirection * CameraPushPixels);
            var impactVisuals = new Dictionary<Node2D, ImpactVisualSnapshot>();
            CaptureImpactVisuals(targetNodes, impactVisuals);
            ApplyDeathSquashes(impactVisuals.Values);
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
                    if (DoomHurtPoseController.TryFreeze(targetNode))
                    {
                        frozenHurtTracks.Add(targetNode);
                    }
                }

                if (ownerSnapshot is { } snapshot)
                {
                    snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
                }

                _camera.PlayScreenShake(
                    ShakeStrength.TooMuch,
                    ShakeDuration.Short,
                    rejectWeakerReplacement: true);
                float elapsed = 0f;
                while (elapsed < ImpactLeadSeconds)
                {
                    elapsed += await NextFrame();
                    float progress = EaseOut(Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f));
                    ApplyEnemyPosition(impactVisuals.Values, progress);
                    ApplyEnemyFlash(impactVisuals.Values, progress);
                    _camera.SetTransform(
                        cameraStartPosition.Lerp(punchPosition, progress),
                        Mathf.Lerp(cameraStartScale, punchScale, progress));
                }

                RestoreEnemyFlash(impactVisuals.Values);
                await RecoverEnemyPositions(
                    impactVisuals.Values,
                    ImpactKnockbackRecoverySeconds,
                    NextFrame);
                float holdSeconds = DoomPoseSeconds
                    - ImpactLeadSeconds
                    - ImpactKnockbackRecoverySeconds
                    - ImpactRecoverySeconds;
                if (holdSeconds > 0f)
                {
                    await WaitSeconds(holdSeconds);
                }

                elapsed = 0f;
                while (elapsed < ImpactRecoverySeconds)
                {
                    elapsed += await NextFrame();
                    float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / ImpactRecoverySeconds);
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
                DoomHurtPoseController.Resume(frozenHurtTracks);
                RestoreImpactVisuals(impactVisuals.Values);
            }
        }

        private async Task PlayEnhancedDoomPoseImpact(
            IReadOnlyList<NCreature> targetNodes,
            CancellationToken cancellationToken)
        {
            float impactDirection = ResolveImpactDirection(_ownerNode, _focusNode);
            Vector2 cameraStartPosition = _camera.CurrentPosition;
            float cameraStartScale = _camera.CurrentScale;
            float punchScale = _camera.BaselineScale.X * FinalHitZoomMultiplier * CameraPunchScaleMultiplier;
            float recoveryScale = _camera.BaselineScale.X * FinalHitZoomMultiplier;
            Vector2 punchPosition = GetFramedCameraPosition(
                punchScale,
                impactDirection * CameraPushPixels);
            Vector2 recoveryPosition = GetFramedCameraPosition(recoveryScale);
            var impactVisuals = new Dictionary<Node2D, ImpactVisualSnapshot>();
            CaptureImpactVisuals(targetNodes, impactVisuals);
            ApplyDeathSquashes(impactVisuals.Values);
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
                    if (DoomHurtPoseController.TryFreeze(targetNode))
                    {
                        frozenHurtTracks.Add(targetNode);
                    }
                }

                if (ownerSnapshot is { } snapshot)
                {
                    snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
                }

                _camera.PlayScreenShake(
                    ShakeStrength.TooMuch,
                    ShakeDuration.Short,
                    rejectWeakerReplacement: true);
                float elapsed = 0f;
                while (elapsed < ImpactLeadSeconds)
                {
                    elapsed += await NextEnhancedFrame(cancellationToken);
                    float linearProgress = Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f);
                    float progress = EaseOut(linearProgress);
                    ApplyEnemyPosition(impactVisuals.Values, progress);
                    ApplyEnhancedEnemyFeedback(impactVisuals.Values, progress, flash: true);
                    presentation.SetImpactState(targetNodes, progress, Mathf.Sin(linearProgress * Mathf.Pi));
                    _camera.SetTransform(
                        cameraStartPosition.Lerp(punchPosition, progress),
                        Mathf.Lerp(cameraStartScale, punchScale, progress));
                }

                RestoreEnemyFlash(impactVisuals.Values);
                presentation.SetImpactState(targetNodes, 1f, 0f);
                await RecoverEnemyPositions(
                    impactVisuals.Values,
                    ImpactKnockbackRecoverySeconds,
                    () => NextEnhancedFrame(cancellationToken));
                float holdSeconds = DoomPoseSeconds
                    - ImpactLeadSeconds
                    - ImpactKnockbackRecoverySeconds
                    - ImpactRecoverySeconds;
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
                DoomHurtPoseController.Resume(frozenHurtTracks);
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
                LogWarningSafely($"Finisher backdrop transition failed; legacy presentation will be used: {ex}");
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
                Vector2 startPosition = _camera.CurrentPosition;
                float startScale = _camera.CurrentScale;
                float targetScale = _camera.BaselineScale.X * scaleMultiplier;
                Vector2 targetPosition = GetFramedCameraPosition(targetScale);
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
                LogWarningSafely($"Finisher camera transition failed: {ex}");
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
                    Vector2 originalScale = _deathSquashOriginalScales.GetValueOrDefault(body, body.Scale);
                    snapshots.Add(body, new ImpactVisualSnapshot(
                        body,
                        body.Position,
                        originalScale,
                        body.Rotation,
                        body.SelfModulate,
                        ResolveImpactDirection(_ownerNode, creatureNode)));
                }
            }
        }

        private CanvasItem GetCameraFocus() =>
            NinjaSlayerVisualRig.GetCinematicFocus(_ownerNode.Visuals) is { } cinematicFocus
                ? cinematicFocus
                : _ownerNode.Visuals.Bounds;

        private Vector2 GetFramedCameraPosition(float scale, float horizontalScreenOffset = 0f)
        {
            Vector2 center = FinisherCameraFraming.ResolveCenter(
                _camera,
                GetCameraFocus(),
                _cameraFrame,
                scale,
                horizontalScreenOffset);
            return _camera.GetCameraPosition(center, scale, _camera.ViewportSize * 0.5f);
        }

        private void ApplyDeathSquashes(IEnumerable<ImpactVisualSnapshot> snapshots)
        {
            Vector2 multiplier = GetDeathSquashMultiplier();
            foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
            {
                _deathSquashOriginalScales.TryAdd(snapshot.Body, snapshot.Scale);
                snapshot.Body.Scale = snapshot.Scale * multiplier;
            }
        }

        private void RestoreDeathSquashes()
        {
            foreach ((Node2D body, Vector2 scale) in _deathSquashOriginalScales)
            {
                if (GodotObject.IsInstanceValid(body))
                {
                    body.Scale = scale;
                }
            }

            _deathSquashOriginalScales.Clear();
        }

        private Vector2 GetDeathSquashMultiplier() =>
            _usesJumpDeathSquash ? JumpDeathSquash : DefaultDeathSquash;

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

        private static void ApplyEnemyPosition(
            IEnumerable<ImpactVisualSnapshot> snapshots,
            float amount)
        {
            foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
            {
                snapshot.Body.Position = snapshot.Position
                    + Vector2.Right * snapshot.Direction * EnemyKnockbackPixels * amount;
            }
        }

        private static void ApplyEnemyFlash(
            IEnumerable<ImpactVisualSnapshot> snapshots,
            float amount)
        {
            foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
            {
                snapshot.Body.SelfModulate = snapshot.SelfModulate.Lerp(
                    new Color(1.8f, 1.8f, 1.8f, snapshot.SelfModulate.A),
                    amount);
            }
        }

        private static async Task RecoverEnemyPositions(
            IReadOnlyCollection<ImpactVisualSnapshot> snapshots,
            float duration,
            Func<Task<float>> nextFrame)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += await nextFrame();
                float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / duration);
                ApplyEnemyPosition(snapshots, 1f - progress);
            }

            ApplyEnemyPosition(snapshots, 0f);
        }

        private void ApplyEnhancedEnemyFeedback(
            IEnumerable<ImpactVisualSnapshot> snapshots,
            float amount,
            bool flash)
        {
            Vector2 squashMultiplier = GetDeathSquashMultiplier();
            foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
            {
                snapshot.Body.Scale = snapshot.Scale * squashMultiplier;
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

        private void RestoreImpactVisuals(IEnumerable<ImpactVisualSnapshot> snapshots)
        {
            Vector2 squashMultiplier = GetDeathSquashMultiplier();
            foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
            {
                snapshot.Body.Position = snapshot.Position;
                snapshot.Body.Scale = _deathSquashOriginalScales.ContainsKey(snapshot.Body)
                    ? snapshot.Scale * squashMultiplier
                    : snapshot.Scale;
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
            }

            return _cachedFrameDelta;
        }

        private async Task RunCameraShakePump()
        {
            ulong lastFrameMsec = Time.GetTicksMsec();
            try
            {
                while (!_disposed && GodotObject.IsInstanceValid(_room) && _room.IsInsideTree())
                {
                    await _room.ToSignal(_room.GetTree(), SceneTree.SignalName.ProcessFrame);
                    ulong now = Time.GetTicksMsec();
                    float delta = _room.ProcessMode == Node.ProcessModeEnum.Disabled
                        ? 0f
                        : Math.Min((now - lastFrameMsec) / 1000f, 0.05f);
                    lastFrameMsec = now;
                    _camera.Advance(delta);
                }
            }
            catch (OperationCanceledException) when (_disposed || !GodotObject.IsInstanceValid(_room))
            {
            }
            catch (Exception ex)
            {
                LogWarningSafely($"Finisher camera shake pump stopped unexpectedly: {ex}");
            }
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
                target.Position.X - direction * (targetHalfWidth + NinjaSlayerCombatVisuals.CloseRangeApproachGap),
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
    public static bool TryCreateSpec(AttackCommand command, out FinisherAttackSpec? spec)
    {
        spec = null;
        if (!GameCompatibility.Finisher.TryReadAttackCommand(
                command,
                out GameCompatibility.AttackCommandState commandState)
            || command.ModelSource is not CardModel { Type: CardType.Attack } card
            || command.CardPlay is not { } cardPlay
            || command.Attacker == null
            || card.Owner?.Creature != command.Attacker)
        {
            return false;
        }

        CalculatedDamageVar? calculatedDamage = commandState.CalculatedDamage;
        decimal damagePerHit = commandState.DamagePerHit;
        int hitCount = commandState.HitCount;
        Creature? singleTarget = commandState.SingleTarget;
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

    public static FinisherForecastOutcome Evaluate(
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
            return FinisherForecastOutcome.NotGuaranteed;
        }

        int hits = spec.HitCount;
        if (command != null)
        {
            hits = (int)Math.Ceiling(Math.Max(0m, Hook.ModifyAttackHitCount(combatState, command, hits)));
        }

        if (hits <= 0)
        {
            return FinisherForecastOutcome.NotGuaranteed;
        }

        var enemyIndices = enemies
            .Select((enemy, index) => (enemy, index))
            .ToDictionary(pair => pair.enemy, pair => pair.index);
        List<FinisherForecastPostEffect<ForecastState>> postCardEffects = [];
        foreach (IFinisherForecastContributor contributor in PostCardContributors)
        {
            if (contributor.TryCreateEffect(owner, spec, out FinisherForecastEffect? effect) && effect != null)
            {
                postCardEffects.Add(new FinisherForecastPostEffect<ForecastState>(
                    effect.Targeting,
                    (states, targets) =>
                    {
                        foreach (int target in targets)
                        {
                            ApplyDamage(
                                owner,
                                enemies,
                                states,
                                target,
                                effect.Amount,
                                effect.Props,
                                effect.Dealer,
                                effect.CardSource,
                                effect.CardPlay);
                        }

                        return true;
                    }));
            }
        }

        result = new FinisherForecastResult(hits, postCardEffects.Count > 0);
        ForecastState[] states = enemies.Select(enemy => new ForecastState(
            enemy.CurrentHp,
            enemy.Block,
            enemy.GetPowerAmount<KaratePower>())).ToArray();
        Creature? singleTarget = spec.SingleTarget ?? spec.CardPlay.Target;
        int? singleTargetIndex = singleTarget != null && enemyIndices.TryGetValue(singleTarget, out int singleIndex)
            ? singleIndex
            : null;
        int[]? fixedTargets = spec.FixedTargets?
            .Where(enemyIndices.ContainsKey)
            .Select(target => enemyIndices[target])
            .ToArray();
        FinisherForecastTargeting targeting = spec.Targeting switch
        {
            FinisherTargeting.Single => FinisherForecastTargeting.Single,
            FinisherTargeting.All => FinisherForecastTargeting.All,
            FinisherTargeting.Random => FinisherForecastTargeting.Random,
            FinisherTargeting.Fixed => FinisherForecastTargeting.Fixed,
            _ => throw new ArgumentOutOfRangeException(nameof(spec.Targeting), spec.Targeting, null)
        };
        if (targeting == FinisherForecastTargeting.Single && (enemies.Count != 1 || singleTargetIndex == null)
            || targeting == FinisherForecastTargeting.Fixed
            && (fixedTargets is not { Length: > 0 } || fixedTargets.Length != spec.FixedTargets!.Count))
        {
            return FinisherForecastOutcome.NotGuaranteed;
        }

        var simulation = new FinisherForecastSimulation<ForecastState>(
            states,
            hits,
            targeting,
            state => state.Hp > 0,
            state => $"{state.Hp},{state.Block},{state.Karate}",
            (current, targets, hitIndex) =>
            {
                ApplyHit(owner, enemies, current, spec, targets, hitIndex);
                return true;
            },
            singleTargetIndex,
            fixedTargets,
            postCardEffects);
        return FinisherForecastEngine.Evaluate(simulation);
    }

    private static void ApplyHit(
        Creature owner,
        IReadOnlyList<Creature> enemies,
        ForecastState[] states,
        FinisherAttackSpec spec,
        IReadOnlyList<int> targets,
        int hitIndex)
    {
        List<(int Target, bool TriggerKarate)> damageResults = [];
        foreach (int targetIndex in targets)
        {
            if (states[targetIndex].Hp <= 0)
            {
                continue;
            }

            Creature target = enemies[targetIndex];
            decimal rawDamage = spec.Damage(target);
            decimal postHookMultiplier = spec.Card is TornadoFist && hitIndex > 0
                && target.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>() <= 0
                    ? 1.5m
                    : 1m;

            bool dealtDamage = ApplyDamage(
                owner,
                enemies,
                states,
                targetIndex,
                rawDamage,
                spec.Props,
                owner,
                spec.Card,
                spec.CardPlay,
                postHookMultiplier);
            damageResults.Add((targetIndex, dealtDamage));
        }

        foreach ((int target, bool triggerKarate) in damageResults)
        {
            ForecastState state = states[target];
            if (triggerKarate && state.Hp > 0 && state.Karate > 0 && spec.Props.IsPoweredAttack()
                && KarateTriggerRules.CanTriggerFromCardSource(spec.Card))
            {
                ApplyDamage(
                    owner,
                    enemies,
                    states,
                    target,
                    state.Karate,
                    ValueProp.Unpowered,
                    owner,
                    null,
                    null);
                ForecastState afterKarate = states[target];
                if (afterKarate.Hp > 0)
                {
                    states[target] = afterKarate with { Karate = Math.Max(0, afterKarate.Karate - 1) };
                }
            }

            if (owner.GetPower<NarakuPower>() is { } naraku && spec.Props.IsPoweredAttack())
            {
                foreach (int enemy in AliveTargets(states))
                {
                    ApplyDamage(
                        owner,
                        enemies,
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
        IReadOnlyList<Creature> enemies,
        ForecastState[] states,
        int targetIndex,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        decimal postHookMultiplier = 1m)
    {
        ForecastState state = states[targetIndex];
        if (state.Hp <= 0)
        {
            return false;
        }

        Creature target = enemies[targetIndex];
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
        states[targetIndex] = state with
        {
            Block = state.Block - blocked,
            Hp = state.Hp - Math.Max(0, (int)hpLoss)
        };
        return modified > 0m;
    }

    private static IEnumerable<int> AliveTargets(IReadOnlyList<ForecastState> states) =>
        Enumerable.Range(0, states.Count).Where(index => states[index].Hp > 0);

    private sealed record ForecastState(int Hp, int Block, int Karate);
}
