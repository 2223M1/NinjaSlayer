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
    internal bool IsTornadoReturnProbe => _configuration.PreviewFormFinisher == "tornado-return";

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
        _previewAutoSlayer = new AutoSlayer();
        _previewAutoSlayer.Start(_configuration.Seed, _configuration.AutoSlayLogPath);
        await WaitTaskAsync(_firstCombatCompleted.Task, "Tornado preview did not complete", TimeSpan.FromMinutes(4));
        await FinishPreviewAsync();
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
                    if (!IsTornadoReturnProbe)
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
            if (IsTornadoReturnProbe)
            {
                _firstCombatCompleted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
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
        NCreature[] actors = card is AlabamaDropRedesignV1
            ? [actor, NCombatRoom.Instance.GetCreatureNode(target)!] : [actor];
        Vector2 UiPosition(NCreature actor) => actor.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse()
            * actor.GetNode<Control>("%HealthBar").GetGlobalTransformWithCanvas().Origin;
        Vector2[] roots = actors.Select(actor => actor.Position).ToArray();
        Vector2[] ui = actors.Select(UiPosition).ToArray();
        float maxRootShift = 0f, maxUiShift = 0f;
        Vector2 CorePosition() => actor.GetGlobalTransformWithCanvas().AffineInverse()
            * actor.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
        float coreBefore = CorePosition().Y;
        var tornadoFrames = new JsonArray();
        void Sample()
        {
            for (int i = 0; i < actors.Length; i++)
            {
                maxRootShift = Math.Max(maxRootShift, actors[i].Position.DistanceTo(roots[i]));
                maxUiShift = Math.Max(maxUiShift, UiPosition(actors[i]).DistanceTo(ui[i]));
            }
            if (card is TornadoFistRedesignV1)
            {
                Node2D aim = actor.Visuals.GetNode<Node2D>("%AimPose");
                Node2D air = actor.Visuals.GetNode<Node2D>("AirborneAnchor");
                Sprite2D body = actor.Visuals.GetNode<Sprite2D>("%Visuals");
                Vector2 Point(CanvasItem item) => actor.GetGlobalTransformWithCanvas().AffineInverse()
                    * item.GetGlobalTransformWithCanvas().Origin;
                tornadoFrames.Add(new JsonObject
                {
                    ["frame"] = Engine.GetProcessFrames(),
                    ["bodyY"] = Point(body).Y, ["bodyX"] = Point(body).X,
                    ["aimY"] = aim.Position.Y, ["airY"] = air.Position.Y,
                    ["bodyLocalY"] = body.Position.Y, ["bodyScaleY"] = body.Scale.Y,
                    ["rotation"] = body.Rotation, ["coreY"] = Point(actor.Visuals.VfxSpawnPosition).Y,
                    ["returning"] = (bool)AccessTools.Field(aim.GetType(), "_returning").GetValue(aim)!,
                    ["launch"] = (float)AccessTools.Field(aim.GetType(), "_launch").GetValue(aim)!
                });
            }
        }
        RenderingServer.FramePreDraw += Sample;
        try
        {
            await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, target);
            await WaitFrames(60);
            if (IsTornadoReturnProbe && card is TornadoFistRedesignV1)
                Require(CorePosition().Y <= coreBefore + 1f,
                    $"Tornado return sank the actual body: before={coreBefore}, after={CorePosition().Y}.");
            Require(maxRootShift < .1f && maxUiShift < .1f,
                $"{card.Id} moved combat UI: root={maxRootShift:F3}px, UI={maxUiShift:F3}px.");
            _checkpoints.Write("attack.stationary-ui", data: new JsonObject
            {
                ["card"] = card.Id.ToString(), ["rootShift"] = maxRootShift, ["uiShift"] = maxUiShift
            });
        }
        finally
        {
            RenderingServer.FramePreDraw -= Sample;
            if (tornadoFrames.Count > 0 && _configuration.TornadoPreviewDirectory is { } directory)
                File.WriteAllText(Path.Combine(directory, $"tornado-return-{Engine.GetProcessFrames()}.json"), tornadoFrames.ToJsonString());
        }
    }
}

