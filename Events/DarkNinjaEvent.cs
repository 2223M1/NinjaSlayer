using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using NinjaSlayer.Content;
using NinjaSlayer.Encounters;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Events;

[RegisterActEvent(typeof(Glory))]
public sealed class DarkNinjaEvent : ModEventTemplate
{
    private const string DefaultLayoutScenePath =
        "res://scenes/events/default_event_layout.tscn";

    private const string PortraitPath = DarkNinjaMonster.StandingTexturePath;

    private bool _showResultLayout;

    [SavedProperty]
    private bool ShowResultLayout
    {
        get => _showResultLayout;
        set
        {
            AssertMutable();
            _showResultLayout = value;
        }
    }

    public override bool IsShared => true;

    public override EventLayoutType LayoutType =>
        ShowResultLayout ? EventLayoutType.Default : EventLayoutType.Combat;

    public override EncounterModel CanonicalEncounter =>
        ModelDb.Encounter<DarkNinjaEncounter>();

    public override EventAssetProfile AssetProfile => new(
        InitialPortraitPath: PortraitPath);

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new GoldVar(100)];

    public override bool IsAllowed(IRunState runState) =>
        NinjaSlayerContentAccess.HasNinjaSlayer(runState);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        new EventOption(this, Escape, InitialOptionKey("ESCAPE")),
        new EventOption(this, Stay, InitialOptionKey("STAY"))
    ];

    public override IEnumerable<string> GetAssetPaths(IRunState runState)
    {
        if (TestMode.IsOn)
        {
            return [];
        }

        return base.GetAssetPaths(runState)
            .Concat([DefaultLayoutScenePath, PortraitPath])
            .Distinct();
    }

    public override async Task Resume(AbstractRoom exitedRoom)
    {
        ShowResultLayout = true;
        SetEventFinished(PageDescription("VICTORY"));

        var owner = Owner ?? throw new InvalidOperationException(
            "Dark Ninja event resumed without an owner.");
        List<Reward> rewards =
        [
            new RelicReward(
                RelicFactory.PullNextRelicFromFront(owner).ToMutable(),
                owner),
            new RelicReward(
                RelicFactory.PullNextRelicFromFront(owner).ToMutable(),
                owner)
        ];

        await RewardsCmd.OfferCustom(owner, rewards);
    }

    private async Task Escape()
    {
        await PlayerCmd.GainGold(DynamicVars.Gold.BaseValue, Owner!);
        SetEventFinished(PageDescription("ESCAPED"));
    }

    private Task Stay()
    {
        SetEventState(
            PageDescription("FIGHT"),
            [new EventOption(this, Fight, ModOptionKey("FIGHT", "FIGHT"))]);
        return Task.CompletedTask;
    }

    private Task Fight()
    {
        ShowResultLayout = true;
        if (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is EventRoom eventRoom)
        {
            DarkNinjaMusicSession.Begin(eventRoom);
        }

        EncounterModel encounter = CanonicalEncounter;
#if NINJASLAYER_CHANNEL_STABLE
        encounter = encounter.ToMutable();
#endif
        EnterCombatWithoutExitingEvent(
            encounter,
            [],
            shouldResumeAfterCombat: true);
        return Task.CompletedTask;
    }
}

internal static class DarkNinjaMusicSession
{
    private const int FmodStoppedPlaybackState = 2;

    private static EventRoom? _eventRoom;
    private static CombatRoom? _combatRoom;
    private static ulong _previousMusicInstanceId;
    private static ulong _musicInstanceId;
    private static int _generation;
    private static bool _ending;
    private static bool _subscribed;

    public static void Begin(EventRoom eventRoom)
    {
        Clear();
        _eventRoom = eventRoom;
        if (TryGetCurrentMusicEvent(out _, out ulong instanceId))
        {
            _previousMusicInstanceId = instanceId;
        }

        RunManager.Instance.RoomEntered += OnRoomEntered;
        RunManager.Instance.RoomExited += OnRoomExited;
        _subscribed = true;
    }

    public static void EndBattle()
    {
        if (_eventRoom == null || _ending)
        {
            return;
        }

        _ending = true;
        TryCaptureMusicEvent();
        int generation = _generation;
        _ = TaskHelper.RunSafely(WaitForOutro(generation));
    }

    private static void OnRoomEntered()
    {
        AbstractRoom? currentRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
        if (currentRoom is CombatRoom combatRoom
            && combatRoom.Encounter is DarkNinjaEncounter)
        {
            _combatRoom = combatRoom;
            int generation = _generation;
            _ = TaskHelper.RunSafely(CaptureMusicEvent(generation));
            return;
        }

        if (ReferenceEquals(currentRoom, _eventRoom)
            || ReferenceEquals(currentRoom, _combatRoom))
        {
            return;
        }

        RestoreIfOwnedOrClear(_generation);
    }

