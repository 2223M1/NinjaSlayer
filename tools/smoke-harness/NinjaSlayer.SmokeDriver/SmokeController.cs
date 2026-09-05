using System.Text.Json.Nodes;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Screens;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Events;
using NinjaSlayer.Monsters;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private const int RestartRequestedExitCode = 20;
    private const string InjectedDarkStrikeFailure = "Injected Dark Strike damage-hook failure.";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);
    private readonly SmokeConfiguration _configuration;
    private readonly SmokeCheckpointWriter _checkpoints;
    private readonly SceneTree _tree;
    private readonly TaskCompletionSource _firstCombatCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstMapReached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _sawatariCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _reverseFinisherCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Creature? _observedDarkStrikeAttacker;
    private Creature[] _observedDarkStrikeTargets = [];
    private readonly List<Creature> _darkStrikeDamageHookTargets = [];
    private readonly List<Vector2> _darkStrikeDamageHookPositions = [];
    private readonly List<string> _darkStrikeAudioEvents = [];
    private readonly List<Vector2> _darkStrikeStabVfxPositions = [];
    private DamageResult[] _darkStrikeAttackResults = [];
    private int _darkStrikeBeforeAttackCount;
    private int _darkStrikeAfterAttackCount;
    private bool _throwOnDarkStrikeDamageHook;
    private Action? _onFirstDarkStrikeDamageHook;
    private ICombatState? _observedSawatariCombat;
    private int _sawatariBeforeCombatStartCount;
    private int _sawatariAfterCombatEndCount;
    private int _sawatariDuelBannerCount;
    private bool _observeSawatariDuelBanner;
    private int _tutorialUnknownRollCount;
    private int _hostFilteredUnknownRollCount;
    private int _sawatariEventForced;
    private int _firstCombatClaimed;
    private int _firstMapClaimed;

    public SmokeController(SmokeConfiguration configuration, SceneTree tree)
    {
        _configuration = configuration;
        _checkpoints = new SmokeCheckpointWriter(configuration);
        _tree = tree;
    }

    public static SmokeController? Current { get; private set; }
    public bool ShouldForceCharacter => _configuration.Phase is
        SmokePhase.Fresh
        or SmokePhase.FullAutoSlay
        or SmokePhase.SawatariSameCombat
        or SmokePhase.ReverseFinisher
        or SmokePhase.TransitionPerf;

    public void Start()
    {
        if (Current is not null)
        {
            throw new InvalidOperationException("A smoke controller is already active.");
        }

        Current = this;
        TaskHelper.RunSafely(RunSafelyAsync());
    }

    public bool TryClaimFirstCombat() =>
        _configuration.Phase is SmokePhase.Fresh or SmokePhase.ReverseFinisher
        && Interlocked.CompareExchange(ref _firstCombatClaimed, 1, 0) == 0;

    public Task ExecuteClaimedCombatAsync(Rng random, CancellationToken cancellationToken) =>
        _configuration.Phase == SmokePhase.ReverseFinisher
            ? ExecuteReverseFinisherCombatAsync(cancellationToken)
            : ExecuteFirstCombatAsync(random, cancellationToken);

    public bool TryHoldFirstMap(ref Task result)
    {
        if (_configuration.Phase != SmokePhase.Fresh
            || !_firstCombatCompleted.Task.IsCompleted
            || Interlocked.CompareExchange(ref _firstMapClaimed, 1, 0) != 0)
        {
            return false;
        }

        result = HoldFirstMapAsync();
        return true;
    }

    public bool TryHandleSawatariEventCombat(CancellationToken cancellationToken, ref Task result)
    {
        if (_configuration.Phase != SmokePhase.SawatariSameCombat
            || !RunManager.Instance.EventSynchronizer.Events.OfType<SawatariEvent>().Any())
        {
            return false;
        }

        result = VerifySawatariEventCombat(cancellationToken);
        return true;
    }

    public void ForceFirstSawatariEvent(RunState runState, ref EventModel nextEvent)
    {
        if (_configuration.Phase != SmokePhase.SawatariSameCombat
            || Interlocked.Exchange(ref _sawatariEventForced, 1) != 0)
        {
            return;
        }

        EventModel sawatari = ModelDb.Event<SawatariEvent>();
        runState.AddVisitedEvent(sawatari);
        nextEvent = sawatari;
    }

    public void ObserveSawatariBeforeCombatStart(ICombatState? combatState)
    {
        if (ReferenceEquals(combatState, _observedSawatariCombat))
        {
            _sawatariBeforeCombatStartCount++;
        }
    }

    public void ObserveSawatariAfterCombatEnd(ICombatState? combatState)
    {
        if (ReferenceEquals(combatState, _observedSawatariCombat))
        {
            _sawatariAfterCombatEndCount++;
        }
    }

    public void ObserveCombatStartBanner()
    {
        if (_observeSawatariDuelBanner)
        {
            _sawatariDuelBannerCount++;
        }
    }

    public void ObserveUnknownRoomRoll(
        IRunState runState,
        int hookCalls,
        bool monsterOddsRestored)
    {
        bool tutorialUnknown = runState.UnlockState.NumberOfRuns == 0
            && runState.MapPointHistory
                .SelectMany(history => history)
                .Count(entry => entry.MapPointType == MapPointType.Unknown) < 3;
        Require(
            hookCalls == (tutorialUnknown ? 0 : 1),
            $"UnknownMapPointOdds.Roll invoked room-type hooks {hookCalls} times on a "
            + (tutorialUnknown ? "tutorial" : "normal")
            + " roll.");
        Require(monsterOddsRestored, "UnknownMapPointOdds.Roll leaked temporary MonsterOdds.");
        if (tutorialUnknown)
        {
            _tutorialUnknownRollCount++;
        }
        else
        {
            _hostFilteredUnknownRollCount++;
        }
    }

    private async Task HoldFirstMapAsync()
    {
        await WaitUntilAsync(
            () => NMapScreen.Instance?.IsOpen == true,
            "map did not become visible after first combat",
            timeout: TimeSpan.FromMinutes(2));
        _firstMapReached.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    public void ReportCharacterSelected(string characterId) =>
        _checkpoints.Write(
            "character.selected",
            data: new JsonObject { ["characterId"] = characterId });

    public void ObserveDarkStrikeDamageHook(
        Creature? target,
        Creature? dealer)
    {
        if (target is null
            || !ReferenceEquals(dealer, _observedDarkStrikeAttacker))
        {
            return;
        }

        if (!_observedDarkStrikeTargets.Any(candidate => ReferenceEquals(candidate, target)))
        {
            return;
        }

        NCreature? targetNode = NCombatRoom.Instance?.GetCreatureNode(target);
        if (targetNode is null || !GodotObject.IsInstanceValid(targetNode))
        {
            throw new InvalidOperationException("A Dark Strike impact target node was unavailable.");
        }

        _darkStrikeDamageHookTargets.Add(target);
        _darkStrikeDamageHookPositions.Add(targetNode.VfxSpawnPosition);
        if (_darkStrikeDamageHookTargets.Count == 1)
        {
            Action? action = _onFirstDarkStrikeDamageHook;
            _onFirstDarkStrikeDamageHook = null;
            action?.Invoke();
        }

        if (_throwOnDarkStrikeDamageHook)
        {
            throw new InvalidOperationException(InjectedDarkStrikeFailure);
        }
    }

    public void ObserveDarkStrikeAudio(string? eventPath)
    {
        if (_observedDarkStrikeAttacker is null || eventPath is null)
        {
            return;
        }

        if (eventPath == NinjaSlayerAudio.DarkNinjaStabEvent
            || eventPath == NinjaSlayerAudio.DarkNinjaFailedEvent
            || eventPath == NinjaSlayerAudio.DarkNinjaKirisuteGomenEvent)
        {
            _darkStrikeAudioEvents.Add(eventPath);
        }
    }

    public void ObserveDarkStrikeVfx(Vector2 position, string? path)
    {
        if (_observedDarkStrikeAttacker is not null && path == VfxCmd.dramaticStabPath)
        {
            _darkStrikeStabVfxPositions.Add(position);
        }
    }

    public void ObserveDarkStrikeAttackHook(AttackCommand command, bool after)
    {
        if (!ReferenceEquals(command.Attacker, _observedDarkStrikeAttacker))
        {
            return;
        }

        if (after)
        {
            _darkStrikeAfterAttackCount++;
            _darkStrikeAttackResults = command.Results
                .SelectMany(results => results)
                .ToArray();
        }
        else
        {
            _darkStrikeBeforeAttackCount++;
        }
    }

    public void BeforeFullAutoSlayExit(ref int exitCode)
    {
        if (_configuration.Phase != SmokePhase.FullAutoSlay)
        {
            return;
        }

        try
        {
            Require(exitCode == 0, $"AutoSlay requested failure exit code {exitCode}.");
            Require(_tutorialUnknownRollCount > 0, "Full AutoSlay did not exercise tutorial unknown-room rolls.");
            Require(_hostFilteredUnknownRollCount > 0, "Full AutoSlay did not exercise host-filtered unknown-room rolls.");
            _checkpoints.Write("full-autoslay.completed");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            _checkpoints.Write("driver.failed", "failed", ExceptionData(exception));
            TryCaptureFailureScreenshot();
        }
    }

    public async Task ExecuteFirstCombatAsync(Rng random, CancellationToken cancellationToken)
    {
        _ = random;
        await WaitUntilAsync(() => CombatManager.Instance.IsInProgress, "combat did not start", cancellationToken);
        ICombatState combatState = CombatManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("Combat state was unavailable.");
        Player player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())
            ?? throw new InvalidOperationException("Local player was unavailable.");
        ValidateRedesignRunIdentity(player);
        await WaitUntilAsync(
            () => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "player play phase did not start",
            cancellationToken);
        NTransition transition = FindDescendant<NTransition>(_tree.Root)
            ?? throw new InvalidOperationException("The transition node was unavailable after combat started.");
        NinjaSlayerTransitionOverlay overlay = FindDescendant<NinjaSlayerTransitionOverlay>(transition)
            ?? throw new InvalidOperationException("The NinjaSlayer transition overlay was not created.");
        Require(!overlay.Visible, "The NinjaSlayer transition overlay remained visible after reveal.");
        Require(!transition.InTransition, "The transition remained active after combat started.");
        Require(
            transition.MouseFilter == Control.MouseFilterEnum.Ignore,
            "The transition continued blocking mouse input after reveal.");
        Require(CombatManager.Instance.IsInProgress, "Combat ended before transition verification.");
        _checkpoints.Write("transition.completed");
        _checkpoints.Write(
            "combat.started",
            data: new JsonObject { ["enemyCount"] = combatState.Enemies.Count });

        await PlayerCmd.SetEnergy(10m, player);
        PreparedShurikenRedesignV1 readyBlade = combatState.CreateCard<PreparedShurikenRedesignV1>(player);
        await CardPileCmd.Add(readyBlade, PileType.Hand);
        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), readyBlade, player.Creature);
        Require(player.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single().StackCount == 1,
            "Prepared Shuriken did not create one stock.");
        _checkpoints.Write("shuriken.created");

        var enemyTurnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlayerCmd.EndTurn(
            player,
            canBackOut: false,
            actionDuringEnemyTurn: () =>
            {
                enemyTurnStarted.TrySetResult();
                return Task.CompletedTask;
            });
        await WaitTaskAsync(enemyTurnStarted.Task, "enemy turn did not start", DefaultTimeout);
        await WaitUntilAsync(
            () => CombatManager.Instance.IsInProgress
                && player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "next player turn did not start",
            cancellationToken);
        Require(player.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single().StackCount == 1,
            "End-turn hand cleanup incorrectly consumed shuriken stock.");
        var discarded = combatState.CreateCard<DefendNinjaSlayerRedesignV1>(player);
        await CardPileCmd.Add(discarded, PileType.Hand);
        await CardCmd.Discard(new BlockingPlayerChoiceContext(), discarded);
        Require(!player.PlayerCombatState.OrbQueue.Orbs.OfType<ShurikenOrb>().Any(),
            "An actual discard did not consume and remove the last shuriken stock.");
        _checkpoints.Write("shuriken.lifecycle-cleared");

        Creature focus = combatState.HittableEnemies.FirstOrDefault()
            ?? throw new InvalidOperationException("No hittable enemy remained for the X attack scenario.");
        await VerifyRedesignCardsAndPowers(combatState, player, focus);
        await VerifyCurrentPresentation(combatState, player, focus, cancellationToken);
        await WaitUntilAsync(
            () => !StaggerAnimation.IsActive(player.Creature),
            "The preceding hit animation did not settle before the X attack scenario.",
            cancellationToken);
        await PlayerCmd.SetEnergy(1m, player);
        TornadoFistRedesignV1 nonLethal = combatState.CreateCard<TornadoFistRedesignV1>(player);
        await CardPileCmd.Add(nonLethal, PileType.Hand);
        NCreature playerNode = NCombatRoom.Instance?.GetCreatureNode(player.Creature)
            ?? throw new InvalidOperationException("The local player creature node was unavailable.");
        Vector2 playerPosition = playerNode.Position;
        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), nonLethal, focus);
        Require(CombatManager.Instance.IsInProgress && focus.IsAlive, "The non-lethal X attack unexpectedly ended combat.");
        await WaitUntilAsync(
            () => GodotObject.IsInstanceValid(playerNode)
                && playerNode.Position.IsEqualApprox(playerPosition),
            "The local player did not return to its combat position after the non-lethal X attack.",
            cancellationToken,
            TimeSpan.FromSeconds(20));
        _checkpoints.Write("x-attack.nonlethal-completed");

        await VerifyCardPlayEvasion(combatState, player, focus);
        await VerifyMoveEvasion(combatState, player, focus);
        VerifyPlatformSpineScene();
        await VerifyDarkStrike(combatState, player);

        foreach (Creature enemy in combatState.HittableEnemies.Where(enemy => !ReferenceEquals(enemy, focus)).ToArray())
        {
            await CreatureCmd.Kill(enemy, force: true);
        }
        Require(focus.IsAlive, "The finisher focus died while removing additional enemies.");
        if (focus.CurrentHp > 1)
        {
            await CreatureCmd.Damage(
                new ThrowingPlayerChoiceContext(),
                focus,
                focus.CurrentHp - 1,
                ValueProp.Unblockable | ValueProp.Unpowered,
                player.Creature);
        }
        Require(focus.IsAlive && focus.CurrentHp <= 3, "Could not prepare a deterministic lethal target.");

        FinisherSmokeObserver.Reset(injectPresentationFailure: true);
        await PlayerCmd.SetEnergy(3m, player);
        TornadoFistRedesignV1 lethal = combatState.CreateCard<TornadoFistRedesignV1>(player);
        await CardPileCmd.Add(lethal, PileType.Hand);
        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), lethal, focus);
        _checkpoints.Write(
            "finisher.card-returned",
            data: new JsonObject
            {
                ["combatInProgress"] = CombatManager.Instance.IsInProgress,
                ["focusAlive"] = focus.IsAlive,
                ["focusCurrentHp"] = focus.CurrentHp,
                ["focusBlock"] = focus.Block,
                ["hittableEnemyCount"] = combatState.HittableEnemies.Count
            });
        await WaitUntilAsync(
            () => !focus.IsAlive
                && focus.CurrentHp == 0
                && combatState.HittableEnemies.Count == 0,
            "The lethal X attack did not finish the target and clear hittable enemies.",
            cancellationToken,
            TimeSpan.FromSeconds(20));
        Require(!focus.IsAlive && focus.CurrentHp == 0, "The lethal X attack did not kill its focus.");
        Require(combatState.HittableEnemies.Count == 0, "A hittable enemy remained after the lethal X attack.");
        FinisherSessionSnapshot finisher = await RequireCompletedFinisherAsync(
            "NinjaSlayerAttack",
            resolvedHits: 3,
            focus,
            cancellationToken);
        Require(
            FinisherSmokeObserver.PresentationFailureWasInjected,
            "The trusted presentation failure injection did not reach FinisherImpactPresentation.Create.");
        _checkpoints.Write(
            "finisher.multi-hit.completed",
            data: new JsonObject
            {
                ["sessionId"] = finisher.SessionId,
                ["resolvedHits"] = finisher.ResolvedHits,
                ["killAttempts"] = finisher.KillAttempts.GetValueOrDefault(focus),
                ["successfulKills"] = finisher.SuccessfulKills.GetValueOrDefault(focus)
            });
        _checkpoints.Write("finisher.presentation-fallback.completed");
        bool combatEnded = await CombatManager.Instance.CheckWinCondition();
        Require(combatEnded && !CombatManager.Instance.IsInProgress, "The completed finisher did not end combat.");
        ValidateRedesignCombatProgress(combatState);
        _checkpoints.Write(
            "finisher.completed",
            data: new JsonObject
            {
                ["combatInProgress"] = CombatManager.Instance.IsInProgress,
                ["focusAlive"] = focus.IsAlive,
                ["focusCurrentHp"] = focus.CurrentHp,
                ["hittableEnemyCount"] = combatState.HittableEnemies.Count
            });
        _firstCombatCompleted.TrySetResult();
    }

    private async Task ExecuteReverseFinisherCombatAsync(CancellationToken cancellationToken)
    {
        await WaitUntilAsync(
            () => CombatManager.Instance.IsInProgress,
            "reverse Finisher combat did not start",
            cancellationToken);
        ICombatState combatState = CombatManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("Reverse Finisher combat state was unavailable.");
        Player player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())
            ?? throw new InvalidOperationException("Reverse Finisher local player was unavailable.");
        ValidateRedesignRunIdentity(player);
        await WaitUntilAsync(
            () => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "reverse Finisher combat did not reach the player play phase",
            cancellationToken);

        Creature target = player.Creature;
        await PowerCmd.Remove<ArtifactPower>(target);
        await PowerCmd.Remove<EvasionPower>(target);
        await PowerCmd.Remove<WeakPower>(target);
        if (target.Block > 0)
        {
            await RemoveSmokeBlock(target);
        }

        await CreatureCmd.SetCurrentHp(target, 1);
        Creature attacker = await CreatureCmd.Add<DarkNinjaMonster>(combatState);
        DarkNinjaMonster monster = attacker.Monster as DarkNinjaMonster
            ?? throw new InvalidOperationException("Reverse Finisher did not create a Dark Ninja attacker.");
        FinisherSmokeObserver.Reset();
        await PerformObservedDarkStrike(monster, [target]);
        await WaitUntilAsync(
            () => target.IsDead && target.CurrentHp == 0,
            "Dark Ninja's real move did not kill the one-HP Ninja Slayer",
            cancellationToken,
            TimeSpan.FromSeconds(20));
        FinisherSessionSnapshot reverseFinisher = await RequireCompletedFinisherAsync(
            "EnemyExecutesNinjaSlayer",
            resolvedHits: 1,
            target,
            cancellationToken);

        await WaitFrames(2);
        NTransition transition = FindDescendant<NTransition>(_tree.Root)
            ?? throw new InvalidOperationException("The transition node was unavailable after reverse Finisher.");
        NinjaSlayerTransitionOverlay? overlay =
            FindDescendant<NinjaSlayerTransitionOverlay>(transition);
        Require(!transition.InTransition, "Reverse Finisher left the transition in progress.");
        Require(
            transition.MouseFilter == Control.MouseFilterEnum.Ignore,
            "Reverse Finisher left the transition blocking input.");
        Require(overlay?.Visible != true, "Reverse Finisher left a black transition overlay visible.");
        Require(
            NCombatRoom.Instance?.FindChild(
                "NinjaSlayerFinisherBackdrop",
                recursive: true,
                owned: false) == null,
            "Reverse Finisher left its black backdrop in the combat tree.");
        _checkpoints.Write(
            "finisher.reverse.completed",
            data: new JsonObject
            {
                ["sessionId"] = reverseFinisher.SessionId,
                ["resolvedHits"] = reverseFinisher.ResolvedHits,
                ["killAttempts"] = reverseFinisher.KillAttempts.GetValueOrDefault(target),
                ["successfulKills"] = reverseFinisher.SuccessfulKills.GetValueOrDefault(target),
                ["targetHp"] = target.CurrentHp
            });
        _reverseFinisherCompleted.TrySetResult();
    }

    private async Task<FinisherSessionSnapshot> RequireCompletedFinisherAsync(
        string scenario,
        int resolvedHits,
        Creature victim,
        CancellationToken cancellationToken)
    {
        FinisherSessionSnapshot? completed = null;
        await WaitUntilAsync(
            () =>
            {
                FinisherSessionSnapshot[] matches = FinisherSmokeObserver.Snapshots()
                    .Where(snapshot => snapshot.Scenario == scenario)
                    .ToArray();
                completed = matches.Length == 1 && matches[0].CompletionObserved
                    ? matches[0]
                    : null;
                return completed != null;
            },
            $"{scenario} Finisher completion was not observed",
            cancellationToken,
            TimeSpan.FromSeconds(20));

        FinisherSessionSnapshot snapshot = completed!;
        Require(
            FinisherSmokeObserver.Snapshots().Length == 1,
            "The Finisher smoke window observed more than one session.");
        Require(snapshot.ResolvedHits == resolvedHits,
            $"{scenario} Finisher resolved {snapshot.ResolvedHits} hit(s), expected {resolvedHits}.");
        Require(
            snapshot.Victims.Count == 1 && ReferenceEquals(snapshot.Victims[0], victim),
            $"{scenario} Finisher did not own exactly the expected victim.");
        Require(snapshot.CommitDeaths, $"{scenario} Finisher completed as a cancellation.");
        Require(snapshot.CompletionFailure == null,
            $"{scenario} Finisher completion faulted: {snapshot.CompletionFailure}");
        Require(snapshot.ResourcesReleased,
            $"{scenario} Finisher completion did not release its session resources.");
        Require(snapshot.KillAttempts.GetValueOrDefault(victim) == 1,
            $"{scenario} Finisher attempted to kill its victim more or less than once.");
        Require(snapshot.SuccessfulKills.GetValueOrDefault(victim) == 1,
            $"{scenario} Finisher did not complete exactly one successful victim death.");
        await WaitUntilAsync(
            () => !FinisherSmokeObserver.HasRegisteredSession()
                && !FinisherSmokeObserver.HasActiveCameraLease()
                && FinisherSmokeObserver.ActiveHoverTipLeaseCount() == 0,
            $"{scenario} Finisher left registry, camera, or hover-tip ownership active",
            cancellationToken,
            TimeSpan.FromSeconds(5));
        return snapshot;
    }

    private async Task VerifySawatariEventCombat(CancellationToken cancellationToken)
    {
        await WaitUntilAsync(
            () => CombatManager.Instance.IsInProgress,
            "Sawatari combat did not start",
            cancellationToken);
        CombatManager manager = CombatManager.Instance;
        CombatState state = manager.DebugOnlyGetState()
            ?? throw new InvalidOperationException("Sawatari combat state was unavailable.");
        NCombatRoom room = NEventRoom.Instance?.EmbeddedCombatRoom
            ?? throw new InvalidOperationException("Sawatari combat room was unavailable.");
        Player player = LocalContext.GetMe(state.RunState)
            ?? throw new InvalidOperationException("Sawatari local player was unavailable.");
        PlayerCombatState playerState = player.PlayerCombatState
            ?? throw new InvalidOperationException("Sawatari player combat state was unavailable.");
        object history = manager.History;
        object rng = state.RunState.Rng;
        CardPile[] piles = playerState.AllPiles.ToArray();
        AbstractModel[] powers = player.Creature.Powers.Cast<AbstractModel>().ToArray();

        _observedSawatariCombat = state;
        _sawatariBeforeCombatStartCount = 0;
        _sawatariAfterCombatEndCount = 0;
        _sawatariDuelBannerCount = 0;
        Func<bool> autoSlayerCheck = NonInteractiveMode.AutoSlayerCheck;
        NonInteractiveMode.AutoSlayerCheck = static () => false;
        try
        {
            Creature[] firstEnemies = state.Enemies.Where(enemy => enemy.IsAlive).ToArray();
            Require(firstEnemies.Length > 0, "Sawatari's first combat had no enemies.");
            Creature finisherTarget = firstEnemies[^1];
            foreach (Creature enemy in firstEnemies[..^1])
            {
                await CreatureCmd.Kill(enemy, force: true);
            }

            await WaitUntilAsync(
                () => playerState.Phase == PlayerTurnPhase.Play,
                "Sawatari's first wave did not reach the player play phase",
                cancellationToken);
            if (finisherTarget.CurrentHp > 1)
            {
                await CreatureCmd.Damage(
                    new ThrowingPlayerChoiceContext(),
                    finisherTarget,
                    finisherTarget.CurrentHp - 1,
                    ValueProp.Unblockable | ValueProp.Unpowered,
                    player.Creature);
            }

            Require(
                finisherTarget.IsAlive && finisherTarget.CurrentHp == 1,
                "Could not prepare Sawatari's first-wave Finisher target at one HP.");
            FinisherSmokeObserver.Reset();
            await PlayerCmd.SetEnergy(10m, player);
            StrikeNinjaSlayerRedesignV1 strike =
                state.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
            await CardPileCmd.Add(strike, PileType.Hand);
            await CardCmd.AutoPlay(
                new BlockingPlayerChoiceContext(),
                strike,
                finisherTarget);
            CardModel[] cards = playerState.AllCards.ToArray();
            FinisherSessionSnapshot normalFinisher = await RequireCompletedFinisherAsync(
                "NinjaSlayerAttack",
                resolvedHits: 1,
                finisherTarget,
                cancellationToken);
            _checkpoints.Write(
                "finisher.normal.completed",
                data: new JsonObject
                {
                    ["sessionId"] = normalFinisher.SessionId,
                    ["resolvedHits"] = normalFinisher.ResolvedHits,
                    ["successfulKills"] = normalFinisher.SuccessfulKills.GetValueOrDefault(finisherTarget)
                });
            await WaitUntilAsync(
                () => manager.IsPaused && GetSawatariOptions().Count == 2,
                "Sawatari intermission did not pause combat and show both choices",
                cancellationToken);

            int round = state.RoundNumber;
            int turn = playerState.TurnNumber;
            int energy = playerState.Energy;
            Require(ReferenceEquals(manager.DebugOnlyGetState(), state), "Sawatari intermission replaced CombatState.");
            Require(ReferenceEquals(NEventRoom.Instance?.EmbeddedCombatRoom, room), "Sawatari intermission replaced CombatRoom.");
            Require(ReferenceEquals(manager.History, history), "Sawatari intermission replaced combat history.");
            Require(ReferenceEquals(state.RunState.Rng, rng), "Sawatari intermission replaced combat RNG.");
            Require(!room.Ui.Visible, "Sawatari intermission left the combat UI visible.");
            Require(manager.PlayerActionsDisabled, "Sawatari intermission left player actions enabled.");
            await WaitFrames(12);
            Require(state.RoundNumber == round, "Sawatari intermission advanced the combat round.");
            Require(playerState.TurnNumber == turn, "Sawatari intermission advanced the player turn.");
            Require(playerState.Energy == energy, "Sawatari intermission changed player energy.");
            Require(piles.SequenceEqual(playerState.AllPiles), "Sawatari intermission replaced card piles.");
            Require(cards.SequenceEqual(playerState.AllCards), "Sawatari intermission changed combat cards.");
            Require(powers.SequenceEqual(player.Creature.Powers.Cast<AbstractModel>()), "Sawatari intermission changed player powers.");

            _observeSawatariDuelBanner = true;
            await UiHelper.Click(GetSawatariOptions()[1]);
            await WaitUntilAsync(
                () => !manager.IsPaused
                    && !manager.PlayerActionsDisabled
                    && state.Enemies.Any(enemy => enemy.IsAlive && enemy.Monster is SawatariMonster),
                "Sawatari duel did not resume the original combat",
                cancellationToken);
            Require(state.RoundNumber == round + 1, "Sawatari duel did not start on the next round.");
            _observeSawatariDuelBanner = false;
            Require(ReferenceEquals(manager.DebugOnlyGetState(), state), "Sawatari duel created a second CombatState.");
            Require(ReferenceEquals(NEventRoom.Instance?.EmbeddedCombatRoom, room), "Sawatari duel created a second CombatRoom.");
            Require(_sawatariBeforeCombatStartCount == 0, "Sawatari duel reran BeforeCombatStart.");
            Require(_sawatariDuelBannerCount == 1, "Sawatari duel did not show exactly one combat-start banner.");
            Require(room.Ui.Visible, "Sawatari duel did not restore the combat UI.");
            Require(!manager.PlayerActionsDisabled, "Sawatari duel did not restore local player actions.");

            Creature duel = state.Enemies.Single(enemy => enemy.IsAlive && enemy.Monster is SawatariMonster);
            await CreatureCmd.Kill(duel);
            await WaitUntilAsync(
                () => manager.IsPaused && GetSawatariOptions().Count == 1,
                "Sawatari duel result did not pause combat and show its reward choice",
                cancellationToken);
            Require(!room.Ui.Visible, "Sawatari duel result left the combat UI visible.");
            Require(manager.PlayerActionsDisabled, "Sawatari duel result left player actions enabled.");
            Require(ReferenceEquals(manager.DebugOnlyGetState(), state), "Sawatari duel result replaced CombatState.");

            await UiHelper.Click(GetSawatariOptions()[0]);
            await WaitUntilAsync(
                () => !manager.IsInProgress,
                "Sawatari combat did not end after the final reward choice",
                cancellationToken);
            Require(!manager.IsPaused, "Sawatari finalization left combat paused.");
            Require(_sawatariAfterCombatEndCount == 1, "Sawatari combat did not run AfterCombatEnd exactly once.");
            _checkpoints.Write("sawatari.same-combat-completed");
            _sawatariCompleted.TrySetResult();
        }
        finally
        {
            NonInteractiveMode.AutoSlayerCheck = autoSlayerCheck;
            _observeSawatariDuelBanner = false;
            _observedSawatariCombat = null;
        }
    }

    private static List<NEventOptionButton> GetSawatariOptions() =>
        NEventRoom.Instance == null
            ? []
            : UiHelper.FindAll<NEventOptionButton>(NEventRoom.Instance)
                .Where(button => !button.Option.IsLocked)
                .ToList();

    private async Task WaitFrames(int count)
    {
        for (int index = 0; index < count; index++)
        {
            await _tree.ToSignal(_tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task VerifyCardPlayEvasion(
        ICombatState combatState,
        Player player,
        Creature target)
    {
        await PowerCmd.Remove<ArtifactPower>(target);
        await PowerCmd.Remove<VulnerablePower>(target);
        Require(
            NinjaSlayerCombatMetrics.PreviousFinishedCardWasAttack(player),
            "The evasion card scenario did not follow an attack card.");

        int initialHp = target.CurrentHp;
        int initialBlock = target.Block;
        int initialHistoryEntries = CombatManager.Instance.History.Entries
            .OfType<DamageReceivedEntry>()
            .Count(entry => ReferenceEquals(entry.Receiver, target));
        await PlayerCmd.SetEnergy(2m, player);

        await PowerCmd.Apply<EvasionPower>(
            new ThrowingPlayerChoiceContext(),
            target,
            1,
            target,
            null);
        RightHeavyPunchRedesignV1 first = combatState.CreateCard<RightHeavyPunchRedesignV1>(player);
        await CardPileCmd.Add(first, PileType.Hand);
        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), first, target);
        Require(target.GetPower<EvasionPower>()?.Amount is null or 0, "Evasion was not consumed by an attack card.");
        Require(!target.HasPower<VulnerablePower>(), "Evasion did not suppress a newly applied card debuff.");

        await PowerCmd.Apply<VulnerablePower>(
            new ThrowingPlayerChoiceContext(),
            target,
            1,
            player.Creature,
            null);
        int vulnerableBefore = target.GetPower<VulnerablePower>()?.Amount
            ?? throw new InvalidOperationException("The smoke fixture could not apply Vulnerable.");
        await PowerCmd.Apply<EvasionPower>(
            new ThrowingPlayerChoiceContext(),
            target,
            1,
            target,
            null);
        RightHeavyPunchRedesignV1 second = combatState.CreateCard<RightHeavyPunchRedesignV1>(player);
        await CardPileCmd.Add(second, PileType.Hand);
        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), second, target);

        Require(target.CurrentHp == initialHp, "An evaded attack card reduced HP.");
        Require(target.Block == initialBlock, "An evaded attack card reduced Block.");
        Require(
            target.GetPower<VulnerablePower>()?.Amount == vulnerableBefore,
            "Evasion did not suppress card debuff stacking.");
        int finalHistoryEntries = CombatManager.Instance.History.Entries
            .OfType<DamageReceivedEntry>()
            .Count(entry => ReferenceEquals(entry.Receiver, target));
        Require(finalHistoryEntries == initialHistoryEntries, "Evaded attack cards created damage history entries.");
        await PowerCmd.Remove<VulnerablePower>(target);
        _checkpoints.Write("evasion.cardplay-completed");
    }

    private async Task VerifyMoveEvasion(
        ICombatState combatState,
        Player player,
        Creature attacker)
    {
        Creature target = player.Creature;
        await PowerCmd.Remove<ArtifactPower>(target);
        await PowerCmd.Remove<WeakPower>(target);
        int initialHp = target.CurrentHp;
        int initialBlock = target.Block;
        int initialHistoryEntries = CombatManager.Instance.History.Entries
            .OfType<DamageReceivedEntry>()
            .Count(entry => ReferenceEquals(entry.Receiver, target));

        IReadOnlyList<DamageResult> evadedResults = [];
        var evadedMove = new MoveState(
            "NINJASLAYER_SMOKE_EVASION",
            async targets =>
            {
                evadedResults = (await CreatureCmd.Damage(
                    new ThrowingPlayerChoiceContext(),
                    targets,
                    1,
                    ValueProp.Move,
                    attacker)).ToArray();
                await PowerCmd.Apply<WeakPower>(
                    new ThrowingPlayerChoiceContext(),
                    targets,
                    1,
                    attacker,
                    null);
            });
        await PowerCmd.Apply<EvasionPower>(
            new ThrowingPlayerChoiceContext(),
            target,
            1,
            target,
            null);
        await evadedMove.PerformMove([target]);

        Require(evadedResults.Count == 0, "An evaded monster move returned a fabricated damage result.");
        Require(target.CurrentHp == initialHp, "An evaded monster move reduced HP.");
        Require(target.Block == initialBlock, "An evaded monster move reduced Block.");
        Require(!target.HasPower<WeakPower>(), "Evasion did not suppress a monster move debuff.");
        int evadedHistoryEntries = CombatManager.Instance.History.Entries
            .OfType<DamageReceivedEntry>()
            .Count(entry => ReferenceEquals(entry.Receiver, target));
        Require(evadedHistoryEntries == initialHistoryEntries, "An evaded monster move created damage history.");

        IReadOnlyList<DamageResult> firstResults = [];
        IReadOnlyList<DamageResult> secondResults = [];
        var mixedMove = new MoveState(
            "NINJASLAYER_SMOKE_MIXED_EVASION",
            async targets =>
            {
                firstResults = (await CreatureCmd.Damage(
                    new ThrowingPlayerChoiceContext(),
                    targets,
                    1,
                    ValueProp.Move,
                    attacker)).ToArray();
                secondResults = (await CreatureCmd.Damage(
                    new ThrowingPlayerChoiceContext(),
                    targets,
                    1,
                    ValueProp.Move,
                    attacker)).ToArray();
                await PowerCmd.Apply<WeakPower>(
                    new ThrowingPlayerChoiceContext(),
                    targets,
                    1,
                    attacker,
                    null);
            });
        await PowerCmd.Apply<EvasionPower>(
            new ThrowingPlayerChoiceContext(),
            target,
            1,
            target,
            null);
        await mixedMove.PerformMove([target]);

        Require(firstResults.Count == 0, "The first hit did not consume the only Evasion layer.");
        Require(secondResults.Any(result => ReferenceEquals(result.Receiver, target)), "The second hit did not connect.");
        Require(target.HasPower<WeakPower>(), "A connected hit incorrectly suppressed its move debuff.");
        await PowerCmd.Remove<WeakPower>(target);

        DefendNinjaSlayerRedesignV1 nonAttackSource = combatState.CreateCard<DefendNinjaSlayerRedesignV1>(player);
        var nonAttackSourceMove = new MoveState(
            "NINJASLAYER_SMOKE_NON_ATTACK_SOURCE",
            async targets =>
            {
                await CreatureCmd.Damage(
                    new ThrowingPlayerChoiceContext(),
                    targets,
                    1,
                    ValueProp.Move,
                    attacker,
                    nonAttackSource
#if !NINJASLAYER_LEGACY_DAMAGE_API
                    , null
#endif
                );
                await PowerCmd.Apply<WeakPower>(
                    new ThrowingPlayerChoiceContext(),
                    targets,
                    1,
                    attacker,
                    nonAttackSource);
            });
        await PowerCmd.Apply<EvasionPower>(
            new ThrowingPlayerChoiceContext(),
            target,
            1,
            target,
            null);
        await nonAttackSourceMove.PerformMove([target]);
        Require(
            target.HasPower<WeakPower>(),
            "A non-attack card source was incorrectly treated as a bound monster-move debuff.");
        await PowerCmd.Remove<WeakPower>(target);
        _checkpoints.Write("evasion.move-completed");
    }

    private async Task VerifyDarkStrike(ICombatState combatState, Player player)
    {
        Creature target = player.Creature;
        await PowerCmd.Remove<ArtifactPower>(target);
        await PowerCmd.Remove<EvasionPower>(target);
        await PowerCmd.Remove<WeakPower>(target);
        if (target.Block > 0)
        {
            await RemoveSmokeBlock(target);
        }

        Creature attacker = await CreatureCmd.Add<DarkNinjaMonster>(combatState);
        var monster = attacker.Monster as DarkNinjaMonster
            ?? throw new InvalidOperationException("CreatureCmd.Add returned a non-Dark-Ninja monster.");
        NCombatRoom room = NCombatRoom.Instance
            ?? throw new InvalidOperationException("The combat room was unavailable for Dark Strike smoke.");
        NCreature attackerNode = room.GetCreatureNode(attacker)
            ?? throw new InvalidOperationException("The Dark Ninja combat node was unavailable.");
        Sprite2D sourceBody = NinjaSlayerVisualRig.GetBodySprite(attackerNode.Visuals)
            ?? throw new InvalidOperationException("The Dark Ninja source body was unavailable.");

        int AttackHistoryCount() => CombatManager.Instance.History.Entries
            .OfType<DamageReceivedEntry>()
            .Count(entry => ReferenceEquals(entry.Dealer, attacker));

        DamageReceivedEntry[] NewAttackHistory(int previousCount) =>
            CombatManager.Instance.History.Entries
                .OfType<DamageReceivedEntry>()
                .Where(entry => ReferenceEquals(entry.Dealer, attacker))
                .Skip(previousCount)
                .ToArray();

        static bool SameTargets(IReadOnlyList<Creature> actual, IReadOnlyList<Creature> expected) =>
            actual.Count == expected.Count
            && actual.Zip(expected).All(pair => ReferenceEquals(pair.First, pair.Second));

        static bool SamePositions(IReadOnlyList<Vector2> actual, IReadOnlyList<Vector2> expected) =>
            actual.Count == expected.Count
            && actual.Zip(expected).All(pair => pair.First.DistanceSquaredTo(pair.Second) <= 1f);

        void RequireObservation(
            (Creature[] DamageHookTargets, Vector2[] DamageHookPositions, string[] AudioEvents, Vector2[] StabVfxPositions, DamageResult[] AttackResults) observation,
            IReadOnlyList<Creature> expectedDamageHookTargets,
            IReadOnlyList<string> expectedAudioEvents,
            IReadOnlyList<Creature> expectedStabVfxTargets)
        {
            Vector2[] expectedStabVfxPositions = expectedStabVfxTargets
                .Select(expectedTarget =>
                {
                    int index = Array.FindIndex(
                        observation.DamageHookTargets,
                        actualTarget => ReferenceEquals(actualTarget, expectedTarget));
                    return index >= 0
                        ? observation.DamageHookPositions[index]
                        : throw new InvalidOperationException(
                            "A Dark Strike VFX target had no observed damage impact.");
                })
                .ToArray();
            Require(
                SameTargets(observation.DamageHookTargets, expectedDamageHookTargets),
                $"Dark Strike damage-hook order was [{string.Join(", ", observation.DamageHookTargets.Select(item => item.CombatId))}].");
            Require(
                observation.AudioEvents.SequenceEqual(expectedAudioEvents, StringComparer.Ordinal),
                $"Dark Strike feedback audio was [{string.Join(", ", observation.AudioEvents)}].");
            Require(
                SamePositions(observation.StabVfxPositions, expectedStabVfxPositions),
                $"Dark Strike stab VFX positions were [{string.Join(", ", observation.StabVfxPositions)}]; "
                + $"expected [{string.Join(", ", expectedStabVfxPositions)}].");
        }

        await CreatureCmd.SetCurrentHp(target, target.MaxHp);
        await CreatureCmd.SetCurrentHp(attacker, attacker.MaxHp - 40);
        int normalTargetHp = target.CurrentHp;
        int normalAttackerHp = attacker.CurrentHp;
        int historyBefore = AttackHistoryCount();
        var normalObservation = await PerformObservedDarkStrike(monster, [target]);
        RequireObservation(
            normalObservation,
            [target],
            [NinjaSlayerAudio.DarkNinjaStabEvent, NinjaSlayerAudio.DarkNinjaKirisuteGomenEvent],
            [target]);
        DamageReceivedEntry[] normalEntries = NewAttackHistory(historyBefore);
        Require(normalEntries.Length == 1, "Dark Strike normal impact did not create exactly one real damage result.");
        DamageResult normalResult = normalEntries[0].Result;
        Require(!normalResult.WasFullyBlocked && normalResult.UnblockedDamage > 0,
            "Dark Strike normal impact did not connect as unblocked damage.");
        Require(target.CurrentHp == normalTargetHp - normalResult.UnblockedDamage,
            "Dark Strike normal impact did not use the host damage result.");
        int normalHealing = normalResult.BlockedDamage + normalResult.UnblockedDamage + normalResult.OverkillDamage;
        Require(attacker.CurrentHp == Math.Min(attacker.MaxHp, normalAttackerHp + normalHealing),
            "Dark Strike healing did not come from its real damage result.");
        Require(target.GetPower<WeakPower>()?.Amount == 2, "Dark Strike normal impact did not apply Weak.");
        await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: true);

        await PowerCmd.Remove<WeakPower>(target);
        await CreatureCmd.SetCurrentHp(target, target.MaxHp);
        await CreatureCmd.SetCurrentHp(attacker, attacker.MaxHp - 40);
        await CreatureCmd.GainBlock(target, 100, ValueProp.Unpowered, null, fast: true);
        int blockedTargetHp = target.CurrentHp;
        int blockedAttackerHp = attacker.CurrentHp;
        historyBefore = AttackHistoryCount();
        var blockedObservation = await PerformObservedDarkStrike(monster, [target]);
        RequireObservation(
            blockedObservation,
            [target],
            [NinjaSlayerAudio.DarkNinjaFailedEvent],
            []);
        DamageReceivedEntry[] blockedEntries = NewAttackHistory(historyBefore);
        Require(blockedEntries.Length == 1 && blockedEntries[0].Result.WasFullyBlocked,
            "Dark Strike fully blocked impact did not preserve the host result.");
        Require(target.CurrentHp == blockedTargetHp, "A fully blocked Dark Strike reduced HP.");
        Require(attacker.CurrentHp == blockedAttackerHp, "A fully blocked Dark Strike incorrectly healed.");
        Require(target.GetPower<WeakPower>()?.Amount == 2, "A fully blocked Dark Strike did not apply Weak.");
        await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: true);

        await PowerCmd.Remove<WeakPower>(target);
        if (target.Block > 0)
        {
            await RemoveSmokeBlock(target);
        }
        await CreatureCmd.SetCurrentHp(target, target.MaxHp);
        await CreatureCmd.SetCurrentHp(attacker, attacker.MaxHp - 40);
        await PowerCmd.Apply<EvasionPower>(
            new ThrowingPlayerChoiceContext(),
            target,
            1,
            target,
            null);
        int evadedTargetHp = target.CurrentHp;
        int evadedAttackerHp = attacker.CurrentHp;
        historyBefore = AttackHistoryCount();
        var evadedObservation = await PerformObservedDarkStrike(monster, [target]);
        RequireObservation(evadedObservation, [], [], []);
        Require(NewAttackHistory(historyBefore).Length == 0, "An evaded Dark Strike fabricated a damage result.");
        Require(target.CurrentHp == evadedTargetHp && target.Block == 0, "An evaded Dark Strike changed HP or Block.");
        Require(attacker.CurrentHp == evadedAttackerHp, "An evaded Dark Strike incorrectly healed.");
        Require(!target.HasPower<WeakPower>(), "An evaded Dark Strike applied Weak.");
        Require(target.GetPower<EvasionPower>()?.Amount is null or 0, "Dark Strike did not consume Evasion.");
        await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: true);

        await CreatureCmd.SetCurrentHp(target, target.MaxHp);
        historyBefore = AttackHistoryCount();
        var failedObservation = await PerformObservedDarkStrike(
            monster,
            [target],
            injectDamageHookFailure: true);
        RequireObservation(failedObservation, [target], [], []);
        Require(NewAttackHistory(historyBefore).Length == 0, "A failed Dark Strike impact created damage history.");
        Require(!target.HasPower<WeakPower>(), "A failed Dark Strike impact applied Weak.");
        await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: true);

        var temporaryTargets = new List<Creature>();
        try
        {
            Creature blockedTarget = await CreatureCmd.Add(
                ModelDb.Monster<DarkNinjaMonster>().ToMutable(),
                combatState,
                CombatSide.Player);
            temporaryTargets.Add(blockedTarget);
            Creature evadedTarget = await CreatureCmd.Add(
                ModelDb.Monster<DarkNinjaMonster>().ToMutable(),
                combatState,
                CombatSide.Player);
            temporaryTargets.Add(evadedTarget);
            await PowerCmd.Remove<EvasionPower>(blockedTarget);
            await PowerCmd.Remove<EvasionPower>(evadedTarget);

            NCreature targetNode = room.GetCreatureNode(target)
                ?? throw new InvalidOperationException("The player target node was unavailable.");
            NCreature blockedNode = room.GetCreatureNode(blockedTarget)
                ?? throw new InvalidOperationException("The blocked target node was unavailable.");
            NCreature evadedNode = room.GetCreatureNode(evadedTarget)
                ?? throw new InvalidOperationException("The evading target node was unavailable.");
            float targetCenterX = targetNode.Visuals.Bounds.GetGlobalRect().GetCenter().X;
            SetCreatureCanvasCenterX(blockedNode, targetCenterX + 280f);
            SetCreatureCanvasCenterX(evadedNode, targetCenterX + 560f);

            await CreatureCmd.SetCurrentHp(target, target.MaxHp);
            await CreatureCmd.SetCurrentHp(blockedTarget, blockedTarget.MaxHp);
            await CreatureCmd.SetCurrentHp(evadedTarget, evadedTarget.MaxHp);
            await CreatureCmd.SetCurrentHp(attacker, attacker.MaxHp - 40);
            await CreatureCmd.GainBlock(blockedTarget, 100, ValueProp.Unpowered, null, fast: true);
            await PowerCmd.Apply<EvasionPower>(
                new ThrowingPlayerChoiceContext(),
                evadedTarget,
                1,
                evadedTarget,
                null);

            int mixedTargetHp = target.CurrentHp;
            int mixedBlockedHp = blockedTarget.CurrentHp;
            int mixedEvadedHp = evadedTarget.CurrentHp;
            int mixedAttackerHp = attacker.CurrentHp;
            historyBefore = AttackHistoryCount();
            var mixedObservation = await PerformObservedDarkStrike(
                monster,
                [evadedTarget, target, blockedTarget],
                useMonsterExecution: false);
            RequireObservation(
                mixedObservation,
                [target, blockedTarget],
                [
                    NinjaSlayerAudio.DarkNinjaStabEvent,
                    NinjaSlayerAudio.DarkNinjaKirisuteGomenEvent,
                    NinjaSlayerAudio.DarkNinjaFailedEvent
                ],
                [target]);

            DamageReceivedEntry[] mixedEntries = NewAttackHistory(historyBefore);
            Require(
                mixedEntries.Length == 2
                && ReferenceEquals(mixedEntries[0].Receiver, target)
                && ReferenceEquals(mixedEntries[1].Receiver, blockedTarget),
                "Dark Strike did not preserve left-to-right result order for mixed multiplayer targets.");
            DamageResult mixedNormalResult = mixedEntries[0].Result;
            DamageResult mixedBlockedResult = mixedEntries[1].Result;
            Require(!mixedNormalResult.WasFullyBlocked && mixedNormalResult.UnblockedDamage > 0,
                "The mixed Dark Strike normal target did not take real damage.");
            Require(mixedBlockedResult.WasFullyBlocked,
                "The mixed Dark Strike blocked target did not preserve its real blocked result.");
            Require(target.CurrentHp == mixedTargetHp - mixedNormalResult.UnblockedDamage,
                "The mixed Dark Strike normal target HP did not match its real result.");
            Require(blockedTarget.CurrentHp == mixedBlockedHp,
                "The mixed Dark Strike fully blocked target lost HP.");
            Require(evadedTarget.CurrentHp == mixedEvadedHp,
                "The mixed Dark Strike evading target lost HP.");
            Require(target.GetPower<WeakPower>()?.Amount == 2
                    && blockedTarget.GetPower<WeakPower>()?.Amount == 2
                    && !evadedTarget.HasPower<WeakPower>(),
                "Dark Strike did not bind Weak to exactly the connected mixed targets.");
            Require(evadedTarget.GetPower<EvasionPower>()?.Amount is null or 0,
                "The mixed Dark Strike did not consume the evading target's layer.");
            int mixedHealing = mixedEntries
                .Select(entry => entry.Result)
                .Where(result => result.UnblockedDamage + result.OverkillDamage > 0)
                .Select(result => result.BlockedDamage + result.UnblockedDamage + result.OverkillDamage)
                .DefaultIfEmpty(0)
                .Max();
            Require(attacker.CurrentHp == Math.Min(attacker.MaxHp, mixedAttackerHp + mixedHealing),
                "Dark Strike did not keep max-per-hit healing for mixed targets.");
            await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: true);

            await PowerCmd.Remove<WeakPower>(target);
            await PowerCmd.Remove<WeakPower>(blockedTarget);
            await PowerCmd.Remove<WeakPower>(evadedTarget);
            if (blockedTarget.Block > 0)
            {
                await RemoveSmokeBlock(blockedTarget);
            }

            await CreatureCmd.SetCurrentHp(target, target.MaxHp);
            await CreatureCmd.SetCurrentHp(attacker, attacker.MaxHp - 40);
            int invalidVisualTargetHp = target.CurrentHp;
            historyBefore = AttackHistoryCount();
            bool invalidatedDetachedVisual = false;
            var invalidVisualObservation = await PerformObservedDarkStrike(
                monster,
                [target],
                useMonsterExecution: false,
                onFirstDamageHook: () =>
                {
                    Node2D detached = room.SceneContainer.GetNodeOrNull<Node2D>(
                        "DarkNinjaDarkStrike")
                        ?? throw new InvalidOperationException(
                            "The detached Dark Strike visual was unavailable for invalidation.");
                    detached.Free();
                    invalidatedDetachedVisual = true;
                });
            Require(invalidatedDetachedVisual, "The detached Dark Strike visual was not invalidated.");
            RequireObservation(
                invalidVisualObservation,
                [target],
                [NinjaSlayerAudio.DarkNinjaStabEvent, NinjaSlayerAudio.DarkNinjaKirisuteGomenEvent],
                [target]);
            DamageReceivedEntry[] invalidVisualEntries = NewAttackHistory(historyBefore);
            Require(
                invalidVisualEntries.Length == 1
                && ReferenceEquals(invalidVisualEntries[0].Receiver, target)
                && target.CurrentHp == invalidVisualTargetHp - invalidVisualEntries[0].Result.UnblockedDamage,
                "Dark Strike did not finish its real impact after the detached visual was freed.");
            Require(target.GetPower<WeakPower>()?.Amount == 2,
                "Dark Strike lost its bound Weak after the detached visual was freed.");
            await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: true);
            await PowerCmd.Remove<WeakPower>(target);

            await CreatureCmd.SetCurrentHp(target, target.MaxHp);
            await CreatureCmd.SetCurrentHp(evadedTarget, evadedTarget.MaxHp);
            await CreatureCmd.SetCurrentHp(attacker, attacker.MaxHp - 40);
            int deadLaterTargetHp = evadedTarget.CurrentHp;
            historyBefore = AttackHistoryCount();
            var deadLaterObservation = await PerformObservedDarkStrike(
                monster,
                [target, evadedTarget],
                useMonsterExecution: false,
                onFirstDamageHook: () => evadedTarget.SetCurrentHpInternal(0));
            RequireObservation(
                deadLaterObservation,
                [target],
                [NinjaSlayerAudio.DarkNinjaStabEvent, NinjaSlayerAudio.DarkNinjaKirisuteGomenEvent],
                [target]);
            DamageReceivedEntry[] deadLaterEntries = NewAttackHistory(historyBefore);
            Require(
                deadLaterEntries.Length == 1
                && ReferenceEquals(deadLaterEntries[0].Receiver, target),
                "Dark Strike damaged a target that died before its impact.");
            Require(evadedTarget.CurrentHp == 0 && deadLaterTargetHp > 0,
                "The target-death smoke did not invalidate the later target.");
            Require(!evadedTarget.HasPower<WeakPower>(),
                "Dark Strike applied Weak to a target that died before its impact.");
            await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: true);
            evadedTarget.SetCurrentHpInternal(evadedTarget.MaxHp);
            await PowerCmd.Remove<WeakPower>(target);

            Creature removedTarget = await CreatureCmd.Add(
                ModelDb.Monster<DarkNinjaMonster>().ToMutable(),
                combatState,
                CombatSide.Player);
            temporaryTargets.Add(removedTarget);
            await PowerCmd.Remove<EvasionPower>(removedTarget);
            NCreature removedNode = room.GetCreatureNode(removedTarget)
                ?? throw new InvalidOperationException("The removable Dark Strike target node was unavailable.");
            SetCreatureCanvasCenterX(removedNode, targetCenterX + 840f);
            await CreatureCmd.SetCurrentHp(target, target.MaxHp);
            await CreatureCmd.SetCurrentHp(attacker, attacker.MaxHp - 40);
            historyBefore = AttackHistoryCount();
            var removedTargetObservation = await PerformObservedDarkStrike(
                monster,
                [target, removedTarget],
                useMonsterExecution: false,
                onFirstDamageHook: () => RemoveTemporaryCreature(combatState, room, removedTarget));
            RequireObservation(
                removedTargetObservation,
                [target],
                [NinjaSlayerAudio.DarkNinjaStabEvent, NinjaSlayerAudio.DarkNinjaKirisuteGomenEvent],
                [target]);
            DamageReceivedEntry[] removedTargetEntries = NewAttackHistory(historyBefore);
            Require(
                removedTargetEntries.Length == 1
                && ReferenceEquals(removedTargetEntries[0].Receiver, target),
                "Dark Strike damaged a target after it left combat.");
            Require(!combatState.ContainsCreature(removedTarget) && !removedTarget.HasPower<WeakPower>(),
                "Dark Strike retained or debuffed a target after it left combat.");
            await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: true);
            await PowerCmd.Remove<WeakPower>(target);

            await PowerCmd.Apply<MinionPower>(
                new ThrowingPlayerChoiceContext(),
                attacker,
                1,
                attacker,
                null);
            Dictionary<Creature, int> primaryEnemyHp = combatState.Enemies
                .Where(enemy => !ReferenceEquals(enemy, attacker)
                    && enemy.IsAlive
                    && enemy.IsPrimaryEnemy)
                .ToDictionary(enemy => enemy, enemy => enemy.CurrentHp);
            Require(primaryEnemyHp.Count > 0,
                "The combat-ending Dark Strike smoke had no primary enemy to suspend.");
            await CreatureCmd.SetCurrentHp(target, target.MaxHp);
            await CreatureCmd.SetCurrentHp(evadedTarget, evadedTarget.MaxHp);
            await CreatureCmd.SetCurrentHp(attacker, attacker.MaxHp - 40);
            int endingTargetHp = target.CurrentHp;
            int endingLaterTargetHp = evadedTarget.CurrentHp;
            int endingAttackerHp = attacker.CurrentHp;
            bool enteredEndingState = false;
            (Creature[] DamageHookTargets, Vector2[] DamageHookPositions, string[] AudioEvents, Vector2[] StabVfxPositions, DamageResult[] AttackResults)
                endingObservation;
            try
            {
                endingObservation = await PerformObservedDarkStrike(
                    monster,
                    [target, evadedTarget],
                    useMonsterExecution: false,
                    onFirstDamageHook: () =>
                    {
                        foreach (Creature enemy in primaryEnemyHp.Keys)
                        {
                            enemy.SetCurrentHpInternal(0);
                        }

                        enteredEndingState = CombatManager.Instance.IsOverOrEnding;
                    });
            }
            finally
            {
                foreach ((Creature enemy, int hp) in primaryEnemyHp)
                {
                    enemy.SetCurrentHpInternal(hp);
                }

                await PowerCmd.Remove<MinionPower>(attacker);
            }
            Require(enteredEndingState,
                "The combat-ending Dark Strike smoke did not enter the host ending state.");
            RequireObservation(
                endingObservation,
                [target],
                [NinjaSlayerAudio.DarkNinjaStabEvent, NinjaSlayerAudio.DarkNinjaKirisuteGomenEvent],
                [target]);
            DamageResult[] endingResults = endingObservation.AttackResults;
            Require(
                endingResults.Length == 1
                && ReferenceEquals(endingResults[0].Receiver, target)
                && target.CurrentHp == endingTargetHp - endingResults[0].UnblockedDamage,
                "Dark Strike did not finish its current impact as combat began ending.");
            Require(evadedTarget.CurrentHp == endingLaterTargetHp && !evadedTarget.HasPower<WeakPower>(),
                "Dark Strike affected a later target after combat began ending.");
            Require(attacker.CurrentHp == endingAttackerHp,
                "Dark Strike healed after combat began ending.");
            Require(!target.HasPower<WeakPower>(),
                "Dark Strike applied Weak after combat began ending.");
            await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: true);
            await PowerCmd.Remove<WeakPower>(target);

            await CreatureCmd.SetCurrentHp(target, target.MaxHp);
            await CreatureCmd.SetCurrentHp(evadedTarget, evadedTarget.MaxHp);
            await PowerCmd.Apply<ThornsPower>(
                new ThrowingPlayerChoiceContext(),
                target,
                999,
                target,
                null);
            await CreatureCmd.SetCurrentHp(attacker, 1);
            int thornsTargetHp = target.CurrentHp;
            int untouchedTargetHp = evadedTarget.CurrentHp;
            historyBefore = AttackHistoryCount();
            var thornsObservation = await PerformObservedDarkStrike(
                monster,
                [evadedTarget, target],
                useMonsterExecution: false);
            RequireObservation(
                thornsObservation,
                [target],
                [NinjaSlayerAudio.DarkNinjaStabEvent, NinjaSlayerAudio.DarkNinjaKirisuteGomenEvent],
                [target]);
            DamageReceivedEntry[] thornsEntries = NewAttackHistory(historyBefore);
            Require(
                thornsEntries.Length == 1
                && ReferenceEquals(thornsEntries[0].Receiver, target)
                && thornsEntries[0].Result.UnblockedDamage > 0,
                "The Dark Strike impact did not finish after lethal Thorns retaliation.");
            Require(target.CurrentHp == thornsTargetHp - thornsEntries[0].Result.UnblockedDamage,
                "Lethal Thorns retaliation interrupted the current Dark Strike impact.");
            Require(attacker.IsDead && attacker.CurrentHp == 0,
                "Lethal Thorns retaliation did not kill the Dark Ninja.");
            Require(target.GetPower<WeakPower>()?.Amount == 2,
                "The completed Thorns impact did not apply its bound Weak.");
            Require(evadedTarget.CurrentHp == untouchedTargetHp && !evadedTarget.HasPower<WeakPower>(),
                "Lethal Thorns retaliation allowed Dark Strike to affect a later target.");
            await RequireDarkStrikeVisualReleased(room, sourceBody, shouldRestoreBody: false);
            await WaitUntilAsync(
                () => !GodotObject.IsInstanceValid(attackerNode) || !attackerNode.IsInsideTree(),
                "The Dark Ninja death presentation did not release its combat node.",
                timeout: TimeSpan.FromSeconds(20));
            await PowerCmd.Remove<ThornsPower>(target);
            await PowerCmd.Remove<WeakPower>(target);
        }
        finally
        {
            foreach (Creature temporaryTarget in temporaryTargets)
            {
                RemoveTemporaryCreature(combatState, room, temporaryTarget);
            }
        }

        _checkpoints.Write("dark-strike.completed");
    }

    private void VerifyPlatformSpineScene()
    {
        PackedScene scene = GD.Load<PackedScene>(
            "res://NinjaSlayer/scenes/creature_visuals/yamoto_koki_missile.tscn");
        NCreatureVisuals visuals = scene.Instantiate<NCreatureVisuals>();
        try
        {
            Node spine = visuals.GetNode("Visuals");
            Require(
                spine.GetClass().ToString() == "SpineSprite",
                $"The Yamoto Koki missile scene resolved Visuals as {spine.GetClass()}, not SpineSprite.");
            _checkpoints.Write("spine.platform-extension-completed");
        }
        finally
        {
            visuals.Free();
        }
    }

    private async Task<(
        Creature[] DamageHookTargets,
        Vector2[] DamageHookPositions,
        string[] AudioEvents,
        Vector2[] StabVfxPositions,
        DamageResult[] AttackResults)> PerformObservedDarkStrike(
        DarkNinjaMonster monster,
        IReadOnlyList<Creature> targets,
        bool injectDamageHookFailure = false,
        bool useMonsterExecution = true,
        Action? onFirstDamageHook = null)
    {
        MoveState move = monster.MoveStateMachine?.States
            .GetValueOrDefault(DarkNinjaMonster.DarkStrikeMoveId) as MoveState
            ?? throw new InvalidOperationException("The Dark Strike move was unavailable.");
        _observedDarkStrikeAttacker = monster.Creature;
        _observedDarkStrikeTargets = targets.ToArray();
        _darkStrikeDamageHookTargets.Clear();
        _darkStrikeDamageHookPositions.Clear();
        _darkStrikeAudioEvents.Clear();
        _darkStrikeStabVfxPositions.Clear();
        _darkStrikeAttackResults = [];
        _darkStrikeBeforeAttackCount = 0;
        _darkStrikeAfterAttackCount = 0;
        _throwOnDarkStrikeDamageHook = injectDamageHookFailure;
        _onFirstDarkStrikeDamageHook = onFirstDamageHook;
        try
        {
            if (injectDamageHookFailure)
            {
                try
                {
                    await move.PerformMove(targets);
                    throw new InvalidOperationException("The injected Dark Strike hook failure was not observed.");
                }
                catch (InvalidOperationException exception) when (exception.Message == InjectedDarkStrikeFailure)
                {
                }
            }
            else if (useMonsterExecution)
            {
                monster.SetMoveImmediate(move, forceTransition: true);
                await monster.PerformMove();
            }
            else
            {
                await move.PerformMove(targets);
            }

            Require(_darkStrikeBeforeAttackCount == 1, "Dark Strike did not run BeforeAttack exactly once.");
            Require(_darkStrikeAfterAttackCount == 1, "Dark Strike did not run AfterAttack exactly once.");
            return (
                _darkStrikeDamageHookTargets.ToArray(),
                _darkStrikeDamageHookPositions.ToArray(),
                _darkStrikeAudioEvents.ToArray(),
                _darkStrikeStabVfxPositions.ToArray(),
                _darkStrikeAttackResults);
        }
        finally
        {
            _observedDarkStrikeAttacker = null;
            _observedDarkStrikeTargets = [];
            _throwOnDarkStrikeDamageHook = false;
            _onFirstDarkStrikeDamageHook = null;
        }
    }

    private async Task RequireDarkStrikeVisualReleased(
        NCombatRoom room,
        Sprite2D sourceBody,
        bool shouldRestoreBody)
    {
        await WaitUntilAsync(
            () => room.SceneContainer.GetNodeOrNull<Node2D>("DarkNinjaDarkStrike") is null,
            "Dark Strike left a detached presentation node behind.",
            timeout: TimeSpan.FromSeconds(5));
        bool sourceBodyVisible = GodotObject.IsInstanceValid(sourceBody) && sourceBody.Visible;
        Require(
            shouldRestoreBody ? sourceBodyVisible : !sourceBodyVisible,
            shouldRestoreBody
                ? "Dark Strike did not restore the surviving Dark Ninja body."
                : "Dark Strike restored the dead Dark Ninja body over its death animation.");
    }

    private static void SetCreatureCanvasCenterX(NCreature creature, float centerX)
    {
        float currentCenterX = creature.Visuals.Bounds.GetGlobalRect().GetCenter().X;
        creature.GlobalPosition += Vector2.Right * (centerX - currentCenterX);
    }

    private static void RemoveTemporaryCreature(
        ICombatState combatState,
        NCombatRoom room,
        Creature creature)
    {
        if (!combatState.ContainsCreature(creature))
        {
            return;
        }

        if (room.GetCreatureNode(creature) is { } node)
        {
            room.RemoveCreatureNode(node);
            node.QueueFreeSafely();
        }

        CombatManager.Instance.RemoveCreature(creature);
        combatState.RemoveCreature(creature);
    }

    private static Task RemoveSmokeBlock(Creature target)
    {
#if NINJASLAYER_CHANNEL_STABLE
        return CreatureCmd.LoseBlock(target, target.Block);
#else
        return CreatureCmd.LoseBlock(new ThrowingPlayerChoiceContext(), target, target.Block, null);
#endif
    }

    private async Task RunSafelyAsync()
    {
        try
        {
            _checkpoints.Write("driver.started");
            await WaitUntilAsync(() => NGame.Instance?.MainMenu is not null, "main menu did not initialize");
            ValidateLoadedMods();
            if (_configuration.Phase == SmokePhase.TransitionPerf)
            {
                _checkpoints.Write("transition-perf.content-audit-skipped");
            }
            else
            {
                ValidateRedesignContent();
            }
            if (_configuration.Phase == SmokePhase.Fresh)
            {
                await RunFreshPhaseAsync();
            }
            else if (_configuration.Phase == SmokePhase.Resume)
            {
                await RunResumePhaseAsync();
            }
            else if (_configuration.Phase == SmokePhase.ReverseFinisher)
            {
                await RunReverseFinisherPhaseAsync();
            }
            else if (_configuration.Phase == SmokePhase.SawatariSameCombat)
            {
                await RunSawatariSameCombatPhaseAsync();
            }
            else if (_configuration.Phase == SmokePhase.TransitionPerf)
            {
                await RunTransitionPerfPhaseAsync();
            }
            else
            {
                await RunFullAutoSlayPhaseAsync();
            }
        }
        catch (Exception exception)
        {
            _checkpoints.Write("driver.failed", "failed", ExceptionData(exception));
            TryCaptureFailureScreenshot();
            _tree.Quit(1);
        }
        finally
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
            _checkpoints.Dispose();
        }
    }

    private async Task RunFreshPhaseAsync()
    {
        NGame.Instance!.DebugSeedOverride = _configuration.Seed;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
        SaveManager.Instance.SetFtuesEnabled(enabled: false);
        _checkpoints.Write("fresh.autoslay-starting");
        var autoSlayer = new AutoSlayer();
        autoSlayer.Start(_configuration.Seed, _configuration.AutoSlayLogPath);

        await WaitTaskAsync(_firstCombatCompleted.Task, "first combat scenario did not complete", TimeSpan.FromMinutes(3));
        await WaitTaskAsync(_firstMapReached.Task, "map did not stabilize after first combat", TimeSpan.FromMinutes(2));
        Require(!CombatManager.Instance.IsInProgress && (NMapScreen.Instance?.IsOpen ?? false),
            "The first map gate was reached before combat and rewards completed.");
        await SaveManager.Instance.SaveRun(null);
        Require(SaveManager.Instance.HasRunSave, "Run save was not created after first combat.");
        _checkpoints.Write("fresh.saved");
        _checkpoints.Write("fresh.restart-requested");
        _tree.Quit(RestartRequestedExitCode);
    }

    private async Task RunFullAutoSlayPhaseAsync()
    {
        NGame.Instance!.DebugSeedOverride = _configuration.Seed;
        _checkpoints.Write("full-autoslay.starting");
        var autoSlayer = new AutoSlayer();
        autoSlayer.Start(_configuration.Seed, _configuration.AutoSlayLogPath);
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private async Task RunReverseFinisherPhaseAsync()
    {
        NGame.Instance!.DebugSeedOverride = _configuration.Seed;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
        SaveManager.Instance.SetFtuesEnabled(enabled: false);
        _checkpoints.Write("finisher.reverse.starting");
        var autoSlayer = new AutoSlayer();
        autoSlayer.Start(_configuration.Seed, _configuration.AutoSlayLogPath);
        await WaitTaskAsync(
            _reverseFinisherCompleted.Task,
            "reverse Finisher scenario did not complete",
            TimeSpan.FromMinutes(3));
        _tree.Quit(0);
    }

    private async Task RunSawatariSameCombatPhaseAsync()
    {
        NGame.Instance!.DebugSeedOverride = _configuration.Seed;
        _checkpoints.Write("sawatari.starting");
        var autoSlayer = new AutoSlayer();
        autoSlayer.Start(_configuration.Seed, _configuration.AutoSlayLogPath);

        await WaitTaskAsync(
            _sawatariCompleted.Task,
            "Sawatari same-combat scenario did not complete",
            TimeSpan.FromMinutes(3));
        await WaitFrames(2);
        _checkpoints.Write("sawatari.completed");
        _tree.Quit(0);
    }

    private async Task RunResumePhaseAsync()
    {
        Control mainMenu = _tree.Root.GetNode<Control>("/root/Game/RootSceneContainer/MainMenu");
        NButton continueButton = mainMenu.GetNode<NButton>("MainMenuTextButtons/ContinueButton");
        await WaitUntilAsync(() => continueButton.Visible && continueButton.IsEnabled, "continue button was unavailable");
        await UiHelper.Click(continueButton);
        await WaitUntilAsync(
            () => RunManager.Instance.IsInProgress
                && NRun.Instance is not null
                && (NMapScreen.Instance?.IsOpen == true || CombatManager.Instance.IsInProgress),
            "saved run did not load",
            timeout: TimeSpan.FromMinutes(2));
        Player player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())
            ?? throw new InvalidOperationException("Resumed local player was unavailable.");
        ValidateRedesignRunIdentity(player);
        ModelId canonicalCharacterId = ModelDb.Character<NinjaSlayerCharacter>().Id;
        int lossesBeforeAbandon = SaveManager.Instance.Progress
            .GetOrCreateCharacterStats(canonicalCharacterId)
            .TotalLosses;
        _checkpoints.Write("resume.loaded");

        Node root = _tree.Root;
        NTopBarPauseButton pause = await WaitForNodeAsync<NTopBarPauseButton>(
            root,
            "/root/Game/RootSceneContainer/Run/GlobalUi/TopBar/RightAlignedStuff/PauseButton");
        await UiHelper.Click(pause);
        NPauseMenu? pauseMenu = null;
        await WaitUntilAsync(
            () => (pauseMenu = UiHelper.FindFirst<NPauseMenu>(root)) is { } menu && menu.IsVisibleInTree(),
            "pause menu did not open");
        NPauseMenuButton giveUp = pauseMenu!.GetNode<Control>("%ButtonContainer").GetNode<NPauseMenuButton>("GiveUp");
        await UiHelper.Click(giveUp);
        NAbandonRunConfirmPopup? confirm = null;
        await WaitUntilAsync(
            () => (confirm = UiHelper.FindFirst<NAbandonRunConfirmPopup>(root)) is not null,
            "abandon confirmation did not open");
        await UiHelper.Click(confirm!.GetNode<NVerticalPopup>("VerticalPopup").YesButton);
        await WaitUntilAsync(() => NOverlayStack.Instance?.Peek() is NGameOverScreen, "game over screen did not appear");
        await new GameOverScreenHandler().HandleAsync(new Rng(1), CancellationToken.None);
        await WaitUntilAsync(
            () => root.GetNodeOrNull<Control>("/root/Game/RootSceneContainer/MainMenu")?.IsVisibleInTree() == true,
            "main menu did not return after abandon");
        Require(
            SaveManager.Instance.Progress.GetOrCreateCharacterStats(canonicalCharacterId).TotalLosses == lossesBeforeAbandon + 1,
            "Abandoning the Redesign run did not record exactly one canonical Ninja Slayer loss.");
        _checkpoints.Write("resume.completed");
        _tree.Quit(0);
    }

    private void ValidateLoadedMods()
    {
        string[] required = ["STS2-RitsuLib", "NinjaSlayer", "NinjaSlayer-SmokeDriver"];
        var loadedMods = MegaCrit.Sts2.Core.Modding.ModManager.Mods
            .Where(mod => mod.state.ToString() == "Loaded" && mod.manifest?.id is not null)
            .ToArray();
        var loaded = loadedMods
            .Select(mod => mod.manifest!.id!)
            .ToHashSet(StringComparer.Ordinal);
        string[] missing = required.Where(id => !loaded.Contains(id)).ToArray();
        Require(missing.Length == 0, $"Required smoke mods were not loaded: {string.Join(", ", missing)}");

        var ninjaSlayerMod = loadedMods.Single(mod => mod.manifest!.id == "NinjaSlayer");
        Assembly implementation = typeof(DarkNinjaMonster).Assembly;
        Type modType = ninjaSlayerMod.GetType();
        object? association = modType.GetField("assemblies")?.GetValue(ninjaSlayerMod)
            ?? modType.GetField("assembly")?.GetValue(ninjaSlayerMod);
        bool associated = association switch
        {
            Assembly assembly => ReferenceEquals(assembly, implementation),
            IEnumerable<Assembly> assemblies => assemblies.Any(
                assembly => ReferenceEquals(assembly, implementation)),
            _ => false
        };
        Require(associated,
            "The host did not associate the NinjaSlayer implementation assembly with the mod.");

        var loadedIds = new JsonArray();
        foreach (string id in loaded.OrderBy(id => id, StringComparer.Ordinal))
        {
            loadedIds.Add(id);
        }
        _checkpoints.Write("mods.loaded", data: new JsonObject { ["loaded"] = loadedIds });
    }

    private async Task WaitUntilAsync(
        Func<bool> predicate,
        string failure,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + (timeout ?? DefaultTimeout);
        while (!predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(failure);
            }
            await _tree.ToSignal(_tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task WaitTaskAsync(Task task, string failure, TimeSpan timeout)
    {
        Task completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            throw new TimeoutException(failure);
        }
        await task;
    }

    private async Task<T> WaitForNodeAsync<T>(Node root, string path) where T : Node
    {
        T? node = null;
        await WaitUntilAsync(() => (node = root.GetNodeOrNull<T>(path)) is not null, $"Node was unavailable: {path}");
        return node!;
    }

    private static T? FindDescendant<T>(Node root) where T : Node
    {
        if (root is T match)
        {
            return match;
        }

        foreach (Node child in root.GetChildren())
        {
            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void TryCaptureFailureScreenshot()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configuration.FailureScreenshotPath)!);
            _tree.Root.GetViewport().GetTexture().GetImage().SavePng(_configuration.FailureScreenshotPath);
        }
        catch
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static JsonObject ExceptionData(Exception exception) =>
        new() { ["exception"] = exception.ToString() };

}
