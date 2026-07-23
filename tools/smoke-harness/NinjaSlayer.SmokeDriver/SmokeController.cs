using Godot;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Screens;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
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
using NinjaSlayer.Afflictions;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Diagnostics;

namespace NinjaSlayer.SmokeDriver;

internal sealed class SmokeController
{
    private const int RestartRequestedExitCode = 20;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);
    private readonly SmokeConfiguration _configuration;
    private readonly SmokeCheckpointWriter _checkpoints;
    private readonly SceneTree _tree;
    private readonly TaskCompletionSource _firstCombatCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstMapReached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _firstCombatClaimed;
    private int _firstMapClaimed;

    public SmokeController(SmokeConfiguration configuration, SceneTree tree)
    {
        _configuration = configuration;
        _checkpoints = new SmokeCheckpointWriter(configuration);
        _tree = tree;
    }

    public static SmokeController? Current { get; private set; }
    public bool ShouldForceCharacter => _configuration.Phase is SmokePhase.Fresh or SmokePhase.FullAutoSlay;

    public void Start()
    {
        if (Current is not null)
        {
            throw new InvalidOperationException("A smoke controller is already active.");
        }

        Current = this;
        _ = RunSafelyAsync();
    }

    public bool TryClaimFirstCombat() =>
        _configuration.Phase == SmokePhase.Fresh
        && Interlocked.CompareExchange(ref _firstCombatClaimed, 1, 0) == 0;

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
        _checkpoints.Write("character.selected", data: new { characterId });

    public void BeforeFullAutoSlayExit(ref int exitCode)
    {
        if (_configuration.Phase != SmokePhase.FullAutoSlay)
        {
            return;
        }

        try
        {
            Require(exitCode == 0, $"AutoSlay requested failure exit code {exitCode}.");
            ValidateRuntimeIdle("full-autoslay.runtime-idle");
            _checkpoints.Write("full-autoslay.completed");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            _checkpoints.Write("driver.failed", "failed", new { exception = exception.ToString() });
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
        await WaitUntilAsync(
            () => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "player play phase did not start",
            cancellationToken);
        _checkpoints.Write("combat.started", data: new { enemyCount = combatState.Enemies.Count });

        await PlayerCmd.SetEnergy(10m, player);
        ReadyBlade readyBlade = combatState.CreateCard<ReadyBlade>(player);
        await CardPileCmd.Add(readyBlade, PileType.Hand);
        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), readyBlade, null);
        bool preparedCreated = player.PlayerCombatState!.AllCards.Any(card => card.Affliction is PreparedAffliction);
        Require(preparedCreated, "ReadyBlade did not create a Prepared card.");
        _checkpoints.Write("prepared.created");

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
        bool illegalPrepared = player.PlayerCombatState!.AllCards.Any(card =>
            card.Affliction is PreparedAffliction && card.Pile?.Type != PileType.Draw);
        Require(!illegalPrepared, "A Prepared card left the draw pile without clearing its affliction.");
        _checkpoints.Write("prepared.lifecycle-cleared");

        Creature focus = combatState.HittableEnemies.FirstOrDefault()
            ?? throw new InvalidOperationException("No hittable enemy remained for the X attack scenario.");
        await PlayerCmd.SetEnergy(1m, player);
        TornadoFist nonLethal = combatState.CreateCard<TornadoFist>(player);
        await CardPileCmd.Add(nonLethal, PileType.Hand);
        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), nonLethal, focus);
        Require(CombatManager.Instance.IsInProgress && focus.IsAlive, "The non-lethal X attack unexpectedly ended combat.");
        await WaitForRuntimeIdleAsync(cancellationToken);
        _checkpoints.Write("x-attack.nonlethal-completed");

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

        NinjaSlayerRuntimeHealthSnapshot beforeFinisher = NinjaSlayerRuntimeHealth.Capture();
        await PlayerCmd.SetEnergy(3m, player);
        TornadoFist lethal = combatState.CreateCard<TornadoFist>(player);
        await CardPileCmd.Add(lethal, PileType.Hand);
        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), lethal, focus);
        await WaitUntilAsync(() => !CombatManager.Instance.IsInProgress, "finisher did not end combat", cancellationToken);
        await WaitForRuntimeIdleAsync(cancellationToken);
        NinjaSlayerRuntimeHealthSnapshot afterFinisher = NinjaSlayerRuntimeHealth.Capture();
        Require(
            afterFinisher.FinisherSucceeded + afterFinisher.FinisherDegraded
                > beforeFinisher.FinisherSucceeded + beforeFinisher.FinisherDegraded,
            "The lethal X attack did not complete a finisher session.");
        _checkpoints.Write("finisher.completed", data: afterFinisher);
        _firstCombatCompleted.TrySetResult();
    }

    private async Task RunSafelyAsync()
    {
        try
        {
            _checkpoints.Write("driver.started");
            await WaitUntilAsync(() => NGame.Instance?.MainMenu is not null, "main menu did not initialize");
            ValidateLoadedMods();
            ValidateCoreCapabilities();
            if (_configuration.Phase == SmokePhase.Fresh)
            {
                await RunFreshPhaseAsync();
            }
            else if (_configuration.Phase == SmokePhase.Resume)
            {
                await RunResumePhaseAsync();
            }
            else
            {
                await RunFullAutoSlayPhaseAsync();
            }
        }
        catch (Exception exception)
        {
            _checkpoints.Write("driver.failed", "failed", new { exception = exception.ToString() });
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
        ValidateRuntimeIdle("fresh.saved");
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

    private async Task RunResumePhaseAsync()
    {
        Control mainMenu = _tree.Root.GetNode<Control>("/root/Game/RootSceneContainer/MainMenu");
        NButton continueButton = mainMenu.GetNode<NButton>("MainMenuTextButtons/ContinueButton");
        await WaitUntilAsync(() => continueButton.Visible && continueButton.IsEnabled, "continue button was unavailable");
        await UiHelper.Click(continueButton);
        await WaitUntilAsync(
            () => RunManager.Instance.IsInProgress
                && NRun.Instance is not null
                && NMapScreen.Instance?.IsOpen == true,
            "saved run did not load",
            timeout: TimeSpan.FromMinutes(2));
        await WaitForRuntimeIdleAsync(CancellationToken.None);
        ValidateRuntimeIdle("resume.loaded");
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
        ValidateRuntimeIdle("resume.abandoned");
        _checkpoints.Write("resume.completed");
        _tree.Quit(0);
    }

    private void ValidateLoadedMods()
    {
        string[] required = ["STS2-RitsuLib", "NinjaSlayer", "NinjaSlayer-SmokeDriver"];
        var loaded = MegaCrit.Sts2.Core.Modding.ModManager.Mods
            .Where(mod => mod.state.ToString() == "Loaded" && mod.manifest?.id is not null)
            .Select(mod => mod.manifest!.id)
            .ToHashSet(StringComparer.Ordinal);
        string[] missing = required.Where(id => !loaded.Contains(id)).ToArray();
        Require(missing.Length == 0, $"Required smoke mods were not loaded: {string.Join(", ", missing)}");
        _checkpoints.Write("mods.loaded", data: new { loaded = loaded.OrderBy(id => id).ToArray() });
    }

    private void ValidateCoreCapabilities()
    {
        NinjaSlayerRuntimeHealthSnapshot health = NinjaSlayerRuntimeHealth.Capture();
        string[] required = ["gameplay", "prepared-safety", "prepared-gameplay", "finisher-core", "transition-core"];
        string[] unavailable = required
            .Where(id => !health.Capabilities.TryGetValue(id, out NinjaSlayerCapabilityHealth? status) || !status.IsOperational)
            .ToArray();
        Require(unavailable.Length == 0, $"Required capabilities were unavailable: {string.Join(", ", unavailable)}");
        _checkpoints.Write("capabilities.operational", data: health.Capabilities);
    }

    private void ValidateRuntimeIdle(string checkpoint)
    {
        NinjaSlayerRuntimeHealthSnapshot health = NinjaSlayerRuntimeHealth.Capture();
        Require(!health.FinisherSessionActive, "A finisher session remained active.");
        Require(!health.TransitionSessionActive && !health.TransitionPending, "A transition session remained active.");
        Require(!health.CinematicCameraActive, "A cinematic camera lease remained active.");
        Require(!health.ScreenShakeSuppressed, "Screen shake suppression remained active.");
        Require(!health.XAttackAudioSuppressed && !health.XAttackComboActive, "An X attack scope remained active.");
        Require(health.PreparedRepairFailed == 0, "Prepared safety reported an unrepaired failure.");
        _checkpoints.Write(checkpoint, data: health);
    }

    private async Task WaitForRuntimeIdleAsync(CancellationToken cancellationToken)
    {
        await WaitUntilAsync(
            () =>
            {
                NinjaSlayerRuntimeHealthSnapshot health = NinjaSlayerRuntimeHealth.Capture();
                return !health.FinisherSessionActive
                    && !health.TransitionSessionActive
                    && !health.TransitionPending
                    && !health.CinematicCameraActive
                    && !health.ScreenShakeSuppressed
                    && !health.XAttackAudioSuppressed
                    && !health.XAttackComboActive;
            },
            "runtime ownership did not return to idle",
            cancellationToken,
            TimeSpan.FromSeconds(20));
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
}
