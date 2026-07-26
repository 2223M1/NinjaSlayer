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
using NinjaSlayer.Code.Diagnostics;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Scripts;
using static NinjaSlayer.Code.ExternalAnimations.FinisherTimeline;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class FinisherSession : IAsyncDisposable
{
    private readonly ICombatState _combatState;
    private readonly NCreature _actorNode;
    private readonly NCreature _focusNode;
    private readonly FinisherDamageLedger _ledger;
    private readonly Dictionary<Node2D, Vector2> _deathSquashOriginalScales = [];
    private readonly Dictionary<NCreature, DeathKickVisual> _deathKickVisuals = [];
    private readonly CombatCinematicCameraLease _camera;
    private readonly NCombatRoom _room;
    private readonly Vector2 _actorStartPosition;
    private readonly HashSet<ulong> _vfxBaselineChildIds;
    private readonly bool _usesJumpDeathSquash;
    private readonly bool _usesNinjaSlayerSignatureImpact;
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
    private Task _returnToBaselineTask = Task.CompletedTask;
    private int _cameraTransitionGeneration;
    private int _backdropTransitionGeneration;
    private int _primaryAnimationsStarted;
    private int _primaryDamageCalls;
    private float _backdropIntensity;
    private bool _finalZoomStarted;
    private bool _backdropDarkeningStarted;
    private bool _enhancedImpactScheduled;
    private bool _enhancedImpactFailed;
    private bool _impactAudioPlayed;
    private bool _committing;
    private bool _deathCommitStarted;
    private bool _returnTimelineStarted;
    private bool _returnTimelineCompleted;
    private float _returnTimelineProgress;
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
        FinisherSessionRequest request)
    {
        SessionId = sessionId;
        CombatEpoch = combatEpoch;
        RegistryGeneration = registryGeneration;
        _combatState = combatState;
        _room = room;
        Scenario = request.Scenario;
        CompletionCondition = request.CompletionCondition;
        Actor = request.Actor;
        _actorNode = request.ActorNode;
        _focusNode = request.FocusNode;
        _camera = request.Camera;
        _completionProtocol = new FinisherCompletionProtocol(sessionId);
        _ledger = new FinisherDamageLedger(
            request.Victims,
            sessionId,
            combatEpoch,
            combatState,
            IsCurrentCombatContext);
        _actorStartPosition = request.ActorNode.Position;
        _vfxBaselineChildIds = request.VfxBaselineChildIds?.ToHashSet()
            ?? _room.CombatVfxContainer.GetChildren()
                .Select(child => child.GetInstanceId())
                .ToHashSet();
        _room.TreeExiting += OnRoomTreeExiting;
        _lastFrameMsec = Time.GetTicksMsec();
        _usesJumpDeathSquash = request.Scenario == FinisherScenarioKind.NinjaSlayerAttack
            && JumpAnimation.IsActive(request.Actor);
        _usesNinjaSlayerSignatureImpact = request.UsesNinjaSlayerSignatureImpact;
        CardPlay = request.CardPlay;
        RequiresAfterCardPlayed = request.RequiresAfterCardPlayed;
        ResolvedHits = Math.Max(1, request.ResolvedHits);
    }

    public long SessionId { get; }
    public long CombatEpoch { get; }
    public long RegistryGeneration { get; }
    public FinisherScenarioKind Scenario { get; }
    public FinisherCompletionCondition CompletionCondition { get; }
    public Creature Actor { get; }
    public CardPlay? CardPlay { get; }
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
        if (Scenario == FinisherScenarioKind.NinjaSlayerAttack)
        {
            NinjaSlayerFacingState.SyncForTarget(Actor, _focusNode.Entity);
        }
        if (FinisherPresentationSettings.Mode == FinisherPresentationMode.Enhanced)
        {
            _hoverTipSuppression = NinjaSlayerHoverTipSuppression.Acquire();
            if (CardPlay is { } cardPlay)
            {
                _cardVisualSuppression = FinisherCardVisualSuppression.Acquire(_room, cardPlay);
            }
            try
            {
                _presentation = _usesNinjaSlayerSignatureImpact
                    ? FinisherImpactPresentation.Create(_room, _ledger.Victims.Count)
                    : FinisherImpactPresentation.CreateBackdropOnly(_room);
            }
            catch (Exception ex)
            {
                _enhancedImpactFailed = true;
                FinisherLog.Warn($"Could not create enhanced finisher presentation; legacy presentation will be used: {ex}");
            }
        }

        if (Scenario == FinisherScenarioKind.NinjaSlayerAttack)
        {
            Vector2 destination = ResolveApproachPosition(_actorNode, _focusNode);
            _actorNode.Position = destination;
        }
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
        bool deferReversePresentation = Scenario == FinisherScenarioKind.EnemyExecutesNinjaSlayer;
        _finalZoomStarted = !deferReversePresentation && ResolvedHits <= 1;
        if (!deferReversePresentation)
        {
            StartCameraTransition(
                ResolvedHits > 1 ? MultiHitZoomMultiplier : FinalHitZoomMultiplier,
                ResolvedHits > 1 ? MultiHitZoomSeconds : SingleHitZoomSeconds);
            if (ResolvedHits <= 1)
            {
                StartBackdropDarkening();
            }
        }

        return Task.CompletedTask;
    }

    public bool TryAwaitPostCard() => _completionProtocol.TryAwaitPostCard();

    public void NotifyPrimaryAttackAnimation(Creature creature, string triggerName)
    {
        if (_disposed
            || _committing
            || ResolvedHits <= 1
            || creature != Actor
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
            || dealer != Actor
            || CardPlay is not { } sessionCardPlay
            || cardSource != sessionCardPlay.Card
            || cardPlay != sessionCardPlay)
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

    public void NotifyDeathAnimationStarting(NCreature creatureNode)
    {
        if (_disposed
            || !_deathCommitStarted
            || !_deathKickVisuals.TryGetValue(creatureNode, out DeathKickVisual? visual)
            || visual.Triggered)
        {
            return;
        }

        visual.Triggered = true;
        if (!GodotObject.IsInstanceValid(visual.Body) || _returnTimelineCompleted)
        {
            RestoreDeathKick(visual);
            return;
        }

        visual.JoinedAtReturnProgress = _returnTimelineProgress;
        visual.Body.Position = visual.Position
            + Vector2.Right * visual.Direction * EnemyKnockbackPixels;
        StartReturnTimeline(includeSettle: true);
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
        if (!_disposed
            && !_committing
            && Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
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

        FinisherSessionRegistry.MarkSessionCompleting(this);
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
            FinisherLog.Error($"NinjaSlayer finisher session {SessionId} completion failed: {ex}");
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
                    FinisherLog.Error(
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
                FinisherLog.Error($"NinjaSlayer finisher session {SessionId} restoration failed: {ex}");
            }
            finally
            {
                FinisherSessionRegistry.UnregisterSession(this);
                _completionProtocol.TryTransition(FinisherSessionPhase.Finished);
                var completionResult = new FinisherCompletionResult(
                    SessionId,
                    status,
                    mode,
                    finalDiagnostic);
                NinjaSlayerRuntimeCounters.RecordFinisher(completionResult.Status);
                _completionProtocol.Finish(completionResult);
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
        bool guaranteedClearMatchedRuntime = IsCompletionConditionSatisfied();
        List<Creature> toKill = _ledger.LivingDeferredDeaths();
        if (!guaranteedClearMatchedRuntime)
        {
            FinisherLog.Warn(
                $"Finisher session {SessionId} forecast did not match runtime damage; committing confirmed deaths without the pose.");
            await KillDeferredDeathsOnce(toKill, useDeathKick: false);
            return false;
        }

        List<NCreature> targetNodes = toKill
            .Select(creature => _room.GetCreatureNode(creature))
            .Where(node => node != null && GodotObject.IsInstanceValid(node))
            .Cast<NCreature>()
            .ToList();
        if (toKill.Count > 0 && targetNodes.Count == 0)
        {
            await KillDeferredDeathsOnce(toKill, useDeathKick: false);
            return false;
        }

        if (targetNodes.Count > 0)
        {
            if (Scenario == FinisherScenarioKind.EnemyExecutesNinjaSlayer)
            {
                StartBackdropDarkening();
            }

            if (FinisherPresentationSettings.Mode == FinisherPresentationMode.Enhanced)
            {
                TryScheduleEnhancedImpact();
                await _enhancedImpactTask;
            }

            if (FinisherPresentationSettings.Mode == FinisherPresentationMode.Legacy
                || !_enhancedImpactScheduled
                || _enhancedImpactFailed)
            {
                if (FinisherPresentationSettings.Mode == FinisherPresentationMode.Enhanced)
                {
                    _finalZoomStarted = false;
                }

                StartFinalZoom();
                await _cameraTransitionTask;
                await PlayDoomPoseImpact(targetNodes);
            }
        }

        bool useDeathContinuation = true;
        if (await KillDeferredDeathsOnce(toKill, useDeathContinuation))
        {
            if (Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
            {
                StartReturnTimeline(includeSettle: true);
            }
        }

        return true;
    }

    private async Task CommitDeferredDeathsWithoutPoseCore()
    {
        _committing = true;
        _impactCancellation.Cancel();
        await _enhancedImpactTask;
        _ledger.ReleasePendingProtections(mayRestoreCurrentCombat: true);
        await KillDeferredDeathsOnce(_ledger.LivingDeferredDeaths(), useDeathKick: false);
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
            FinisherLog.Warn(
                $"Finisher session {SessionId} could not cancel its impact during fallback commit: {ex}");
        }

        try
        {
            _ledger.ReleasePendingProtections(mayRestoreCurrentCombat: true);
        }
        catch (Exception ex)
        {
            FinisherLog.Warn(
                $"Finisher session {SessionId} could not release every pending protection during fallback commit: {ex}");
        }

        await KillDeferredDeathsOnce(_ledger.LivingDeferredDeaths(), useDeathKick: false);
    }

    private async Task<bool> KillDeferredDeathsOnce(
        IEnumerable<Creature> deferredDeaths,
        bool useDeathKick)
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
            FinisherLog.Warn(
                $"Finisher session {SessionId} could not restore a death squash before committing deaths: {ex}");
        }

        if (useDeathKick && Scenario == FinisherScenarioKind.EnemyExecutesNinjaSlayer)
        {
            FinisherDeathContinuationRegistry.Arm(toKill, SessionId);
            StartReturnTimeline(includeSettle: false);
        }
        else if (useDeathKick)
        {
            ArmDeathKicks(toKill);
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
            await cleanup.CaptureAsync(EnsureReturnToBaseline);
        }
        await cleanup.CaptureAsync(() => _cameraShakePumpTask);

        if (mayRestoreCurrentCombat && GodotObject.IsInstanceValid(_actorNode))
        {
            cleanup.Capture(() => _actorNode.Position = _actorStartPosition);
        }

        cleanup.Capture(() => _hoverTipSuppression?.Dispose());
        _hoverTipSuppression = null;
        cleanup.Capture(() => _cardVisualSuppression?.Dispose());
        _cardVisualSuppression = null;
        cleanup.Capture(() => _ledger.Clear(mayRestoreCurrentCombat));
        cleanup.Capture(() => FinisherDeathContinuationRegistry.Clear(SessionId));
        cleanup.Capture(RestoreDeathSquashes);
        cleanup.Capture(RestoreDeathKicks);
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

            FinisherLog.Error(
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
            FinisherLog.Error($"NinjaSlayer finisher session {SessionId} watchdog failed: {ex}");
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
            FinisherLog.Error($"NinjaSlayer finisher session {SessionId} room-exit cleanup failed: {ex}");
        }
    }

    private bool IsCurrentCombatContext() =>
        FinisherSessionRegistry.IsSessionCurrent(this)
        && ReferenceEquals(Actor.CombatState, _combatState)
        && ReferenceEquals(NCombatRoom.Instance, _room)
        && GodotObject.IsInstanceValid(_room)
        && _room.IsInsideTree();

    private static string AppendDiagnostic(string? current, string next) =>
        string.IsNullOrWhiteSpace(current) ? next : $"{current} {next}";

    private void TryScheduleEnhancedImpact()
    {
        if (FinisherPresentationSettings.Mode != FinisherPresentationMode.Enhanced
            || _enhancedImpactScheduled
            || _enhancedImpactFailed
            || _disposed
            || !IsFinalPrimaryHitReady()
            || !IsCompletionConditionSatisfied())
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

    private bool IsCompletionConditionSatisfied() => CompletionCondition switch
    {
        FinisherCompletionCondition.AllCandidatesLethal => _ledger.GuaranteedClearMatchedRuntime(),
        FinisherCompletionCondition.AnyCandidateLethal => _ledger.DeferredDeaths.Count > 0,
        _ => false
    };

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
            FinisherLog.Warn($"Enhanced finisher impact failed; legacy presentation will be used: {ex}");
        }
    }

    private async Task PlayDoomPoseImpact(IReadOnlyList<NCreature> targetNodes)
    {
        float impactDirection = ResolveImpactDirection(_actorNode, _focusNode);
        Vector2 cameraStartPosition = _camera.CurrentPosition;
        float cameraStartScale = _camera.CurrentScale;
        float punchScale = cameraStartScale * CameraPunchScaleMultiplier;
        Vector2 punchPosition = GetFramedCameraPosition(
            punchScale,
            impactDirection * CameraPushPixels);
        var impactVisuals = new Dictionary<Node2D, ImpactVisualSnapshot>();
        CaptureImpactVisuals(targetNodes, impactVisuals);
        List<ReverseVictimVisualSnapshot> reverseVictims = CaptureReverseVictimVisuals(targetNodes);
        ApplyDeathSquashes(impactVisuals.Values);
        List<NCreature> frozenHurtTracks = [];
        List<ProcessModeSnapshot> frozenImpactVfx = [];
        Node actorFreezeNode = GetActorFreezeNode();
        ProcessModeSnapshot? ownerSnapshot = GodotObject.IsInstanceValid(actorFreezeNode)
            ? new ProcessModeSnapshot(actorFreezeNode, actorFreezeNode.ProcessMode)
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
            FreezeReverseVictimVisuals(reverseVictims);

            _camera.PlayScreenShake(
                ShakeStrength.TooMuch,
                ShakeDuration.Short,
                rejectWeakerReplacement: true);
            PlayReverseImpactAudio();
            float elapsed = 0f;
            while (elapsed < ImpactLeadSeconds)
            {
                elapsed += await NextFrame();
                float progress = EaseOut(Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f));
                ApplyEnemyFlash(impactVisuals.Values, progress);
                ApplyReverseVictimRotation(reverseVictims, progress);
                _camera.SetTransform(
                    cameraStartPosition.Lerp(punchPosition, progress),
                    Mathf.Lerp(cameraStartScale, punchScale, progress));
            }

            RestoreEnemyFlash(impactVisuals.Values);
            ApplyReverseVictimRotation(reverseVictims, 1f);
            float holdSeconds = DoomPoseSeconds
                - ImpactLeadSeconds
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
            RestoreReverseVictimVisuals(reverseVictims);
            RestoreImpactVisuals(impactVisuals.Values);
        }
    }

    private async Task PlayEnhancedDoomPoseImpact(
        IReadOnlyList<NCreature> targetNodes,
        CancellationToken cancellationToken)
    {
        float impactDirection = ResolveImpactDirection(_actorNode, _focusNode);
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
        List<ReverseVictimVisualSnapshot> reverseVictims = CaptureReverseVictimVisuals(targetNodes);
        ApplyDeathSquashes(impactVisuals.Values);
        List<NCreature> frozenHurtTracks = [];
        List<ProcessModeSnapshot> frozenImpactVfx = [];
        Node actorFreezeNode = GetActorFreezeNode();
        ProcessModeSnapshot? ownerSnapshot = GodotObject.IsInstanceValid(actorFreezeNode)
            ? new ProcessModeSnapshot(actorFreezeNode, actorFreezeNode.ProcessMode)
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
            FreezeReverseVictimVisuals(reverseVictims);

            _camera.PlayScreenShake(
                ShakeStrength.TooMuch,
                ShakeDuration.Short,
                rejectWeakerReplacement: true);
            PlayReverseImpactAudio();
            float elapsed = 0f;
            while (elapsed < ImpactLeadSeconds)
            {
                elapsed += await NextEnhancedFrame(cancellationToken);
                float linearProgress = Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f);
                float progress = EaseOut(linearProgress);
                ApplyEnhancedVictimFeedback(impactVisuals.Values, reverseVictims, progress, flash: true);
                SetSignatureImpactState(
                    presentation,
                    targetNodes,
                    progress,
                    Mathf.Sin(linearProgress * Mathf.Pi));
                _camera.SetTransform(
                    cameraStartPosition.Lerp(punchPosition, progress),
                    Mathf.Lerp(cameraStartScale, punchScale, progress));
            }

            RestoreEnemyFlash(impactVisuals.Values);
            SetSignatureImpactState(presentation, targetNodes, 1f, 0f);
            float holdSeconds = DoomPoseSeconds
                - ImpactLeadSeconds
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
                ApplyEnhancedVictimFeedback(
                    impactVisuals.Values,
                    reverseVictims,
                    1f - progress,
                    flash: false,
                    reverseRotationAmount: 1f);
                SetSignatureImpactState(presentation, targetNodes, 1f - progress, 0f);
                _camera.SetTransform(
                    punchPosition.Lerp(recoveryPosition, progress),
                    Mathf.Lerp(punchScale, recoveryScale, progress));
            }

            _camera.SetTransform(recoveryPosition, recoveryScale);
        }
        finally
        {
            SetSignatureImpactState(presentation, [], 0f, 0f);
            if (ownerSnapshot is { } snapshot && GodotObject.IsInstanceValid(snapshot.Node))
            {
                snapshot.Node.ProcessMode = snapshot.Mode;
            }

            RestoreProcessModes(frozenImpactVfx);
            DoomHurtPoseController.Resume(frozenHurtTracks);
            RestoreReverseVictimVisuals(reverseVictims);
            RestoreImpactVisuals(impactVisuals.Values);
        }
    }

    private async Task<float> NextEnhancedFrame(CancellationToken cancellationToken)
    {
        float delta = await NextFrame();
        cancellationToken.ThrowIfCancellationRequested();
        return delta;
    }

    private void SetSignatureImpactState(
        FinisherImpactPresentation presentation,
        IReadOnlyList<NCreature> targets,
        float intensity,
        float flash)
    {
        if (_usesNinjaSlayerSignatureImpact)
        {
            presentation.SetImpactState(targets, intensity, flash);
        }
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
        if (FinisherPresentationSettings.Mode != FinisherPresentationMode.Enhanced
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
            FinisherLog.Warn($"Finisher backdrop transition failed; legacy presentation will be used: {ex}");
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
            FinisherLog.Warn($"Finisher camera transition failed: {ex}");
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
                    ResolveImpactDirection(_actorNode, creatureNode)));
            }
        }
    }

    private CanvasItem GetCameraFocus() =>
        Scenario == FinisherScenarioKind.NinjaSlayerAttack
        && NinjaSlayerVisualRig.GetCinematicFocus(_actorNode.Visuals) is { } cinematicFocus
            ? cinematicFocus
            : _actorNode.Visuals.Bounds;

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

    private void ArmDeathKicks(IEnumerable<Creature> targets)
    {
        _deathKickVisuals.Clear();
        foreach (Creature target in targets)
        {
            NCreature? creatureNode = _room.GetCreatureNode(target);
            if (creatureNode == null || !GodotObject.IsInstanceValid(creatureNode))
            {
                continue;
            }

            Node2D body = creatureNode.Visuals.GetCurrentBody();
            if (!GodotObject.IsInstanceValid(body))
            {
                continue;
            }

            _deathKickVisuals[creatureNode] = new DeathKickVisual(
                body,
                body.Position,
                ResolveImpactDirection(_actorNode, creatureNode));
        }
    }

    private void StartReturnTimeline(bool includeSettle)
    {
        if (_returnTimelineStarted)
        {
            return;
        }

        _returnTimelineStarted = true;
        _returnToBaselineTask = RunReturnTimeline(includeSettle);
    }

    private async Task RunReturnTimeline(bool includeSettle)
    {
        if (includeSettle)
        {
            await WaitSeconds(DeathKickSettleSeconds);
        }

        await ReturnToBaseline();
    }

    private async Task EnsureReturnToBaseline()
    {
        StartReturnTimeline(includeSettle: false);
        await _returnToBaselineTask;
    }

    private void ApplyDeathKickRecovery(float sharedProgress)
    {
        _returnTimelineProgress = Mathf.Clamp(sharedProgress, 0f, 1f);
        foreach (DeathKickVisual visual in _deathKickVisuals.Values.Where(visual => visual.Triggered))
        {
            if (!GodotObject.IsInstanceValid(visual.Body))
            {
                continue;
            }

            float recovery = FinisherDeathKickTimeline.GetRecoveryProgress(
                _returnTimelineProgress,
                visual.JoinedAtReturnProgress);
            visual.Body.Position = visual.Position
                + Vector2.Right * visual.Direction * EnemyKnockbackPixels * (1f - recovery);
        }
    }

    private void RestoreDeathKicks()
    {
        foreach (DeathKickVisual visual in _deathKickVisuals.Values)
        {
            RestoreDeathKick(visual);
        }

        _deathKickVisuals.Clear();
    }

    private static void RestoreDeathKick(DeathKickVisual visual)
    {
        if (GodotObject.IsInstanceValid(visual.Body))
        {
            visual.Body.Position = visual.Position;
        }
    }

    private Vector2 GetDeathSquashMultiplier() => Scenario switch
    {
        FinisherScenarioKind.EnemyExecutesNinjaSlayer => Vector2.One,
        _ => _usesJumpDeathSquash ? JumpDeathSquash : DefaultDeathSquash
    };

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

    private void ApplyEnhancedVictimFeedback(
        IEnumerable<ImpactVisualSnapshot> snapshots,
        IReadOnlyList<ReverseVictimVisualSnapshot> reverseVictims,
        float amount,
        bool flash,
        float? reverseRotationAmount = null)
    {
        Vector2 squashMultiplier = GetDeathSquashMultiplier();
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
            snapshot.Body.Scale = snapshot.Scale * squashMultiplier;
            snapshot.Body.Rotation = Scenario == FinisherScenarioKind.EnemyExecutesNinjaSlayer
                ? snapshot.Rotation
                : snapshot.Rotation + Mathf.DegToRad(EnhancedEnemyTiltDegrees * snapshot.Direction * amount);
            snapshot.Body.SelfModulate = flash
                ? snapshot.SelfModulate.Lerp(
                    new Color(1.8f, 1.8f, 1.8f, snapshot.SelfModulate.A),
                    amount)
                : snapshot.SelfModulate;
        }

        ApplyReverseVictimRotation(reverseVictims, reverseRotationAmount ?? amount);
    }

    private List<ReverseVictimVisualSnapshot> CaptureReverseVictimVisuals(
        IEnumerable<NCreature> targetNodes)
    {
        if (Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
        {
            return [];
        }

        return targetNodes
            .Select(target => NinjaSlayerVisualRig.GetAirborneAnchor(target.Visuals))
            .Where(anchor => anchor != null && GodotObject.IsInstanceValid(anchor))
            .Cast<Node2D>()
            .Distinct()
            .Select(anchor => new ReverseVictimVisualSnapshot(
                anchor,
                anchor.RotationDegrees,
                anchor.ProcessMode))
            .ToList();
    }

    private static void FreezeReverseVictimVisuals(
        IEnumerable<ReverseVictimVisualSnapshot> snapshots)
    {
        foreach (ReverseVictimVisualSnapshot snapshot in snapshots.Where(snapshot =>
                     GodotObject.IsInstanceValid(snapshot.Anchor)))
        {
            snapshot.Anchor.ProcessMode = Node.ProcessModeEnum.Disabled;
        }
    }

    private static void ApplyReverseVictimRotation(
        IEnumerable<ReverseVictimVisualSnapshot> snapshots,
        float amount)
    {
        foreach (ReverseVictimVisualSnapshot snapshot in snapshots.Where(snapshot =>
                     GodotObject.IsInstanceValid(snapshot.Anchor)))
        {
            snapshot.Anchor.RotationDegrees = snapshot.RotationDegrees + 15f * amount;
        }
    }

    private static void RestoreReverseVictimVisuals(
        IEnumerable<ReverseVictimVisualSnapshot> snapshots)
    {
        foreach (ReverseVictimVisualSnapshot snapshot in snapshots.Where(snapshot =>
                     GodotObject.IsInstanceValid(snapshot.Anchor)))
        {
            snapshot.Anchor.RotationDegrees = snapshot.RotationDegrees;
            snapshot.Anchor.ProcessMode = snapshot.ProcessMode;
        }
    }

    private void PlayReverseImpactAudio()
    {
        if (_impactAudioPlayed || Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
        {
            return;
        }

        _impactAudioPlayed = true;
        foreach (Creature victim in _ledger.DeferredDeaths)
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(victim).Death);
            break;
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
        if (!GodotObject.IsInstanceValid(_actorNode))
        {
            ApplyDeathKickRecovery(1f);
            _returnTimelineCompleted = true;
            SetBackdropIntensity(0f);
            _camera.ResetToBaseline();
            return;
        }

        Vector2 ownerFrom = _actorNode.Position;
        Vector2 cameraFrom = _camera.CurrentPosition;
        float scaleFrom = _camera.CurrentScale;
        float backdropFrom = _backdropIntensity;
        float actorReturnSeconds = Scenario == FinisherScenarioKind.YamotoKokiIaiSlash ? 0.25f : ReturnSeconds;
        float totalReturnSeconds = Math.Max(ReturnSeconds, actorReturnSeconds);
        float elapsed = 0f;
        while (elapsed < totalReturnSeconds)
        {
            elapsed += await NextFrame();
            float cameraLinearProgress = Mathf.Clamp(elapsed / ReturnSeconds, 0f, 1f);
            float cameraProgress = CombatCinematicCameraLease.EaseOutCubic(cameraLinearProgress);
            float actorProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(elapsed / actorReturnSeconds, 0f, 1f));
            ApplyDeathKickRecovery(cameraLinearProgress);
            if (Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
            {
                _actorNode.Position = ownerFrom.Lerp(_actorStartPosition, actorProgress);
            }
            _camera.SetTransform(
                cameraFrom.Lerp(_camera.BaselinePosition, cameraProgress),
                Mathf.Lerp(scaleFrom, _camera.BaselineScale.X, cameraProgress));
            SetBackdropIntensity(Mathf.Lerp(backdropFrom, 0f, cameraProgress));
        }

        ApplyDeathKickRecovery(1f);
        _returnTimelineCompleted = true;
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
            FinisherLog.Warn($"Finisher camera shake pump stopped unexpectedly: {ex}");
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

    private Node GetActorFreezeNode() => Scenario == FinisherScenarioKind.NinjaSlayerAttack
        ? _actorNode
        : _actorNode.Visuals.GetCurrentBody();

    private readonly record struct ProcessModeSnapshot(Node Node, Node.ProcessModeEnum Mode);
    private readonly record struct ImpactVisualSnapshot(
        Node2D Body,
        Vector2 Position,
        Vector2 Scale,
        float Rotation,
        Color SelfModulate,
        float Direction);
    private readonly record struct ReverseVictimVisualSnapshot(
        Node2D Anchor,
        float RotationDegrees,
        Node.ProcessModeEnum ProcessMode);

    private sealed class DeathKickVisual(Node2D body, Vector2 position, float direction)
    {
        public Node2D Body { get; } = body;
        public Vector2 Position { get; } = position;
        public float Direction { get; } = direction;
        public bool Triggered { get; set; }
        public float JoinedAtReturnProgress { get; set; }
    }

    private static float EaseOut(float value) => 1f - (1f - value) * (1f - value);
}
