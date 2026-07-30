using Godot;
using System.Globalization;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Audio;

namespace NinjaSlayer.Code.ExternalAnimations;

internal readonly record struct BossBurstRegistration(
    Task Cue,
    Task CombatRelease,
    Task Completion);

internal readonly record struct BossBurstParticipant(
    string MonsterId,
    Func<BossDismembermentSpawn> SpawnFragments);

public sealed partial class BossBurstPresentationCoordinator : Node
{
    public const string VideoPath =
        "res://NinjaSlayer/videos/ninja_slayer_boss_burst.ogv";
    public const int VideoZIndex = 100;
    public const int ActorZIndex = 105;
    public const int FragmentZIndex = 106;
    public const int TopBarZIndex = 110;

    private const float VideoAspectRatio = 16f / 9f;
    private const float MaximumFrameDelta = 0.05f;
    private const float MaximumVideoOverrun = 0.25f;
    private const int FmodPlaybackStateStopped = 2;

    private static readonly Dictionary<ulong, BossBurstPresentationCoordinator> Active = [];

    private readonly CinematicSessionLifetime _lifetime = new();
    private readonly List<BurstBatch> _batches = [];
    private readonly Dictionary<CanvasItem, LayerSnapshot> _layerSnapshots = [];
    private NCombatRoom _room = null!;
    private ulong _roomInstanceId;
    private BurstBatch? _joiningBatch;
    private int _activeVideoLayers;

    public static IEnumerable<string> AssetPaths => [VideoPath];

    internal static BossBurstRegistration Register(
        NCombatRoom room,
        BossBurstParticipant participant)
    {
        if (!GodotObject.IsInstanceValid(room) || !room.IsInsideTree())
        {
            return new BossBurstRegistration(
                Task.CompletedTask,
                Task.CompletedTask,
                Task.CompletedTask);
        }

        BossBurstPresentationCoordinator coordinator = GetOrCreate(room);
        return coordinator.RegisterParticipant(participant);
    }

