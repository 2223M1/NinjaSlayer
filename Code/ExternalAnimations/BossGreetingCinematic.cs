using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
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
    private const float BossCameraMoveSeconds = 0.2f;
    private const float CameraReturnSeconds = 0.2f;
    private const float DefaultBossZoomMultiplier = 1.5f;
    private const float TargetBubbleWidth = 620f;
    private static readonly HashSet<string> ProcessedRoomKeys = [];

    public static bool ShouldStage(Player player)
    {
        ICombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        return combatState != null
            && player.Character is INinjaSlayerCharacter
            && TryGetRoomKey(combatState, out string roomKey)
            && !ProcessedRoomKeys.Contains(GetProcessRoomKey(combatState.RunState, roomKey))
            && !NinjaSlayerRunData.HasCompletedBossGreeting(player, roomKey);
    }

    public static async Task<bool> TryPlay(ICombatState combatState)
    {
        List<Player> ninjaSlayers = combatState.Players
            .Where(player => player.Character is INinjaSlayerCharacter)
            .ToList();
        if (ninjaSlayers.Count == 0 || !TryGetRoomKey(combatState, out string roomKey))
        {
            return false;
        }

        string processRoomKey = GetProcessRoomKey(combatState.RunState, roomKey);
        if (ProcessedRoomKeys.Contains(processRoomKey)
            || ninjaSlayers.All(player => NinjaSlayerRunData.HasCompletedBossGreeting(player, roomKey)))
        {
            return false;
        }

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
            room.SceneContainer.Position = context.BaselinePosition;
            room.SceneContainer.Scale = context.BaselineScale;
            foreach (Player player in ninjaSlayers)
            {
                NCreature? node = room.GetCreatureNode(player.Creature);
                node?.Visuals.Show();
            }
        }

        return true;
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

        var variants = ninjaSlayers
            .Select((player, index) => (player, variant: AncientEntranceAnimation.FromRoll(StableRoll(context.RoomKeySeed, index))))
            .ToDictionary(pair => pair.player, pair => pair.variant);
        Task[] entranceTasks = ninjaSlayers
            .Select(player => AncientEntranceAnimation.Play(player, variants[player], context))
            .ToArray();
        Task allEntrances = Task.WhenAll(entranceTasks);

        float zoomDuration = AncientEntranceAnimation.GetDuration(variants[followedPlayer]);
        float elapsed = 0f;
        while (!allEntrances.IsCompleted)
        {
            float delta = await context.NextFrame();
            elapsed += delta;
            float progress = Mathf.Clamp(elapsed / Math.Max(zoomDuration, 0.01f), 0f, 1f);
            float zoom = context.BaselineScale.X * Mathf.Lerp(1f, PlayerZoomMultiplier, EaseOut(progress));
            context.FrameCameraOn(followedNode.Visuals.Bounds, zoom);
        }

        await allEntrances;
        context.FrameCameraOn(followedNode.Visuals.Bounds, context.BaselineScale.X * PlayerZoomMultiplier);

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
                    GetBossActionDuration(boss) + 1.2f)
                : NSpeechBubbleVfx.Create(dialogue, boss, GetBossActionDuration(boss) + 1.2f);
            if (bubble != null)
            {
                bubble.Visible = false;
                bubble.ProcessMode = Node.ProcessModeEnum.Disabled;
                room.SceneContainer.AddChildSafely(bubble);
                context.TrackNode(bubble);
                await context.NextFrame();
                Control? container = bubble.GetNodeOrNull<Control>("%Container");
                if (container != null && container.Size.X > 1f)
                {
                    targetZoom = TargetBubbleWidth / container.Size.X;
                }
            }
        }

        await context.TweenCameraTo(bossFocus, targetZoom, BossCameraMoveSeconds);
        if (bubble != null)
        {
            bubble.ProcessMode = Node.ProcessModeEnum.Inherit;
            if (!anchorBubbleToBoss)
            {
                await context.NextFrame();
                Control? container = bubble.GetNodeOrNull<Control>("%Container");
                if (container != null)
                {
                    Vector2 desiredTopLeft = context.ViewportSize * new Vector2(0.06f, 0.12f);
                    bubble.GlobalPosition += desiredTopLeft - container.GetGlobalRect().Position;
                }
            }
            bubble.Visible = true;
        }

        await PlayBossAction(boss, bossNode, context);
        if (bubble != null && GodotObject.IsInstanceValid(bubble))
        {
            bubble.QueueFreeSafely();
        }

        await context.TweenCameraToBaseline(CameraReturnSeconds);
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
    }

    private static async Task PlayBossAction(Creature boss, NCreature bossNode, CinematicContext context)
    {
        if (IsKaiserBoss(boss))
        {
            await context.WaitSeconds(1.75f);
            return;
        }

        switch (boss.Monster)
        {
            case CeremonialBeast:
                context.PlaySfx("event:/sfx/enemy/enemy_attacks/ceremonial_beast/ceremonial_beast_shrill");
                bossNode.SetAnimationTrigger("Cast");
                await context.WaitSeconds(0.3f);
                MegaCrit.Sts2.Core.Commands.VfxCmd.PlayOnCreatureCenter(boss, "vfx/vfx_scream");
                await context.WaitSeconds(0.75f);
                break;
            case KinPriest:
                context.PlaySfx("event:/sfx/enemy/enemy_attacks/the_kin_priest/the_kin_priest_rally");
                bossNode.SetAnimationTrigger("Rally");
                await context.WaitSeconds(1f);
                break;
            case Vantom:
                context.PlaySfx("event:/sfx/enemy/enemy_attacks/vantom/vantom_buff");
                bossNode.SetAnimationTrigger("BUFF");
                await context.WaitSeconds(0.6f);
                break;
            case LagavulinMatriarch:
                bossNode.SetAnimationTrigger("Sleep");
                await context.WaitSeconds(1f);
                break;
            case WaterfallGiant:
                context.PlaySfx("event:/sfx/enemy/enemy_attacks/waterfall_giant/waterfall_giant_eruption");
                bossNode.SetAnimationTrigger("Heal");
                await context.WaitSeconds(0.8f);
                break;
            case SoulFysh:
                context.PlaySfx("event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_beckon");
                bossNode.SetAnimationTrigger("Beckon");
                await context.WaitSeconds(0.3f);
                MegaCrit.Sts2.Core.Commands.VfxCmd.PlayOnCreatureCenter(boss, "vfx/vfx_spooky_scream");
                await context.WaitSeconds(0.3f);
                break;
            case TheInsatiable:
                context.PlaySfx("event:/sfx/enemy/enemy_attacks/the_insatiable/the_insatiable_liquify_ground");
                bossNode.SetAnimationTrigger("LiquifySand");
                await context.WaitSeconds(0.5f);
                MegaCrit.Sts2.Core.Commands.VfxCmd.PlayOnCreatureCenter(boss, "vfx/vfx_scream");
                await context.WaitSeconds(0.75f);
                break;
            case KnowledgeDemon:
                bossNode.SetAnimationTrigger("MindRotTrigger");
                await context.WaitSeconds(1f);
                break;
            case Queen:
                context.PlaySfx("event:/sfx/enemy/enemy_attacks/queen/queen_cast");
                bossNode.SetAnimationTrigger("Cast");
                await context.WaitSeconds(0.5f);
                break;
            case TestSubject:
                context.PlaySfx("event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite");
                bossNode.SetAnimationTrigger("BiteTrigger");
                await context.WaitSeconds(0.25f);
                break;
            case Aeonglass:
                bossNode.SetAnimationTrigger("Cast");
                await context.WaitSeconds(0.4f);
                break;
            default:
                await context.WaitSeconds(0.8f);
                break;
        }
    }

    private static float GetBossActionDuration(Creature boss)
    {
        if (IsKaiserBoss(boss))
        {
            return 1.75f;
        }

        return boss.Monster switch
        {
            CeremonialBeast => 1.05f,
            KinPriest => 1f,
            Vantom => 0.6f,
            LagavulinMatriarch => 1f,
            WaterfallGiant => 0.8f,
            SoulFysh => 0.6f,
            TheInsatiable => 1.25f,
            KnowledgeDemon => 1f,
            Queen => 0.5f,
            TestSubject => 0.25f,
            Aeonglass => 0.4f,
            _ => 0.8f
        };
    }

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
        private readonly NGlobalUi _globalUi;
        private readonly bool _singlePlayer;
        private readonly List<AudioEventHandle> _audioEvents = [];
        private readonly List<CanvasItem> _ownedVisuals = [];
        private readonly Dictionary<CanvasItem, LayerSnapshot> _layerSnapshots = [];
        private readonly CancellationTokenSource _cancellation = new();
        private VideoStreamPlayer? _video;
        private bool _paused;
        private bool _disposed;
        private bool _spaceWasDown;
        private Node.ProcessModeEnum _roomProcessMode;
        private ulong _lastFrameMsec;

        public CinematicContext(NCombatRoom room, NGlobalUi globalUi, uint roomKeySeed)
        {
            _room = room;
            _globalUi = globalUi;
            _singlePlayer = RunManager.Instance.IsSingleplayerOrFakeMultiplayer;
            BaselinePosition = room.SceneContainer.Position;
            BaselineScale = room.SceneContainer.Scale;
            ViewportSize = room.GetViewportRect().Size;
            RoomKeySeed = roomKeySeed;
            _roomProcessMode = room.ProcessMode;
            _lastFrameMsec = Time.GetTicksMsec();
            RaiseTopBarLayers();
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

        public void PlaySfx(string eventPath)
        {
            try
            {
                AudioEventHandle? audioEvent = FmodStudioEventInstances.TryCreateHandle(
                    AudioSource.Event(eventPath),
                    new AudioPlaybackOptions());
                if (audioEvent == null)
                {
                    Entry.Logger.Warn($"Could not create cinematic SFX '{eventPath}'.");
                    return;
                }

                if (!audioEvent.TryPlay())
                {
                    audioEvent.TryRelease();
                    Entry.Logger.Warn($"Could not start cinematic SFX '{eventPath}'.");
                    return;
                }

                if (_paused)
                {
                    audioEvent.TryPause();
                }
                _audioEvents.Add(audioEvent);
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"Could not play cinematic SFX '{eventPath}': {ex.Message}");
            }
        }

        public async Task<float> NextFrame()
        {
            await _room.ToSignal(_room.GetTree(), SceneTree.SignalName.ProcessFrame);
            UpdatePauseAndSkip();
            _cancellation.Token.ThrowIfCancellationRequested();

            ulong now = Time.GetTicksMsec();
            float delta = _paused ? 0f : Math.Min((now - _lastFrameMsec) / 1000f, 0.05f);
            _lastFrameMsec = now;
            return delta;
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
            _room.SceneContainer.Scale = Vector2.One * scale;
            _room.SceneContainer.Position = ViewportSize * 0.5f - localTarget * scale;
        }

        public async Task TweenCameraTo(CanvasItem target, float targetScale, float duration)
        {
            Vector2 startPosition = _room.SceneContainer.Position;
            float startScale = _room.SceneContainer.Scale.X;
            Vector2 localTarget = GetLocalCenter(target);
            Vector2 targetPosition = ViewportSize * 0.5f - localTarget * targetScale;
            await TweenCamera(startPosition, startScale, targetPosition, targetScale, duration);
        }

        public Task TweenCameraToBaseline(float duration) =>
            TweenCamera(_room.SceneContainer.Position, _room.SceneContainer.Scale.X, BaselinePosition, BaselineScale.X, duration);

        public void AttachVideo(VideoStreamPlayer? video) => _video = video;

        public void TrackNode(CanvasItem node) => _ownedVisuals.Add(node);

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
                _room.SceneContainer.Position = startPosition.Lerp(targetPosition, progress);
                _room.SceneContainer.Scale = Vector2.One * Mathf.Lerp(startScale, targetScale, progress);
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

        private readonly record struct LayerSnapshot(int ZIndex, bool ZAsRelative);
    }
}
