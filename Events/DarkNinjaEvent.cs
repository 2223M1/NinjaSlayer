using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using NinjaSlayer.Content;
using NinjaSlayer.Encounters;
using NinjaSlayer.Monsters;
using NinjaSlayer.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Events;

[RegisterActEvent(typeof(Glory))]
public sealed class DarkNinjaEvent : ModEventTemplate
{
    private const string DefaultLayoutScenePath =
        "res://scenes/events/default_event_layout.tscn";

    private const string PortraitPath = DarkNinjaMonster.StandingTexturePath;
    private const string VictoryPortraitPath = "res://NinjaSlayer/images/events/dark_ninja_victory.png";

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
        InitialPortraitPath: ShowResultLayout && IsFinished ? VictoryPortraitPath : PortraitPath);

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
            .Concat([DefaultLayoutScenePath, PortraitPath, VictoryPortraitPath])
            .Distinct();
    }

    public override Task Resume(AbstractRoom exitedRoom)
    {
        ShowResultLayout = true;
        SetEventFinished(PageDescription("VICTORY"));

        return Task.CompletedTask;
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
            [new EventOption(this, Fight, ModOptionKey("FIGHT", "FIGHT"),
                HoverTipFactory.FromRelic<BeppinFragmentRelic>())]);
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
            [new RelicReward(ModelDb.Relic<BeppinFragmentRelic>().ToMutable(), Owner!), new RelicReward(Owner!)],
            shouldResumeAfterCombat: true);
        return Task.CompletedTask;
    }
}

internal static class DarkNinjaMusicSession
{
    private static AbstractRoom? _eventRoom;
    private static bool _ending;
    private static int _generation;

    public static void Begin(AbstractRoom room)
    {
        Clear();
        _eventRoom = room;
        RunManager.Instance.RoomEntered += OnRoomChanged;
        RunManager.Instance.RoomExited += OnRoomChanged;
    }

    public static void ResumeCombat(CombatRoom room)
    {
        if (_eventRoom == null) Begin(room);
    }

    public static void EndBattle()
    {
        if (_eventRoom == null || _ending || NonInteractiveMode.IsActive) return;
        _ending = true;
        // The native controller and RitsuLib both play this bank event. Observe its
        // FMOD description, not the legacy Godot proxy that RitsuLib bypasses.
        var description = STS2RitsuLib.Audio.FmodStudioServer.TryGetEventDescriptionFromGuid(
            NinjaSlayerAudio.DarkNinjaBattleMusicGuid);
        if (description == null)
        {
            NinjaSlayer.Scripts.Entry.Logger.Warn("Dark Ninja music bank is unavailable; restoring act music.");
            RestoreMusic();
            return;
        }
        var instances = description.Call("get_instance_list").AsGodotArray();
        GodotObject? music = instances.Select(value => value.AsGodotObject())
            .SingleOrDefault(instance => instance.Call("get_playback_state").AsInt32() != 2);
        if (music == null) RestoreMusic();
        else _ = TaskHelper.RunSafely(WaitForOutro(music, _generation));
    }

    private static async Task WaitForOutro(GodotObject music, int generation)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        while (_eventRoom != null && generation == _generation)
        {
            if (!GodotObject.IsInstanceValid(music) || music.Call("get_playback_state").AsInt32() == 2)
            {
                RestoreMusic();
                return;
            }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void OnRoomChanged()
    {
        AbstractRoom? current = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
        if (ReferenceEquals(current, _eventRoom)
            || current is CombatRoom { Encounter: DarkNinjaEncounter }
            || current is EventRoom { CanonicalEvent: DarkNinjaEvent }) return;
        RestoreMusic();
    }

    private static void RestoreMusic()
    {
        Clear();
        NRunMusicController? controller = NRunMusicController.Instance;
        controller?.StopCustomMusic();
        if (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom != null)
        {
            controller?.UpdateMusic();
            controller?.UpdateTrack();
        }
    }

    private static void Clear()
    {
        if (_eventRoom != null)
        {
            RunManager.Instance.RoomEntered -= OnRoomChanged;
            RunManager.Instance.RoomExited -= OnRoomChanged;
        }
        _eventRoom = null;
        _ending = false;
        _generation++;
    }
}
