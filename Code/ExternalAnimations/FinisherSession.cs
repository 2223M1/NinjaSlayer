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
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Scripts;
using static NinjaSlayer.Code.ExternalAnimations.FinisherTimeline;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed partial class FinisherSession : IAsyncDisposable
{
    private readonly NinjaSlayerFreeControl.CinematicLease? _freeControlLease;
    private readonly ICombatState _combatState;
    private readonly NCreature _actorNode;
    private readonly NCreature _focusNode;
    private readonly FinisherDamageLedger _ledger;
    private readonly HashSet<Creature> _committedDeaths =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Node2D, DeathSquashVisualState> _deathSquashStates = [];
    private readonly Dictionary<NCreature, DeathKickVisual> _deathKickVisuals = [];
    private readonly CombatCinematicCameraLease _camera;
    private readonly NCombatRoom _room;
    private readonly Vector2 _actorStartPosition;
    private Vector2 _actorReturnPosition;
    private readonly HashSet<ulong> _vfxBaselineChildIds;
    private HashSet<ulong>? _completedWaveVfx;
    private HashSet<ulong>? _impactWaveBaseline;
    private readonly Dictionary<ulong, float> _vfxBirthTimes = [];
    private readonly HashSet<ulong> _foreignVfx = [];
    private float _activeSeconds;
    private readonly FinisherPreviewProfile _previewProfile = PreviewProfile;
    private bool _impactCamera => _previewProfile != FinisherPreviewProfile.A;
    private float? _impactStartedAt;
    private Task? _measuredCameraTask;
    private readonly Dictionary<Creature, Vector2> _impactAxes = [];
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    private bool _returnTimelineStarted;
    private bool _returnTimelineCompleted;
    private float _returnTimelineProgress;
    private bool _disposed;
    private bool _begun;
    private bool _completionStarted;
    private bool _actionStarted;
    private bool _actionPeakReached;
    private float _actionPeakSeconds = CombatActionTimingRuntime.SlowAttackSeconds;
    private Vector2 _impactPosition;
    private NinjaSlayerHoverTipSuppression? _hoverTipSuppression;
    private FinisherCardVisualSuppression? _cardVisualSuppression;
    private FinisherActorLayerLease? _actorLayerLease;
    private NinjaSlayerAimPose? _actorAimPose;
    private Tween? _comboTravelTween;
    private Vector2 _comboTravel;
    private Vector2 _comboPeakOffset;
    private bool _comboAtPeak;
    private FinisherImpactPresentation? _presentation;
    private FinisherApproach? _approach;
    private readonly FinisherRangedAction? _ranged;
    private readonly bool _continuousPlayerApproach;
    internal bool IsRanged => _ranged != null;
    internal Task Completion => _completion.Task;
    internal Vector2 ActorBaseline => _actorStartPosition;
    internal Task ImpactVisualCompletion => _enhancedImpactTask;

    public FinisherSession(
        long sessionId,
        ICombatState combatState,
        NCombatRoom room,
        FinisherSessionRequest request)
    {
        SessionId = sessionId;
        _combatState = combatState;
        _room = room;
        Scenario = request.Scenario;
        _ranged = request.RangedAction;
        _continuousPlayerApproach = request.ContinuousPlayerApproach;
        CompletionCondition = request.CompletionCondition;
        Actor = request.Actor;
        _actorNode = request.ActorNode;
        _focusNode = request.FocusNode;
        _camera = request.Camera;
        _ledger = new FinisherDamageLedger(
            request.Victims,
            combatState,
            IsCurrentCombatContext);
        _actorStartPosition = !IsRanged && request.Scenario == FinisherScenarioKind.NinjaSlayerAttack
            ? NinjaSlayerRapidAnimationCoordinator.ClaimExclusiveBaseline(request.Actor, request.ActorNode)
            : request.ActorNode.Position;
        _freeControlLease = NinjaSlayerFreeControl.Get(request.Actor)?.SuspendForCinematic(_actorStartPosition);
        if (_freeControlLease != null) _actorStartPosition = _freeControlLease.Baseline;
        _actorReturnPosition = _actorStartPosition;
        _impactPosition = request.ActorNode.Position;
        _actionPeakReached = !IsCompanionIai;
        _vfxBaselineChildIds = (request.VfxBaselineChildIds ?? _ranged?.Baseline)?.ToHashSet()
            ?? FinisherImpactVfxFreezeLease.CaptureBaseline(_room).ToHashSet();
        _room.TreeExiting += OnRoomTreeExiting;
        _lastFrameMsec = Time.GetTicksMsec();
        CardPlay = request.CardPlay;
        RequiresAfterCardPlayed = request.RequiresAfterCardPlayed;
        ResolvedHits = Math.Max(1, request.ResolvedHits);
        _primaryDamageCalls = request.ObservedPrimaryHits;
        _ranged?.Attach(this);
    }

    private bool IsCompanionIai => !IsRanged && Scenario == FinisherScenarioKind.CompanionAttack
        && Actor.Monster is NinjaSlayer.Monsters.YamotoKokiMonster;

    public long SessionId { get; }
    public FinisherScenarioKind Scenario { get; }
    public FinisherCompletionCondition CompletionCondition { get; }
    public Creature Actor { get; }
    public CardPlay? CardPlay { get; }
    internal CardPlay? EventVisualPlay => _continuousPlayerApproach ? CardPlay : null;
    public bool RequiresAfterCardPlayed { get; }
    public int ResolvedHits { get; }
    internal ICombatState CombatState => _combatState;
    internal NCombatRoom Room => _room;
    internal NCreature? FindImpactTarget(Vector2 point) => _ledger.Victims
        .Select(victim => _room.GetCreatureNode(victim)).Where(node => node != null)
        .MinBy(node => node!.VfxSpawnPosition.DistanceSquaredTo(point));
    internal bool CanTransferToAfterCardPlayed => _begun && !_completionStarted;

    public void Begin()
    {
        if (_begun)
        {
            throw new InvalidOperationException($"Finisher session {SessionId} has already begun.");
        }

        _begun = true;
        _camera.IncludeCombatEffects();
        _room.CombatVfxContainer.ChildEnteredTree += ObserveVfxBirth;
        _room.BackCombatVfxContainer.ChildEnteredTree += ObserveVfxBirth;
        foreach (Creature victim in _ledger.Victims) TornadoHurtPause.Cancel(victim);
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
            _presentation = FinisherImpactPresentation.CreateBackdropOnly(_room, _camera);
        }
        catch (Exception ex)
        {
            _enhancedImpactFailed = true;
            Entry.Logger.Warn($"Could not create finisher presentation; fallback presentation will be used: {ex}");
        }

        if (!IsRanged && Scenario == FinisherScenarioKind.NinjaSlayerAttack)
        {
            try
            {
                _actorAimPose = NinjaSlayerAimPose.Get(Actor);
                if (CardPlay?.Card is NinjaSlayer.Cards.RedesignV1.TornadoFistRedesignV1 tornado)
                    _actorAimPose?.BeginTornado(_focusNode.Entity, exclusive: true, empowered: tornado.IsEmpowered(ResolvedHits));
                else if (AlabamaContact == null)
                    _actorAimPose?.BeginAction(_focusNode.Entity, exclusive: true);
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
            _actorLayerLease = IsAlabamaDrop ? null : FinisherActorLayerLease.TryAcquire(
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
                GetDeathSquashMultiplier(_focusNode.Entity),
                NinjaSlayerCombatVisuals.CloseRangeApproachGap),
            _actorNode.Position.Y);
        if (IsRanged)
        {
            _actionStarted = _actionPeakReached = true;
        }
        else if (IsCompanionIai)
        {
            _approach = FinisherApproach.Create(_actorNode, _focusNode, GetDeathSquashMultiplier(_focusNode.Entity));
        }
        else if (Scenario == FinisherScenarioKind.CompanionAttack && Actor.Monster is NinjaSlayer.Monsters.SawatariMonster)
        {
            _approach = FinisherApproach.Create(_actorNode, _focusNode, GetDeathSquashMultiplier(_focusNode.Entity));
            float peak = Actor.Monster is NinjaSlayer.Monsters.SawatariMonster { ActThree: true }
                ? SawatariWeaponVisuals.DualCycleSeconds * 2f / 7f
                : SawatariBambooAnimation.CycleSeconds * SawatariBambooAnimation.PeakPhase;
            _approach.Start(CombatActionTimingRuntime.VisualSeconds(peak));
            _actionStarted = _actionPeakReached = true;
        }
        else if (Scenario == FinisherScenarioKind.CompanionAttack)
        {
            // Ranged companions keep their position; their projectile owns the impact gate.
            _actionStarted = _actionPeakReached = true;
        }
        else if (Scenario == FinisherScenarioKind.NinjaSlayerAttack)
        {
            if (_continuousPlayerApproach)
                _approach = FinisherApproach.Create(_actorNode, _focusNode, GetDeathSquashMultiplier(_focusNode.Entity));
            else if (AlabamaContact != null)
                // The drop already reached contact before its damage-only command
                // acquired this session. Placing it again duplicates the approach.
                _impactPosition = _actorNode.Position;
            else if (_actorAimPose != null)
                _actorAimPose.PlaceAtImpact(_focusNode.Entity, _impactPosition.X);
            else
                _actorNode.Position = _impactPosition;
            _actionStarted = !_continuousPlayerApproach;
            _actionPeakReached = true;
            _actionPeakTask = Task.CompletedTask;
        }
        else
        {
            // A missed prediction never adds a hit-frame teleport.
            _approach = FinisherApproach.Claim(Actor);
            _actionStarted = _actionPeakReached = true;
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
        bool deferPresentation = _impactCamera
            ? ResolvedHits <= 1 || _primaryDamageCalls >= ResolvedHits
            : IsRanged;
        _finalZoomStarted = !deferPresentation && ResolvedHits <= 1;
        if (!deferPresentation)
        {
            StartCameraTransition(
                ResolvedHits > 1 ? (_impactCamera ? 1.35f : MultiHitZoomMultiplier) : FinalHitZoomMultiplier,
                ResolvedHits > 1 ? MultiHitZoomSeconds : SingleHitZoomSeconds);
            if (ResolvedHits <= 1)
            {
                StartBackdropDarkening();
            }
        }

    }

    public Task PlayActionToPeak(Creature creature, float repeatWaitSeconds, float attackDistance = 0f)
    {
        if (!IsRanged && !_disposed && creature == Actor && Scenario == FinisherScenarioKind.NinjaSlayerAttack)
            return PlayAimedAction(repeatWaitSeconds, attackDistance);
        if (_disposed
            || creature != Actor
            || !IsCompanionIai)
        {
            return Cmd.Wait(Math.Max(0f, repeatWaitSeconds));
        }

        bool startedNow = !_actionStarted;
        if (startedNow)
        {
            _actionStarted = true;
            _actionPeakSeconds = Math.Max(0f, repeatWaitSeconds);
            _actionPeakTask = RunActionToPeak();
        }

        return startedNow
            ? _actionPeakTask
            : Cmd.Wait(Math.Max(0f, repeatWaitSeconds));
    }

    private async Task PlayAimedAction(float seconds, float attackDistance)
    {
        if (_continuousPlayerApproach && !_actionStarted)
        {
            _actionStarted = true;
            _approach!.Start(seconds);
        }
        if (NinjaSlayerAimPose.IsSomersaultHeavy(CardPlay?.Card) && _actorAimPose != null)
        {
            await _actorAimPose.PlaySomersaultInPlace(seconds);
            return;
        }
        bool repeatLunge = attackDistance > 0f && _actorAimPose is { IsTornado: false };
        if (!repeatLunge)
        {
            await Task.WhenAll(_actorAimPose?.PrepareKick(CardPlay) ?? Task.CompletedTask,
                Cmd.Wait(Math.Max(0f, seconds)));
            return;
        }

        float preparation = _actorAimPose!.KickPreparationSeconds(CardPlay);
        Task kick = _actorAimPose.PrepareKick(CardPlay);
        float outbound = NinjaSlayerRapidAnimationCoordinator.StandardOutboundSeconds(attackDistance, seconds, preparation);
        bool slow = attackDistance >= NinjaSlayerCombatVisuals.SlowAttackLungeDistance;
        // Use the same full action Tween and completion gate as ordinary combat,
        // including its peak hold. The first action is already at the endpoint.
        StartComboTravel(Vector2.Zero, seconds, p =>
        {
            float elapsed = seconds * p;
            float travel = elapsed < preparation ? 0f
                : outbound <= 0f ? 1f : Mathf.Clamp((elapsed - preparation) / outbound, 0f, 1f);
            return slow ? FinisherActionTrajectory.SlowProgress(travel) : FinisherActionTrajectory.FastProgress(travel);
        });
        Task<bool> playback = _comboTravelTween is { } tween
            ? TweenPlayback.AwaitCompletion(tween, _actorNode) : Task.FromResult(true);
        await Task.WhenAll(kick, playback);
        if (await playback && !_disposed && !_actionCancellation.IsCancellationRequested)
        {
            StopComboTravel();
            _comboTravel = Vector2.Zero;
            _actorAimPose!.SetFinisherContactTravel(Vector2.Zero);
            _comboPeakOffset = _actorAimPose.DirectionLocal() * attackDistance;
            _comboAtPeak = true;
        }
    }

    internal void BeginComboRecovery(float seconds)
    {
        if (!_comboAtPeak || _disposed || _committing || _actionCancellation.IsCancellationRequested
            || _primaryDamageCalls >= ResolvedHits || _actorAimPose is not { IsExclusive: true }) return;
        _comboAtPeak = false;
        // The opening teleport is already one full lunge. Recover inside the
        // existing damage wait, then reuse the next attack's ordinary outbound gate.
        StartComboTravel(-_comboPeakOffset, seconds, FinisherActionTrajectory.FastProgress);
        NinjaSlayerShadowController.Get(Actor)?.BeginReturn(seconds);
    }

    private void StartComboTravel(Vector2 destination, float seconds, Func<float, float> curve)
    {
        StopComboTravel();
        Vector2 from = _comboTravel;
        void Apply(float p)
        {
            if (_disposed || _actionCancellation.IsCancellationRequested) return;
            _comboTravel = from.Lerp(destination, curve(p));
            _actorAimPose?.SetFinisherContactTravel(_comboTravel);
        }
        if (seconds <= 0f) { Apply(1f); return; }
        _comboTravelTween = _actorNode.CreateTween();
        _comboTravelTween.TweenMethod(Callable.From<float>(Apply), 0f, 1f, seconds);
    }

    private void StopComboTravel()
    {
        if (_comboTravelTween is { } tween && tween.IsValid()) tween.Kill();
        _comboTravelTween = null;
    }

    internal bool OwnsProtection(FinisherProtectionToken token) =>
        ReferenceEquals(token.Ledger, _ledger);

    public void NotifyPrimaryAttackAnimation(Creature creature, string triggerName)
    {
        if (_disposed
            || _committing
            || IsRanged
            || ResolvedHits <= 1
            || creature != Actor
            || !IsPrimaryAttackTrigger(triggerName))
        {
            return;
        }

        _primaryAnimationsStarted++;
        if (!_impactCamera && _primaryAnimationsStarted >= ResolvedHits)
        {
            StartFinalZoom();
        }
    }

    public void NotifyPrimaryDamage(Creature? dealer, CardModel? cardSource, CardPlay? cardPlay,
        bool rangedImpact = false)
    {
        if (_disposed
            || _committing
            || dealer != Actor || IsRanged && !rangedImpact)
        {
            return;
        }
        bool primary = CardPlay is { } sessionCardPlay
            ? cardSource == sessionCardPlay.Card && cardPlay == sessionCardPlay
            : _ranged != null
                ? ReferenceEquals(FinisherRangedAction.For(dealer), _ranged)
                : cardSource == null;
        if (!primary)
        {
            return;
        }

        _primaryDamageCalls++;
        _impactWaveBaseline = new(_completedWaveVfx ?? _vfxBaselineChildIds);
        foreach (Creature victim in _ledger.Victims)
        {
            if (_room.GetCreatureNode(victim) is not { } node) continue;
            Vector2 axis = node.VfxSpawnPosition - _actorNode.VfxSpawnPosition;
            if (axis.LengthSquared() > 0.0001f) _impactAxes[victim] = axis.Normalized();
        }
        bool isFinalHit = _primaryDamageCalls >= ResolvedHits;
        _camera.PlayScreenShake(
            isFinalHit ? ShakeStrength.TooMuch : ShakeStrength.Medium,
            ShakeDuration.Short,
            rejectWeakerReplacement: true);
        if (!_impactCamera && !IsRanged && ResolvedHits > 1 && isFinalHit)
        {
            StartFinalZoom();
        }

        TryScheduleEnhancedImpact();
    }

    public void NotifyDeathAnimationStarting(NCreature creatureNode)
    {
        if (_disposed
            || !_committing
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

    private void ObserveVfxBirth(Node node)
    {
        ulong id = node.GetInstanceId();
        _vfxBirthTimes[id] = _activeSeconds;
        Creature? source = FinisherRangedAction.Active?.Actor
            ?? NinjaSlayerAttackExecution.CurrentCommand?.Attacker
            ?? FinisherAttackVfxBaselineContext.CurrentAttacker
            ?? NinjaSlayerAttackExecution.CurrentPlay?.Card.Owner.Creature;
        if (source != null && source != Actor) _foreignVfx.Add(id);
    }

    internal bool IsForeignVfx(Node node) => _foreignVfx.Contains(node.GetInstanceId());

    internal float ImpactVfxAge(Node node) => _vfxBirthTimes.TryGetValue(node.GetInstanceId(), out float born)
        ? Math.Max(0f, _activeSeconds - born) : 0f;

    internal async Task<IEnumerable<DamageResult>> ObserveDamageCompletion(
        Task<IEnumerable<DamageResult>> task, Creature? dealer)
    {
        int wave = _primaryDamageCalls;
        IEnumerable<DamageResult> result = await task;
        if (!_disposed && dealer == Actor && wave == _primaryDamageCalls && IsCurrentCombatContext())
            _completedWaveVfx = FinisherImpactVfxFreezeLease.CaptureBaseline(_room).ToHashSet();
        return result;
    }

    public bool TryProtectLethalDamage(
        Creature target,
        ref decimal amount,
        out FinisherProtectionToken? token)
    {
        token = null;
        if (_room.GetCreatureNode(target) is { } targetNode)
        {
            Vector2 axis = targetNode.VfxSpawnPosition - _actorNode.VfxSpawnPosition;
            if (axis.LengthSquared() > 0.0001f) _impactAxes[target] = axis.Normalized();
        }
        if (_disposed
            || !IsCurrentCombatContext()
            || !_ledger.TryProtect(target, _committing, ref amount, out token))
        {
            return false;
        }

        return true;
    }

    internal bool HasConfirmedDeath(Creature creature) =>
        !_disposed && IsCurrentCombatContext() && _ledger.DeferredDeaths.Contains(creature)
        && !_committedDeaths.Contains(creature);

    public void NotifyProtectedDamageConfirmed()
    {
        if (!_disposed && !_committing)
        {
            if (CompletionCondition == FinisherCompletionCondition.AllCandidatesLethal
                && IsCompletionConditionSatisfied())
            {
                CompanionIntentLifecycle.InvalidateCombat(_combatState);
            }

            TryScheduleEnhancedImpact();
        }
    }

    public bool TryTakeDamageDisplayOverride(DamageResult result, out int displayDamage) =>
        _ledger.TryTakeDamageDisplayOverride(result, out displayDamage);

    public Task CompleteAsync(bool playPose)
    {
        StartCompletion(commitDeaths: true, playPose);
        return _completion.Task;
    }

    public Task CancelAsync()
    {
        StartCompletion(commitDeaths: false, playPose: false);
        return _completion.Task;
    }

    internal void PrepareArchitectExit(float retreatDistance)
    {
        if (!_continuousPlayerApproach || _approach == null)
            throw new InvalidOperationException("Architect recovery requires its continuous approach.");
        Vector2 approach = _approach.OffsetInActorParent;
        // Transfer the retained approach to the event actor's root while the
        // visual lease recovers. Together they move back only one ordinary lunge.
        _actorReturnPosition = _actorNode.Position + new Vector2(
            approach.X - Math.Sign(approach.X) * retreatDistance, 0f);
    }

    internal Task ReturnArchitectAlabama()
    {
        PrepareArchitectExit(NinjaSlayerCombatVisuals.SlowAttackLungeDistance);
        StartReturnTimeline(includeSettle: false);
        return _returnToBaselineTask;
    }

    // The Architect is an event, not a combat death. Reuse the real impact and
    // camera without manufacturing damage, protections, Hooks or death history.
    internal async Task PlayArchitectImpact(CancellationToken cancellationToken)
    {
        if (!_continuousPlayerApproach || _focusNode.Entity.Monster is not MegaCrit.Sts2.Core.Models.Monsters.Architect)
            throw new InvalidOperationException("Architect impact requires its event approach.");
        FinisherApproach.ReachImpact(Actor);
        NotifyPrimaryDamage(Actor, CardPlay?.Card, CardPlay);
        _finalZoomStarted = true;
        _cameraTransitionGeneration++;
        StartBackdropDarkening();
        AdvanceClock();
        _impactStartedAt = _activeSeconds;
        _enhancedImpactTask = PlayEnhancedDoomPoseImpact([_focusNode], cancellationToken);
        await _enhancedImpactTask;
        RestoreDeathSquashes(preserveAlabamaContact: true);
    }

    public ValueTask DisposeAsync() => new(
        IsCurrentCombatContext()
            ? CompleteAsync(playPose: false)
            : CancelAsync());

    private void StartCompletion(bool commitDeaths, bool playPose)
    {
        if (_completionStarted)
        {
            return;
        }

        _completionStarted = true;
        _ = CompleteCore(commitDeaths, playPose);
    }

    private async Task CompleteCore(bool commitDeaths, bool playPose)
    {
        var failures = new List<Exception>();
        if (commitDeaths)
        {
            if (!IsCurrentCombatContext())
            {
                int uncommittedDeaths = _ledger.LivingDeferredDeaths().Count;
                if (uncommittedDeaths > 0)
                {
                    var ownershipFailure = new InvalidOperationException(
                        $"Finisher session {SessionId} lost combat ownership with "
                        + $"{uncommittedDeaths} confirmed death(s) still living.");
                    failures.Add(ownershipFailure);
                    Entry.Logger.Error(ownershipFailure.Message);
                }
            }
            else
            {
                try
                {
                    if (playPose)
                    {
                        await CommitDeathsWithPoseCore();
                    }
                    else
                    {
                        await CommitDeferredDeathsWithoutPoseCore();
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    Entry.Logger.Error($"NinjaSlayer finisher session {SessionId} death commit failed: {ex}");
                    try
                    {
                        await CommitConfirmedDeathsEmergencyCore();
                    }
                    catch (Exception fallbackEx)
                    {
                        failures.Add(fallbackEx);
                        Entry.Logger.Error(
                            $"NinjaSlayer finisher session {SessionId} fallback death commit failed: {fallbackEx}");
                    }
                }
            }
        }

        bool mayRestoreCurrentCombat = commitDeaths && IsCurrentCombatContext();
        try
        {
            await RestoreResourcesCore(mayRestoreCurrentCombat);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
            Entry.Logger.Error($"NinjaSlayer finisher session {SessionId} restoration failed: {ex}");
        }

        try
        {
            FinisherSessionRegistry.UnregisterSession(this);
            if (FinisherSessionRegistry.IsSessionCurrent(this))
            {
                throw new InvalidOperationException(
                    $"Finisher session {SessionId} remained registered after detach.");
            }
        }
        catch (Exception ex)
        {
            failures.Add(ex);
            Entry.Logger.Error($"NinjaSlayer finisher session {SessionId} unregister failed: {ex}");
        }

        if (failures.Count == 0)
        {
            _completion.TrySetResult();
        }
        else
        {
            _completion.TrySetException(
                failures.Count == 1
                    ? failures[0]
                    : new AggregateException(
                        $"Finisher session {SessionId} failed to complete.",
                        failures));
        }
    }

    private async Task CommitDeathsWithPoseCore()
    {
        _committing = true;
        _ledger.ReleasePendingProtections(mayRestoreCurrentCombat: true);
        bool guaranteedClearMatchedRuntime = IsCompletionConditionSatisfied();
        List<Creature> toKill = _ledger.LivingDeferredDeaths();
        if (!guaranteedClearMatchedRuntime)
        {
            Entry.Logger.Warn(
                $"Finisher session {SessionId} forecast did not match runtime damage; committing confirmed deaths without the pose.");
            await KillDeferredDeathsOnce(toKill, useDeathKick: false);
            return;
        }

        try
        {
            await EnsureActionPeak();
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn(
                $"Finisher session {SessionId} action pose failed; committing deaths without it: {ex}");
        }

        if (CompletionCondition == FinisherCompletionCondition.AllCandidatesLethal)
        {
            CompanionIntentLifecycle.InvalidateCombat(_combatState);
        }

        List<NCreature> targetNodes;
        try
        {
            targetNodes = toKill
                .Select(creature => _room.GetCreatureNode(creature))
                .Where(node => node != null && GodotObject.IsInstanceValid(node))
                .Cast<NCreature>()
                .ToList();
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn(
                $"Finisher session {SessionId} could not resolve pose targets; committing deaths without them: {ex}");
            targetNodes = [];
        }

        if (toKill.Count > 0 && targetNodes.Count == 0)
        {
            await KillDeferredDeathsOnce(toKill, useDeathKick: false);
            return;
        }

        if (targetNodes.Count > 0)
        {
            try
            {
                TryScheduleEnhancedImpact();
                await _enhancedImpactTask;

                if (!_enhancedImpactScheduled
                    || _enhancedImpactFailed)
                {
                    _finalZoomStarted = false;
                    StartFinalZoom();
                    await _cameraTransitionTask;
                    await PlayDoomPoseImpact(targetNodes);
                }
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn(
                    $"Finisher session {SessionId} impact presentation failed; committing gameplay deaths: {ex}");
            }
        }

        bool useDeathContinuation = true;
        if (await KillDeferredDeathsOnce(toKill, useDeathContinuation))
        {
            if (Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
            {
                try
                {
                    StartReturnTimeline(includeSettle: true);
                }
                catch (Exception ex)
                {
                    Entry.Logger.Warn(
                        $"Finisher session {SessionId} return presentation could not start: {ex}");
                }
            }
        }
    }

    private async Task CommitDeferredDeathsWithoutPoseCore()
    {
        _committing = true;
        try
        {
            _actionCancellation.Cancel();
            _impactCancellation.Cancel();
            await _enhancedImpactTask;
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn(
                $"Finisher session {SessionId} could not stop its presentation before committing deaths: {ex}");
        }

        _ledger.ReleasePendingProtections(mayRestoreCurrentCombat: true);
        await KillDeferredDeathsOnce(_ledger.LivingDeferredDeaths(), useDeathKick: false);
    }

    private async Task CommitConfirmedDeathsEmergencyCore()
    {
        _committing = true;
        var failures = new List<Exception>();
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
            failures.Add(ex);
        }

        try
        {
            await KillDeferredDeathsOnce(_ledger.LivingDeferredDeaths(), useDeathKick: false);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(
                $"Finisher session {SessionId} emergency commit encountered multiple failures.",
                failures);
        }
    }

    private async Task<bool> KillDeferredDeathsOnce(
        IEnumerable<Creature> deferredDeaths,
        bool useDeathKick)
    {
        List<Creature> toKill = deferredDeaths
            .Where(creature => creature.IsAlive && !_committedDeaths.Contains(creature))
            .ToList();
        if (toKill.Count == 0)
        {
            return false;
        }

        if (!IsCurrentCombatContext())
        {
            throw new InvalidOperationException(
                $"Finisher session {SessionId} lost its combat before committing {toKill.Count} confirmed death(s).");
        }

        try
        {
            RestoreDeathSquashes(preserveAlabamaContact: true);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn(
                $"Finisher session {SessionId} could not restore a death squash before committing deaths: {ex}");
        }

        try
        {
            foreach (Creature target in toKill)
                if (_room.GetCreatureNode(target) is { } node)
                    AlabamaDropAnimation.ReleaseVictimBeforeDeath(node);
            if (useDeathKick && Scenario == FinisherScenarioKind.EnemyExecutesNinjaSlayer)
            {
                FinisherDeathContinuationRegistry.Arm(toKill, SessionId);
                StartReturnTimeline(includeSettle: false);
            }
            else if (useDeathKick)
            {
                ArmDeathKicks(toKill);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn(
                $"Finisher session {SessionId} could not prepare death presentation; committing deaths without it: {ex}");
        }

        bool committedAny = false;
        foreach (Creature target in toKill)
        {
            if (!target.IsAlive || _committedDeaths.Contains(target))
            {
                continue;
            }

            if (!IsCurrentCombatContext())
            {
                throw new InvalidOperationException(
                    $"Finisher session {SessionId} lost its combat while committing confirmed deaths.");
            }

            Telemetry.NinjaSlayerCombatTelemetry.BeforeFinisherDeath(target);
            await CreatureCmd.Kill(target);
            // Native death hooks may revive the target (for example, Waterfall Giant's
            // self-destruct phase). A completed death command must not be retried.
            _committedDeaths.Add(target);
            committedAny = true;
        }

        List<Creature> remaining = _ledger.LivingDeferredDeaths()
            .Where(creature => !_committedDeaths.Contains(creature)).ToList();
        if (remaining.Count > 0)
        {
            throw new InvalidOperationException(
                $"Finisher session {SessionId} left {remaining.Count} confirmed death(s) uncommitted.");
        }

        return committedAny;
    }

    private async Task RestoreResourcesCore(bool mayRestoreCurrentCombat)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (GodotObject.IsInstanceValid(_room.CombatVfxContainer))
            _room.CombatVfxContainer.ChildEnteredTree -= ObserveVfxBirth;
        if (GodotObject.IsInstanceValid(_room.BackCombatVfxContainer))
            _room.BackCombatVfxContainer.ChildEnteredTree -= ObserveVfxBirth;
        var failures = new List<Exception>();
        void Capture(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        async Task CaptureAsync(Func<Task> cleanup)
        {
            try
            {
                await cleanup();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        Capture(_watchdogCancellation.Cancel);
        Capture(_actionCancellation.Cancel);
        Capture(StopComboTravel);
        Capture(_impactCancellation.Cancel);
        await CaptureAsync(() => _actionPeakTask);
        await CaptureAsync(() => _enhancedImpactTask);
        _cameraTransitionGeneration++;
        _backdropTransitionGeneration++;
        await CaptureAsync(() => _cameraTransitionTask);
        await CaptureAsync(() => _backdropTransitionTask);
        if (mayRestoreCurrentCombat)
        {
            await CaptureAsync(EnsureReturnToBaseline);
        }
        if (_measuredCameraTask != null) await CaptureAsync(() => _measuredCameraTask);
        await CaptureAsync(() => _cameraShakePumpTask);

        if (!IsRanged && !AlabamaOwnsRecovery && mayRestoreCurrentCombat && GodotObject.IsInstanceValid(_actorNode))
        {
            Capture(() => _actorNode.Position = _actorReturnPosition);
        }

        Capture(() => _hoverTipSuppression?.Dispose());
        _hoverTipSuppression = null;
        Capture(() => _cardVisualSuppression?.Dispose());
        _cardVisualSuppression = null;
        Capture(() => _actorLayerLease?.Dispose());
        _actorLayerLease = null;
        Capture(() => _approach?.Dispose());
        _approach = null;
        Capture(RestoreActorLeapPose);
        Capture(() => _freeControlLease?.Dispose());
        Capture(() => _ledger.Clear(mayRestoreCurrentCombat));
        Capture(() => FinisherDeathContinuationRegistry.Clear(SessionId));
        Capture(() => RestoreDeathSquashes());
        Capture(RestoreDeathKicks);
        Capture(DisposeEnhancedPresentation);
        if (GodotObject.IsInstanceValid(_room))
        {
            Capture(() => _room.TreeExiting -= OnRoomTreeExiting);
        }
        Capture(_impactCancellation.Dispose);
        Capture(_actionCancellation.Dispose);
        Capture(_watchdogCancellation.Dispose);
        Capture(_camera.Dispose);
        Capture(() => _ranged?.ReleaseVisuals());
        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"Finisher session {SessionId} encountered {failures.Count} resource-restoration failure(s).",
                failures);
        }
    }

    private void RestoreActorLeapPose()
    {
        if (!AlabamaOwnsRecovery) _actorAimPose?.Reset();
        _actorAimPose = null;
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
                    await CancelAsync();
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
            await CompleteAsync(playPose: false);
        }
        catch (OperationCanceledException) when (_watchdogCancellation.IsCancellationRequested || _disposed)
        {
        }
        catch (OperationCanceledException ex)
        {
            Entry.Logger.Warn($"NinjaSlayer finisher session {SessionId} watchdog was cancelled: {ex.Message}");
            await CancelAsync();
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NinjaSlayer finisher session {SessionId} watchdog failed: {ex}");
            if (_completionStarted)
            {
                return;
            }

            try
            {
                if (IsCurrentCombatContext())
                {
                    await CompleteAsync(playPose: false);
                }
                else
                {
                    await CancelAsync();
                }
            }
            catch (Exception completionEx)
            {
                Entry.Logger.Error(
                    $"NinjaSlayer finisher session {SessionId} watchdog cleanup failed: {completionEx}");
            }
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
            await CancelAsync();
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

        if (IsRanged || _impactCamera)
        {
            // The impact itself performs the zoom; starting another camera task here
            // would compete with it on the next frame.
            _finalZoomStarted = true;
            StartBackdropDarkening();
        }
        _enhancedImpactScheduled = true;
        AdvanceClock();
        _impactStartedAt = _activeSeconds;
        _enhancedImpactTask = RunEnhancedImpact();
    }

    private bool IsFinalPrimaryHitReady() =>
        ResolvedHits <= 1
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
        List<ProcessModeSnapshot> processes = CaptureImpactProcesses(targetNodes);
        ApplyDeathSquashes(impactVisuals.Values);
        List<NCreature> frozenHurtTracks = [];
        FinisherImpactVfxFreezeLease? frozenImpactVfx = null;

        try
        {
            frozenImpactVfx = FinisherImpactVfxFreezeLease.Acquire(
                _room,
                targetNodes,
                _impactWaveBaseline ?? _vfxBaselineChildIds,
                ImpactVfxTargetMargin,
                _ranged?.Visuals);
            foreach (NCreature targetNode in targetNodes)
            {
                if (DoomHurtPoseController.TryFreeze(targetNode))
                {
                    frozenHurtTracks.Add(targetNode);
                }
            }

            FreezeImpactProcesses(processes);

            _camera.PlayScreenShake(
                ShakeStrength.TooMuch,
                ShakeDuration.Short,
                rejectWeakerReplacement: true);
            PlayReverseImpactAudio();
            if (_impactCamera)
            {
                await PlayMeasuredImpactCamera(impactVisuals.Values);
                return;
            }
            float elapsed = 0f;
            while (elapsed < ImpactLeadSeconds)
            {
                elapsed += await NextFrame();
                float progress = EaseOut(Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f));
                ApplyEnemyFlash(impactVisuals.Values, progress);
                _camera.SetTransform(
                    cameraStartPosition.Lerp(punchPosition, progress),
                    Mathf.Lerp(cameraStartScale, punchScale, progress));
            }

            RestoreEnemyFlash(impactVisuals.Values);
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
            frozenImpactVfx?.Dispose();
            DoomHurtPoseController.Resume(frozenHurtTracks);
            RestoreImpactVisuals(impactVisuals.Values);
            RestoreImpactProcesses(processes);
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
        List<ProcessModeSnapshot> processes = CaptureImpactProcesses(targetNodes);
        ApplyDeathSquashes(impactVisuals.Values);
        List<NCreature> frozenHurtTracks = [];
        FinisherImpactVfxFreezeLease? frozenImpactVfx = null;
        FinisherImpactPresentation presentation = _presentation
            ?? throw new InvalidOperationException("The enhanced finisher presentation was not initialized.");

        try
        {
            frozenImpactVfx = FinisherImpactVfxFreezeLease.Acquire(
                _room,
                targetNodes,
                _impactWaveBaseline ?? _vfxBaselineChildIds,
                ImpactVfxTargetMargin,
                _ranged?.Visuals);
            foreach (NCreature targetNode in targetNodes)
            {
                if (DoomHurtPoseController.TryFreeze(targetNode))
                {
                    frozenHurtTracks.Add(targetNode);
                }
            }

            FreezeImpactProcesses(processes);

            _camera.PlayScreenShake(
                ShakeStrength.TooMuch,
                ShakeDuration.Short,
                rejectWeakerReplacement: true);
            PlayReverseImpactAudio();
            if (_impactCamera)
            {
                await PlayMeasuredImpactCamera(impactVisuals.Values);
                return;
            }
            float elapsed = 0f;
            while (elapsed < ImpactLeadSeconds)
            {
                elapsed += await NextEnhancedFrame(cancellationToken);
                float linearProgress = Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f);
                float progress = EaseOut(linearProgress);
                ApplyEnhancedVictimFeedback(impactVisuals.Values, progress, flash: true);
                _camera.SetTransform(
                    cameraStartPosition.Lerp(punchPosition, progress),
                    Mathf.Lerp(cameraStartScale, punchScale, progress));
            }

            RestoreEnemyFlash(impactVisuals.Values);
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
                    1f - progress,
                    flash: false);
                _camera.SetTransform(
                    punchPosition.Lerp(recoveryPosition, progress),
                    Mathf.Lerp(punchScale, recoveryScale, progress));
            }

            _camera.SetTransform(recoveryPosition, recoveryScale);
        }
        finally
        {
            frozenImpactVfx?.Dispose();
            DoomHurtPoseController.Resume(frozenHurtTracks);
            RestoreImpactVisuals(impactVisuals.Values);
            RestoreImpactProcesses(processes);
        }
    }

    private async Task PlayMeasuredImpactCamera(IEnumerable<ImpactVisualSnapshot> visuals)
    {
        AdvanceClock();
        _impactStartedAt ??= _activeSeconds;
        _measuredCameraTask = RunMeasuredImpactCamera();
        float releaseSeconds = ImpactReleaseSeconds(_previewProfile);
        while (_activeSeconds - _impactStartedAt.Value < releaseSeconds)
        {
            _impactCancellation.Token.ThrowIfCancellationRequested();
            float elapsed = _activeSeconds - _impactStartedAt.Value;
            ApplyEnhancedVictimFeedback(visuals,
                Mathf.Clamp(1f - elapsed / ImpactLeadSeconds, 0f, 1f), flash: elapsed < ImpactLeadSeconds);
            await NextFrame();
        }
    }

    private async Task RunMeasuredImpactCamera()
    {
        Vector2 start = _camera.CurrentPosition;
        float from = _camera.CurrentScale;
        float to = _camera.BaselineScale.X * FinalHitZoomMultiplier * CameraPunchScaleMultiplier;
        Vector2 end = GetFramedCameraPosition(to);
        float origin = _impactStartedAt!.Value;
        float returnStart = ImpactReturnStart(_previewProfile);
        float endSeconds = ImpactEndSeconds(_previewProfile);
        while (_activeSeconds - origin < endSeconds)
        {
            float elapsed = _activeSeconds - origin;
            if (_impactCancellation.IsCancellationRequested && elapsed < ImpactReleaseSeconds(_previewProfile)) break;
            _camera.ApplyHeldScreenShake(elapsed, ImpactReleaseSeconds(_previewProfile), endSeconds);
            if (elapsed < returnStart)
            {
                float scale = ImpactZoom(from, to, elapsed / ImpactZoomSeconds(_previewProfile));
                float progress = Math.Abs(to - from) < 0.0001f ? 1f : (scale - from) / (to - from);
                _camera.SetTransform(start.Lerp(end, progress), scale);
            }
            else
            {
                float progress = CombatCinematicCameraLease.EaseOutCubic(
                    (elapsed - returnStart) / (endSeconds - returnStart));
                _camera.SetTransform(end.Lerp(_camera.BaselinePosition, progress),
                    Mathf.Lerp(to, _camera.BaselineScale.X, progress));
                SetBackdropIntensity(1f - progress);
            }
            await NextFrame();
        }
        _camera.ResetToBaseline();
        SetBackdropIntensity(0f);
    }

    private async Task<float> NextEnhancedFrame(CancellationToken cancellationToken)
    {
        float delta = await NextFrame();
        cancellationToken.ThrowIfCancellationRequested();
        return delta;
    }

    public async Task EnsureActionPeak()
    {
        if (!IsCompanionIai || _actionPeakReached)
        {
            return;
        }

        if (!_actionStarted)
        {
            _actionStarted = true;
            _actionPeakTask = RunActionToPeak();
        }

        await _actionPeakTask;
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
                _approach?.ApplyProgress(elapsed / duration);
            }

            _approach?.ApplyProgress(1f);
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
