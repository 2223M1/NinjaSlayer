using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Rooms;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Steamworks;

namespace SaveProbe;

[ModInitializer(nameof(Init))]
public static class Probe
{
    internal static string Output = "";
    internal static string Prefix = "";
    internal static bool Ninja;
    internal static SceneTree Tree = null!;
    internal static long Frames;
    internal static int Trial;
    private static double _lastFrame;
    private static bool _claimed;
    private static AutoSlayer _slayer = null!;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly TaskCompletionSource Done = new();

    public static void Init()
    {
        Output = CommandLineHelper.GetValue("save-probe-output") ?? throw new InvalidOperationException("Missing probe output.");
        string scope = CommandLineHelper.GetValue("save-probe-scope") ?? throw new InvalidOperationException("Missing remote scope.");
        if (!scope.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) throw new InvalidOperationException("Invalid scope.");
        Prefix = "ninjaslayer-diagnostics/" + scope + "/";
        Ninja = CommandLineHelper.HasArg("save-probe-ninja");
        Tree = (SceneTree)Engine.GetMainLoop();
        Directory.CreateDirectory(Output);
        try { new Harmony("NinjaSlayer.SaveDiagnosticProbe").PatchAll(typeof(Probe).Assembly); }
        catch (Exception ex)
        {
            Write("probe.init-failed", ex.ToString());
            System.Environment.Exit(1);
            throw;
        }
        Tree.ProcessFrame += Frame;
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        Write("probe.init", new { Prefix, steam = SteamInitializer.Initialized, host = typeof(NGame).Assembly.ManifestModule.ModuleVersionId });
        if (!SteamInitializer.Initialized)
        {
            Write("probe.unavailable", "Native Steam callback pump is not running.");
            System.Environment.Exit(2);
        }
        TaskHelper.RunSafely(Run());
    }

    private static void Frame()
    {
        double now = Clock.Elapsed.TotalMilliseconds;
        if (_lastFrame != 0 && now - _lastFrame > 250) Write("frame.gap", new { milliseconds = now - _lastFrame });
        _lastFrame = now;
        Frames++;
    }

    internal static void Write(string name, object? data = null) =>
        File.AppendAllText(Path.Combine(Output, "events.jsonl"), JsonSerializer.Serialize(new
        { name, trial = Trial, ms = Clock.Elapsed.TotalMilliseconds, frames = Frames, data }) + "\n");

    internal static async Task Observe(Task task, string name, string path)
    {
        double start = Clock.Elapsed.TotalMilliseconds;
        try { await task; Write(name, new { path, durationMs = Clock.Elapsed.TotalMilliseconds - start }); }
        catch (Exception ex) { Write(name + ".failed", new { path, error = ex.ToString() }); }
    }

    private static async Task Wait(Func<bool> predicate, string what)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timer.Elapsed.TotalSeconds > 90) throw new TimeoutException(what);
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private static async Task Run()
    {
        try
        {
            await Wait(() => NGame.Instance?.MainMenu != null, "Main menu");
            Write("mods", ModManager.Mods.Where(m => m.state == ModLoadState.Loaded).Select(m => new { id = m.manifest?.id, assembly = m.assembly?.FullName }).ToArray());
            SaveManager.Instance.SetFtuesEnabled(false);
            _slayer = new AutoSlayer();
            _slayer.Start("SAVE_DIAG_20260916", Path.Combine(Output, "autoslay.log"));
            await Done.Task;
            Write("probe.complete");
            NGame.Instance!.Quit();
        }
        catch (Exception ex)
        {
            Write("probe.failed", ex.ToString());
            Tree.Root.GetTexture().GetImage().SavePng(Path.Combine(Output, "failure.png"));
            Tree.Quit(1);
        }
    }

    internal static bool Claim(ref Task result)
    {
        if (_claimed) return true;
        _claimed = true;
        result = Battles();
        return false;
    }

    private static async Task Battles()
    {
        try
        {
            NonInteractiveMode.AutoSlayerCheck = static () => false;
            var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
            var choice = new BlockingPlayerChoiceContext();
            var manager = CombatManager.Instance;
            for (Trial = 1; Trial <= 10; Trial++)
            {
                AutoSlayer.CurrentWatchdog?.Reset($"Save trial {Trial}");
                if (Trial > 1)
                {
                    var next = new FightConsoleCmd().Process(player, ["TURRET_OPERATOR_WEAK"]);
                    if (!next.success) throw new InvalidOperationException(next.msg);
                    await next.task!;
                }
                await Wait(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "Player turn");
                var state = manager.DebugOnlyGetState()!;
                player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
                foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray()) await CardPileCmd.Add(card, PileType.Discard);
                foreach (var enemy in state.Enemies)
                {
                    enemy.SetCurrentHpInternal(1);
                    await CreatureCmd.LoseBlock(enemy, enemy.Block);
                }
                int wins = 0;
                void Won(CombatRoom _) { wins++; Write("combat.won"); }
                manager.CombatWon += Won;
                try
                {
                    Write("combat.finish-start", new { mode = Trial % 2 == 0 ? "turn-end" : "attack", Ninja });
                    if (Trial % 2 == 0)
                    {
                        if (Ninja)
                        {
                            var flame = ModelDb.AllCards.Single(c => c.GetType().Name == "BlackFlameRedesignV1");
                            await CardPileCmd.Add(state.CreateCard(flame, player), PileType.Hand);
                        }
                        else foreach (var enemy in state.Enemies.ToArray())
                            await PowerCmd.Apply<PoisonPower>(choice, enemy, 1, player.Creature, null);
                        PlayerCmd.EndTurn(player, canBackOut: false);
                    }
                    else
                    {
                        foreach (var enemy in state.Enemies.ToArray())
                        {
                            var strike = state.CreateCard<StrikeIronclad>(player);
                            await CardPileCmd.Add(strike, PileType.Hand);
                            await CardCmd.AutoPlay(choice, strike, enemy);
                            Write("attack.complete", new { hp = enemy.CurrentHp, block = enemy.Block });
                        }
                        await manager.CheckWinCondition();
                    }
                    await Wait(() => wins == 1 && NOverlayStack.Instance?.Peek() is NRewardsScreen, "Victory rewards");
                    Write("rewards.visible", new { wins });
                    if (Trial == 10)
                    {
                        Tree.Root.GetTexture().GetImage().SavePng(Path.Combine(Output, "rewards.png"));
                        Write("menu.return-start");
                        await NGame.Instance!.ReturnToMainMenu();
                        await Wait(() => NGame.Instance.MainMenu != null, "Return to menu");
                        Write("menu.return-complete");
                    }
                }
                finally { manager.CombatWon -= Won; }
            }
            Done.TrySetResult();
            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex) { Done.TrySetException(ex); throw; }
    }
}

