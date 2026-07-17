using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Audio;
using System.Runtime.CompilerServices;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class BossGreetingCinematic
{
    private const string VideoPath = "res://NinjaSlayer/videos/ninja_slayer_domo.ogv";
    private const float VideoSeconds = 260f / 24f;
    private const float PlayerZoomMultiplier = 2f;
    private const float PlayerCameraLeadSeconds = 0.2f;
    private const float PlayerCameraFollowDelaySeconds = 0.2f;
    private const float PlayerCameraSettleSeconds = 0.12f;
    private const float BossCameraMoveSeconds = 0.2f;
    private const float CameraReturnSeconds = 0.2f;
    private const float MinimumBossCameraHoldSeconds = 1f;
    private const float BossActionTimeoutSeconds = 8f;
    private const int FmodPlaybackStateStopped = 2;
    private const float DefaultBossZoomMultiplier = 1.5f;
    private const float BossBubbleLifetimeSeconds = 999f;
    private const float PostCombatStartBubbleSeconds = 2f;
    private static readonly Vector2 PlayerFinalCameraOffset = new(0f, -60f);
    private static readonly HashSet<string> ProcessedRoomKeys = [];
    private static string? _deferredBossBgm;
    private static bool _musicBusMuted;

    public static bool ShouldStage(Player player)
    {
        ICombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        return combatState != null
            && player.Character is INinjaSlayerCharacter
            && IsGreetingPending(combatState, out _, out _);
    }

    public static async Task<bool> TryPlay(ICombatState combatState)
    {
        if (!IsGreetingPending(combatState, out string roomKey, out List<Player> ninjaSlayers))
        {
            return false;
        }

        string processRoomKey = GetProcessRoomKey(combatState.RunState, roomKey);
        NCombatRoom? room = NCombatRoom.Instance;
        NRun? run = NRun.Instance;
        if (room == null || run?.GlobalUi == null)
        {
            return false;
        }

        ProcessedRoomKeys.Add(processRoomKey);
        foreach (Player player in ninjaSlayers)
        {
            NinjaSlayerRunData.MarkBossGreetingCompleted(player, roomKey);
        }

        await SaveManager.Instance.SaveRun(null, saveProgress: false);

        var context = new CinematicContext(room, run.GlobalUi, StableHash(roomKey));
        try
        {
            await PlayInternal(combatState, ninjaSlayers, room, run.GlobalUi, context);
        }
        catch (OperationCanceledException)
        {
            // Space skips the complete single-player presentation.
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Boss greeting cinematic failed safely: {ex}");
        }
        finally
        {
            context.Dispose();
            context.RestoreCameraAndScreenShakeTarget();
            if (GodotObject.IsInstanceValid(room) && room.IsInsideTree())
            {
                foreach (Player player in ninjaSlayers)
                {
                    NCreature? node = room.GetCreatureNode(player.Creature);
                    node?.Visuals.Show();
                }
            }
        }

        return true;
    }

    public static bool TryDeferBossBgm(string customMusic)
    {
        ICombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null || !IsGreetingPending(combatState, out _, out _))
        {
            return false;
        }

        _deferredBossBgm = customMusic;
        if (!_musicBusMuted)
        {
            _musicBusMuted = FmodStudioBusAccess.TrySetMute(FmodStudioRouting.MusicBus, true);
            Entry.Logger.Info(_musicBusMuted
                ? "Muted Music Bus for boss greeting."
                : "Could not mute Music Bus for boss greeting.");
        }

        Entry.Logger.Info($"Deferred boss BGM until greeting completes: {customMusic}");
        return true;
    }

    public static void PlayDeferredBossBgm()
    {
        string? customMusic = _deferredBossBgm;
        _deferredBossBgm = null;
        try
        {
            if (string.IsNullOrEmpty(customMusic))
            {
                return;
            }

            Entry.Logger.Info($"Starting deferred boss BGM while Music Bus is muted: {customMusic}");
            NRunMusicController.Instance?.PlayCustomMusic(customMusic);
        }
        finally
        {
            if (_musicBusMuted)
            {
                bool unmuted = FmodStudioBusAccess.TrySetMute(FmodStudioRouting.MusicBus, false);
                Entry.Logger.Info(unmuted
                    ? "Unmuted Music Bus after boss greeting."
                    : "Could not unmute Music Bus after boss greeting.");
                _musicBusMuted = false;
            }
        }
    }

    private static async Task PlayInternal(
        ICombatState combatState,
        List<Player> ninjaSlayers,
        NCombatRoom room,
        NGlobalUi globalUi,
        CinematicContext context)
    {
        Player followedPlayer = SelectFollowedPlayer(combatState, ninjaSlayers, context.RoomKeySeed);
        NCreature? followedNode = room.GetCreatureNode(followedPlayer.Creature);
        if (followedNode == null)
        {
            return;
        }

        CanvasItem followedFocus = (CanvasItem?)NinjaSlayerVisualRig.GetCinematicFocus(followedNode.Visuals)
            ?? (CanvasItem?)followedNode.Visuals.GetNodeOrNull<Node2D>("%CenterPos")
            ?? followedNode.Visuals.Bounds;

        var variants = ninjaSlayers
            .Select((player, index) => (player, variant: AncientEntranceAnimation.FromRoll(StableRoll(context.RoomKeySeed, index))))
            .ToDictionary(pair => pair.player, pair => pair.variant);
        var entranceStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] entranceTasks = ninjaSlayers
            .Select(player => AncientEntranceAnimation.Play(player, variants[player], context, entranceStart.Task))
            .ToArray();
        Task allEntrances = Task.WhenAll(entranceTasks);
        Entry.Logger.Info("Boss greeting entrances staged outside the scene; starting camera lead.");

        float playerZoom = context.BaselineScale.X * PlayerZoomMultiplier;
        try
        {
            await context.TweenCameraToClamped(followedFocus, playerZoom, PlayerCameraLeadSeconds);
        }
        catch
        {
            entranceStart.TrySetCanceled();
            try
            {
                await allEntrances;
            }
            catch (OperationCanceledException)
            {
                // Staged entrances restore their visual state when the cinematic is cancelled.
            }

            throw;
        }

        context.BeginDelayedCameraFollow(followedFocus);
        Entry.Logger.Info("Boss greeting camera lead completed; releasing entrance start gate.");
        entranceStart.TrySetResult();
        float elapsed = 0f;
        while (!allEntrances.IsCompleted)
        {
            float delta = await context.NextFrame();
            elapsed += delta;
            context.FrameCameraOnDelayed(
                followedFocus,
                playerZoom,
                elapsed,
                PlayerCameraFollowDelaySeconds);
        }

        await allEntrances;
        await context.TweenCameraToClamped(
            followedFocus,
            playerZoom,
            PlayerCameraSettleSeconds,
            PlayerFinalCameraOffset);

        float entranceAudioDuration = variants.Values
            .Max(AncientEntranceAnimation.GetCinematicAudioDuration);
        float entranceVisualDuration = variants.Values
            .Max(AncientEntranceAnimation.GetDuration);
        float remainingAudioSeconds = entranceAudioDuration - entranceVisualDuration - PlayerCameraSettleSeconds;
        if (remainingAudioSeconds > 0f)
        {
            Entry.Logger.Info($"Waiting {remainingAudioSeconds:0.###}s for entrance SFX before DOMO video.");
            await context.WaitSeconds(remainingAudioSeconds);
        }

        await PlayGreetingVideo(globalUi, context);

        Creature? boss = SelectBoss(combatState);
        if (boss == null)
        {
            return;
        }

        NCreature? bossNode = room.GetCreatureNode(boss);
        CanvasItem? bossFocus = GetBossFocus(room, boss, bossNode);
        if (bossNode == null || bossFocus == null)
        {
            return;
        }

        NSpeechBubbleVfx? bubble = null;
        float targetZoom = context.BaselineScale.X * DefaultBossZoomMultiplier;
        Vector2? targetCameraPosition = null;
        bool showBubble = boss.Monster is not LagavulinMatriarch;
        bool anchorBubbleToBoss = IsKaiserBoss(boss);
        if (showBubble)
        {
            string title = combatState.Encounter?.Title.GetFormattedText() ?? boss.Monster?.Id.Entry ?? "Boss";
            string dialogue = $"Domo, Ninja Slayer=san, {title} desu.";
            bubble = anchorBubbleToBoss
                ? NSpeechBubbleVfx.Create(
                    dialogue,
                    DialogueSide.Right,
                    GetGlobalCenter(bossFocus),
                    BossBubbleLifetimeSeconds)
                : NSpeechBubbleVfx.Create(dialogue, boss, BossBubbleLifetimeSeconds);
            if (bubble != null)
            {
                Sprite2D? bubbleSprite = bubble.GetNodeOrNull<Sprite2D>("%Bubble");
                Vector2 finalBubbleScale = bubbleSprite?.Scale ?? Vector2.One * 0.75f;
                bubble.Visible = false;
                bubble.ProcessMode = Node.ProcessModeEnum.Disabled;
                room.SceneContainer.AddChildSafely(bubble);
                context.TrackNode(bubble);
                await context.NextFrame();

                if (context.TryFrameBossAndBubble(
                    bossNode.Visuals.Bounds,
                    bossFocus,
                    bubble,
                    finalBubbleScale,
                    out Vector2 measuredPosition,
                    out float measuredScale))
                {
                    targetCameraPosition = measuredPosition;
                    targetZoom = measuredScale;
                }
            }
        }

        if (targetCameraPosition is { } cameraPosition)
        {
            await context.TweenCameraTo(cameraPosition, targetZoom, BossCameraMoveSeconds);
        }
        else
        {
            await context.TweenCameraTo(bossFocus, targetZoom, BossCameraMoveSeconds);
        }

        if (bubble != null)
        {
            bubble.ProcessMode = Node.ProcessModeEnum.Inherit;
            bubble.Visible = true;
        }

        AudioEventHandle? bossAudio = await PlayBossAction(boss, bossNode, context);
        await context.TweenCameraToBaseline(CameraReturnSeconds);
        context.HandoffAudioForNaturalRelease(bossAudio, BossActionTimeoutSeconds);
        if (bubble != null && GodotObject.IsInstanceValid(bubble))
        {
            context.ReleaseNode(bubble);
            _ = TaskHelper.RunSafely(FadeBossBubbleAfterCombatStart(bubble));
        }
    }

    private static async Task FadeBossBubbleAfterCombatStart(NSpeechBubbleVfx bubble)
    {
        SceneTreeTimer timer = ((SceneTree)Engine.GetMainLoop()).CreateTimer(
            PostCombatStartBubbleSeconds,
            processAlways: false);
        await timer.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
        if (GodotObject.IsInstanceValid(bubble) && bubble.IsInsideTree())
        {
            await bubble.AnimOut();
        }
    }

    private static async Task PlayGreetingVideo(NGlobalUi globalUi, CinematicContext context)
    {
        var player = new VideoStreamPlayer
        {
            Name = "NinjaSlayerBossGreetingVideo",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Expand = true,
            ZIndex = 100,
            ZAsRelative = false
        };
        VideoStream? stream = ResourceLoader.Load<VideoStream>(VideoPath, cacheMode: ResourceLoader.CacheMode.Reuse);
        if (stream == null)
        {
            Entry.Logger.Warn($"Boss greeting video is missing: {VideoPath}");
            return;
        }

        player.Stream = stream;
        globalUi.AddChildSafely(player);
        player.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        context.AttachVideo(player);
        context.PlaySfx(NinjaSlayerAudio.NinjaSlayerDomoEvent);
        player.Play();
        await context.WaitWhile(player.IsPlaying, VideoSeconds + 1f);
        player.Stop();
        player.QueueFreeSafely();
        context.AttachVideo(null);
        await context.NextFrame();
    }

    private static async Task<AudioEventHandle?> PlayBossAction(
        Creature boss,
        NCreature bossNode,
        CinematicContext context)
    {
        BossGreetingActionSpec action = GetBossGreetingAction(boss);
        AudioEventHandle? audioEvent = action.SfxPath == null
            ? null
            : context.PlaySfxWithHandle(action.SfxPath);
        if (action.AnimationTrigger != null)
        {
            bossNode.SetAnimationTrigger(action.AnimationTrigger);
        }

        float elapsedBeforeCompletionWait = 0f;
        if (action.VfxPath != null)
        {
            await context.WaitSeconds(action.VfxDelay);
            elapsedBeforeCompletionWait = action.VfxDelay;
            MegaCrit.Sts2.Core.Commands.VfxCmd.PlayOnCreatureCenter(boss, action.VfxPath);
        }

        float finishAt = Math.Max(action.MinimumDuration, MinimumBossCameraHoldSeconds);
        await context.WaitSeconds(Math.Max(0f, finishAt - elapsedBeforeCompletionWait));

        string bossName = boss.Monster?.Id.Entry ?? boss.GetType().Name;
        Entry.Logger.Info(
            $"Boss greeting action completed its calibrated duration: boss={bossName}, waited={Math.Max(finishAt, elapsedBeforeCompletionWait):0.###}s.");
        return audioEvent;
    }

    private static BossGreetingActionSpec GetBossGreetingAction(Creature boss)
    {
        if (IsKaiserBoss(boss))
        {
            return new(null, null, 1.75f);
        }

        return boss.Monster switch
        {
            CeremonialBeast => new(
                "Cast",
                "event:/sfx/enemy/enemy_attacks/ceremonial_beast/ceremonial_beast_shrill",
                1.05f,
                "vfx/vfx_scream",
                0.3f),
            KinPriest => new(
                "Rally",
                "event:/sfx/enemy/enemy_attacks/the_kin_priest/the_kin_priest_rally",
                1f),
            Vantom => new(
                "BUFF",
                "event:/sfx/enemy/enemy_attacks/vantom/vantom_buff",
                0.6f),
            LagavulinMatriarch => new("Sleep", null, 1f),
            WaterfallGiant => new(
                "Heal",
                "event:/sfx/enemy/enemy_attacks/waterfall_giant/waterfall_giant_eruption",
                0.8f),
            SoulFysh => new(
                "Beckon",
                "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_beckon",
                0.6f,
                "vfx/vfx_spooky_scream",
                0.3f),
            TheInsatiable => new(
                "LiquifySand",
                "event:/sfx/enemy/enemy_attacks/the_insatiable/the_insatiable_liquify_ground",
                1.25f,
                "vfx/vfx_scream",
                0.5f),
            KnowledgeDemon => new("MindRotTrigger", null, 1f),
            Queen => new(
                "Cast",
                "event:/sfx/enemy/enemy_attacks/queen/queen_cast",
                0.5f),
            TestSubject => new(
                "BiteTrigger",
                "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite",
                0.25f),
            Aeonglass => new("Cast", null, 0.4f),
            _ => new(null, null, 0.8f)
        };
    }

    private sealed record BossGreetingActionSpec(
        string? AnimationTrigger,
        string? SfxPath,
        float MinimumDuration,
        string? VfxPath = null,
        float VfxDelay = 0f);

    private static Creature? SelectBoss(ICombatState state)
    {
        if (state.Encounter is KaiserCrabBoss)
        {
            return state.Enemies.FirstOrDefault();
        }

        Creature? kinPriest = state.Enemies.FirstOrDefault(creature => creature.Monster is KinPriest);
        return kinPriest ?? state.Enemies.FirstOrDefault(creature => creature.Monster is
            CeremonialBeast or Vantom or LagavulinMatriarch or WaterfallGiant or SoulFysh
            or TheInsatiable or KnowledgeDemon or Queen or TestSubject or Aeonglass)
            ?? state.Enemies.FirstOrDefault();
    }

    private static CanvasItem? GetBossFocus(NCombatRoom room, Creature boss, NCreature? bossNode)
    {
        if (IsKaiserBoss(boss))
        {
            Node? spittleSlot = room.FindChild("SpittleSlot", recursive: true, owned: false);
            if (spittleSlot is Node2D node2D)
            {
                return node2D;
            }
        }

        return bossNode?.Visuals.Bounds;
    }

    private static bool IsKaiserBoss(Creature boss) => boss.CombatState?.Encounter is KaiserCrabBoss;

    private static Vector2 GetGlobalCenter(CanvasItem target) => target switch
    {
        Control control => control.GetGlobalRect().GetCenter(),
        Node2D node2D => node2D.GlobalPosition,
        _ => Vector2.Zero
    };

    private static Player SelectFollowedPlayer(ICombatState state, List<Player> ninjaSlayers, uint seed)
    {
        Player? local = LocalContext.GetMe(state);
        if (local?.Character is INinjaSlayerCharacter)
        {
            return local;
        }

        int index = (int)(seed % (uint)ninjaSlayers.Count);
        return ninjaSlayers[index];
    }

    private static bool TryGetRoomKey(ICombatState combatState, out string roomKey)
    {
        IRunState runState = combatState.RunState;
        if (runState.CurrentRoom is not CombatRoom room || room.RoomType != RoomType.Boss || combatState.Encounter == null)
        {
            roomKey = string.Empty;
            return false;
        }

        string coord = runState.CurrentMapCoord is { } mapCoord ? $"{mapCoord.col}:{mapCoord.row}" : "none";
        roomKey = $"{runState.Rng.Seed}:{runState.CurrentActIndex}:{coord}:{combatState.Encounter.Id.Entry}";
        return true;
    }

    private static bool IsGreetingPending(
        ICombatState combatState,
        out string roomKey,
        out List<Player> ninjaSlayers)
    {
        ninjaSlayers = combatState.Players
            .Where(player => player.Character is INinjaSlayerCharacter)
            .ToList();
        if (ninjaSlayers.Count == 0 || !TryGetRoomKey(combatState, out roomKey))
        {
            roomKey = string.Empty;
            return false;
        }

        string processRoomKey = GetProcessRoomKey(combatState.RunState, roomKey);
        if (ProcessedRoomKeys.Contains(processRoomKey))
        {
            return false;
        }

        bool replayAfterLoad = RunManager.Instance.NetService.Type == NetGameType.Singleplayer
            && LocalContext.GetMe(combatState)?.Character is NinjaSlayerDebugCharacter;
        string completedRoomKey = roomKey;
        return replayAfterLoad
            || !ninjaSlayers.All(player => NinjaSlayerRunData.HasCompletedBossGreeting(player, completedRoomKey));
    }

    private static string GetProcessRoomKey(IRunState runState, string roomKey) =>
        $"{RuntimeHelpers.GetHashCode(runState)}:{roomKey}";

    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return hash;
    }

    private static float StableRoll(uint seed, int index)
    {
        uint value = seed ^ ((uint)index + 1u) * 0x9E3779B9u;
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) / 16777216f;
    }

    private static float EaseOut(float value) => 1f - (1f - value) * (1f - value);

    private sealed class CinematicContext : ICinematicAnimationContext, IDisposable
    {
        private readonly NCombatRoom _room;
        private readonly Control _sceneContainer;
        private readonly NGlobalUi _globalUi;
        private readonly bool _singlePlayer;
        private readonly List<AudioEventHandle> _audioEvents = [];
        private readonly List<CanvasItem> _ownedVisuals = [];
        private readonly List<CameraFollowSample> _cameraFollowSamples = [];
        private readonly Dictionary<CanvasItem, LayerSnapshot> _layerSnapshots = [];
        private readonly CancellationTokenSource _cancellation = new();
        private VideoStreamPlayer? _video;
        private bool _paused;
        private bool _disposed;
        private bool _screenShakeTargetSuspended;
        private bool _spaceWasDown;
        private Node.ProcessModeEnum _roomProcessMode;
        private ulong _lastFrameMsec;
        private ulong _lastDeltaFrame = ulong.MaxValue;
        private float _cachedFrameDelta;
        private Vector2 _cameraBasePosition;
        private float _cameraScale;
        private Vector2 _cameraShakeOffset;
        private ScreenPunchInstance? _cameraShake;

        public CinematicContext(NCombatRoom room, NGlobalUi globalUi, uint roomKeySeed)
        {
            _room = room;
            _sceneContainer = room.SceneContainer;
            _globalUi = globalUi;
            _singlePlayer = RunManager.Instance.IsSingleplayerOrFakeMultiplayer;
            BaselinePosition = _sceneContainer.Position;
            BaselineScale = _sceneContainer.Scale;
            _cameraBasePosition = BaselinePosition;
            _cameraScale = BaselineScale.X;
            ViewportSize = room.GetViewportRect().Size;
            RoomKeySeed = roomKeySeed;
            _roomProcessMode = room.ProcessMode;
            _lastFrameMsec = Time.GetTicksMsec();
            RaiseTopBarLayers();

            NGame? game = NGame.Instance;
            if (game != null && ReferenceEquals(game.ScreenshakeTarget, _sceneContainer))
            {
                _screenShakeTargetSuspended = true;
                game.ClearScreenShakeTarget();
                Entry.Logger.Info("Suspended combat screen shake target for boss greeting camera control.");
            }
        }

        public CancellationToken CancellationToken => _cancellation.Token;
        public Vector2 BaselinePosition { get; }
        public Vector2 BaselineScale { get; }
        public Vector2 ViewportSize { get; }
        public uint RoomKeySeed { get; }

        public async Task AwaitTween(Node owner, Tween tween)
        {
            while (tween.IsValid() && tween.IsRunning())
            {
                try
                {
                    await NextFrame();
                }
                catch
                {
                    tween.Kill();
                    throw;
                }
            }
        }

        public void PlaySfx(string eventPath) => PlaySfxWithHandle(eventPath);

        public void PlayScreenShake(ShakeStrength strength, ShakeDuration duration, float degrees = -1f)
        {
            float multiplier = NScreenshakePaginator.GetShakeMultiplier(
                SaveManager.Instance.PrefsSave.ScreenShakeOptionIndex);
            float scaledStrength = GetShakeStrength(strength) * multiplier;
            if (scaledStrength <= 0f)
            {
                _cameraShake = null;
                _cameraShakeOffset = Vector2.Zero;
                ApplyCameraTransform();
                return;
            }

            float angle = degrees < 0f
                ? MegaCrit.Sts2.Core.Random.Rng.Chaotic.NextFloat(360f)
                : degrees;
            _cameraShake = new ScreenPunchInstance(scaledStrength, GetShakeDuration(duration), angle);
            _cameraShakeOffset = Vector2.Zero;
            ApplyCameraTransform();
        }

        public AudioEventHandle? PlaySfxWithHandle(string eventPath)
        {
            try
            {
                AudioEventHandle? audioEvent = FmodStudioEventInstances.TryCreateHandle(
                    AudioSource.Event(eventPath),
                    new AudioPlaybackOptions
                    {
                        AutoPlay = false,
                        StartPaused = false,
                        Volume = 1f,
                        Pitch = 1f,
                        Scope = AudioLifecycleScope.Manual
                    });
                if (audioEvent == null)
                {
                    Entry.Logger.Warn($"Could not create cinematic SFX '{eventPath}'.");
                    return null;
                }

                GodotObject? rawInstance = audioEvent.RawInstance;
                if (rawInstance == null || !GodotObject.IsInstanceValid(rawInstance) || !rawInstance.HasMethod("start"))
                {
                    audioEvent.TryRelease();
                    Entry.Logger.Warn($"Could not start cinematic SFX '{eventPath}'.");
                    return null;
                }

                rawInstance.Call("start");
                bool pauseStateSet = _paused ? audioEvent.TryPause() : audioEvent.TryResume();
                if (!pauseStateSet)
                {
                    Entry.Logger.Warn($"Could not set initial pause state for cinematic SFX '{eventPath}'.");
                }

                _audioEvents.Add(audioEvent);
                Entry.Logger.Info($"Cinematic SFX started: {eventPath}");
                return audioEvent;
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"Could not play cinematic SFX '{eventPath}': {ex.Message}");
                return null;
            }
        }

        public async Task<float> NextFrame()
        {
            await _room.ToSignal(_room.GetTree(), SceneTree.SignalName.ProcessFrame);
            UpdatePauseAndSkip();
            _cancellation.Token.ThrowIfCancellationRequested();

            ulong processFrame = Engine.GetProcessFrames();
            if (processFrame != _lastDeltaFrame)
            {
                ulong now = Time.GetTicksMsec();
                _cachedFrameDelta = _paused ? 0f : Math.Min((now - _lastFrameMsec) / 1000f, 0.05f);
                _lastFrameMsec = now;
                _lastDeltaFrame = processFrame;
                UpdateCameraShake(_cachedFrameDelta);
            }

            return _cachedFrameDelta;
        }

        public async Task WaitSeconds(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += await NextFrame();
            }
        }

        public async Task WaitWhile(Func<bool> predicate, float timeout)
        {
            float elapsed = 0f;
            while (predicate() && elapsed < timeout)
            {
                elapsed += await NextFrame();
            }
        }

        public void FrameCameraOn(CanvasItem target, float scale)
        {
            Vector2 localTarget = GetLocalCenter(target);
            FrameCameraOnLocalPoint(localTarget, scale);
        }

        public void FrameCameraOnClamped(CanvasItem target, float scale)
        {
            Vector2 localTarget = ClampCameraTargetToScene(GetLocalCenter(target), scale);
            FrameCameraOnLocalPoint(localTarget, scale);
        }

        public void BeginDelayedCameraFollow(CanvasItem target)
        {
            _cameraFollowSamples.Clear();
            _cameraFollowSamples.Add(new CameraFollowSample(0f, GetLocalCenter(target)));
        }

        public void FrameCameraOnDelayed(
            CanvasItem target,
            float scale,
            float elapsed,
            float delay)
        {
            Vector2 currentTarget = GetLocalCenter(target);
            AddCameraFollowSample(elapsed, currentTarget);

            float sampleTime = Math.Max(0f, elapsed - delay);
            while (_cameraFollowSamples.Count > 2 && _cameraFollowSamples[1].Time <= sampleTime)
            {
                _cameraFollowSamples.RemoveAt(0);
            }

            Vector2 delayedTarget = SampleCameraFollowPosition(sampleTime);
            FrameCameraOnLocalPoint(ClampCameraTargetToScene(delayedTarget, scale), scale);
        }

        public async Task TweenCameraTo(CanvasItem target, float targetScale, float duration)
        {
            Vector2 startPosition = _cameraBasePosition;
            float startScale = _cameraScale;
            Vector2 localTarget = GetLocalCenter(target);
            Vector2 targetPosition = GetCameraPosition(localTarget, targetScale, ViewportSize * 0.5f);
            await TweenCamera(startPosition, startScale, targetPosition, targetScale, duration);
        }

        public async Task TweenCameraToClamped(
            CanvasItem target,
            float targetScale,
            float duration,
            Vector2 localOffset = default)
        {
            float startScale = _cameraScale;
            Vector2 startCenter = GetCameraCenter(_cameraBasePosition, startScale, ViewportSize * 0.5f);
            Vector2 targetCenter = GetLocalCenter(target) + localOffset;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += await NextFrame();
                float progress = EaseOut(Mathf.Clamp(elapsed / duration, 0f, 1f));
                float scale = Mathf.Lerp(startScale, targetScale, progress);
                Vector2 center = startCenter.Lerp(targetCenter, progress);
                FrameCameraOnLocalPoint(ClampCameraTargetToScene(center, scale), scale);
            }
        }

        public Task TweenCameraTo(Vector2 targetPosition, float targetScale, float duration) =>
            TweenCamera(_cameraBasePosition, _cameraScale, targetPosition, targetScale, duration);

        public bool TryFrameBossAndBubble(
            Control bossBounds,
            CanvasItem bossFocus,
            NSpeechBubbleVfx bubble,
            Vector2 finalBubbleScale,
            out Vector2 targetPosition,
            out float targetScale)
        {
            targetPosition = Vector2.Zero;
            targetScale = BaselineScale.X * DefaultBossZoomMultiplier;

            Sprite2D? bubbleSprite = bubble.GetNodeOrNull<Sprite2D>("%Bubble");
            Sprite2D? shadow = bubble.GetNodeOrNull<Sprite2D>("%Shadow");
            Control? text = bubble.GetNodeOrNull<Control>("%Text");
            if (bubbleSprite == null || shadow == null || text == null)
            {
                Entry.Logger.Warn("Could not measure the original boss speech bubble; using fallback camera zoom.");
                return false;
            }

            Vector2 animatedScale = bubbleSprite.Scale;
            float animatedRotation = bubble.Rotation;
            try
            {
                bubbleSprite.Scale = finalBubbleScale;
                bubble.Rotation = 0f;

                Rect2 composition = GetSceneLocalRect(bossBounds);
                composition = composition.Merge(GetSceneLocalPointRect(bossFocus));
                composition = composition.Merge(GetSceneLocalRect(bubbleSprite));
                composition = composition.Merge(GetSceneLocalRect(shadow));
                composition = composition.Merge(GetSceneLocalRect(text));
                if (composition.Size.X <= 1f || composition.Size.Y <= 1f)
                {
                    Entry.Logger.Warn("Measured boss greeting bounds were invalid; using fallback camera zoom.");
                    return false;
                }

                var safeViewport = new Rect2(
                    ViewportSize * new Vector2(0.07f, 0.09f),
                    ViewportSize * new Vector2(0.86f, 0.80f));
                float fitScale = Math.Min(
                    safeViewport.Size.X / composition.Size.X,
                    safeViewport.Size.Y / composition.Size.Y);
                targetScale = Math.Min(fitScale, BaselineScale.X * DefaultBossZoomMultiplier);
                if (!float.IsFinite(targetScale) || targetScale <= 0f)
                {
                    return false;
                }

                targetPosition = GetCameraPosition(
                    composition.GetCenter(),
                    targetScale,
                    safeViewport.GetCenter());
                Entry.Logger.Info(
                    $"Boss greeting camera bounds={composition.Position}/{composition.Size}, scale={targetScale:0.###}.");
                return true;
            }
            finally
            {
                bubbleSprite.Scale = animatedScale;
                bubble.Rotation = animatedRotation;
            }
        }

        public Task TweenCameraToBaseline(float duration) =>
            TweenCamera(_cameraBasePosition, _cameraScale, BaselinePosition, BaselineScale.X, duration);

        public void AttachVideo(VideoStreamPlayer? video) => _video = video;

        public void TrackNode(CanvasItem node) => _ownedVisuals.Add(node);

        public void ReleaseNode(CanvasItem node) => _ownedVisuals.Remove(node);

        public void HandoffAudioForNaturalRelease(AudioEventHandle? audioEvent, float timeout)
        {
            if (audioEvent == null || !_audioEvents.Remove(audioEvent))
            {
                return;
            }

            _ = TaskHelper.RunSafely(ReleaseAudioWhenStopped(audioEvent, timeout));
        }

        public void RestoreCameraAndScreenShakeTarget()
        {
            bool shouldRestoreScreenShake = _screenShakeTargetSuspended;
            _screenShakeTargetSuspended = false;

            if (!GodotObject.IsInstanceValid(_room)
                || !GodotObject.IsInstanceValid(_sceneContainer)
                || !_room.IsInsideTree()
                || !ReferenceEquals(NCombatRoom.Instance, _room))
            {
                if (shouldRestoreScreenShake)
                {
                    Entry.Logger.Warn("Skipped restoring boss greeting screen shake target because the combat room changed.");
                }

                return;
            }

            _cameraShake = null;
            _cameraShakeOffset = Vector2.Zero;
            _cameraBasePosition = BaselinePosition;
            _cameraScale = BaselineScale.X;
            ApplyCameraTransform();
            if (!shouldRestoreScreenShake)
            {
                return;
            }

            NGame? game = NGame.Instance;
            if (game == null)
            {
                Entry.Logger.Warn("Skipped restoring boss greeting screen shake target because the game instance is unavailable.");
                return;
            }

            Control? currentTarget = game.ScreenshakeTarget;
            if (currentTarget != null && !ReferenceEquals(currentTarget, _sceneContainer))
            {
                Entry.Logger.Warn("Skipped restoring boss greeting screen shake target because another target took ownership.");
                return;
            }

            if (!ReferenceEquals(currentTarget, _sceneContainer))
            {
                game.SetScreenShakeTarget(_sceneContainer);
            }

            Entry.Logger.Info("Restored combat screen shake target after boss greeting camera reset.");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _video?.Stop();
            if (_video != null && GodotObject.IsInstanceValid(_video))
            {
                _video.QueueFreeSafely();
            }

            foreach (AudioEventHandle audioEvent in _audioEvents)
            {
                audioEvent.TryStop(allowFadeOut: false);
                audioEvent.TryRelease();
            }

            _audioEvents.Clear();
            foreach (CanvasItem visual in _ownedVisuals.Where(GodotObject.IsInstanceValid))
            {
                visual.Visible = false;
                visual.QueueFreeSafely();
            }
            _ownedVisuals.Clear();
            _cameraShake = null;
            _cameraShakeOffset = Vector2.Zero;
            _room.ProcessMode = _roomProcessMode;
            RestoreTopBarLayers();
            _cancellation.Dispose();
        }

        private async Task TweenCamera(Vector2 startPosition, float startScale, Vector2 targetPosition, float targetScale, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += await NextFrame();
                float progress = EaseOut(Mathf.Clamp(elapsed / duration, 0f, 1f));
                SetCameraTransform(
                    startPosition.Lerp(targetPosition, progress),
                    Mathf.Lerp(startScale, targetScale, progress));
            }
        }

        private Vector2 GetLocalCenter(CanvasItem target)
        {
            Vector2 globalCenter = target switch
            {
                Control control => control.GetGlobalRect().GetCenter(),
                Node2D node2D => node2D.GlobalPosition,
                _ => Vector2.Zero
            };
            return _room.SceneContainer.GetGlobalTransformWithCanvas().AffineInverse() * globalCenter;
        }

        private Vector2 GetCameraPosition(Vector2 localTarget, float scale, Vector2 screenTarget)
        {
            Vector2 pivot = _room.SceneContainer.PivotOffset;
            return screenTarget - pivot - (localTarget - pivot) * scale;
        }

        private Vector2 GetCameraCenter(Vector2 cameraPosition, float scale, Vector2 screenTarget)
        {
            Vector2 pivot = _sceneContainer.PivotOffset;
            return pivot + (screenTarget - pivot - cameraPosition) / scale;
        }

        private Vector2 ClampCameraTargetToScene(Vector2 localTarget, float scale)
        {
            if (scale <= 0f)
            {
                return _sceneContainer.Size * 0.5f;
            }

            Vector2 halfViewport = ViewportSize / (2f * scale);
            Vector2 minimum = halfViewport;
            Vector2 maximum = _sceneContainer.Size - halfViewport;
            return new Vector2(
                minimum.X <= maximum.X
                    ? Mathf.Clamp(localTarget.X, minimum.X, maximum.X)
                    : _sceneContainer.Size.X * 0.5f,
                minimum.Y <= maximum.Y
                    ? Mathf.Clamp(localTarget.Y, minimum.Y, maximum.Y)
                    : _sceneContainer.Size.Y * 0.5f);
        }

        private void FrameCameraOnLocalPoint(Vector2 localTarget, float scale)
        {
            SetCameraTransform(GetCameraPosition(localTarget, scale, ViewportSize * 0.5f), scale);
        }

        private void SetCameraTransform(Vector2 position, float scale)
        {
            _cameraBasePosition = position;
            _cameraScale = scale;
            ApplyCameraTransform();
        }

        private void ApplyCameraTransform()
        {
            if (!GodotObject.IsInstanceValid(_sceneContainer))
            {
                return;
            }

            _sceneContainer.Scale = Vector2.One * _cameraScale;
            _sceneContainer.Position = _cameraBasePosition + _cameraShakeOffset;
        }

        private void UpdateCameraShake(float delta)
        {
            if (_cameraShake == null || delta <= 0f)
            {
                return;
            }

            _cameraShakeOffset = _cameraShake.Update(delta);
            if (_cameraShake.IsDone)
            {
                _cameraShake = null;
                _cameraShakeOffset = Vector2.Zero;
            }

            ApplyCameraTransform();
        }

        private static float GetShakeStrength(ShakeStrength strength) => strength switch
        {
            ShakeStrength.VeryWeak => 2f,
            ShakeStrength.Weak => 5f,
            ShakeStrength.Medium => 20f,
            ShakeStrength.Strong => 40f,
            ShakeStrength.TooMuch => 80f,
            _ => 0f
        };

        private static double GetShakeDuration(ShakeDuration duration) => duration switch
        {
            ShakeDuration.Short => 0.3,
            ShakeDuration.Normal => 0.8,
            ShakeDuration.Long => 1.2,
            ShakeDuration.Forever => 999999999.0,
            _ => 0.0
        };

        private void AddCameraFollowSample(float elapsed, Vector2 position)
        {
            if (_cameraFollowSamples.Count == 0)
            {
                _cameraFollowSamples.Add(new CameraFollowSample(elapsed, position));
                return;
            }

            CameraFollowSample last = _cameraFollowSamples[^1];
            if (elapsed <= last.Time + 0.0001f)
            {
                _cameraFollowSamples[^1] = new CameraFollowSample(last.Time, position);
                return;
            }

            _cameraFollowSamples.Add(new CameraFollowSample(elapsed, position));
        }

        private Vector2 SampleCameraFollowPosition(float sampleTime)
        {
            if (_cameraFollowSamples.Count == 0)
            {
                return Vector2.Zero;
            }

            CameraFollowSample first = _cameraFollowSamples[0];
            if (_cameraFollowSamples.Count == 1 || sampleTime <= first.Time)
            {
                return first.Position;
            }

            CameraFollowSample second = _cameraFollowSamples[1];
            float duration = second.Time - first.Time;
            if (duration <= 0.0001f)
            {
                return second.Position;
            }

            float progress = Mathf.Clamp((sampleTime - first.Time) / duration, 0f, 1f);
            return first.Position.Lerp(second.Position, progress);
        }

        private static bool TryGetAudioPlaybackState(AudioEventHandle? audioEvent, out int? playbackState)
        {
            playbackState = null;
            GodotObject? rawInstance = audioEvent?.RawInstance;
            if (rawInstance == null
                || !GodotObject.IsInstanceValid(rawInstance)
                || !rawInstance.HasMethod("get_playback_state"))
            {
                return false;
            }

            try
            {
                playbackState = rawInstance.Call("get_playback_state").AsInt32();
                return playbackState != FmodPlaybackStateStopped;
            }
            catch
            {
                playbackState = null;
                return false;
            }
        }

        private static async Task ReleaseAudioWhenStopped(AudioEventHandle audioEvent, float timeout)
        {
            ulong startedAt = Time.GetTicksMsec();
            SceneTree tree = (SceneTree)Engine.GetMainLoop();
            while ((Time.GetTicksMsec() - startedAt) / 1000f < timeout)
            {
                if (!TryGetAudioPlaybackState(audioEvent, out _))
                {
                    audioEvent.TryRelease();
                    return;
                }

                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            audioEvent.TryStop(allowFadeOut: false);
            audioEvent.TryRelease();
            Entry.Logger.Warn($"Boss greeting audio exceeded its {timeout:0.###}s handoff timeout and was stopped.");
        }

        private Rect2 GetSceneLocalRect(Control control) =>
            TransformRectToSceneLocal(new Rect2(Vector2.Zero, control.Size), control.GetGlobalTransformWithCanvas());

        private Rect2 GetSceneLocalRect(Sprite2D sprite) =>
            TransformRectToSceneLocal(sprite.GetRect(), sprite.GetGlobalTransformWithCanvas());

        private Rect2 GetSceneLocalPointRect(CanvasItem item)
        {
            Vector2 center = GetLocalCenter(item);
            return new Rect2(center - Vector2.One, Vector2.One * 2f);
        }

        private Rect2 TransformRectToSceneLocal(Rect2 localRect, Transform2D globalTransform)
        {
            Transform2D toScene = _room.SceneContainer.GetGlobalTransformWithCanvas().AffineInverse();
            Vector2 topLeft = toScene * (globalTransform * localRect.Position);
            Vector2 topRight = toScene * (globalTransform * new Vector2(localRect.End.X, localRect.Position.Y));
            Vector2 bottomLeft = toScene * (globalTransform * new Vector2(localRect.Position.X, localRect.End.Y));
            Vector2 bottomRight = toScene * (globalTransform * localRect.End);
            Vector2 minimum = topLeft.Min(topRight).Min(bottomLeft).Min(bottomRight);
            Vector2 maximum = topLeft.Max(topRight).Max(bottomLeft).Max(bottomRight);
            return new Rect2(minimum, maximum - minimum);
        }

        private void UpdatePauseAndSkip()
        {
            bool overlayOpen = _globalUi.Overlays.ScreenCount > 0
                || _globalUi.CapstoneContainer.InUse
                || _globalUi.MapScreen.IsOpen
                || NModalContainer.Instance?.OpenModal != null;
            bool shouldPause = _singlePlayer && overlayOpen;
            if (shouldPause != _paused)
            {
                _paused = shouldPause;
                _room.ProcessMode = _paused ? Node.ProcessModeEnum.Disabled : _roomProcessMode;
                if (_video != null && GodotObject.IsInstanceValid(_video))
                {
                    _video.Paused = _paused;
                }

                foreach (AudioEventHandle audioEvent in _audioEvents)
                {
                    if (_paused)
                    {
                        audioEvent.TryPause();
                    }
                    else
                    {
                        audioEvent.TryResume();
                    }
                }
            }

            bool spaceDown = Input.IsKeyPressed(Key.Space);
            if (_singlePlayer && !_paused && spaceDown && !_spaceWasDown)
            {
                _cancellation.Cancel();
            }

            _spaceWasDown = spaceDown;
        }

        private void RaiseTopBarLayers()
        {
            SetLayer(_globalUi.TopBar, 110);
            SetLayer(_globalUi.Overlays, 120);
            SetLayer(_globalUi.MapScreen, 120);
            SetLayer(_globalUi.CapstoneContainer, 120);
            SetLayer(_globalUi.SubmenuStack, 125);
            SetLayer(_globalUi.AboveTopBarVfxContainer, 130);
            if (NGame.Instance?.HoverTipsContainer is CanvasItem hoverTips)
            {
                SetLayer(hoverTips, 130);
            }
            if (NModalContainer.Instance != null)
            {
                SetLayer(NModalContainer.Instance, 140);
            }
        }

        private void SetLayer(CanvasItem item, int zIndex)
        {
            _layerSnapshots[item] = new LayerSnapshot(item.ZIndex, item.ZAsRelative);
            item.ZAsRelative = false;
            item.ZIndex = zIndex;
        }

        private void RestoreTopBarLayers()
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
        }

        private readonly record struct CameraFollowSample(float Time, Vector2 Position);
        private readonly record struct LayerSnapshot(int ZIndex, bool ZAsRelative);
    }
}
