using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Events;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Models;

namespace NinjaSlayer.Code.Combat;

internal sealed class SawatariEventSession
{
    private const float IntermissionMoveSeconds = 0.4f;

    private static readonly ConditionalWeakTable<CombatState, SawatariEventSession> Sessions = new();

    private readonly CombatState _state;
    private readonly NCombatRoom _room;
    private readonly SawatariEvent[] _events;
    private readonly Player _ninjaSlayer;
    private readonly SawatariEventPhaseGate _phases = new();
    private Creature _companion;
    private Creature? _duelCreature;
    private Control? _enemyContainer;
    private int _supportRound;
    private bool _entrancePlayed;
    private bool _bambooVoicePending;
    private bool _ownsCombatPause;

    private SawatariEventSession(
        CombatState state,
        NCombatRoom room,
        SawatariEvent[] events,
        Player ninjaSlayer)
    {
        _state = state;
        _room = room;
        _events = events;
        _ninjaSlayer = ninjaSlayer;
        var model = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
        _companion = state.CreateCreature(model, CombatSide.Player, null);
        _companion.PetOwner = ninjaSlayer;
    }

    private void Initialize(EventRoom eventRoom)
    {
        _state.AddCreature(_companion);
        _room.AddCreature(_companion);
        SetFacing(_companion, faceRight: true);
        YamotoKokiAllyLayoutPatch.Reflow(_room);
        SawatariMusicSession.Begin(eventRoom);
    }

    public SawatariEventPhase Phase => _phases.Current;
    public bool UseDuelRewards { get; private set; }

    public static SawatariEventSession Create(
        CombatState state,
        NCombatRoom room,
        SawatariEvent[] events,
        Player localOwner,
        EventRoom eventRoom)
    {
        if (Sessions.TryGetValue(state, out SawatariEventSession? existing))
        {
            return existing;
        }

        Player ninjaSlayer = state.RunState.Players
            .Where(player => player.Character is INinjaSlayerCharacter)
            .OrderBy(state.RunState.GetPlayerSlotIndex)
            .FirstOrDefault()
            ?? localOwner;
        var session = new SawatariEventSession(state, room, events, ninjaSlayer);
        try
        {
            session.Initialize(eventRoom);
            Sessions.Add(state, session);
            return session;
        }
        catch
        {
            session.AbortBeforeCombat();
            throw;
        }
    }

    public static bool TryGet(
        ICombatState? combatState,
        [NotNullWhen(true)]
        out SawatariEventSession? session)
    {
        if (combatState is CombatState state && Sessions.TryGetValue(state, out session))
        {
            return true;
        }

        session = null;
        return false;
    }

    public static bool ShouldStopCombat(ICombatState combatState) =>
        TryGet(combatState, out SawatariEventSession? session)
        && session.Phase != SawatariEventPhase.Finalizing;

    public static bool IsActiveDuelCreature(Creature creature) =>
        TryGet(creature.CombatState, out SawatariEventSession? session)
        && session.Phase == SawatariEventPhase.Duel
        && ReferenceEquals(session._duelCreature, creature);

    public static bool ShouldReplaceRewards(CombatRoom room) =>
        TryGet(room.CombatState, out SawatariEventSession? session)
        && session.UseDuelRewards;

    public async Task PlayNinjaSlayerEntrance()
    {
        if (_entrancePlayed)
        {
            return;
        }

        _entrancePlayed = true;
        await AncientEntranceAnimation.Play(_ninjaSlayer);
    }

    public Task PlaySupportTurn(SawatariMonster monster, CombatSide side)
    {
        if (side != CombatSide.Player
            || Phase != SawatariEventPhase.FirstCombat
            || !ReferenceEquals(monster.Creature, _companion)
            || _supportRound == _state.RoundNumber)
        {
            return Task.CompletedTask;
        }

        _supportRound = _state.RoundNumber;
        Creature[] targets = _state.HittableEnemies
            .Where(target => target.IsAlive && target.IsHittable)
            .ToArray();
        Creature? target = targets.Length == 0
            ? null
            : _state.RunState.Rng.CombatTargets.NextItem(targets);
        return target == null ? Task.CompletedTask : monster.PlayAttack(target);
    }

