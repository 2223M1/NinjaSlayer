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

internal sealed partial class FinisherSession : IAsyncDisposable
{
    private readonly ICombatState _combatState;
    private readonly NCreature _actorNode;
    private readonly NCreature _focusNode;
    private readonly FinisherDamageLedger _ledger;
    private readonly Dictionary<Node2D, DeathSquashVisualState> _deathSquashStates = [];
    private readonly Dictionary<NCreature, DeathKickVisual> _deathKickVisuals = [];
    private readonly CombatCinematicCameraLease _camera;
    private readonly NCombatRoom _room;
    private readonly Vector2 _actorStartPosition;
    private readonly object _actionSync = new();
    private readonly HashSet<ulong> _vfxBaselineChildIds;
    private readonly bool _usesJumpDeathSquash;
    private readonly bool _usesNinjaSlayerSignatureImpact;
    private readonly FinisherCompletionProtocol _completionProtocol;
    private FinisherCameraFrame _cameraFrame = new([], false);
    private readonly CinematicSessionLifetime _impactCancellation = new();
    private readonly CinematicSessionLifetime _actionCancellation = new();
    private readonly CinematicSessionLifetime _watchdogCancellation = new();
    private ulong _lastFrameMsec;
    private ulong _lastDeltaFrame = ulong.MaxValue;
    private float _cachedFrameDelta;
    private Task _cameraTransitionTask = Task.CompletedTask;
    private Task _backdropTransitionTask = Task.CompletedTask;
    private Task _enhancedImpactTask = Task.CompletedTask;
    private Task _cameraShakePumpTask = Task.CompletedTask;
    private Task _returnToBaselineTask = Task.CompletedTask;
    private Task _actionPeakTask = Task.CompletedTask;
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
    private bool _actionStarted;
    private bool _actionPeakReached;
    private float _actionPeakSeconds = FinisherActionTrajectory.SlowTravelSeconds;
    private Vector2 _actionStartPosition;
    private Vector2 _impactPosition;
    private NinjaSlayerHoverTipSuppression? _hoverTipSuppression;
    private FinisherCardVisualSuppression? _cardVisualSuppression;
    private FinisherActorLayerLease? _actorLayerLease;
    private FinisherActorLeapPose? _actorLeapPose;
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
        _actorStartPosition = request.Scenario == FinisherScenarioKind.NinjaSlayerAttack
            ? NinjaSlayerRapidAnimationCoordinator.ClaimExclusiveBaseline(request.Actor, request.ActorNode)
            : request.ActorNode.Position;
        _actionStartPosition = request.ActorNode.Position;
        _impactPosition = request.ActorNode.Position;
        _actionPeakReached = request.Scenario != FinisherScenarioKind.YamotoKokiIaiSlash;
        _vfxBaselineChildIds = request.VfxBaselineChildIds?.ToHashSet()
            ?? FinisherImpactVfxFreezeLease.CaptureBaseline(_room).ToHashSet();
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

        if (Scenario == FinisherScenarioKind.EnemyExecutesNinjaSlayer)
        {
            foreach (Creature victim in _ledger.Victims.Where(victim =>
                         victim.Player?.Character is INinjaSlayerCharacter))
            {
                NinjaSlayerRapidAnimationCoordinator.CancelAndRestore(victim);
            }
        }

        _ = RunWatchdog();
        if (Scenario == FinisherScenarioKind.NinjaSlayerAttack)
        {
            NinjaSlayerFacingState.SyncForTarget(Actor, _focusNode.Entity);
        }
        _hoverTipSuppression = NinjaSlayerHoverTipSuppression.Acquire();
        if (CardPlay is { } cardPlay)
        {
            _cardVisualSuppression = FinisherCardVisualSuppression.Acquire(_room, cardPlay);
        }
        try
        {
            _presentation = _usesNinjaSlayerSignatureImpact
                ? FinisherImpactPresentation.Create(_room, _camera, _ledger.Victims.Count)
                : FinisherImpactPresentation.CreateBackdropOnly(_room, _camera);
        }
        catch (Exception ex)
        {
            _enhancedImpactFailed = true;
            Entry.Logger.Warn($"Could not create finisher presentation; fallback presentation will be used: {ex}");
        }