[HarmonyPatch]
internal static class TornadoReturnFreeSetting
{
    private static System.Reflection.MethodBase TargetMethod() => AccessTools.PropertyGetter(
        typeof(NinjaSlayer.Content.NinjaSlayerSettings), "FreeControlEnabled");
    private static bool Prefix(ref bool __result)
    {
        if (SmokeController.Current?.IsTornadoReturnProbe != true) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch]
internal static class TornadoReturnBackgroundPhysics
{
    private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(
        typeof(NinjaSlayerAimPose).Assembly.GetType("NinjaSlayer.Code.Nodes.NinjaSlayerFreeControl", true)!, "IsBlocked");
    private static bool Prefix(ref bool __result)
    {
        if (SmokeController.Current?.IsTornadoReturnProbe != true) return true;
        __result = false;
        return false;
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
    private static TornadoViewportRecording? _active;
    private readonly Channel<(byte[] Pixels, int Repeats)> _frames = Channel.CreateUnbounded<(byte[], int)>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private Process? _encoder;
    private Task? _writer;
    private long _start;
    private int _frameCount;
    private byte[]? _previousFrame;
    private readonly List<(long Timestamp, int Frame)> _capturedFrames = [];
    private readonly List<(long Timestamp, string Event)> _audioEvents = [];
    private bool _recording;
    private Exception? _failure;
    internal long StartTimestamp => _start;

    internal async Task Start()
    {
        await viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _start = Stopwatch.GetTimestamp();
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
        image.Convert(Image.Format.Rgba8);
        _previousFrame = image.GetData();
        _frames.Writer.TryWrite((_previousFrame, 1));
        _frameCount = 1;
        _capturedFrames.Add((_start, 0));
        File.WriteAllText(Path.Combine(directory, "recording-start.json"), new JsonObject
        { ["timestamp"] = _start, ["frequency"] = Stopwatch.Frequency }.ToJsonString());
        _recording = true;
        _active = this;
        RenderingServer.FramePostDraw += Capture;
    }

    internal static void ObserveAudio(string eventPath)
    {
        _active?._audioEvents.Add((Stopwatch.GetTimestamp(), eventPath));
    }

    private void Capture()
    {
        if (!_recording) return;
        try
        {
            long timestamp = Stopwatch.GetTimestamp();
            int expected = (int)Math.Floor((timestamp - _start) * 60d / Stopwatch.Frequency) + 1;
            if (expected <= _frameCount) return;
            using Image image = viewport.GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            if (expected - _frameCount > 1 && !_frames.Writer.TryWrite((_previousFrame!, expected - _frameCount - 1)))
                throw new IOException("Preview encoder stopped accepting frames.");
            byte[] pixels = image.GetData();
            if (!_frames.Writer.TryWrite((pixels, 1)))
                throw new IOException("Preview encoder stopped accepting frames.");
            _previousFrame = pixels;
            _frameCount = expected;
            _capturedFrames.Add((timestamp, expected - 1));
        }
        catch (Exception exception) { _failure = exception; _recording = false; }
    }

    internal async Task Stop()
    {
        long stop = Stopwatch.GetTimestamp();
        _recording = false;
        _active = null;
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
        { ["timestamp"] = stop, ["frames"] = _frameCount }.ToJsonString());
        File.WriteAllLines(Path.Combine(directory, "video-frames.csv"),
            new[] { "qpc,frame" }.Concat(_capturedFrames.Select(f => $"{f.Timestamp},{f.Frame}")));
        File.WriteAllLines(Path.Combine(directory, "audio-events.jsonl"), _audioEvents.Select(e =>
            new JsonObject { ["qpc"] = e.Timestamp, ["event"] = e.Event }.ToJsonString()));
        if (_failure != null) throw new IOException("Preview capture failed.", _failure);
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Audio.NAudioManager), "PlayOneShot",
    [typeof(string), typeof(Dictionary<string, float>), typeof(float)])]
internal static class PreviewAudioTimestampObserver
{
    private static void Prefix(ref string path, float volume)
    {
        TornadoViewportRecording.ObserveAudio(path);
        SmokeController.Current?.SawatariSoundObserver?.Invoke(path, volume);
    }
}

[HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)])]
internal static class PreviewModAudioTimestampObserver
{
    private static void Prefix(string sfx)
    {
        // RitsuLib plays private-bank events without going through the host audio node.
        if (sfx.StartsWith("event:/NinjaSlayerAudio/", StringComparison.Ordinal))
            TornadoViewportRecording.ObserveAudio(sfx);
    }
}