    public void CaptureDyingCreature(Creature creature)
    {
        if (Phase != SawatariEventPhase.FirstCombat || !creature.IsPrimaryEnemy)
        {
            return;
        }

        NCreature? node = _room.GetCreatureNode(creature);
        if (node?.GetParent() is Control enemyContainer)
        {
            _enemyContainer = enemyContainer;
        }
    }

    public void ObserveDeath(Creature creature, float deathAnimLength)
    {
        if (Phase == SawatariEventPhase.FirstCombat
            && creature.IsPrimaryEnemy
            && !_state.Enemies.Any(enemy =>
                !ReferenceEquals(enemy, creature)
                && enemy.IsAlive
                && enemy.IsPrimaryEnemy)
            && _phases.TryMove(
                SawatariEventPhase.FirstCombat,
                SawatariEventPhase.Intermission))
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.ForestSawatariEndEvent);
            BeginDecisionPhase(() => BeginIntermission(deathAnimLength));
        }
        else if (Phase == SawatariEventPhase.Duel
            && ReferenceEquals(creature, _duelCreature)
            && _phases.TryMove(
                SawatariEventPhase.Duel,
                SawatariEventPhase.DuelResult))
        {
            BeginDecisionPhase(BeginDuelResult);
        }
    }

    public async Task TakeRegularLoot()
    {
        if (Phase != SawatariEventPhase.Intermission)
        {
            return;
        }

        UseDuelRewards = false;
        RemoveCreature(_companion);
        _phases.FinalizeEvent();
        SawatariEventUi.Hide();
        SawatariMusicSession.PlayLeave();
        ExitDecisionState();
        await CombatManager.Instance.CheckWinCondition();
    }

    public async Task StartDuel()
    {
        if (!_phases.TryMove(SawatariEventPhase.Intermission, SawatariEventPhase.DuelTransition))
        {
            return;
        }

        SawatariMusicSession.PlayDuel();
        SawatariEventUi.Hide();
        try
        {
            NCreature companionNode = _room.GetCreatureNode(_companion)
                ?? throw new InvalidOperationException("Sawatari companion node is unavailable.");
            CreatureVisualTransform transform = CreatureVisualTransform.Capture(companionNode);
            RemoveCreature(_companion);

            var model = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
            Creature duelCreature = _state.CreateCreature(model, CombatSide.Enemy, null);
            _duelCreature = duelCreature;
            Task addTask = CreatureCmd.Add(duelCreature);
            NCreature? duelNode = _room.GetCreatureNode(duelCreature);
            if (duelNode != null)
            {
                duelNode.Hide();
                transform.Apply(duelNode);
            }

            await addTask;
            duelNode ??= _room.GetCreatureNode(duelCreature);
            if (duelNode == null)
            {
                throw new InvalidOperationException("Sawatari duel node is unavailable.");
            }

            transform.Apply(duelNode);
            SetFacing(duelCreature, faceRight: false);
            _room.SetCreatureIsInteractable(duelCreature, on: true);
            duelNode.Show();

            if (!_phases.TryMove(SawatariEventPhase.DuelTransition, SawatariEventPhase.Duel))
            {
                throw new InvalidOperationException("Sawatari duel phase changed during setup.");
            }

            _bambooVoicePending = true;
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.ForestSawatariDuelEvent);
            bool endPlayerTurn = _state.CurrentSide == CombatSide.Player;
            if (endPlayerTurn)
            {
                foreach (Player player in _state.Players)
                {
                    PlayerCmd.EndTurn(player, canBackOut: false);
                }
            }

            _room.AddChildSafely(NCombatStartBanner.Create());
            ExitDecisionState();
        }
        catch (Exception exception)
        {
            await FallBackToRegularRewards(exception);
        }
    }

    public bool ConsumeBambooVoiceAfterAttack(Creature creature)
    {
        if (Phase != SawatariEventPhase.Duel
            || !ReferenceEquals(creature, _duelCreature)
            || !_bambooVoicePending)
        {
            return false;
        }

        _bambooVoicePending = false;
        return true;
    }

    public async Task TakeDuelRewards()
    {
        if (Phase != SawatariEventPhase.DuelResult)
        {
            return;
        }

        UseDuelRewards = true;
        _phases.FinalizeEvent();
        SawatariEventUi.Hide();
        ExitDecisionState();
        await CombatManager.Instance.CheckWinCondition();
    }

    private async Task BeginIntermission(float deathAnimLength)
    {
        if (deathAnimLength > 0f)
        {
            await Cmd.Wait(deathAnimLength);
        }
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        await NextFrame();

        SawatariMusicSession.PlayDecision();
        try
        {
            NCreature companionNode = _room.GetCreatureNode(_companion)
                ?? throw new InvalidOperationException("Sawatari companion node is unavailable.");
            Vector2 destination = ResolveIntermissionPosition(companionNode);
            await TweenGlobalPosition(companionNode, destination, IntermissionMoveSeconds);
            SetFacing(_companion, faceRight: false);

            foreach (SawatariEvent eventModel in _events)
            {
                eventModel.ShowIntermissionPage();
            }
            SawatariEventUi.Show(_state.Players.Count > 1);
        }
        catch (Exception exception)
        {
            await FallBackToRegularRewards(exception);
        }
    }

    private async Task BeginDuelResult()
    {
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        await NextFrame();
        SawatariMusicSession.PlayDuelEnd();
        try
        {
            if (_duelCreature != null)
            {
                RemoveCreature(_duelCreature);
            }

            foreach (SawatariEvent eventModel in _events)
            {
                eventModel.ShowDuelResultPage();
            }
            SawatariEventUi.Show(_state.Players.Count > 1);
        }
        catch (Exception exception)
        {
            await FallBackToRegularRewards(exception);
        }
    }

    private async Task FallBackToRegularRewards(Exception exception)
    {
        Entry.Logger.Error($"Sawatari event phase failed; using regular combat rewards: {exception}");
        SawatariEventPhase failedPhase = Phase;
        UseDuelRewards = false;
        RemoveCreatureIfPresent(_duelCreature);
        RemoveCreatureIfPresent(_companion);
        foreach (SawatariEvent eventModel in _events)
        {
            eventModel.FinishForFallback();
        }
        _phases.FinalizeEvent();
        SawatariEventUi.Hide();
        SawatariMusicSession.FinishFallback(failedPhase);
        ExitDecisionState();
        await CombatManager.Instance.CheckWinCondition();
    }

    private void BeginDecisionPhase(Func<Task> present)
    {
        try
        {
            EnterDecisionState();
            _ = TaskHelper.RunSafely(present());
        }
        catch (Exception exception)
        {
            _ = TaskHelper.RunSafely(FallBackToRegularRewards(exception));
        }
    }

    private void EnterDecisionState()
    {
        CombatManager manager = CombatManager.Instance;
        manager.OnEndedTurnLocally();
        if (!_ownsCombatPause && !manager.IsPaused)
        {
            manager.Pause();
            _ownsCombatPause = manager.IsPaused;
        }

        _room.Ui.Hide();
    }

    private void ExitDecisionState()
    {
        try
        {
            _room.Ui.Show();
        }
        finally
        {
            if (_ownsCombatPause)
            {
                _ownsCombatPause = false;
                CombatManager.Instance.Unpause();
            }
        }
    }

    private Vector2 ResolveIntermissionPosition(NCreature companionNode)
    {
        if (_enemyContainer != null)
        {
            float scaling = _state.Encounter?.GetCameraScaling()
                ?? _room.SceneContainer.Scale.X;
            Vector2 localPosition = new(
                SawatariEventRules.ResolveSingleEnemyX(
                    companionNode.Visuals.Bounds.Size.X,
                    scaling),
                SawatariEventRules.SingleEnemyY);
            return _enemyContainer.GetGlobalTransform() * localPosition;
        }

        return companionNode.GlobalPosition + Vector2.Right * 600f;
    }

    private void RemoveCreatureIfPresent(Creature? creature)
    {
        if (creature != null && _state.ContainsCreature(creature))
        {
            RemoveCreature(creature);
        }
    }

    private void RemoveCreature(Creature creature)
    {
        NCreature? node = _room.GetCreatureNode(creature);
        if (node != null)
        {
            node.Hide();
            _room.RemoveCreatureNode(node);
            node.QueueFreeSafely();
        }

        if (CombatManager.Instance.IsInProgress)
        {
            CombatManager.Instance.RemoveCreature(creature);
        }
        if (_state.ContainsCreature(creature))
        {
            _state.RemoveCreature(creature);
        }
    }

    private void SetFacing(Creature creature, bool faceRight)
    {
        var visuals = _room.GetCreatureNode(creature)?.Visuals;
        Sprite2D? body = NinjaSlayerVisualRig.GetBodySprite(visuals);
        if (body != null)
        {
            body.FlipH = faceRight;
        }

        if (NinjaSlayerVisualRig.GetShadow(visuals) is { } shadow)
        {
            shadow.FlipH = faceRight;
        }
    }

    internal void AbortBeforeCombat()
    {
        RemoveCreatureIfPresent(_companion);
        SawatariMusicSession.Abort();
    }

    private static async Task TweenGlobalPosition(Control node, Vector2 destination, float duration)
    {
        Vector2 origin = node.GlobalPosition;
        Tween tween = node.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress => node.GlobalPosition = origin.Lerp(destination, progress)),
                0f,
                1f,
                duration)
            .SetEase(Tween.EaseType.InOut)
            .SetTrans(Tween.TransitionType.Quad);
        await TweenPlayback.AwaitCompletion(tween, node);
        node.GlobalPosition = destination;
    }

    private static async Task NextFrame()
    {
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private readonly record struct CreatureVisualTransform(
        Vector2 GlobalPosition,
        float Rotation,
        Vector2 Scale)
    {
        public static CreatureVisualTransform Capture(Control node) =>
            new(node.GlobalPosition, node.Rotation, node.Scale);

        public void Apply(Control node)
        {
            node.GlobalPosition = GlobalPosition;
            node.Rotation = Rotation;
            node.Scale = Scale;
        }
    }
}

