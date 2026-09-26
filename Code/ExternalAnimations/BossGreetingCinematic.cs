using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
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
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Audio;
using STS2RitsuLib;
using System.Runtime.CompilerServices;
using static NinjaSlayer.Code.ExternalAnimations.BossGreetingTimeline;

namespace NinjaSlayer.Code.ExternalAnimations;

public static class BossGreetingCinematic
{
    private const string VideoPath = "res://NinjaSlayer/videos/ninja_slayer_domo.ogv";
    private const int FmodPlaybackStateStopped = 2;
    private const float DefaultBossZoomMultiplier = 1.5f;
    private static readonly ConditionalWeakTable<IRunState, ProcessedRoomState> ProcessedRooms = new();
    private static string? _deferredBossBgm;
    private static bool _musicBusMuted;
    private static BossGreetingSync? _sync;

    public static void RegisterLifecycle()
    {
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(evt =>
            ProcessedRooms.GetOrCreateValue(evt.RunState).ResumedLocation = evt.RunState.MapLocation);
        RitsuLibFramework.SubscribeLifecycle<RoomExitedEvent>(evt =>
        {
            _sync?.Dispose();
            _sync = null;
            if (evt.RunManager.DebugOnlyGetState() is { } runState
                && ProcessedRooms.TryGetValue(runState, out ProcessedRoomState? state))
            {
                state.ResumedLocation = null;
            }
        });
        RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(_ =>
        {
            _sync?.Dispose();
            _sync = null;
        });
    }

    public static bool ShouldStage(Player player)
    {
        ICombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        return combatState != null
            && player.Character is INinjaSlayerCharacter
            && RunManager.Instance.NetService.Type != NetGameType.Client
            && !NinjaSlayerSettings.BriefBossGreetingEnabled
            && IsGreetingPending(combatState, out _, out _);
    }

    internal static bool ReplacesAncientEntrance(Player player)
    {
        ICombatState? state = CombatManager.Instance.DebugOnlyGetState();
        return state != null && player.Character is INinjaSlayerCharacter
            && TryGetRoomKey(state, out _);
    }

    internal static bool IsPending(ICombatState state) => NCombatRoom.Instance != null
        && NRun.Instance?.GlobalUi != null
        && state.Players.Any(player => player.Character is INinjaSlayerCharacter)
        && TryGetRoomKey(state, out _)
        && (RunManager.Instance.NetService.Type != NetGameType.Singleplayer
            || IsGreetingPending(state, out _, out _));

    public static async Task<bool> TryPlay(ICombatState combatState)
    {
        bool pending = IsGreetingPending(combatState, out string roomKey, out List<Player> ninjaSlayers);
        if (!TryGetRoomKey(combatState, out roomKey) || ninjaSlayers.Count == 0)
        {
            return false;
        }

        NCombatRoom? room = NCombatRoom.Instance;
        NRun? run = NRun.Instance;
        if (room == null
            || run?.GlobalUi == null)
        {
            return false;
        }

        _sync?.Dispose();
        var sync = new BossGreetingSync(RunManager.Instance.NetService, roomKey,
            combatState.Players.Select(player => player.NetId), NinjaSlayerSettings.BriefBossGreetingEnabled, pending);
        _sync = sync;
        room.TreeExiting += sync.Dispose;
        var context = new BossGreetingSession(room, run.GlobalUi, StableHash(roomKey), sync);
        bool played = false;
        try
        {
            // Ready is retried until Start, covering clients whose room or
            // message handler has not been created when the host arrives.
            float retryReady = 0f;
            while (!sync.Started)
            {
                if (retryReady <= 0f) { sync.Ready(); retryReady = 0.25f; }
                if (!sync.Started) retryReady -= await context.NextFrame();
            }
            if (sync.Present && pending && !sync.Released)
            {
                TryMarkProcessed(combatState.RunState, roomKey);
                foreach (Player player in ninjaSlayers)
                    NinjaSlayerRunData.MarkBossGreetingCompleted(player, roomKey);
                if (!sync.Brief && !sync.Shortened)
                {
                    context.BeginFull();
                    try { await PlayInternal(combatState, ninjaSlayers, room, run.GlobalUi, context); }
                    catch (OperationCanceledException) when (sync.Shortened && !sync.Cancelled)
                    {
                        await context.FinishEntrances();
                        context.BeginBrief();
                        await PlayBrief(combatState, ninjaSlayers, room, context);
                    }
                }
                else
                {
                    context.BeginBrief();
                    await PlayBrief(combatState, ninjaSlayers, room, context);
                }
                played = true;
            }
            context.FinishPresentation();
            sync.Complete();
            while (!sync.Released) await context.NextFrame();
            if (played && sync.IsAuthority) NinjaSlayerSettings.CompleteFirstBossGreeting();
            Entry.Logger.Info($"Boss greeting released combat-start hooks: room={roomKey}, played={played}.");
        }
        catch (OperationCanceledException)
        {
            // Room exit/disconnection is not a request for a brief greeting.
            throw;
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Boss greeting cinematic failed safely: {ex}");
            context.FinishPresentation();
            sync.Complete();
            while (!sync.Released) await context.NextFrame();
        }
        finally
        {
            try { await context.FinishEntrances(); }
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
        }

        return played;
    }

