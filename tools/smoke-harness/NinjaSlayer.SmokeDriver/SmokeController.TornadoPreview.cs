using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private CardModel? _timedTornado;
    private double _tornadoStart;
    private readonly List<double> _tornadoHits = [];
    private Creature? _timedTarget;
    private int _tornadoPauses;
    internal bool IsNativeTimingReference => _timedTornado is Whirlwind;

    internal void ObserveTornadoStart()
    {
        if (_timedTornado != null) _tornadoStart = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }

    internal void ObserveTornadoDamage(Creature target, CardModel? card)
    {
        if (ReferenceEquals(card, _timedTornado) && ReferenceEquals(target, _timedTarget))
        {
            _tornadoHits.Add(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency - _tornadoStart);
            if (NCombatRoom.Instance!.GetCreatureNode(target)!.GetNodeOrNull<Node>("TornadoHurtPause") != null)
                _tornadoPauses++;
        }
    }

    private async Task RunTornadoPreviewAsync()
    {
        NGame.Instance!.DebugSeedOverride = _configuration.Seed;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        SaveManager.Instance.SetFtuesEnabled(false);
        Engine.MaxFps = 60;
        new AutoSlayer().Start(_configuration.Seed, _configuration.AutoSlayLogPath);
        await WaitTaskAsync(_firstCombatCompleted.Task, "Tornado preview did not complete", TimeSpan.FromMinutes(4));
        AccessTools.Method(typeof(AutoSlayer), "QuitGame").Invoke(null, [0]);
    }

    private async Task ExecuteTornadoPreviewAsync(CancellationToken cancellationToken)
    {
        Func<bool> autoSlayerCheck = NonInteractiveMode.AutoSlayerCheck;
        try
        {
            await WaitUntilAsync(() => CombatManager.Instance.IsInProgress, "Preview combat did not start", cancellationToken);
            ICombatState combat = CombatManager.Instance.DebugOnlyGetState()!;
            var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Preview player phase did not start", cancellationToken);
            NonInteractiveMode.AutoSlayerCheck = static () => false;
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            SaveManager.Instance.PrefsSave.MuteInBackground = false;
            Require(!NonInteractiveMode.IsActive, "Preview must use interactive gameplay timing and effects.");
            await WaitFrames(240);
            FindDescendant<NPlayerTurnBanner>(_tree.Root)?.QueueFree();
            foreach (Node toast in _tree.Root.FindChildren("*Toast*", "", true, false))
                if (toast is CanvasItem item) item.Hide();
            foreach (Creature enemy in combat.HittableEnemies)
            {
                enemy.SetMaxHpInternal(1000);
                await CreatureCmd.SetCurrentHp(enemy, 1000);
            }
            _timedTarget = combat.HittableEnemies.First();
            NCreature actor = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
            var strike = combat.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
            await CardPileCmd.Add(strike, PileType.Hand);
            await PlayWithStationaryCombatUi(strike, _timedTarget);
            Node2D pose = actor.Visuals.GetNode<Node2D>("%AimPose");
            Type poseType = pose.GetType();
            AccessTools.Property(poseType, "UseTornadoHitStop").SetValue(pose, _configuration.TornadoHitStop);
            var drag = new Node { Name = "TornadoPreviewDrag" };
            actor.AddChild(drag);
            string directory = _configuration.TornadoPreviewDirectory
                ?? throw new InvalidOperationException("Tornado preview output directory is required.");
            Directory.CreateDirectory(directory);
            var recorder = new TornadoViewportRecording(_tree.Root, directory);
            var timings = new JsonArray();
            try
            {
                await recorder.Start();
                foreach (int x in new[] { 3, 4, 6 })
                {
                    AutoSlayer.CurrentWatchdog?.Reset($"Recording Tornado X={x}");
                    foreach (Creature enemy in combat.HittableEnemies.ToArray())
                        await PowerCmd.Remove<VulnerablePower>(enemy);
                    await PlayerCmd.SetEnergy(x, player);
                    var card = combat.CreateCard<TornadoFistRedesignV1>(player);
                    await CardPileCmd.Add(card, PileType.Hand);
                    await WaitFrames(45);
                    AccessTools.Method(poseType, "Drag").Invoke(pose,
                        [drag, card, new Vector2(1100f, 300f), null]);
                    await WaitFrames(90);
                    AccessTools.Method(poseType, "EndDrag").Invoke(pose, [drag, true]);
                    _timedTornado = card;
                    _tornadoHits.Clear();
                    _tornadoPauses = 0;
                    _tornadoStart = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                    await PlayWithStationaryCombatUi(card, _timedTarget);
                    Require(_tornadoHits.Count == x, $"X={x} produced {_tornadoHits.Count} damage hooks.");
                    Require(_tornadoPauses == (_configuration.TornadoHitStop && x >= 4 ? x : 0),
                        $"X={x} applied {_tornadoPauses} hurt pauses for variant B={_configuration.TornadoHitStop}.");
                    for (int i = 1; i < x; i++)
                        Require(Math.Abs(_tornadoHits[i] - _tornadoHits[i - 1] - .35) < .05,
                            $"X={x} hit interval was {_tornadoHits[i] - _tornadoHits[i - 1]:F4}s, expected .35s.");
                    timings.Add(new JsonObject
                    {
                        ["x"] = x,
                        ["start"] = _tornadoStart,
                        ["hurtPauses"] = _tornadoPauses,
                        ["hits"] = new JsonArray(_tornadoHits.Select(t => JsonValue.Create(t)).ToArray())
                    });
                    _timedTornado = null;
                    Require(pose.Transform.IsEqualApprox(Transform2D.Identity), "Tornado left a posed AimPose after return.");
                }
            }
            finally
            {
                await recorder.Stop();
                File.WriteAllText(Path.Combine(directory, "timing.json"), timings.ToJsonString());
                AccessTools.Property(poseType, "UseTornadoHitStop").SetValue(pose, false);
                drag.QueueFree();
            }
            _checkpoints.Write("tornado.preview-completed", data: new JsonObject { ["timings"] = timings.DeepClone() });
            AccessTools.Property(poseType, "UseTornadoHitStop").SetValue(pose, _configuration.TornadoHitStop);
            foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                AutoSlayer.CurrentWatchdog?.Reset($"Verifying Tornado cadence in {mode}");
                SaveManager.Instance.PrefsSave.FastMode = mode;
                var results = new List<double[]>();
                foreach (bool native in new[] { true, false })
                {
                    await PlayerCmd.SetEnergy(4, player);
                    CardModel card = native ? combat.CreateCard<Whirlwind>(player) : combat.CreateCard<TornadoFistRedesignV1>(player);
                    await CardPileCmd.Add(card, PileType.Hand);
                    _timedTornado = card;
                    _tornadoHits.Clear();
                    _tornadoStart = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                    await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, _timedTarget);
                    results.Add(_tornadoHits.ToArray());
                    Require(_tornadoHits.Count == 4, $"{mode} {card.Id} did not resolve four hits.");
                    await WaitFrames(25);
                }
                for (int i = 1; i < 4; i++)
                    Require(Math.Abs((results[0][i] - results[0][i - 1]) - (results[1][i] - results[1][i - 1])) < .04,
                        $"{mode} Tornado cadence differs from native Whirlwind.");
                _checkpoints.Write("tornado.native-cadence", data: new JsonObject
                {
                    ["mode"] = mode.ToString(),
                    ["native"] = new JsonArray(results[0].Select(t => JsonValue.Create(t)).ToArray()),
                    ["tornado"] = new JsonArray(results[1].Select(t => JsonValue.Create(t)).ToArray())
                });
            }
            _timedTornado = null;
            Type pauseType = typeof(NinjaSlayerAimPose).Assembly.GetType("NinjaSlayer.Code.Nodes.TornadoHurtPause", true)!;
            void PausePng(float seconds) => AccessTools.Method(pauseType, "Start").Invoke(null, [player.Creature, seconds]);
            Vector2 baseline = actor.Position;
            Task hurt = StaggerAnimation.Play(player.Creature);
            PausePng(.08f);
            Vector2 held = actor.Position;
            await WaitFrames(3);
            Require(actor.Position.IsEqualApprox(held), "PNG hurt advanced during its pause.");
            await hurt;
            Require(actor.Position.IsEqualApprox(baseline), "PNG hurt pause did not restore its baseline.");
            hurt = StaggerAnimation.Play(player.Creature);
            PausePng(.12f);
            await WaitFrames(2);
            Task replacement = StaggerAnimation.Play(player.Creature);
            await WaitFrames(7);
            Require(StaggerAnimation.IsActive(player.Creature), "An old pause cancelled the replacement PNG hurt.");
            await replacement;
            await hurt;
            Require(actor.Position.IsEqualApprox(baseline), "Replacement hurt drifted after old pause cleanup.");
            _checkpoints.Write("tornado.png-pause-ownership");
            AutoSlayer.CurrentWatchdog?.Reset("Tornado preview cleanup");
            await NGame.Instance!.ReturnToMainMenu();
            await WaitFrames(10);
            _firstCombatCompleted.TrySetResult();
            // The preview owns this combat until shutdown; AutoSlayer must not seek rewards.
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (Exception exception)
        {
            _firstCombatCompleted.TrySetException(exception);
            throw;
        }
        finally { NonInteractiveMode.AutoSlayerCheck = autoSlayerCheck; }
    }

    private async Task PlayWithStationaryCombatUi(CardModel card, Creature target)
    {
        NCreature actor = NCombatRoom.Instance!.GetCreatureNode(card.Owner.Creature)!;
        Control stateDisplay = actor.GetNode<Control>("%HealthBar");
        CanvasItem combatLayout = actor.GetParent<CanvasItem>();
        Vector2 UiPosition() => combatLayout.GetGlobalTransformWithCanvas().AffineInverse()
            * stateDisplay.GetGlobalTransformWithCanvas().Origin;
        Vector2 root = actor.Position;
        Vector2 ui = UiPosition();
        float maxRootShift = 0f, maxUiShift = 0f;
        void Sample()
        {
            maxRootShift = Math.Max(maxRootShift, actor.Position.DistanceTo(root));
            maxUiShift = Math.Max(maxUiShift, UiPosition().DistanceTo(ui));
        }
        RenderingServer.FramePreDraw += Sample;
        try
        {
            await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, target);
            await WaitFrames(60);
            Require(maxRootShift < .1f && maxUiShift < .1f,
                $"{card.Id} moved combat UI: root={maxRootShift:F3}px, UI={maxUiShift:F3}px.");
            _checkpoints.Write("attack.stationary-ui", data: new JsonObject
            {
                ["card"] = card.Id.ToString(), ["rootShift"] = maxRootShift, ["uiShift"] = maxUiShift
            });
        }
        finally { RenderingServer.FramePreDraw -= Sample; }
    }
}

