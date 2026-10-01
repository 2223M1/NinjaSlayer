using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private sealed partial class TheaterRuntime
    {
        private async Task TornadoAudioCheck(string mode, double seconds, int hits)
        {
            Require(mode is "pause" or "early-kill" or "release" or "sustain", "Unknown Tornado audio check.");
            long startedQpc = System.Diagnostics.Stopwatch.GetTimestamp();
            Task play = PlayCard(new() { Card = "TornadoFist", Energy = hits });
            Node? owner = null;
            for (int frame = 0; frame < 120 && owner == null; frame++)
            {
                owner = _room.GetChildren().FirstOrDefault(n => n.GetType().Name == "SpinComboAudio");
                if (owner == null) await _driver.WaitFrames(1);
            }
            Require(owner != null, "The actual card did not start its Spin instance.");
            Type type = owner!.GetType();
            ulong ownerId = owner.GetInstanceId();
            object handle = AccessTools.Field(type, "_audio").GetValue(owner)!;
            GodotObject raw = (GodotObject)AccessTools.Property(handle.GetType(), "RawInstance").GetValue(handle)!;
            int Position() => raw.Call("get_timeline_position").AsInt32();
            bool Released() => (bool)AccessTools.Property(handle.GetType(), "IsReleased").GetValue(handle)!;
            if (mode == "sustain")
            {
                var trace = new JsonArray();
                int wraps = 0, previous = 0;
                long timerStartedQpc = System.Diagnostics.Stopwatch.GetTimestamp();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var poseType = ProductType("NinjaSlayer.Code.Nodes.NinjaSlayerAimPose");
                var getPose = AccessTools.DeclaredMethod(poseType, "Get",
                    [typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature)]);
                double? poseEnded = null, audioEnded = null, finishSeconds = null;
                bool wasSpinning = false, outroReached = false;
                while (timer.Elapsed.TotalSeconds < 40)
                {
                    bool released = Released();
                    int position = released ? -1 : Position();
                    int playbackState = released ? 2 : raw.Call("get_playback_state").AsInt32();
                    bool finishing = GodotObject.IsInstanceValid(owner)
                        && (bool)AccessTools.Field(type, "_finishing").GetValue(owner)!;
                    if (finishing) finishSeconds ??= timer.Elapsed.TotalSeconds;
                    object? pose = getPose.Invoke(null, [Actor("ninja")]);
                    bool spinning = pose != null && (bool)AccessTools.Property(poseType, "IsTornado").GetValue(pose)!;
                    if (wasSpinning && !spinning) poseEnded ??= timer.Elapsed.TotalSeconds;
                    wasSpinning |= spinning;
                    outroReached |= position >= 2554;
                    if (outroReached && !released && playbackState == 2) audioEnded ??= timer.Elapsed.TotalSeconds;
                    if (!finishing && previous > 2300 && position is >= 1300 and < 1600) wraps++;
                    trace.Add(new JsonObject { ["seconds"] = timer.Elapsed.TotalSeconds,
                        ["qpc"] = System.Diagnostics.Stopwatch.GetTimestamp(),
                        ["timelineMs"] = position, ["finishing"] = finishing, ["released"] = released,
                        ["spinning"] = spinning, ["playbackState"] = playbackState });
                    previous = position;
                    if (play.IsCompleted && released && poseEnded != null) break;
                    await _driver.WaitFrames(1);
                }
                Require(play.IsCompleted, "Tornado did not finish within the recording budget.");
                await play;
                await Wait(1.5);
                Require(TornadoAudioReleaseObserver.States.Remove(ownerId, out var release),
                    "Spin release was not observed.");
                // FMOD resets the timeline to zero after natural STOPPED.
                Require(release.State == 2,
                    $"Spin was released while native FMOD was in state {release.State} at {release.Position} ms.");
                audioEnded ??= (release.Qpc - timerStartedQpc) / (double)System.Diagnostics.Stopwatch.Frequency;
                File.WriteAllText(Path.Combine(_directory, $"tornado-audio-sustain-{hits}.json"),
                    new JsonObject { ["hits"] = hits, ["startedQpc"] = startedQpc,
                        ["endedQpc"] = System.Diagnostics.Stopwatch.GetTimestamp(),
                        ["frequency"] = System.Diagnostics.Stopwatch.Frequency, ["finishSeconds"] = finishSeconds,
                        ["loopWraps"] = wraps, ["released"] = Released(), ["poseEnded"] = poseEnded,
                        ["audioEnded"] = audioEnded, ["nativeStateAtRelease"] = release.State,
                        ["nativePositionAtRelease"] = release.Position, ["trace"] = trace }.ToJsonString());
                Require(finishSeconds != null, "The final hit never requested Outro.");
                int requiredWraps = Math.Max(0, (int)((finishSeconds!.Value - 1.369977) / 1.184218) - 1);
                Require(wraps >= requiredWraps, $"Tornado did not sustain its native FMOD loop: {wraps}/{requiredWraps} wraps.");
                Require(outroReached && audioEnded != null, "The actual FMOD instance never completed Outro.");
                Require(poseEnded != null, "The Tornado pose did not finish.");
                Require(audioEnded!.Value - finishSeconds!.Value is >= 1.05 and <= 1.3,
                    "The full Outro did not finish naturally after the impact request.");
                Require(Released(), "Sustained Spin handle leaked after the action/outro.");
                Cover("tornado-audio-sustain");
                return;
            }
            await Wait(seconds > 0 ? seconds : .6);
            var evidence = new JsonObject { ["mode"] = mode, ["beforePositionMs"] = Position() };
            if (mode == "pause")
            {
                AccessTools.Property(typeof(CombatManager), "IsPaused").SetValue(CombatManager.Instance, true);
                try
                {
                    await _driver.WaitFrames(3);
                    int before = Position();
                    await _driver.WaitFrames(30);
                    int after = Position();
                    evidence["pausedBeforeMs"] = before;
                    evidence["pausedAfterMs"] = after;
                    Require(Math.Abs(after - before) <= 1, "Paused FMOD Spin timeline advanced.");
                }
                finally { AccessTools.Property(typeof(CombatManager), "IsPaused").SetValue(CombatManager.Instance, false); }
            }
            else if (mode == "early-kill")
            {
                await CreatureCmd.Kill(Actor("enemy"));
                await _driver.WaitFrames(2);
                Require((bool)AccessTools.Field(type, "_finishing").GetValue(owner)!,
                    "Unexpected enemy death did not enter the Spin outro.");
            }
            else
            {
                _room.RemoveChild(owner);
                owner.QueueFree();
                Require(Released(), "Removing the audio owner did not release its FMOD handle.");
            }
            await play;
            await Wait(1.5);
            Require(Released(), "Spin handle leaked after the action/outro.");
            evidence["released"] = Released();
            File.WriteAllText(Path.Combine(_directory, "tornado-audio-" + mode + ".json"), evidence.ToJsonString());
            Cover("tornado-audio-" + mode);
        }
    }
}

// Observe the backend before production releases it; a released handle alone is not natural completion.
[HarmonyPatch]
internal static class TornadoAudioReleaseObserver
{
    internal static readonly Dictionary<ulong, (int State, int Position, long Qpc)> States = [];
    private static System.Reflection.MethodBase TargetMethod() =>
        AccessTools.Method("NinjaSlayer.Code.ExternalAnimations.SpinComboAudio:_ExitTree");

    private static void Prefix(Node __instance)
    {
        object handle = AccessTools.Field(__instance.GetType(), "_audio").GetValue(__instance)!;
        var raw = (GodotObject)AccessTools.Property(handle.GetType(), "RawInstance").GetValue(handle)!;
        States[__instance.GetInstanceId()] = (raw.Call("get_playback_state").AsInt32(),
            raw.Call("get_timeline_position").AsInt32(), System.Diagnostics.Stopwatch.GetTimestamp());
    }
}