    private static async Task PlayBrief(ICombatState state, List<Player> players, NCombatRoom room, BossGreetingSession context)
    {
        Player followed = SelectFollowedPlayer(state, players, context.RoomKeySeed);
        Creature? boss = SelectBoss(state);
        NCreature? bossNode = boss == null ? null : room.GetCreatureNode(boss);
        var bows = new List<(NinjaSlayerAimPose Pose, NinjaSlayerAimPose.VisualMotion Motion)>();
        foreach (Player player in players)
        {
            room.GetCreatureNode(player.Creature)?.Visuals.Show();
            NinjaSlayerAimPose? pose = NinjaSlayerAimPose.Get(player.Creature);
            if (pose?.BeginVisualMotion(NinjaSlayerAimPose.MotionKind.Offset, 1f) is not { } motion) continue;
            motion.Paused = true;
            bows.Add((pose, motion));
        }
        string name = LocManager.Instance.Language == "zhs" ? "忍者杀手" : "NINJA SLAYER";
        string title = state.Encounter?.Title.GetFormattedText() ?? boss?.Monster?.Id.Entry ?? "Boss";
        var bubble = NSpeechBubbleVfx.Create($"DOMO, {title}=SAN, {name} DESU.".ToUpperInvariant(), followed.Creature, 1.8f);
        if (bubble != null) { room.SceneContainer.AddChildSafely(bubble); context.TrackNode(bubble); }
        float elapsed = 0f;
        bool recovered = false;
        float duration = Math.Max(2f, context.BossResponseStarted
            ? context.BossResponseRemaining : 0.5f + (boss == null ? 0f : BossGreetingActionCatalog.Get(boss).MinimumDuration));
        Entry.Logger.Info($"Brief boss greeting started: duration={duration:0.###}s, continuingBoss={context.BossResponseStarted}.");
        try
        {
            while (elapsed < duration)
            {
                float weight = elapsed < 0.2f ? Mathf.SmoothStep(0f, 1f, elapsed / 0.2f)
                    : elapsed < 0.7f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (elapsed - 0.7f) / 0.3f);
                foreach (var (pose, motion) in bows)
                {
                    motion.Radians = Mathf.DegToRad(18f) * motion.Facing * weight;
                    pose.SyncNow();
                }
                if (elapsed >= 0.5f && !context.BossResponseStarted && boss != null && bossNode != null)
                {
                    ShowBriefBossBubble(state, room, boss, bossNode, context);
                    context.StartBossResponse(boss, bossNode);
                    Entry.Logger.Info($"Brief boss response started: elapsed={elapsed:0.###}s.");
                }
                if (!recovered && elapsed >= 1f)
                {
                    recovered = true;
                    Entry.Logger.Info($"Brief ninja bow recovered: elapsed={elapsed:0.###}s.");
                }
                elapsed += await context.NextFrame();
            }
            context.HandoffBossAudio();
        }
        finally
        {
            foreach (var (pose, motion) in bows) { motion.Dispose(); if (GodotObject.IsInstanceValid(pose)) pose.SyncNow(); }
        }
    }

    private static void ShowBriefBossBubble(ICombatState state, NCombatRoom room, Creature boss,
        NCreature bossNode, BossGreetingSession context)
    {
        if (boss.Monster is LagavulinMatriarch) return;
        string title = state.Encounter?.Title.GetFormattedText() ?? boss.Monster?.Id.Entry ?? "Boss";
        var bubble = IsKaiserBoss(boss)
            ? NSpeechBubbleVfx.Create(BuildBossGreetingDialogue(title), DialogueSide.Right,
                GetGlobalCenter(GetBossFocus(room, boss, bossNode)!), 2f)
            : NSpeechBubbleVfx.Create(BuildBossGreetingDialogue(title), boss, 2f);
        if (bubble == null) return;
        room.SceneContainer.AddChildSafely(bubble);
        context.TrackNode(bubble);
        context.BossBubble = bubble;
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

    internal static void CancelDeferredBossBgm()
    {
        _deferredBossBgm = null;
        PlayDeferredBossBgm();
    }

    private static async Task PlayInternal(
        ICombatState combatState,
        List<Player> ninjaSlayers,
        NCombatRoom room,
        NGlobalUi globalUi,
        BossGreetingSession context)
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
        context.Entrances = allEntrances;
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
            string dialogue = BuildBossGreetingDialogue(title);
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

                if (context.TryFrameBossFocusAndBubble(
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
            await context.TweenCameraToClamped(cameraPosition, targetZoom, BossCameraMoveSeconds);
        }
        else
        {
            await context.TweenCameraToClamped(bossFocus, targetZoom, BossCameraMoveSeconds);
        }

        if (bubble != null)
        {
            bubble.ProcessMode = Node.ProcessModeEnum.Inherit;
            bubble.Visible = true;
        }
        context.BossBubble = bubble;

        AudioEventHandle? bossAudio = await PlayBossAction(boss, bossNode, context);
        await context.TweenCameraToBaseline(CameraReturnSeconds);
        context.HandoffAudioForNaturalRelease(bossAudio, BossActionTimeoutSeconds);
        if (bubble != null && GodotObject.IsInstanceValid(bubble))
        {
            context.ReleaseNode(bubble);
            _ = TaskHelper.RunSafely(FadeBossBubbleAfterCombatStart(bubble));
        }
    }

    private static string BuildBossGreetingDialogue(string bossTitle)
    {
        string ninjaSlayerName = LocManager.Instance.Language == "zhs"
            ? "忍者杀手"
            : "NINJA SLAYER";
        return $"DOMO, {ninjaSlayerName}=SAN, {bossTitle} DESU.".ToUpperInvariant();
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

    private static async Task PlayGreetingVideo(NGlobalUi globalUi, BossGreetingSession context)
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
        BossGreetingSession context)
    {
        BossGreetingActionSpec action = BossGreetingActionCatalog.Get(boss);
        context.StartBossResponse(boss, bossNode);
        float finishAt = Math.Max(action.MinimumDuration, MinimumBossCameraHoldSeconds);
        await context.WaitSeconds(finishAt);

        string bossName = boss.Monster?.Id.Entry ?? boss.GetType().Name;
        Entry.Logger.Info(
            $"Boss greeting action completed its calibrated duration: boss={bossName}, waited={finishAt:0.###}s.");
        return context.BossAudio;
    }

    private static Creature? SelectBoss(ICombatState state)
    {
        Creature? firstEnemy = state.Enemies.Count > 0 ? state.Enemies[0] : null;
        if (state.Encounter is KaiserCrabBoss)
        {
            return firstEnemy;
        }

        Creature? hunterKiller = state.Enemies.FirstOrDefault(creature => creature.Monster is HunterKiller);
        if (hunterKiller != null)
        {
            return hunterKiller;
        }

        Creature? kinPriest = state.Enemies.FirstOrDefault(creature => creature.Monster is KinPriest);
        return kinPriest ?? state.Enemies.FirstOrDefault(creature => creature.Monster is
            CeremonialBeast or Vantom or LagavulinMatriarch or WaterfallGiant or SoulFysh
            or TheInsatiable or KnowledgeDemon or Queen or TestSubject or Aeonglass)
            ?? firstEnemy;
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

    private static bool IsGreetingEligible(ICombatState combatState, CombatRoom room) =>
        room.RoomType == RoomType.Boss
        || combatState.Encounter is HunterKillerNormal;

    private static bool TryGetRoomKey(ICombatState combatState, out string roomKey)
    {
        IRunState runState = combatState.RunState;
        if (runState.CurrentRoom is not CombatRoom room
            || combatState.Encounter == null
            || !IsGreetingEligible(combatState, room))
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

        if (WasProcessed(combatState.RunState, roomKey))
        {
            return false;
        }

        string completedRoomKey = roomKey;
        return !ninjaSlayers.All(player => NinjaSlayerRunData.HasCompletedBossGreeting(player, completedRoomKey));
    }

    private static bool TryMarkProcessed(IRunState runState, string roomKey)
    {
        ProcessedRoomState state = ProcessedRooms.GetOrCreateValue(runState);
        lock (state.Gate)
        {
            return state.RoomKeys.Add(roomKey);
        }
    }

    private static bool WasProcessed(IRunState runState, string roomKey)
    {
        if (!ProcessedRooms.TryGetValue(runState, out ProcessedRoomState? state))
        {
            return false;
        }

        lock (state.Gate)
        {
            return state.ResumedLocation == runState.MapLocation || state.RoomKeys.Contains(roomKey);
        }
    }

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

    private sealed class ProcessedRoomState
    {
        public Lock Gate { get; } = new();
        public HashSet<string> RoomKeys { get; } = [];
        public MapLocation? ResumedLocation { get; set; }
    }

    private sealed class BossGreetingSession : ICinematicAnimationContext, IDisposable
    {
        private readonly NCombatRoom _room;
        private readonly Control _sceneContainer;
        private readonly NGlobalUi _globalUi;
        private readonly bool _singlePlayer;
        private readonly List<AudioEventHandle> _audioEvents = [];
        private readonly List<CanvasItem> _ownedVisuals = [];
        private readonly Dictionary<CanvasItem, LayerSnapshot> _layerSnapshots = [];
        private readonly CinematicSessionLifetime _cancellation = new();
        private readonly CinematicSessionLifetime _fullCancellation = new();
        private readonly BossGreetingSync _sync;
        private readonly NinjaSlayerHoverTipSuppression _hoverTipSuppression;
        private VideoStreamPlayer? _video;
        private bool _paused;
        private bool _disposed;
        private bool _spaceWasDown;
        private Node.ProcessModeEnum _roomProcessMode;
        private ulong _lastFrameMsec;
        private ulong _lastDeltaFrame = ulong.MaxValue;
        private float _cachedFrameDelta;
        private CombatCinematicCameraLease? _cameraLease;
        private CombatCinematicCameraLease _camera => _cameraLease
            ?? throw new InvalidOperationException("Brief greetings do not own the camera.");
        private bool _full;
        private Creature? _respondingBoss;
        private BossGreetingActionSpec? _response;
        private float _responseElapsed;
        private bool _responseVfxPlayed;
        public Task? Entrances { get; set; }
        public NSpeechBubbleVfx? BossBubble { get; set; }
        public AudioEventHandle? BossAudio { get; private set; }
        public bool BossResponseStarted => _response != null;
        public float BossResponseRemaining => Math.Max(0f, (_response?.MinimumDuration ?? 0f) - _responseElapsed);

        public BossGreetingSession(NCombatRoom room, NGlobalUi globalUi, uint roomKeySeed, BossGreetingSync sync)
        {
            _room = room;
            _sceneContainer = room.SceneContainer;
            _globalUi = globalUi;
            _sync = sync;
            _singlePlayer = RunManager.Instance.IsSingleplayerOrFakeMultiplayer;
            RoomKeySeed = roomKeySeed;
            _roomProcessMode = room.ProcessMode;
            _lastFrameMsec = Time.GetTicksMsec();
            _spaceWasDown = Input.IsKeyPressed(Key.Space);
            _hoverTipSuppression = NinjaSlayerHoverTipSuppression.Acquire();
        }

        public void BeginFull()
        {
            _full = true;
            NCombatRoom room = _room;
            if (!CombatCinematicCameraLease.TryAcquire(room, "boss greeting", out CombatCinematicCameraLease? camera))
            {
                throw new InvalidOperationException("The combat cinematic camera is already in use.");
            }

            _cameraLease = camera ?? throw new InvalidOperationException("Could not acquire the combat cinematic camera.");
            RaiseTopBarLayers();
        }

        public async Task FinishEntrances()
        {
            _fullCancellation.Cancel();
            if (Entrances == null) return;
            try { await Entrances; }
            catch (OperationCanceledException) { }
        }

        public void BeginBrief()
        {
            FinishPresentation();
            if (_video != null && GodotObject.IsInstanceValid(_video))
            {
                _video.Stop();
                _video.QueueFreeSafely();
            }
            _video = null;
            foreach (var audio in _audioEvents.Where(audio => audio != BossAudio).ToArray())
            {
                audio.TryStop(allowFadeOut: false);
                audio.TryRelease();
                _audioEvents.Remove(audio);
            }
            foreach (var node in _ownedVisuals.Where(node => node != BossBubble).ToArray())
            {
                if (GodotObject.IsInstanceValid(node)) node.QueueFreeSafely();
                _ownedVisuals.Remove(node);
            }
        }

        public void FinishPresentation()
        {
            _full = false;
            RestoreCameraAndScreenShakeTarget();
            RestoreTopBarLayers();
        }

        public void StartBossResponse(Creature boss, NCreature node)
        {
            if (_response != null) return;
            _respondingBoss = boss;
            _response = BossGreetingActionCatalog.Get(boss);
            if (_response.SfxPath != null) BossAudio = PlaySfxWithHandle(_response.SfxPath);
            if (_response.AnimationTrigger != null) node.SetAnimationTrigger(_response.AnimationTrigger);
        }

        public void HandoffBossAudio() => HandoffAudioForNaturalRelease(BossAudio, BossActionTimeoutSeconds);

        public CancellationToken CancellationToken => _full ? _fullCancellation.Token : _cancellation.Token;
        public Vector2 BaselinePosition => _camera.BaselinePosition;
        public Vector2 BaselineScale => _camera.BaselineScale;
        public Vector2 ViewportSize => _camera.ViewportSize;
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
            _camera.PlayScreenShake(strength, duration, degrees);
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
            SceneTree tree = (SceneTree)Engine.GetMainLoop();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(_room) || !_room.IsInsideTree() || _sync.Cancelled)
            {
                _fullCancellation.Cancel();
                _cancellation.Cancel();
            }
            _cancellation.Token.ThrowIfCancellationRequested();
            UpdatePauseAndSkip();
            if (_full && _sync.Shortened)
            {
                _fullCancellation.Cancel();
                _fullCancellation.Token.ThrowIfCancellationRequested();
            }

            ulong processFrame = Engine.GetProcessFrames();
            if (processFrame != _lastDeltaFrame)
            {
                ulong now = Time.GetTicksMsec();
                _cachedFrameDelta = _paused ? 0f : Math.Min((now - _lastFrameMsec) / 1000f, 0.05f);
                _lastFrameMsec = now;
                _lastDeltaFrame = processFrame;
                _cameraLease?.Advance(_cachedFrameDelta);
                if (_response != null)
                {
                    _responseElapsed += _cachedFrameDelta;
                    if (!_responseVfxPlayed && _response.VfxPath != null && _responseElapsed >= _response.VfxDelay)
                    {
                        _responseVfxPlayed = true;
                        MegaCrit.Sts2.Core.Commands.VfxCmd.PlayOnCreatureCenter(_respondingBoss!, _response.VfxPath);
                    }
                }
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
            _camera.FrameOn(target, scale);
        }

        public void FrameCameraOnClamped(CanvasItem target, float scale)
        {
            _camera.FrameOn(target, scale, clamp: true);
        }

        public void BeginDelayedCameraFollow(CanvasItem target)
        {
            _camera.BeginDelayedFollow(target);
        }

        public void FrameCameraOnDelayed(
            CanvasItem target,
            float scale,
            float elapsed,
            float delay)
        {
            _camera.FrameOnDelayed(target, scale, elapsed, delay);
        }

        public async Task TweenCameraToClamped(
            CanvasItem target,
            float targetScale,
            float duration,
            Vector2 localOffset = default)
        {
            float startScale = _camera.CurrentScale;
            Vector2 startCenter = _camera.GetCameraCenter(_camera.CurrentPosition, startScale, ViewportSize * 0.5f);
            Vector2 targetCenter = _camera.GetLocalCenter(target) + localOffset;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += await NextFrame();
                float progress = EaseOut(Mathf.Clamp(elapsed / duration, 0f, 1f));
                float scale = Mathf.Lerp(startScale, targetScale, progress);
                Vector2 center = startCenter.Lerp(targetCenter, progress);
                _camera.FrameOnLocalPoint(_camera.ClampTarget(center, scale), scale);
            }
        }

        public Task TweenCameraToClamped(Vector2 targetPosition, float targetScale, float duration) =>
            TweenCamera(
                _camera.CurrentPosition,
                _camera.CurrentScale,
                targetPosition,
                targetScale,
                duration,
                clampToScene: true);

        public bool TryFrameBossFocusAndBubble(
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

                Rect2 composition = GetSceneLocalPointRect(bossFocus);
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

                targetPosition = _camera.GetCameraPosition(
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
            TweenCamera(
                _camera.CurrentPosition,
                _camera.CurrentScale,
                BaselinePosition,
                BaselineScale.X,
                duration,
                CombatCinematicCameraLease.EaseOutCubic);

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
            _cameraLease?.Dispose();
            _cameraLease = null;
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
            if (GodotObject.IsInstanceValid(_room)) _room.ProcessMode = _roomProcessMode;
            RestoreTopBarLayers();
            _hoverTipSuppression.Dispose();
            _cancellation.Dispose();
            _fullCancellation.Dispose();
        }

        private async Task TweenCamera(
            Vector2 startPosition,
            float startScale,
            Vector2 targetPosition,
            float targetScale,
            float duration,
            Func<float, float>? easing = null,
            bool clampToScene = false)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += await NextFrame();
                float normalized = Mathf.Clamp(elapsed / duration, 0f, 1f);
                float progress = easing?.Invoke(normalized) ?? EaseOut(normalized);
                float scale = Mathf.Lerp(startScale, targetScale, progress);
                Vector2 position = startPosition.Lerp(targetPosition, progress);
                _camera.SetTransform(
                    clampToScene ? _camera.ClampPosition(position, scale) : position,
                    scale);
            }
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
            Vector2 center = _camera.GetLocalCenter(item);
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
            if (_full && !overlayOpen && spaceDown && !_spaceWasDown)
            {
                _sync.RequestBrief();
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

        private readonly record struct LayerSnapshot(int ZIndex, bool ZAsRelative);
    }
}