    internal static async Task WaitForCombatRelease()
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null
            || !Active.TryGetValue(room.GetInstanceId(), out BossBurstPresentationCoordinator? coordinator)
            || !GodotObject.IsInstanceValid(coordinator))
        {
            return;
        }

        while (GodotObject.IsInstanceValid(coordinator)
               && ReferenceEquals(NCombatRoom.Instance, room))
        {
            Task[] releases = coordinator._batches
                .Where(batch => !batch.CombatReleaseSource.Task.IsCompleted)
                .Select(batch => batch.CombatReleaseSource.Task)
                .ToArray();
            if (releases.Length == 0)
            {
                return;
            }

            await Task.WhenAll(releases);
        }
    }

    internal static bool IsPresentationPaused(NCombatRoom room)
    {
        if (!Active.TryGetValue(
                room.GetInstanceId(),
                out BossBurstPresentationCoordinator? coordinator)
            || !GodotObject.IsInstanceValid(coordinator))
        {
            return room.GetTree().Paused;
        }

        bool hasUnreleasedPresentation = coordinator._batches.Any(
            batch => !batch.CombatReleaseSource.Task.IsCompleted);
        return hasUnreleasedPresentation
            && (room.GetTree().Paused || IsBlockingUiOpen());
    }

    private static bool IsBlockingUiOpen()
    {
        if (!RunManager.Instance.IsSingleplayerOrFakeMultiplayer)
        {
            return false;
        }

        NGlobalUi? globalUi = NRun.Instance?.GlobalUi;
        bool hasBlockingOverlay = globalUi?.Overlays.ScreenCount > 0
            && globalUi.Overlays.Peek() is not NRewardsScreen;
        return globalUi != null
            && (hasBlockingOverlay
                || globalUi.CapstoneContainer.InUse
                || globalUi.MapScreen.IsOpen
                || NModalContainer.Instance?.OpenModal != null);
    }

    public override void _ExitTree()
    {
        bool ownsRegistration = Active.TryGetValue(
                _roomInstanceId,
                out BossBurstPresentationCoordinator? active)
            && ReferenceEquals(active, this);
        if (ownsRegistration)
        {
            Active.Remove(_roomInstanceId);
            BossBurstParticipationRegistry.Clear(_room);
        }
        _lifetime.Dispose();
        foreach (BurstBatch batch in _batches.ToArray())
        {
            FinalizeBatch(batch, stopPlayback: true);
        }

        RestorePresentationLayers();
        _batches.Clear();
        _joiningBatch = null;
    }

    private static BossBurstPresentationCoordinator GetOrCreate(NCombatRoom room)
    {
        ulong roomId = room.GetInstanceId();
        if (Active.TryGetValue(roomId, out BossBurstPresentationCoordinator? existing)
            && GodotObject.IsInstanceValid(existing)
            && existing.IsInsideTree())
        {
            return existing;
        }

        Active.Remove(roomId);

        var coordinator = new BossBurstPresentationCoordinator
        {
            Name = "NinjaSlayerBossBurstCoordinator",
            ProcessMode = ProcessModeEnum.Always,
            _room = room,
            _roomInstanceId = roomId
        };
        try
        {
            room.AddChildSafely(coordinator);
            if (!GodotObject.IsInstanceValid(coordinator) || !coordinator.IsInsideTree())
            {
                throw new InvalidOperationException(
                    "The Boss burst coordinator could not enter the combat room scene tree.");
            }

            Active[roomId] = coordinator;
            return coordinator;
        }
        catch
        {
            if (GodotObject.IsInstanceValid(coordinator))
            {
                coordinator.QueueFreeSafely();
            }

            throw;
        }
    }

    private BossBurstRegistration RegisterParticipant(BossBurstParticipant participant)
    {
        BurstBatch batch = _joiningBatch is { CueFired: false }
            ? _joiningBatch
            : StartBatch();
        batch.Participants.Add(participant);
        return new BossBurstRegistration(
            batch.CueSource.Task,
            batch.CombatReleaseSource.Task,
            batch.CompletionSource.Task);
    }

    private BurstBatch StartBatch()
    {
        var batch = new BurstBatch();
        _batches.Add(batch);
        _joiningBatch = batch;
        StartNinjaSoul(batch);
        PrepareVideo(batch);
        TaskHelper.RunSafely(RunBatch(batch, _lifetime.Token));
        return batch;
    }

    private async Task RunBatch(BurstBatch batch, CancellationToken cancelToken)
    {
        try
        {
            await WaitPresentationSeconds(batch, BossBurstTimeline.LeadSeconds, cancelToken);
            batch.CueFired = true;
            if (ReferenceEquals(_joiningBatch, batch))
            {
                _joiningBatch = null;
            }

            if (batch.VideoRoot != null)
            {
                AcquirePresentationLayers();
                batch.HasLayerLease = true;
                batch.VideoRoot.Visible = true;
                batch.VideoRoot.Modulate = Colors.White;
                batch.VideoPlayer!.Play();
            }

            var fragmentTasks = new List<Task>(batch.Participants.Count);
            foreach (BossBurstParticipant participant in batch.Participants.ToArray())
            {
                try
                {
                    BossDismembermentSpawn spawn = participant.SpawnFragments();
                    fragmentTasks.Add(spawn.Completion);
                }
                catch (Exception exception)
                {
                    Entry.Logger.Warn(
                        $"Boss burst fragments failed for "
                        + $"{participant.MonsterId}: {exception}");
                }
            }

            batch.CueSource.TrySetResult();
            Task videoTask = PlayVideoTimeline(batch, cancelToken);
            fragmentTasks.Add(videoTask);
            await Task.WhenAll(fragmentTasks);
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Entry.Logger.Error($"Boss burst presentation failed: {exception}");
        }
        finally
        {
            FinalizeBatch(batch, stopPlayback: cancelToken.IsCancellationRequested);
        }
    }

    private async Task PlayVideoTimeline(BurstBatch batch, CancellationToken cancelToken)
    {
        float videoElapsed = 0f;
        bool playbackObserved = false;
        try
        {
            while (true)
            {
                float delta = await NextPresentationFrame(batch, cancelToken);
                if (delta <= 0f)
                {
                    continue;
                }

                videoElapsed = Math.Min(
                    videoElapsed + delta,
                    BossBurstTimeline.VideoSeconds + MaximumVideoOverrun);
                float videoPosition = GetVideoPosition(batch, videoElapsed);
                if (batch.VideoRoot != null)
                {
                    Color modulate = batch.VideoRoot.Modulate;
                    modulate.A = BossBurstTimeline.ResolveFadeAlpha(videoPosition);
                    batch.VideoRoot.Modulate = modulate;
                }

                if (batch.VideoPlayer == null
                    || !GodotObject.IsInstanceValid(batch.VideoPlayer))
                {
                    if (videoElapsed >= BossBurstTimeline.VideoSeconds)
                    {
                        break;
                    }
                    continue;
                }

                bool isPlaying = batch.VideoPlayer.IsPlaying();
                playbackObserved |= isPlaying || batch.VideoPlayer.StreamPosition > 0d;
                if ((playbackObserved && !isPlaying)
                    || (!playbackObserved && videoElapsed >= BossBurstTimeline.VideoSeconds)
                    || videoElapsed >= BossBurstTimeline.VideoSeconds + MaximumVideoOverrun)
                {
                    break;
                }
            }

            if (batch.VideoRoot != null)
            {
                Color transparent = batch.VideoRoot.Modulate;
                transparent.A = 0f;
                batch.VideoRoot.Modulate = transparent;
            }
        }
        finally
        {
            try
            {
                FreeVideo(batch);
            }
            catch (Exception exception)
            {
                Entry.Logger.Warn($"Boss burst video release failed: {exception.Message}");
            }

            try
            {
                if (batch.HasLayerLease)
                {
                    batch.HasLayerLease = false;
                    ReleasePresentationLayers();
                }
            }
            catch (Exception exception)
            {
                Entry.Logger.Warn($"Boss burst layer restoration failed: {exception.Message}");
            }
            finally
            {
                batch.CombatReleaseSource.TrySetResult();
            }
        }
    }

    private void FinalizeBatch(BurstBatch batch, bool stopPlayback)
    {
        if (Interlocked.Exchange(ref batch.Finalized, 1) != 0)
        {
            return;
        }

        batch.CueFired = true;
        try
        {
            if (batch.HasLayerLease)
            {
                batch.HasLayerLease = false;
                ReleasePresentationLayers();
            }
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Boss burst final layer cleanup failed: {exception.Message}");
        }

        try
        {
            StopAndReleaseAudio(batch, stopPlayback);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Boss burst final audio cleanup failed: {exception.Message}");
        }

        try
        {
            FreeVideo(batch);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Boss burst final video cleanup failed: {exception.Message}");
        }
        finally
        {
            batch.CueSource.TrySetResult();
            batch.CombatReleaseSource.TrySetResult();
            batch.CompletionSource.TrySetResult();
            _batches.Remove(batch);
            if (ReferenceEquals(_joiningBatch, batch))
            {
                _joiningBatch = null;
            }
        }
    }

    private async Task WaitPresentationSeconds(
        BurstBatch batch,
        float seconds,
        CancellationToken cancelToken)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            float delta = await NextPresentationFrame(batch, cancelToken);
            if (delta <= 0f)
            {
                continue;
            }

            elapsed = Math.Min(elapsed + delta, seconds);
        }
    }

    private async Task<float> NextPresentationFrame(
        BurstBatch batch,
        CancellationToken cancelToken)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        cancelToken.ThrowIfCancellationRequested();
        if (!IsRuntimeValid())
        {
            throw new OperationCanceledException(cancelToken);
        }

        bool paused = IsPresentationPaused(_room);
        if (paused != batch.Paused)
        {
            batch.Paused = paused;
            if (batch.VideoPlayer != null && GodotObject.IsInstanceValid(batch.VideoPlayer))
            {
                batch.VideoPlayer.Paused = paused;
            }

            if (paused)
            {
                batch.AudioHandle?.TryPause();
            }
            else
            {
                batch.AudioHandle?.TryResume();
            }
        }

        return paused ? 0f : Math.Min((float)GetProcessDeltaTime(), MaximumFrameDelta);
    }

    private static void StartNinjaSoul(BurstBatch batch)
    {
        try
        {
            AudioEventHandle? audioEvent = FmodStudioEventInstances.TryCreateHandle(
                AudioSource.Event(NinjaSlayerAudio.NinjaSlayerNinjaSoulEvent),
                new AudioPlaybackOptions
                {
                    AutoPlay = false,
                    StartPaused = false,
                    Volume = 1f,
                    Pitch = 1f,
                    Scope = AudioLifecycleScope.Manual
                });
            if (TryStartNinjaSoul(audioEvent, out string playbackState))
            {
                batch.AudioHandle = audioEvent;
                Entry.Logger.Info(
                    $"Boss burst Ninja Soul started: playback_state={playbackState}.");
                return;
            }

            audioEvent?.TryRelease();
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Could not create Ninja Soul playback handle: {exception.Message}");
        }

        try
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerNinjaSoulEvent);
            Entry.Logger.Warn("Boss burst Ninja Soul used the combat-audio fallback.");
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Boss burst Ninja Soul fallback failed: {exception.Message}");
        }
    }

    private static bool TryStartNinjaSoul(
        AudioEventHandle? audioEvent,
        out string playbackState)
    {
        playbackState = "unavailable";
        GodotObject? rawInstance = audioEvent?.RawInstance;
        if (rawInstance == null
            || !GodotObject.IsInstanceValid(rawInstance)
            || !rawInstance.HasMethod("start"))
        {
            return false;
        }

        try
        {
            rawInstance.Call("start");
            if (!rawInstance.HasMethod("get_playback_state"))
            {
                return true;
            }

            int state = rawInstance.Call("get_playback_state").AsInt32();
            playbackState = state.ToString(CultureInfo.InvariantCulture);
            return state != FmodPlaybackStateStopped;
        }
        catch (Exception exception)
        {
            playbackState = $"error:{exception.GetType().Name}";
            Entry.Logger.Warn($"Could not start Ninja Soul FMOD event: {exception.Message}");
            return false;
        }
    }

    private static void PrepareVideo(BurstBatch batch)
    {
        AspectRatioContainer? root = null;
        try
        {
            NGlobalUi? globalUi = NRun.Instance?.GlobalUi;
            VideoStream? stream = ResourceLoader.Load<VideoStream>(
                VideoPath,
                cacheMode: ResourceLoader.CacheMode.Reuse);
            if (globalUi == null
                || !GodotObject.IsInstanceValid(globalUi)
                || stream == null)
            {
                Entry.Logger.Warn($"Boss burst video is unavailable: {VideoPath}");
                return;
            }

            root = new AspectRatioContainer
            {
                Name = "NinjaSlayerBossBurstVideo",
                Ratio = VideoAspectRatio,
                StretchMode = AspectRatioContainer.StretchModeEnum.Cover,
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZAsRelative = false,
                ZIndex = VideoZIndex,
                Visible = false
            };
            var player = new VideoStreamPlayer
            {
                Name = "VideoPlayer",
                Stream = stream,
                Expand = true,
                Volume = 0f,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            globalUi.AddChildSafely(root);
            if (!GodotObject.IsInstanceValid(root) || !root.IsInsideTree())
            {
                root.QueueFreeSafely();
                return;
            }

            root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            root.AddChildSafely(player);
            if (!GodotObject.IsInstanceValid(player) || !player.IsInsideTree())
            {
                throw new InvalidOperationException(
                    "The Boss burst video player could not enter the overlay scene tree.");
            }

            player.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            batch.VideoRoot = root;
            batch.VideoPlayer = player;
        }
        catch (Exception exception)
        {
            root?.QueueFreeSafely();
            batch.VideoRoot = null;
            batch.VideoPlayer = null;
            Entry.Logger.Warn($"Boss burst video setup failed; using the timed fallback: {exception.Message}");
        }
    }

    private void AcquirePresentationLayers()
    {
        _activeVideoLayers++;
        RaiseActorBodies();
        if (_activeVideoLayers > 1)
        {
            return;
        }

        NGlobalUi? globalUi = NRun.Instance?.GlobalUi;
        if (globalUi == null)
        {
            return;
        }

        SetLayer(globalUi.TopBar, TopBarZIndex);
        SetLayer(globalUi.Overlays, 120);
        SetLayer(globalUi.MapScreen, 120);
        SetLayer(globalUi.CapstoneContainer, 120);
        SetLayer(globalUi.SubmenuStack, 125);
        SetLayer(globalUi.AboveTopBarVfxContainer, 130);
        if (NGame.Instance?.HoverTipsContainer is CanvasItem hoverTips)
        {
            SetLayer(hoverTips, 130);
        }

        if (NModalContainer.Instance != null)
        {
            SetLayer(NModalContainer.Instance, 140);
        }
    }

    private void RaiseActorBodies()
    {
        foreach (NCreature creature in _room.CreatureNodes)
        {
            if (GodotObject.IsInstanceValid(creature.Visuals))
            {
                LowerShadows(creature.Visuals);
                Node2D body = creature.Visuals.GetCurrentBody();
                if (GodotObject.IsInstanceValid(body))
                {
                    SetLayer(body, ActorZIndex);
                }
            }
        }
    }

    private void LowerShadows(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is CanvasItem canvas
                && child.Name.ToString().Contains("shadow", StringComparison.OrdinalIgnoreCase))
            {
                SetLayer(canvas, VideoZIndex - 1);
            }

            LowerShadows(child);
        }
    }

    private void ReleasePresentationLayers()
    {
        _activeVideoLayers = Math.Max(0, _activeVideoLayers - 1);
        if (_activeVideoLayers == 0)
        {
            RestorePresentationLayers();
        }
    }

    private void SetLayer(CanvasItem item, int zIndex)
    {
        if (!GodotObject.IsInstanceValid(item))
        {
            return;
        }

        _layerSnapshots.TryAdd(item, new LayerSnapshot(item.ZIndex, item.ZAsRelative));
        item.ZAsRelative = false;
        item.ZIndex = zIndex;
    }

    private void RestorePresentationLayers()
    {
        foreach ((CanvasItem item, LayerSnapshot snapshot) in _layerSnapshots)
        {
            if (!GodotObject.IsInstanceValid(item))
            {
                continue;
            }

            item.ZIndex = snapshot.ZIndex;
            item.ZAsRelative = snapshot.ZAsRelative;
        }

        _layerSnapshots.Clear();
        _activeVideoLayers = 0;
    }

    private static float GetVideoPosition(BurstBatch batch, float fallback)
    {
        if (batch.VideoPlayer == null || !GodotObject.IsInstanceValid(batch.VideoPlayer))
        {
            return fallback;
        }

        float streamPosition = (float)batch.VideoPlayer.StreamPosition;
        float position = batch.VideoPlayer.IsPlaying() || streamPosition > 0f
            ? streamPosition
            : fallback;
        return Mathf.Clamp(position, 0f, BossBurstTimeline.VideoSeconds);
    }

    private static void StopAndReleaseAudio(BurstBatch batch, bool stopPlayback)
    {
        if (batch.AudioHandle == null)
        {
            return;
        }

        if (stopPlayback)
        {
            batch.AudioHandle.TryStop(allowFadeOut: false);
        }

        batch.AudioHandle.TryRelease();
        batch.AudioHandle = null;
    }

    private static void FreeVideo(BurstBatch batch)
    {
        if (batch.VideoPlayer != null && GodotObject.IsInstanceValid(batch.VideoPlayer))
        {
            batch.VideoPlayer.Stop();
        }

        batch.VideoRoot?.QueueFreeSafely();
        batch.VideoRoot = null;
        batch.VideoPlayer = null;
    }

    private bool IsRuntimeValid() =>
        GodotObject.IsInstanceValid(_room)
        && _room.IsInsideTree()
        && ReferenceEquals(NCombatRoom.Instance, _room);

    private sealed class BurstBatch
    {
        public List<BossBurstParticipant> Participants { get; } = [];
        public TaskCompletionSource CueSource { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CombatReleaseSource { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CompletionSource { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AudioEventHandle? AudioHandle { get; set; }
        public AspectRatioContainer? VideoRoot { get; set; }
        public VideoStreamPlayer? VideoPlayer { get; set; }
        public bool CueFired { get; set; }
        public bool Paused { get; set; }
        public bool HasLayerLease { get; set; }
        public int Finalized;
    }

    private readonly record struct LayerSnapshot(int ZIndex, bool ZAsRelative);
}