        if (Scenario == FinisherScenarioKind.NinjaSlayerAttack)
        {
            try
            {
                _actorLeapPose = FinisherActorLeapPose.TryCreate(Actor, _actorNode, _focusNode);
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"Could not apply the Ninja Slayer finisher leap pose: {ex}");
            }
        }

        List<NCreature> framingCandidates = _ledger.Victims
            .Select(victim => _room.GetCreatureNode(victim))
            .Where(node => node != null)
            .Cast<NCreature>()
            .ToList();
        try
        {
            _actorLayerLease = FinisherActorLayerLease.TryAcquire(
                _actorNode,
                framingCandidates);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Finisher actor layer could not be raised above its victims: {exception.Message}");
        }

        _impactPosition = new Vector2(
            FinisherImpactPositionResolver.ResolveImpactX(
                _actorNode,
                _focusNode,
                GetDeathSquashMultiplier(),
                NinjaSlayerCombatVisuals.CloseRangeApproachGap),
            _actorNode.Position.Y);
        if (Scenario == FinisherScenarioKind.YamotoKokiIaiSlash)
        {
            float fallbackDirection = Actor.Side == CombatSide.Player ? 1f : -1f;
            _actionStartPosition = new Vector2(
                FinisherActionTrajectory.ResolveIaiStartX(
                    _actorStartPosition.X,
                    _impactPosition.X,
                    fallbackDirection),
                _impactPosition.Y);
            _actorNode.Position = _actionStartPosition;
        }
        else
        {
            _actorNode.Position = _impactPosition;
            lock (_actionSync)
            {
                _actionStarted = true;
                _actionPeakReached = true;
                _actionPeakTask = Task.CompletedTask;
            }
        }
        float maximumScale = _camera.BaselineScale.X
            * FinalHitZoomMultiplier
            * CameraPunchScaleMultiplier;
        _cameraFrame = FinisherCameraFraming.SelectTargets(
            _camera,
            GetCameraFocusPoint(),
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

    public Task PlayActionToPeak(Creature creature, float repeatWaitSeconds)
    {
        if (_disposed
            || creature != Actor
            || Scenario != FinisherScenarioKind.YamotoKokiIaiSlash)
        {
            return Cmd.Wait(Math.Max(0f, repeatWaitSeconds));
        }

        bool startedNow;
        Task actionTask;
        lock (_actionSync)
        {
            startedNow = !_actionStarted;
            if (startedNow)
            {
                _actionStarted = true;
                _actionPeakSeconds = Math.Max(0f, repeatWaitSeconds);
                _actionPeakTask = RunActionToPeak();
            }

            actionTask = _actionPeakTask;
        }

        return startedNow
            ? actionTask
            : Cmd.Wait(Math.Max(0f, repeatWaitSeconds));
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
            if (CompletionCondition == FinisherCompletionCondition.AllCandidatesLethal
                && IsCompletionConditionSatisfied())
            {
                YamotoKokiIntentLifecycle.InvalidateCombat(_combatState);
            }

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
            Entry.Logger.Error($"NinjaSlayer finisher session {SessionId} completion failed: {ex}");
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
                    Entry.Logger.Error(
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
                Entry.Logger.Error($"NinjaSlayer finisher session {SessionId} restoration failed: {ex}");
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
        await EnsureActionPeak();
        _committing = true;
        _ledger.ReleasePendingProtections(mayRestoreCurrentCombat: true);
        bool guaranteedClearMatchedRuntime = IsCompletionConditionSatisfied();
        List<Creature> toKill = _ledger.LivingDeferredDeaths();
        if (!guaranteedClearMatchedRuntime)
        {
            Entry.Logger.Warn(
                $"Finisher session {SessionId} forecast did not match runtime damage; committing confirmed deaths without the pose.");
            await KillDeferredDeathsOnce(toKill, useDeathKick: false);
            return false;
        }

        if (CompletionCondition == FinisherCompletionCondition.AllCandidatesLethal)
        {
            YamotoKokiIntentLifecycle.InvalidateCombat(_combatState);
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
            await PrepareReverseImpactLead();

            TryScheduleEnhancedImpact();
            await _enhancedImpactTask;

            if (!_enhancedImpactScheduled
                || _enhancedImpactFailed)
            {
                if (Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
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
        _actionCancellation.Cancel();
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
            Entry.Logger.Warn(
                $"Finisher session {SessionId} could not cancel its impact during fallback commit: {ex}");
        }

        try
        {
            _ledger.ReleasePendingProtections(mayRestoreCurrentCombat: true);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn(
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
            Entry.Logger.Warn(
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
        cleanup.Capture(_actionCancellation.Cancel);
        cleanup.Capture(_impactCancellation.Cancel);
        await cleanup.CaptureAsync(() => _actionPeakTask);
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
        cleanup.Capture(() => _actorLayerLease?.Dispose());
        _actorLayerLease = null;
        cleanup.Capture(RestoreActorLeapPose);
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
        cleanup.Capture(_actionCancellation.Dispose);
        cleanup.Capture(_watchdogCancellation.Dispose);
        cleanup.Capture(_camera.Dispose);
        cleanup.ThrowIfAny(
            $"Finisher session {SessionId} encountered {cleanup.FailureCount} resource-restoration failure(s).");
    }

    private void RestoreActorLeapPose()
    {
        _actorLeapPose?.Restore();
        _actorLeapPose = null;
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

            Entry.Logger.Error(
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
            Entry.Logger.Error($"NinjaSlayer finisher session {SessionId} watchdog failed: {ex}");
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
            Entry.Logger.Error($"NinjaSlayer finisher session {SessionId} room-exit cleanup failed: {ex}");
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
        if (_enhancedImpactScheduled
            || _enhancedImpactFailed
            || _disposed
            || !_actionPeakReached
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
            Entry.Logger.Warn($"Enhanced finisher impact failed; fallback presentation will be used: {ex}");
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
        FinisherImpactVfxFreezeLease? frozenImpactVfx = null;
        Node actorFreezeNode = GetActorFreezeNode();
        ProcessModeSnapshot? ownerSnapshot = GodotObject.IsInstanceValid(actorFreezeNode)
            ? new ProcessModeSnapshot(actorFreezeNode, actorFreezeNode.ProcessMode)
            : null;

        try
        {
            frozenImpactVfx = FinisherImpactVfxFreezeLease.Acquire(
                _room,
                targetNodes,
                _vfxBaselineChildIds,
                ImpactVfxTargetMargin);
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

            frozenImpactVfx?.Dispose();
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
        FinisherImpactVfxFreezeLease? frozenImpactVfx = null;
        Node actorFreezeNode = GetActorFreezeNode();
        ProcessModeSnapshot? ownerSnapshot = GodotObject.IsInstanceValid(actorFreezeNode)
            ? new ProcessModeSnapshot(actorFreezeNode, actorFreezeNode.ProcessMode)
            : null;
        FinisherImpactPresentation presentation = _presentation
            ?? throw new InvalidOperationException("The enhanced finisher presentation was not initialized.");

        try
        {
            frozenImpactVfx = FinisherImpactVfxFreezeLease.Acquire(
                _room,
                targetNodes,
                _vfxBaselineChildIds,
                ImpactVfxTargetMargin);
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

            frozenImpactVfx?.Dispose();
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

    public async Task EnsureActionPeak()
    {
        if (Scenario != FinisherScenarioKind.YamotoKokiIaiSlash || _actionPeakReached)
        {
            return;
        }

        Task actionTask;
        lock (_actionSync)
        {
            if (!_actionStarted)
            {
                _actionStarted = true;
                _actionPeakTask = RunActionToPeak();
            }

            actionTask = _actionPeakTask;
        }

        await actionTask;
    }

    private async Task RunActionToPeak()
    {
        try
        {
            if (!GodotObject.IsInstanceValid(_actorNode))
            {
                throw new InvalidOperationException("The finisher actor node was released before its approach began.");
            }

            float duration = _actionPeakSeconds;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += await NextFrame();
                _actionCancellation.Token.ThrowIfCancellationRequested();
                float progress = FinisherActionTrajectory.SlowProgress(
                    elapsed / duration);
                _actorNode.Position = _actionStartPosition.Lerp(_impactPosition, progress);
            }

            _actorNode.Position = _impactPosition;
            _actionPeakReached = true;
            TryScheduleEnhancedImpact();
        }
        catch (OperationCanceledException) when (_actionCancellation.IsCancellationRequested
            || _disposed
            || !GodotObject.IsInstanceValid(_room))
        {
        }
    }
}