internal static class SawatariMusicSession
{
    private static EventRoom? _eventRoom;
    private static bool _subscribed;

    public static void Begin(EventRoom eventRoom)
    {
        Clear(stopMusic: false);
        _eventRoom = eventRoom;
        RunManager.Instance.RoomExited += OnRoomExited;
        _subscribed = true;
        NRunMusicController.Instance?.PlayCustomMusic(
            NinjaSlayerAudio.SawatariCoopMusicEvent);
    }

    public static void PlayDecision() => SetPhase(NinjaSlayerAudio.SawatariCoopDecisionPhase);

    public static void PlayLeave() => SetPhase(NinjaSlayerAudio.SawatariCoopLeavePhase);

    public static void PlayDuel() => SetPhase(NinjaSlayerAudio.SawatariCoopDuelPhase);

    public static void PlayDuelEnd() => SetPhase(NinjaSlayerAudio.SawatariCoopDuelEndPhase);

    public static void FinishFallback(SawatariEventPhase failedPhase)
    {
        switch (failedPhase)
        {
            case SawatariEventPhase.Intermission:
                PlayLeave();
                break;
            case SawatariEventPhase.DuelTransition:
            case SawatariEventPhase.Duel:
            case SawatariEventPhase.DuelResult:
                PlayDuelEnd();
                break;
            default:
                Abort();
                break;
        }
    }

    private static void SetPhase(float phase) =>
        NRunMusicController.Instance?.UpdateMusicParameter(
            NinjaSlayerAudio.SawatariCoopPhaseParameter,
            phase);

    public static void Abort()
    {
        if (_subscribed)
        {
            Clear(stopMusic: true);
        }
    }

    private static void OnRoomExited()
    {
        AbstractRoom? current = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
        if (ReferenceEquals(current, _eventRoom)
            || current is CombatRoom combatRoom
                && combatRoom.ParentEventId == ModelDb.Event<SawatariEvent>().Id)
        {
            return;
        }

        Clear(stopMusic: true);
    }

    private static void Clear(bool stopMusic)
    {
        if (_subscribed)
        {
            RunManager.Instance.RoomExited -= OnRoomExited;
        }

        _eventRoom = null;
        _subscribed = false;

        if (!stopMusic)
        {
            return;
        }

        try
        {
            NRunMusicController? controller = NRunMusicController.Instance;
            controller?.StopCustomMusic();
            if (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom != null)
            {
                controller?.UpdateTrack();
            }
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Sawatari music cleanup failed: {exception.Message}");
        }
    }
}
