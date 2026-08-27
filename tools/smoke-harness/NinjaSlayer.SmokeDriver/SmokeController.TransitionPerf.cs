using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private const double RunLoadStartToleranceMilliseconds = 100d;
    private static readonly FieldInfo AssetName = RequiredAssetField("_name");
    private static readonly FieldInfo AssetToLoad = RequiredAssetField("_toLoad");
    private static readonly FieldInfo AssetLoading = RequiredAssetField("_loading");
    private static readonly FieldInfo AssetFinalizing = RequiredAssetField("_finalizing");
    private static readonly FieldInfo AssetVfxScenes = RequiredAssetField("_vfxScenes");
    private static readonly FieldInfo AssetVfxLoading = RequiredAssetField("_vfxLoading");
    private static readonly FieldInfo AssetCurrentVfxPath = RequiredAssetField("_currentVfxPath");
    private static readonly FieldInfo AssetCache = RequiredAssetField("_cache");
    private readonly Dictionary<AssetLoadingSession, TransitionAssetObservation> _transitionAssets =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<long> _transitionFrameQpcs = [];
    private readonly TaskCompletionSource _transitionPerfCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _transitionNeowHandlerClaimed;
    private int _transitionRevealClaimed;
    private long _transitionAnimationEndedQpc;
    private long _transitionFirstVisibleQpc;
    private long _transitionRevealQpc;
    private long _transitionRunLoadStartQpc;
    private long _transitionStartQpc;

    internal bool TryBeginTransitionPerfInteractiveWait(out Func<bool>? autoSlayerCheck)
    {
        autoSlayerCheck = null;
        if (_configuration.Phase != SmokePhase.TransitionPerf)
        {
            return false;
        }

        autoSlayerCheck = NonInteractiveMode.AutoSlayerCheck;
        NonInteractiveMode.AutoSlayerCheck = static () => false;
        return true;
    }

    public void ObserveTransitionPlaybackStarted()
    {
        if (_configuration.Phase != SmokePhase.TransitionPerf)
        {
            return;
        }

        long timestamp = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(ref _transitionStartQpc, timestamp, 0) == 0)
        {
            _checkpoints.Write(
                "transition-perf.started",
                data: new JsonObject
                {
                    ["qpc"] = timestamp,
                    ["qpcFrequency"] = Stopwatch.Frequency,
                    ["nonInteractiveMode"] = NonInteractiveMode.IsActive,
                    ["fastMode"] = SaveManager.Instance.PrefsSave.FastMode.ToString(),
                    ["timeScale"] = Engine.TimeScale
                });
        }
    }

    public void ObserveTransitionRunLoadingStarted()
    {
        if (_configuration.Phase != SmokePhase.TransitionPerf)
        {
            return;
        }

        long timestamp = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(ref _transitionRunLoadStartQpc, timestamp, 0) == 0)
        {
            long playbackStart = Volatile.Read(ref _transitionStartQpc);
            _checkpoints.Write(
                "transition-perf.run-loading-started",
                data: new JsonObject
                {
                    ["qpc"] = timestamp,
                    ["millisecondsAfterPlayback"] = playbackStart == 0
                        ? null
                        : Stopwatch.GetElapsedTime(playbackStart, timestamp).TotalMilliseconds,
                    ["nonInteractiveMode"] = NonInteractiveMode.IsActive
                });
        }
    }

    public bool TryHoldTransitionPerfNeow(ref Task result)
    {
        if (_configuration.Phase != SmokePhase.TransitionPerf
            || !TryReadNeowRoom(out _)
            || Interlocked.CompareExchange(ref _transitionNeowHandlerClaimed, 1, 0) != 0)
        {
            return false;
        }

        _checkpoints.Write("transition-perf.neow-held");
        result = Task.Delay(Timeout.InfiniteTimeSpan);
        return true;
    }

    public void TryWrapTransitionPerfReveal(ref Task result)
    {
        if (_configuration.Phase == SmokePhase.TransitionPerf
            && Volatile.Read(ref _transitionStartQpc) != 0
            && Interlocked.CompareExchange(ref _transitionRevealClaimed, 1, 0) == 0)
        {
            result = ObserveTransitionPerfRevealAsync(result);
        }
    }

    public void ObserveTransitionOverlayStopped()
    {
        if (_configuration.Phase == SmokePhase.TransitionPerf
            && Volatile.Read(ref _transitionStartQpc) != 0)
        {
            Interlocked.CompareExchange(
                ref _transitionAnimationEndedQpc,
                Stopwatch.GetTimestamp(),
                0);
        }
    }

    public void ObserveTransitionAssetProcessStarting(AssetLoadingSession session)
    {
        if (!IsTransitionPerfCaptureActive)
        {
            return;
        }

        GetTransitionAssetObservation(session).CaptureInitialOutstanding(session);
    }

    public void ObserveTransitionAssetCached(AssetLoadingSession session, string path)
    {
        if (!IsTransitionPerfCaptureActive)
        {
            return;
        }

        TransitionAssetObservation observation = GetTransitionAssetObservation(session);
        observation.AddCounts[path] = observation.AddCounts.GetValueOrDefault(path) + 1;
    }

    public void ObserveTransitionAssetProcessCompleted(AssetLoadingSession session)
    {
        if (!IsTransitionPerfCaptureActive)
        {
            return;
        }

        TransitionAssetObservation observation = GetTransitionAssetObservation(session);
        observation.CaptureFinalState(session);
        if (session.IsCompleted && observation.DrainQpc == 0)
        {
            foreach (string path in observation.InitialOutstanding.Where(observation.Cache.ContainsKey))
            {
                observation.CachedAtCompletion.Add(path);
            }
            observation.DrainQpc = Stopwatch.GetTimestamp();
        }
    }

    private bool IsTransitionPerfCaptureActive =>
        _configuration.Phase == SmokePhase.TransitionPerf
        && Volatile.Read(ref _transitionStartQpc) != 0
        && !_transitionPerfCompleted.Task.IsCompleted;

    private async Task RunTransitionPerfPhaseAsync()
    {
        ValidateTransitionPerfAssembly();
        NGame.Instance!.DebugSeedOverride = _configuration.Seed;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
        SaveManager.Instance.SetFtuesEnabled(enabled: false);
        SaveManager.Instance.ObtainEpochOverride(
            EpochModel.GetId<NeowEpoch>(),
            EpochState.Revealed);
        _checkpoints.Write(
            "transition-perf.autoslay-starting",
            data: new JsonObject
            {
                ["variant"] = _configuration.TransitionVariant,
                ["warmup"] = _configuration.TransitionPerfWarmup
            });

        Task frameCapture = CaptureTransitionFramesAsync();
        var autoSlayer = new MegaCrit.Sts2.Core.AutoSlay.AutoSlayer();
        autoSlayer.Start(_configuration.Seed, _configuration.AutoSlayLogPath);
        await WaitTaskAsync(
            _transitionPerfCompleted.Task,
            "TransitionPerf did not reveal and hold the Neow room",
            TimeSpan.FromMinutes(2));
        await frameCapture;
        WriteTransitionPerfResult();
        _checkpoints.Write("transition-perf.completed");
        _tree.Quit(0);
    }

    private async Task CaptureTransitionFramesAsync()
    {
        try
        {
            while (!_transitionPerfCompleted.Task.IsCompleted)
            {
                await _tree.ToSignal(_tree, SceneTree.SignalName.ProcessFrame);
                if (Volatile.Read(ref _transitionStartQpc) == 0)
                {
                    continue;
                }

                long timestamp = Stopwatch.GetTimestamp();
                _transitionFrameQpcs.Add(timestamp);
                TryCaptureFirstVisibleFrame(timestamp);
                if (Volatile.Read(ref _transitionRevealQpc) != 0
                    && Volatile.Read(ref _transitionFirstVisibleQpc) != 0
                    && Volatile.Read(ref _transitionNeowHandlerClaimed) != 0)
                {
                    _transitionPerfCompleted.TrySetResult();
                }
            }
        }
        catch (Exception ex)
        {
            _transitionPerfCompleted.TrySetException(ex);
        }
    }

    private async Task ObserveTransitionPerfRevealAsync(Task revealTask)
    {
        try
        {
            await revealTask;
            Require(TryReadNeowRoom(out JsonObject room),
                "TransitionPerf revealed a room other than Act 1 Neow from the Ancient map point.");
            long timestamp = Stopwatch.GetTimestamp();
            Interlocked.CompareExchange(ref _transitionRevealQpc, timestamp, 0);
            _checkpoints.Write(
                "transition-perf.revealed",
                data: room);
        }
        catch (Exception ex)
        {
            _transitionPerfCompleted.TrySetException(ex);
            throw;
        }
    }

    private void TryCaptureFirstVisibleFrame(long timestamp)
    {
        if (Volatile.Read(ref _transitionAnimationEndedQpc) == 0
            || Volatile.Read(ref _transitionFirstVisibleQpc) != 0
            || !TryReadNeowRoom(out _))
        {
            return;
        }

        NTransition? transition = FindDescendant<NTransition>(_tree.Root);
        ColorRect? backdrop = transition?.GetNodeOrNull<ColorRect>("SimpleTransition");
        NinjaSlayerTransitionOverlay? overlay = transition is null
            ? null
            : FindDescendant<NinjaSlayerTransitionOverlay>(transition);
        if (transition is not null
            && backdrop is not null
            && backdrop.Modulate.A < 0.999f
            && (overlay is null || !overlay.Visible))
        {
            Interlocked.CompareExchange(ref _transitionFirstVisibleQpc, timestamp, 0);
        }
    }

    private void ValidateTransitionPerfAssembly()
    {
        Assembly product = typeof(NinjaSlayerTransitionOverlay).Assembly;
        Require(
            ReadAssemblyMetadata(product, "NinjaSlayerSourceRevision")
                .Equals(_configuration.CandidateSha, StringComparison.OrdinalIgnoreCase),
            "TransitionPerf product source revision does not match the requested candidate SHA.");
        Require(
            ReadAssemblyMetadata(product, "NinjaSlayerTransitionLoadLimitEnabled")
                .Equals(_configuration.TransitionLoadLimitEnabled!.Value.ToString(), StringComparison.OrdinalIgnoreCase),
            "TransitionPerf load-limit metadata does not match the requested component matrix.");
        Require(
            ReadAssemblyMetadata(product, "NinjaSlayerTransitionFinalizeBatchingEnabled")
                .Equals(_configuration.TransitionFinalizeBatchingEnabled!.Value.ToString(), StringComparison.OrdinalIgnoreCase),
            "TransitionPerf finalize metadata does not match the requested component matrix.");
    }

    private void WriteTransitionPerfResult()
    {
        long start = Volatile.Read(ref _transitionStartQpc);
        long animationEnd = Volatile.Read(ref _transitionAnimationEndedQpc);
        long firstVisible = Volatile.Read(ref _transitionFirstVisibleQpc);
        long reveal = Volatile.Read(ref _transitionRevealQpc);
        long runLoadStart = Volatile.Read(ref _transitionRunLoadStartQpc);
        Require(start != 0
                && runLoadStart >= start
                && animationEnd > runLoadStart
                && firstVisible >= animationEnd
                && reveal >= firstVisible,
            "TransitionPerf timing anchors were incomplete or out of order.");
        double runLoadStartMilliseconds = Stopwatch.GetElapsedTime(start, runLoadStart).TotalMilliseconds;
        double animationEndMilliseconds = Stopwatch.GetElapsedTime(start, animationEnd).TotalMilliseconds;
        double expectedRunLoadStartMilliseconds = NinjaSlayerAudio.EmbarkLoadStartDelaySeconds * 1000d;
        Require(
            Math.Abs(runLoadStartMilliseconds - expectedRunLoadStartMilliseconds)
                <= RunLoadStartToleranceMilliseconds,
            $"TransitionPerf run loading started after {runLoadStartMilliseconds:0.###}ms; "
                + $"expected {expectedRunLoadStartMilliseconds:0.###}ms "
                + $"(+/- {RunLoadStartToleranceMilliseconds:0.###}ms).");
        Require(_transitionFrameQpcs.Count >= 2,
            "TransitionPerf captured fewer than two frames.");

        TransitionAssetObservation[] assets = _transitionAssets.Values
            .Where(observation => observation.InitialOutstanding.Count > 0)
            .ToArray();
        Require(assets.Length > 0,
            "TransitionPerf did not observe an active AssetLoadingSession.");
        Require(assets.All(observation => observation.DrainQpc != 0 && observation.AllQueuesEmpty),
            "TransitionPerf reveal left an AssetLoadingSession queue or in-flight VFX load behind.");
        string[] missingAtCompletion = assets
            .SelectMany(observation => observation.InitialOutstanding
                .Except(observation.CachedAtCompletion)
                .Select(path => $"{observation.Name}: {path}"))
            .ToArray();
        Require(missingAtCompletion.Length == 0,
            "TransitionPerf completed without caching every initially outstanding resource: "
                + string.Join(", ", missingAtCompletion));
        Require(assets.All(observation => observation.AddCounts.Values.All(count => count == 1)),
            "TransitionPerf finalized a resource more than once in one loading session.");

        double[] frameTimes = _transitionFrameQpcs
            .Zip(_transitionFrameQpcs.Skip(1), (left, right) => Stopwatch.GetElapsedTime(left, right).TotalMilliseconds)
            .ToArray();
        double[] sortedFrameTimes = [.. frameTimes.Order()];
        double p99 = sortedFrameTimes[(int)Math.Ceiling(sortedFrameTimes.Length * 0.99) - 1];
        long drain = assets.Max(observation => observation.DrainQpc);

        var rawFrames = new JsonArray();
        for (var index = 0; index < _transitionFrameQpcs.Count; index++)
        {
            rawFrames.Add(new JsonObject
            {
                ["qpc"] = _transitionFrameQpcs[index],
                ["deltaMilliseconds"] = index == 0
                    ? null
                    : Stopwatch.GetElapsedTime(
                        _transitionFrameQpcs[index - 1],
                        _transitionFrameQpcs[index]).TotalMilliseconds
            });
        }

        var assetResults = new JsonArray();
        foreach (TransitionAssetObservation asset in assets.OrderBy(asset => asset.Name, StringComparer.Ordinal))
        {
            int cachedAtReport = asset.InitialOutstanding.Count(asset.Cache.ContainsKey);
            assetResults.Add(new JsonObject
            {
                ["name"] = asset.Name,
                ["initialOutstanding"] = asset.InitialOutstanding.Count,
                ["cached"] = asset.CachedAtCompletion.Count,
                ["cachedAtReport"] = cachedAtReport,
                ["missingAtReport"] = asset.CachedAtCompletion.Count - cachedAtReport,
                ["addCalls"] = asset.AddCounts.Values.Sum(),
                ["duplicateAdds"] = asset.AddCounts.Values.Count(count => count > 1),
                ["drainQpc"] = asset.DrainQpc,
                ["allQueuesEmpty"] = asset.AllQueuesEmpty
            });
        }

        var result = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["candidateSha"] = _configuration.CandidateSha.ToLowerInvariant(),
            ["variant"] = _configuration.TransitionVariant,
            ["warmup"] = _configuration.TransitionPerfWarmup,
            ["seed"] = _configuration.Seed,
            ["resolution"] = "1280x720",
            ["fpsLimit"] = 60,
            ["frameSource"] = "Godot SceneTree.ProcessFrame QPC",
            ["p99Algorithm"] = "nearest-rank over consecutive ProcessFrame QPC deltas",
            ["qpcFrequency"] = Stopwatch.Frequency,
            ["transitionStartQpc"] = start,
            ["runLoadStartQpc"] = runLoadStart,
            ["animationEndQpc"] = animationEnd,
            ["firstVisibleGameplayFrameQpc"] = firstVisible,
            ["revealQpc"] = reveal,
            ["queueDrainQpc"] = drain,
            ["frameCount"] = _transitionFrameQpcs.Count,
            ["p99Milliseconds"] = p99,
            ["runLoadStartMilliseconds"] = runLoadStartMilliseconds,
            ["animationEndMilliseconds"] = animationEndMilliseconds,
            ["runLoadingOverlappedAnimation"] = true,
            ["revealMilliseconds"] = Stopwatch.GetElapsedTime(start, reveal).TotalMilliseconds,
            ["queueDrainMilliseconds"] = Stopwatch.GetElapsedTime(start, drain).TotalMilliseconds,
            ["blackScreenHoldMilliseconds"] = Stopwatch.GetElapsedTime(animationEnd, firstVisible).TotalMilliseconds,
            ["firstVisibleGameplayFrameMilliseconds"] = Stopwatch.GetElapsedTime(start, firstVisible).TotalMilliseconds,
            ["cacheComplete"] = true,
            ["loadLimitEnabled"] = _configuration.TransitionLoadLimitEnabled,
            ["finalizeBatchingEnabled"] = _configuration.TransitionFinalizeBatchingEnabled,
            ["assets"] = assetResults,
            ["frames"] = rawFrames
        };
        string outputPath = _configuration.TransitionPerfOutputPath!;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(
            outputPath,
            result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private TransitionAssetObservation GetTransitionAssetObservation(AssetLoadingSession session)
    {
        if (!_transitionAssets.TryGetValue(session, out TransitionAssetObservation? observation))
        {
            observation = new TransitionAssetObservation(ReadAssetField<string>(session, AssetName));
            _transitionAssets.Add(session, observation);
        }

        return observation;
    }

    private static bool TryReadNeowRoom(out JsonObject data)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        EventRoom? room = runState?.CurrentRoom as EventRoom;
        bool valid = room?.CanonicalEvent is Neow
            && runState!.ExtraFields.StartedWithNeow
            && runState.CurrentActIndex == 0
            && runState.ActFloor == 1
            && runState.CurrentMapPoint?.PointType == MegaCrit.Sts2.Core.Map.MapPointType.Ancient;
        data = new JsonObject
        {
            ["roomType"] = room?.GetType().FullName,
            ["eventType"] = room?.CanonicalEvent.GetType().FullName,
            ["eventId"] = room?.CanonicalEvent.Id.ToString(),
            ["startedWithNeow"] = runState?.ExtraFields.StartedWithNeow,
            ["currentActIndex"] = runState?.CurrentActIndex,
            ["actFloor"] = runState?.ActFloor,
            ["mapPointType"] = runState?.CurrentMapPoint?.PointType.ToString(),
            ["mapCoord"] = runState?.CurrentMapCoord?.ToString()
        };
        return valid;
    }

    private static FieldInfo RequiredAssetField(string name) =>
        AccessTools.Field(typeof(AssetLoadingSession), name)
        ?? throw new MissingFieldException(typeof(AssetLoadingSession).FullName, name);

    private static T ReadAssetField<T>(AssetLoadingSession session, FieldInfo field) =>
        field.GetValue(session) is T value
            ? value
            : throw new InvalidOperationException(
                $"AssetLoadingSession.{field.Name} did not contain {typeof(T).FullName}.");

    private static string ReadAssemblyMetadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value
        ?? throw new InvalidOperationException($"Assembly metadata {key} has no value.");

    private sealed class TransitionAssetObservation(string name)
    {
        public Dictionary<string, int> AddCounts { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CachedAtCompletion { get; } = new(StringComparer.Ordinal);
        public HashSet<string> InitialOutstanding { get; } = new(StringComparer.Ordinal);
        public string Name { get; } = name;
        public ConcurrentDictionary<string, Resource> Cache { get; private set; } = null!;
        public bool AllQueuesEmpty { get; private set; }
        public long DrainQpc { get; set; }
        private bool _capturedInitial;

        public void CaptureInitialOutstanding(AssetLoadingSession session)
        {
            if (_capturedInitial)
            {
                return;
            }

            _capturedInitial = true;
            Cache = ReadAssetField<ConcurrentDictionary<string, Resource>>(session, AssetCache);
            AddQueue(AssetToLoad);
            AddQueue(AssetLoading);
            AddQueue(AssetFinalizing);
            AddQueue(AssetVfxScenes);
            if (ReadAssetField<bool>(session, AssetVfxLoading)
                && AssetCurrentVfxPath.GetValue(session) is string currentVfxPath)
            {
                InitialOutstanding.Add(currentVfxPath);
            }

            void AddQueue(FieldInfo field)
            {
                foreach (string path in ReadAssetField<Queue<string>>(session, field))
                {
                    InitialOutstanding.Add(path);
                }
            }
        }

        public void CaptureFinalState(AssetLoadingSession session)
        {
            AllQueuesEmpty = ReadAssetField<Queue<string>>(session, AssetToLoad).Count == 0
                && ReadAssetField<Queue<string>>(session, AssetLoading).Count == 0
                && ReadAssetField<Queue<string>>(session, AssetFinalizing).Count == 0
                && ReadAssetField<Queue<string>>(session, AssetVfxScenes).Count == 0
                && !ReadAssetField<bool>(session, AssetVfxLoading);
        }
    }
}