[HarmonyPatch(typeof(NinjaSlayerCombatAnimations), nameof(NinjaSlayerCombatAnimations.TryPlayTriggerAnim))]
internal static class TornadoPreviewNativeTimingReference
{
    private static bool Prefix(ref bool __result)
    {
        if (SmokeController.Current?.IsNativeTimingReference != true) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(NinjaSlayerXAttackSequence), nameof(NinjaSlayerXAttackSequence.Run))]
internal static class TornadoPreviewStartObserver
{
    private static void Prefix() => SmokeController.Current?.ObserveTornadoStart();
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageReceived))]
internal static class TornadoPreviewDamageObserver
{
    private static void Prefix(Creature target, CardModel? cardSource) =>
        SmokeController.Current?.ObserveTornadoDamage(target, cardSource);
}

internal sealed class TornadoViewportRecording(Viewport viewport, string directory)
{
    private readonly Channel<(byte[] Pixels, int Repeats)> _frames = Channel.CreateUnbounded<(byte[], int)>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private Process? _encoder;
    private Task? _writer;
    private long _start;
    private int _frameCount;
    private bool _recording;
    private Exception? _failure;

    internal async Task Start()
    {
        await viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = viewport.GetTexture().GetImage();
        var start = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardError = true
        };
        foreach (string arg in new[] { "-y", "-hide_banner", "-loglevel", "error", "-f", "rawvideo", "-pixel_format", "rgba",
                     "-video_size", $"{image.GetWidth()}x{image.GetHeight()}", "-framerate", "60", "-i", "pipe:0",
                     "-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p",
                     Path.Combine(directory, "video.mp4") }) start.ArgumentList.Add(arg);
        _encoder = Process.Start(start)!;
        _writer = Task.Run(async () =>
        {
            await foreach (var (pixels, repeats) in _frames.Reader.ReadAllAsync())
                for (int i = 0; i < repeats; i++) await _encoder.StandardInput.BaseStream.WriteAsync(pixels);
            _encoder.StandardInput.Close();
        });
        _start = Stopwatch.GetTimestamp();
        File.WriteAllText(Path.Combine(directory, "recording-start.json"), new JsonObject
        { ["timestamp"] = _start, ["frequency"] = Stopwatch.Frequency }.ToJsonString());
        _recording = true;
        RenderingServer.FramePostDraw += Capture;
    }

    private void Capture()
    {
        if (!_recording) return;
        try
        {
            int expected = (int)Math.Floor(Stopwatch.GetElapsedTime(_start).TotalSeconds * 60) + 1;
            if (expected <= _frameCount) return;
            using Image image = viewport.GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            if (!_frames.Writer.TryWrite((image.GetData(), expected - _frameCount)))
                throw new IOException("Preview encoder stopped accepting frames.");
            _frameCount = expected;
        }
        catch (Exception exception) { _failure = exception; _recording = false; }
    }

    internal async Task Stop()
    {
        _recording = false;
        RenderingServer.FramePostDraw -= Capture;
        _frames.Writer.TryComplete();
        if (_writer != null) await _writer;
        if (_encoder != null)
        {
            string errors = await _encoder.StandardError.ReadToEndAsync();
            await _encoder.WaitForExitAsync();
            if (_encoder.ExitCode != 0) throw new IOException($"Preview encoder failed: {errors}");
            _encoder.Dispose();
        }
        File.WriteAllText(Path.Combine(directory, "recording-stop.json"), new JsonObject
        { ["timestamp"] = Stopwatch.GetTimestamp(), ["frames"] = _frameCount }.ToJsonString());
        if (_failure != null) throw new IOException("Preview capture failed.", _failure);
    }
}
