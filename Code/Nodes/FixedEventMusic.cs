using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using STS2RitsuLib.Audio;

namespace NinjaSlayer.Code.Nodes;

/// <summary>Local event-room music; never gated by the optional radio mode.</summary>
public partial class FixedEventMusic : Node
{
    private EventModel _event = null!;
    private NRunMusicController? _controller;
    private GodotObject? _instance;
    private bool _ending;

    public static void Start(EventModel model, string path, string eventId)
    {
        if (NonInteractiveMode.IsActive || model.IsFinished) return;
        NEventRoom room = NEventRoom.Instance
            ?? throw new InvalidOperationException("Event music requires its event room.");
        // AfterEventStarted is local-player-only. A second call must not restart music.
        if (room.GetNodeOrNull<FixedEventMusic>(nameof(FixedEventMusic)) != null) return;
        var controller = NRunMusicController.Instance
            ?? throw new InvalidOperationException("Event music requires the run music controller.");
        var description = FmodStudioServer.TryGetEventDescriptionFromGuid(eventId);
        if (description == null)
        {
            Scripts.Entry.Logger.Warn($"Fixed event music unavailable: {path}; retaining act music.");
            return;
        }
        controller.PlayCustomMusic(path);
        var instances = description.Call("get_instance_list").AsGodotArray();
        var instance = instances.Select(value => value.AsGodotObject())
            // An old released instance may still be STOPPING (1) until the mixer update.
            .SingleOrDefault(value => value.Call("get_playback_state").AsInt32() is 0 or 3 or 4);
        if (instance == null)
        {
            controller.StopCustomMusic();
            controller.UpdateTrack();
            Scripts.Entry.Logger.Warn($"Fixed event music did not start: {path}; restored act music.");
            return;
        }
        room.AddChild(new FixedEventMusic
        {
            Name = nameof(FixedEventMusic), _event = model,
            _controller = controller, _instance = instance,
        });
    }

    public override void _Ready() => RunManager.Instance.RoomExited += Restore;

    public override void _Process(double delta)
    {
        if (_controller == null) return;
        if (_event.IsFinished && !_ending)
        {
            _ending = true;
            _controller.UpdateMusicParameter(NinjaSlayerAudio.EventMusicEndParameter, 1f);
        }
        if (_instance == null || !GodotObject.IsInstanceValid(_instance)
            || _instance.Call("get_playback_state").AsInt32() == 2)
            Restore();
    }

    public override void _ExitTree() => Restore();

    private void Restore()
    {
        RunManager.Instance.RoomExited -= Restore;
        var controller = _controller;
        _controller = null;
        _instance = null;
        SetProcess(false);
        if (controller == null || !GodotObject.IsInstanceValid(controller)
            || !controller.IsInsideTree() || !RunManager.Instance.IsInProgress) return;
        controller.StopCustomMusic();
        // StopCustomMusic chooses CombatEnd; restore the actual current room instead.
        if (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom != null)
            controller.UpdateTrack();
    }
}
