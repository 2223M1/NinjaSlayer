using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Audio;

namespace NinjaSlayer.Code.ExternalAnimations;

/// <summary>One Tornado Fist instance, owned until its outro ends or the room unloads.</summary>
internal sealed partial class SpinComboAudio : Node
{
    private AudioEventHandle _audio = null!;
    private NCombatRoom _room = null!;
    private Creature _actor = null!;
    private float _elapsed;
    private bool _finishing, _paused;
    internal Creature Actor => _actor;

    internal static SpinComboAudio Start(Creature actor)
    {
        NCombatRoom room = NCombatRoom.Instance ?? throw new InvalidOperationException("Tornado audio requires a combat room.");
        AudioEventHandle audio = FmodStudioEventInstances.TryCreateHandle(
            AudioSource.Event(NinjaSlayerAudio.NinjaSlayerSpinAttackEvent),
            new AudioPlaybackOptions
            {
                Scope = AudioLifecycleScope.Manual
            }) ?? throw new InvalidOperationException("Could not create the Tornado Fist FMOD event.");
        // TryCreateHandle copies lifecycle scope only; it does not apply playback options.
        if (!audio.TrySetParameter("sustain", 1f)
            || !audio.TrySetParameter("finish", 0f))
        {
            audio.TryRelease();
            throw new InvalidOperationException("Could not initialize the Tornado Fist FMOD parameters.");
        }
        var owner = new SpinComboAudio
        {
            _audio = audio, _room = room, _actor = actor,
            ProcessMode = ProcessModeEnum.Always
        };
        room.AddChild(owner);
        if (!audio.TryPlay())
        {
            owner.QueueFree();
            throw new InvalidOperationException("Could not start the Tornado Fist FMOD event.");
        }
        Entry.Logger.Info("Tornado audio: sustain until the final hit.");
        return owner;
    }

    internal static float RemainingSeconds(Creature actor, int hits)
    {
        FinisherSession? session = FinisherSessionRegistry.GetActiveSession();
        float extra = session?.Actor == actor && !session.IsRanged ? session.TornadoAudioExtraSeconds : 0f;
        return TornadoSpinTiming.RemainingSeconds(hits, CombatActionTimingRuntime.CurrentSpeed) + extra;
    }

    internal void Finish()
    {
        if (_finishing) return;
        if (!_audio.TrySetParameter("finish", 1f))
            throw new InvalidOperationException("Tornado Fist could not enter its outro.");
        _finishing = true;
        Entry.Logger.Info($"Tornado audio: outro at {_elapsed:F6}s.");
    }

    public override void _Process(double delta)
    {
        bool paused = CombatManager.Instance.IsPaused || !_room.CanProcess();
        if (paused != _paused)
        {
            if (!(paused ? _audio.TryPause() : _audio.TryResume()))
                throw new InvalidOperationException("Tornado Fist audio pause failed.");
            _paused = paused;
        }
        if (paused) return;
        _elapsed += (float)delta;
        if (_finishing)
        {
            // Wait for native STOPPED, including FMOD command/mixer latency and the full tail.
            if (_audio.RawInstance!.Call("get_playback_state").AsInt32() == 2) QueueFree();
            return;
        }
        bool targetsGone = _actor.CombatState == null
            || (_actor.CombatState.HittableEnemies.Count == 0 && !NinjaSlayerFinisherCinematic.IsMovementOwned(_actor));
        if (targetsGone || _actor.IsDead || NinjaSlayerAimPose.Get(_actor)?.IsTornado != true)
            Finish();
    }

    public override void _ExitTree()
    {
        _audio.TryStop(false);
        _audio.TryRelease();
    }
}
