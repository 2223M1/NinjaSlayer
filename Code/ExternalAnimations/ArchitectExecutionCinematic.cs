using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Localization;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;

public sealed partial class ArchitectExecutionCinematic : Node
{
    private const string ControllerName = "NinjaSlayerArchitectExecution";
    private const float ExitSpeedPixelsPerSecond = 840f;
    private const float ExitMargin = 160f;

    private TheArchitect _eventModel = null!;
    private Creature _owner = null!;
    private NCreature _ownerNode = null!;
    private NCreature _architectNode = null!;
    private NCombatRoom _room = null!;
    private readonly CinematicSessionLifetime _runLifetime = new();
    private CinematicSessionLifetime? _exitLifetime;
    private Task? _exitTask;
    private Task? _victoryCompletionTask;
    private BossDismembermentSnapshot? _dismembermentSnapshot;
    private BossDismembermentPresentation? _deathPresentation;
    private Vector2 _ownerStartPosition;
    private Vector2 _architectBodyPosition;
    private Vector2 _architectBodyScale;
    private float _architectBodyRotation;
    private Color _architectBodyModulate;
    private bool _initialized;
    private bool _completed;
    private bool _architectDeathCommitted;
    private bool _architectVisualHidden;
    private Task _executionTask = Task.CompletedTask;

    public static Task Play(TheArchitect eventModel)
    {
        Creature? owner = eventModel.Owner?.Creature;
        NCombatRoom? room = NCombatRoom.Instance;
        NCreature? ownerNode = room?.GetCreatureNode(owner);
        NCreature? architectNode = room?.CreatureNodes
            .FirstOrDefault(node => node.Entity.Monster is Architect);
        if (owner?.Player?.Character is not INinjaSlayerCharacter
            || room == null
            || ownerNode == null
            || architectNode == null)
        {
            throw new InvalidOperationException("Architect execution requires the active event's two creature nodes.");
        }
        if (room.GetNodeOrNull<ArchitectExecutionCinematic>(ControllerName) is { } existing)
            return existing._executionTask;

        var controller = new ArchitectExecutionCinematic
        {
            Name = ControllerName,
            _eventModel = eventModel,
            _owner = owner,
            _ownerNode = ownerNode,
            _architectNode = architectNode,
            _room = room
        };
        room.AddChildSafely(controller);
        controller.Begin();
        return controller._executionTask;
    }

    public override void _ExitTree()
    {
        _runLifetime.Dispose();
        Interlocked.Exchange(ref _exitLifetime, null)?.Dispose();
        CancelDeathPresentation();
        DisposeDismembermentSnapshot();
        HideArchitectVisual();
        if (_initialized)
        {
            RestoreTemporaryState(restoreOwnerPosition: _exitTask == null);
        }
    }

    private void Begin()
    {
        _ownerStartPosition = _ownerNode.Position;
        _architectBodyPosition = _architectNode.Body.Position;
        _architectBodyScale = _architectNode.Body.Scale;
        _architectBodyRotation = _architectNode.Body.Rotation;
        _architectBodyModulate = _architectNode.Body.SelfModulate;
        _initialized = true;
        _dismembermentSnapshot = BossDismembermentPresentation.TryCapture(
            _room,
            _architectNode);
        _executionTask = Run(_runLifetime.Token);
    }

    private async Task Run(CancellationToken cancelToken)
    {
        try
        {
            if (ArchitectGreetingBowPatch.Greetings.TryGetValue(_eventModel, out Task? greeting))
                await greeting.WaitAsync(cancelToken);
            await ArchitectPotionIntegration.WaitForThrows(_room, cancelToken);
            cancelToken.ThrowIfCancellationRequested();
            await PlayOwnedMeleeExecution(cancelToken);

            _completed = true;
            await CompleteEvent();
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested || !IsRuntimeValid())
        {
        }
        catch (Exception exception)
        {
            Entry.Logger.Error($"Architect execution cinematic failed: {exception}");
            throw;
        }
        finally
        {
            if (!_completed)
            {
                _exitLifetime?.Cancel();
            }

            CancelDeathPresentation();
            DisposeDismembermentSnapshot();
            HideArchitectVisual();
            RestoreTemporaryState(restoreOwnerPosition: _exitTask == null);
            _runLifetime.Dispose();
        }
    }