// Only storage names are isolated. The native write tasks and Steam callback pump
// are not replaced, delayed, polled independently, or completed by this probe.
[HarmonyPatch(typeof(SteamRemoteSaveStore), nameof(SteamRemoteSaveStore.CanonicalizePath))]
internal static class RemoteScope
{
    static void Postfix(ref string __result)
    {
        if (!__result.StartsWith(Probe.Prefix, StringComparison.Ordinal)) __result = Probe.Prefix + __result;
    }
}
[HarmonyPatch(typeof(SteamRemoteSaveStore), nameof(SteamRemoteSaveStore.HasCloudFiles))]
internal static class RemoteExists
{
    static bool Prefix(ref bool __result)
    {
        __result = Enumerable.Range(0, SteamRemoteStorage.GetFileCount()).Any(i => SteamRemoteStorage.GetFileNameAndSize(i, out _).StartsWith(Probe.Prefix));
        return false;
    }
}
[HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.StoreStats))]
internal static class NoDiagnosticAchievements { static bool Prefix(ref bool __result) { __result = true; return false; } }

[HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFileAsync), [typeof(string), typeof(byte[])])]
internal static class LocalWrite
{
    static void Postfix(string path, Task __result) => _ = Probe.Observe(__result, "local.write-complete", path);
}
[HarmonyPatch(typeof(SteamRemoteSaveStore), nameof(SteamRemoteSaveStore.WriteFileAsync), [typeof(string), typeof(byte[])])]
internal static class RemoteWrite
{
    static void Prefix(string path) => Probe.Write("remote.write-start", new { path });
    static void Postfix(string path, Task __result) => _ = Probe.Observe(__result, "remote.write-complete", path);
}
[HarmonyPatch(typeof(SteamRemoteStorage), nameof(SteamRemoteStorage.FileWriteAsync))]
internal static class RemoteRequest
{
    static void Prefix(string pchFile)
    {
        if (!pchFile.StartsWith(Probe.Prefix, StringComparison.Ordinal)) throw new InvalidOperationException("Remote write escaped diagnostic namespace.");
        Probe.Write("steam.request", new { path = pchFile });
    }
}
[HarmonyPatch(typeof(SteamRemoteStorage), nameof(SteamRemoteStorage.FileWrite))]
internal static class RemoteSyncWriteGuard
{
    static void Prefix(string pchFile)
    {
        if (!pchFile.StartsWith(Probe.Prefix, StringComparison.Ordinal)) throw new InvalidOperationException("Remote write escaped diagnostic namespace.");
    }
}
[HarmonyPatch(typeof(SteamCallResult<RemoteStorageFileWriteAsyncComplete_t>), "OnCallResult")]
internal static class RemoteCallback
{
    static void Prefix(RemoteStorageFileWriteAsyncComplete_t result, bool ioError) => Probe.Write("steam.callback", new { result = result.m_eResult.ToString(), ioError });
}
[HarmonyPatch]
internal static class GodotExceptionTiming
{
    static MethodBase TargetMethod() => AccessTools.Method(
        typeof(GD).Assembly.GetType("Godot.NativeInterop.ExceptionUtils", throwOnError: true),
        "LogException", [typeof(Exception)]);
    static void Prefix(Exception __0) => Probe.Write("godot.exception", __0.ToString());
}
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
internal static class SaveRunTiming
{
    static void Prefix(AbstractRoom? preFinishedRoom) => Probe.Write("save.start", new { victory = preFinishedRoom != null });
    static void Postfix(Task __result) => _ = Probe.Observe(__result, "save.complete", "run");
}
[HarmonyPatch(typeof(CombatRoomHandler), nameof(CombatRoomHandler.HandleAsync))]
internal static class FirstCombat { static bool Prefix(ref Task __result) => Probe.Claim(ref __result); }
[HarmonyPatch(typeof(NCharacterSelectButton), nameof(NCharacterSelectButton.Select))]
internal static class CharacterSelection
{
    private static bool _selecting;
    static bool Prefix(NCharacterSelectButton __instance)
    {
        bool Matches(NCharacterSelectButton b) => Probe.Ninja ? b.Character.GetType().Name == "NinjaSlayerCharacter" : b.Character is Ironclad;
        if (_selecting || Matches(__instance)) return true;
        var button = __instance.GetParent().GetChildren().OfType<NCharacterSelectButton>().Single(Matches);
        _selecting = true;
        try { button.UnlockIfPossible(); button.Select(); } finally { _selecting = false; }
        return false;
    }
}