    private static void OnRoomExited()
    {
        AbstractRoom? currentRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
        if (ReferenceEquals(currentRoom, _eventRoom)
            || ReferenceEquals(currentRoom, _combatRoom))
        {
            return;
        }

        RestoreIfOwnedOrClear(_generation);
    }

    private static async Task CaptureMusicEvent(int generation)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }

        while (IsCurrent(generation)
            && _musicInstanceId == 0
            && ReferenceEquals(
                RunManager.Instance.DebugOnlyGetState()?.CurrentRoom,
                _combatRoom))
        {
            if (TryCaptureMusicEvent())
            {
                return;
            }

            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private static async Task WaitForOutro(int generation)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            ClearIfCurrent(generation);
            return;
        }

        while (IsCurrent(generation) && _ending)
        {
            if (_musicInstanceId == 0)
            {
                TryCaptureMusicEvent();
            }
            else if (!TryGetCurrentMusicEvent(out GodotObject? musicEvent, out ulong instanceId))
            {
                RestoreCurrentRoomMusic(generation);
                return;
            }
            else if (instanceId != _musicInstanceId)
            {
                ClearIfCurrent(generation);
                return;
            }
            else if (musicEvent != null
                && TryGetPlaybackState(musicEvent, out int playbackState)
                && playbackState == FmodStoppedPlaybackState)
            {
                RestoreCurrentRoomMusic(generation);
                return;
            }

            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private static bool TryCaptureMusicEvent()
    {
        if (!TryGetCurrentMusicEvent(out _, out ulong instanceId)
            || instanceId == _previousMusicInstanceId)
        {
            return false;
        }

        _musicInstanceId = instanceId;
        return true;
    }

    private static bool TryGetCurrentMusicEvent(
        out GodotObject? musicEvent,
        out ulong instanceId)
    {
        musicEvent = null;
        instanceId = 0;
        try
        {
            Node? proxy = NRunMusicController.Instance?.GetNodeOrNull<Node>("Proxy");
            if (proxy == null)
            {
                return false;
            }

            Variant value = proxy.Get("_musicEv");
            musicEvent = value.VariantType == Variant.Type.Object
                ? value.AsGodotObject()
                : null;
            if (musicEvent == null || !GodotObject.IsInstanceValid(musicEvent))
            {
                musicEvent = null;
                return false;
            }

            instanceId = musicEvent.GetInstanceId();
            return true;
        }
        catch
        {
            musicEvent = null;
            instanceId = 0;
            return false;
        }
    }

    private static bool TryGetPlaybackState(GodotObject musicEvent, out int playbackState)
    {
        playbackState = default;
        if (!musicEvent.HasMethod("get_playback_state"))
        {
            return false;
        }

        try
        {
            playbackState = musicEvent.Call("get_playback_state").AsInt32();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RestoreIfOwnedOrClear(int generation)
    {
        if (_musicInstanceId == 0)
        {
            ClearIfCurrent(generation);
            return;
        }

        if (TryGetCurrentMusicEvent(out _, out ulong instanceId)
            && instanceId != _musicInstanceId)
        {
            // A stale outro must not stop music already started by a later room.
            ClearIfCurrent(generation);
            return;
        }

        RestoreCurrentRoomMusic(generation);
    }

    private static void RestoreCurrentRoomMusic(int generation)
    {
        if (!IsCurrent(generation))
        {
            return;
        }

        try
        {
            NRunMusicController? controller = NRunMusicController.Instance;
            controller?.StopCustomMusic();
            if (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom != null)
            {
                controller?.UpdateTrack();
            }
        }
        finally
        {
            ClearIfCurrent(generation);
        }
    }

    private static bool IsCurrent(int generation) =>
        _eventRoom != null && generation == _generation;

    private static void ClearIfCurrent(int generation)
    {
        if (generation == _generation)
        {
            Clear();
        }
    }

    private static void Clear()
    {
        if (_subscribed)
        {
            RunManager.Instance.RoomEntered -= OnRoomEntered;
            RunManager.Instance.RoomExited -= OnRoomExited;
        }

        _eventRoom = null;
        _combatRoom = null;
        _previousMusicInstanceId = 0;
        _musicInstanceId = 0;
        _ending = false;
        _subscribed = false;
        _generation++;
    }
}