    internal static async Task PlayGreetingBow(Creature owner)
    {
        await AncientEntranceAnimation.Play(owner.Player!);
        NCombatRoom? room = NCombatRoom.Instance;
        NCreature? architect = room?.CreatureNodes.FirstOrDefault(node => node.Entity.Monster is Architect);
        if (room == null || architect == null) return; // The dialogue room may have been exited while entering.
        NinjaSlayerFacingState.SyncForTarget(owner, architect.Entity);
        NinjaSlayerAimPose? pose = NinjaSlayerAimPose.Get(owner);
        using var bow = pose?.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Offset, 1f);
        if (bow == null) return;
        bow.Paused = true;
        float elapsed = 0f;
        try
        {
            while (elapsed < 1f && GodotObject.IsInstanceValid(room) && room.IsInsideTree()
                && ReferenceEquals(NCombatRoom.Instance, room) && GodotObject.IsInstanceValid(pose))
            {
                float weight = elapsed < .2f ? Mathf.SmoothStep(0f, 1f, elapsed / .2f)
                    : elapsed < .7f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (elapsed - .7f) / .3f);
                bow.Radians = Mathf.DegToRad(18f) * bow.Facing * weight;
                pose!.SyncNow();
                await room.ToSignal(room.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (GodotObject.IsInstanceValid(room) && room.ProcessMode != ProcessModeEnum.Disabled)
                    elapsed += Math.Min((float)room.GetProcessDeltaTime(), .05f);
            }
        }
        finally
        {
            bow.Dispose();
            if (pose != null && GodotObject.IsInstanceValid(pose)) pose.SyncNow();
        }
    }

    internal static string? MeleeTrigger(CardModel card) => card switch
    {
        CollapseFistRedesignV1 or Slaughter or StraightKiRedesignV1
            or KarateStraightRedesignV1 or LeftHeavyPunchRedesignV1 or RightHeavyPunchRedesignV1
            or RightHeavyPunchAfterSkillRedesignV1 or OneDrinkOneStrikeRedesignV1
            or SatsubatsuRedesignV1 or RoundhouseKickRedesignV1 or SweepKickRedesignV1
            or StormFistRedesignV1 => "SlowAttack",
        StrikeNinjaSlayerRedesignV1 or ChopRedesignV1 or CommonChopRedesignV1
            or ChopStrikeRedesignV1 or CombatAdjustmentRedesignV1 or PalmThrustRedesignV1
            or WhiskTeaFlashRedesignV1 or SpiralRoundhouseJumpRedesignV1 => "Attack",
        DragonFlyingKickRedesignV1 => "FlyingKick",
        TornadoFistRedesignV1 => TornadoFistSpinAnimation.TriggerName,
        AlabamaDropRedesignV1 => "AlabamaDrop",
        _ => null
    };

    private async Task PlayOwnedMeleeExecution(CancellationToken cancelToken)
    {
        var cards = _owner.Player!.Deck.Cards.Where(card => MeleeTrigger(card) != null).ToList();
        CardModel card;
        if (cards.Count > 0) card = cards[_eventModel.Rng.NextInt(cards.Count)];
        else
        {
            card = ModelDb.Card<StrikeNinjaSlayerRedesignV1>().ToMutable();
            card.Owner = _owner.Player;
        }
        string trigger = MeleeTrigger(card)!;
        Entry.Logger.Info($"Architect execution selected owned melee: {card.Id.Entry}, trigger={trigger}, fallback={cards.Count == 0}.");
        var play = new CardPlay
        {
            Card = card,
#if !NINJASLAYER_CHANNEL_STABLE
            Player = _owner.Player,
#endif
            Target = _architectNode.Entity,
            ResultPile = PileType.None, Resources = new ResourceInfo
            { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
            IsAutoPlay = true, PlayIndex = 0, PlayCount = 1
        };
        if (!CombatCinematicCameraLease.TryAcquire(_room, "NinjaSlayer Architect execution", out var camera))
            throw new InvalidOperationException("Architect execution could not acquire its camera.");
        var request = new FinisherSessionRequest(FinisherScenarioKind.NinjaSlayerAttack,
            FinisherCompletionCondition.AllCandidatesLethal, _owner, _ownerNode, _architectNode,
            [_architectNode.Entity], camera, play, false, 1, ContinuousPlayerApproach: true);
        if (!FinisherSessionRegistry.TryRegisterSession(request, _owner.CombatState!, _room, out var session))
        {
            camera.Dispose();
            throw new InvalidOperationException("Architect execution could not acquire its finisher.");
        }
        await using (session)
        {
            Task? death = null;
            session.Begin();
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerKorosuBeshiEvent);
            if (trigger == "AlabamaDrop")
                await AlabamaDropAnimation.Play(_owner, _architectNode.Entity, Impact);
            else
            {
                if (trigger == "FlyingKick") await JumpAnimation.PlayFlyingKick(_owner);
                else if (trigger == TornadoFistSpinAnimation.TriggerName)
                    await Task.WhenAll(CreatureCmd.TriggerAnim(_owner, trigger, TornadoFistSpinAnimation.TurnSeconds),
                        session.PlayActionToPeak(_owner, CombatActionTimingRuntime.TriggerSeconds(TornadoFistSpinAnimation.TurnSeconds)));
                else await CreatureCmd.TriggerAnim(_owner, trigger, _owner.Player.Character.AttackAnimDelay);
                await Impact();
            }
            if (trigger != "AlabamaDrop")
                session.PrepareArchitectExit(trigger == "Attack"
                    ? NinjaSlayerCombatVisuals.AttackLungeDistance : NinjaSlayerCombatVisuals.SlowAttackLungeDistance);
            await session.CompleteAsync(playPose: false);
            StartExitScene();
            await Task.WhenAll(death ?? throw new InvalidOperationException(
                "Architect execution did not reach its death handoff."), _exitTask!);

            async Task Impact()
            {
                cancelToken.ThrowIfCancellationRequested();
                Creature target = _architectNode.Entity;
                int score = Math.Max(1, ScoreUtility.CalculateScore(_owner.Player.RunState, won: true));
                _room.CombatVfxContainer.AddChildSafely(NDamageNumVfx.Create(target, score, requireInteractable: false));
                _room.CombatVfxContainer.AddChildSafely(NHitSparkVfx.Create(target, requireInteractable: false));
                if (trigger != "AlabamaDrop")
                {
                    if (NinjaSlayerAimPose.IsSomersaultHeavy(card))
                    {
                        VfxCmd.PlayOnCreature(target, VfxCmd.heavyBluntPath);
                        NDebugAudioManager.Instance?.Play(TmpSfx.heavyAttack);
                    }
                    else NinjaSlayerCombatVfx.PlayDefectStrikeHitFx(target);
                    await CreatureCmd.TriggerAnim(target, "Hit", 0f);
                }
                await session.PlayArchitectImpact(cancelToken);
                // Death begins at Doom release, while the attack recovers.
                death = PlayArchitectDeath(cancelToken);
                _ = TaskHelper.RunSafely(death);
            }
        }
    }

    private async Task PlayArchitectDeath(CancellationToken cancelToken)
    {
        AlabamaDropAnimation.ReleaseVictimBeforeDeath(_architectNode);
        CreatureDeathInteractionAdapter.Disable(_architectNode);
        _architectNode.AnimHideIntent();
        _architectNode.AnimDisableUi();
        _architectDeathCommitted = true;

        BossDismembermentSnapshot? snapshot = _dismembermentSnapshot;
        _dismembermentSnapshot = null;
        try
        {
            _deathPresentation = BossDismembermentPresentation.TrySpawnArchitectLead(
                _room,
                _architectNode,
                snapshot,
                BossBurstPresentationCoordinator.FragmentZIndex);
        }
        finally
        {
            snapshot?.Dispose();
        }

        string monsterId = _architectNode.Entity.Monster?.Id.Entry ?? "ARCHITECT";
        bool fragmentReplacementReady = _deathPresentation != null;
        BossBurstRegistration registration = BossBurstPresentationCoordinator.Register(
            _room,
            new BossBurstParticipant(
                monsterId,
                SpawnArchitectBurst));
        Task whiteout = BossDeathWhiteoutLease.RunUntilCue(this, _room, _architectNode,
            monsterId, registration.Cue, cancelToken);
        await registration.Cue.WaitAsync(cancelToken);
        await Task.WhenAll(
            whiteout,
            registration.CombatRelease.WaitAsync(cancelToken));
        if (!fragmentReplacementReady)
        {
            HideArchitectVisual();
        }
    }

    private BossDismembermentSpawn SpawnArchitectBurst()
    {
        BossDismembermentPresentation? lead = Interlocked.Exchange(ref _deathPresentation, null);
        return lead != null && GodotObject.IsInstanceValid(lead) && lead.IsInsideTree()
            ? new BossDismembermentSpawn(lead.TriggerArchitectBurst(), lead.Completion)
            : new BossDismembermentSpawn(false, Task.CompletedTask);
    }

    private void CancelDeathPresentation()
    {
        BossDismembermentPresentation? lead = Interlocked.Exchange(ref _deathPresentation, null);
        if (lead != null && GodotObject.IsInstanceValid(lead)) lead.CancelPresentation();
    }

    private void StartExitScene()
    {
        if (_exitTask != null)
        {
            return;
        }

        var lifetime = new CinematicSessionLifetime();
        _exitLifetime = lifetime;
        _exitTask = RunExitScene(lifetime);
        TaskHelper.RunSafely(_exitTask);
    }

    private async Task RunExitScene(CinematicSessionLifetime lifetime)
    {
        try
        {
            await ExitScene(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested || !IsRuntimeValid())
        {
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Architect exit movement ended early: {exception.Message}");
        }
        finally
        {
            Interlocked.CompareExchange(ref _exitLifetime, null, lifetime);
            lifetime.Dispose();
        }
    }

    private async Task ExitScene(CancellationToken cancelToken)
    {
        NinjaSlayerFacingState.SetFacing(_ownerNode, faceLeft: false);
        Vector2 start = _ownerNode.Position;
        float exitX = _room.SceneContainer.Size.X + ExitMargin;
        Vector2 destination = new(exitX, start.Y);
        float duration = Math.Max(0.1f, Mathf.Abs(destination.X - start.X) / ExitSpeedPixelsPerSecond);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += await NextFrame(cancelToken);
            _ownerNode.Position = start.Lerp(
                destination,
                Mathf.Clamp(elapsed / duration, 0f, 1f));
        }

        _ownerNode.Position = destination;
    }

    private void RestoreTemporaryState(bool restoreOwnerPosition)
    {
        SoarSpinAnimation.ResetSpinVisual(_owner);
        if (restoreOwnerPosition && GodotObject.IsInstanceValid(_ownerNode))
        {
            _ownerNode.Position = _ownerStartPosition;
        }

        if (!_architectDeathCommitted && GodotObject.IsInstanceValid(_architectNode))
        {
            DoomHurtPoseController.Resume(_architectNode);
            _architectNode.Body.Position = _architectBodyPosition;
            _architectNode.Body.Scale = _architectBodyScale;
            _architectNode.Body.Rotation = _architectBodyRotation;
            _architectNode.Body.SelfModulate = _architectBodyModulate;
        }

    }

    private void DisposeDismembermentSnapshot()
    {
        BossDismembermentSnapshot? snapshot = _dismembermentSnapshot;
        _dismembermentSnapshot = null;
        snapshot?.Dispose();
    }

    private Task CompleteEvent() => _victoryCompletionTask ??= CompleteEventCore();

    private Task CompleteEventCore()
    {
        ArchitectVictoryCleanup.Mark(_owner);
#if NINJASLAYER_LEGACY_ARCHITECT_VICTORY_COMPLETION
        if (_owner.Player!.RunState.Players.Count > 1)
        {
            _room.SetWaitingForOtherPlayersOverlayVisible(visible: true);
        }

        RunManager.Instance.ActChangeSynchronizer.SetLocalPlayerReady();
        return Task.CompletedTask;
#else
        return RunManager.Instance.WinRun();
#endif
    }

    private void HideArchitectVisual()
    {
        if (!_architectDeathCommitted || _architectVisualHidden)
        {
            return;
        }

        _architectVisualHidden = true;
        if (GodotObject.IsInstanceValid(_architectNode)
            && GodotObject.IsInstanceValid(_architectNode.Body))
        {
            _architectNode.Body.Visible = false;
        }
    }

    private async Task<float> NextFrame(CancellationToken cancelToken)
    {
        cancelToken.ThrowIfCancellationRequested();
        if (!IsRuntimeValid())
        {
            throw new OperationCanceledException("Architect execution room was unloaded.", cancelToken);
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        cancelToken.ThrowIfCancellationRequested();
        return _room.ProcessMode == ProcessModeEnum.Disabled
            ? 0f
            : Math.Min((float)GetProcessDeltaTime(), 0.05f);
    }

    private bool IsRuntimeValid() =>
        GodotObject.IsInstanceValid(_room)
        && GodotObject.IsInstanceValid(_ownerNode)
        && (_architectDeathCommitted || GodotObject.IsInstanceValid(_architectNode))
        && _room.IsInsideTree()
        && ReferenceEquals(NCombatRoom.Instance, _room);

}
