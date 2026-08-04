using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class BossBurstParticipationRegistry
{
    private static readonly Lock Gate = new();
    private static readonly ConditionalWeakTable<NCreature, ParticipantMarker> Participants = new();
    private static readonly ConditionalWeakTable<NCombatRoom, RoomState> Rooms = new();

    public static void Mark(
        NCreature boss,
        NCombatRoom sceneRoom,
        CombatRoom modelRoom,
        IRunState runState)
    {
        lock (Gate)
        {
            ParticipantMarker marker = new(new WeakReference<NCombatRoom>(sceneRoom));
            Participants.Remove(boss);
            Participants.Add(boss, marker);

            RoomState roomState = Rooms.GetValue(
                sceneRoom,
                _ => new RoomState(modelRoom, runState));
            roomState.ParticipantIds.Add(boss.GetInstanceId());
        }
    }

    public static bool Unmark(NCreature boss, NCombatRoom sceneRoom)
    {
        lock (Gate)
        {
            Participants.Remove(boss);
            if (Rooms.TryGetValue(sceneRoom, out RoomState? roomState))
            {
                roomState.ParticipantIds.Remove(boss.GetInstanceId());
                if (!roomState.HasParticipant)
                {
                    Rooms.Remove(sceneRoom);
                }

                return roomState.HasParticipant;
            }

            return false;
        }
    }

    public static bool TryClaimBossMusicStop(NCombatRoom sceneRoom)
    {
        lock (Gate)
        {
            if (!Rooms.TryGetValue(sceneRoom, out RoomState? roomState)
                || !roomState.HasParticipant
                || !ReferenceEquals(roomState.ModelRoom, roomState.RunState.CurrentRoom)
                || roomState.BossMusicStopClaimed)
            {
                return false;
            }

            roomState.BossMusicStopClaimed = true;
            return true;
        }
    }

    public static bool ShouldSuppressDeathFade(NCreature creature)
    {
        lock (Gate)
        {
            if (!Participants.TryGetValue(creature, out ParticipantMarker? participant)
                || !participant.SceneRoom.TryGetTarget(out NCombatRoom? sceneRoom)
                || !ReferenceEquals(NCombatRoom.Instance, sceneRoom)
                || !Rooms.TryGetValue(sceneRoom, out RoomState? roomState))
            {
                return false;
            }

            return BossBurstPresentationPolicy.ShouldSuppressDeathFade(
                roomState.HasParticipant,
                ReferenceEquals(roomState.ModelRoom, roomState.RunState.CurrentRoom));
        }
    }

    public static BossBurstCombatEndMusicDecision ResolveCombatEndMusic(
        out IRunState? runState)
    {
        runState = null;
        NCombatRoom? sceneRoom = NCombatRoom.Instance;
        if (sceneRoom == null)
        {
            return BossBurstCombatEndMusicDecision.PassThrough;
        }

        lock (Gate)
        {
            if (!Rooms.TryGetValue(sceneRoom, out RoomState? roomState))
            {
                return BossBurstCombatEndMusicDecision.PassThrough;
            }

            bool isCurrentRoom = ReferenceEquals(roomState.ModelRoom, roomState.RunState.CurrentRoom);
            BossBurstCombatEndMusicDecision decision =
                BossBurstPresentationPolicy.ResolveCombatEndMusic(
                    roomState.HasParticipant,
                    isCurrentRoom,
                    roomState.ModelRoom.RoomType == RoomType.Boss,
                    CombatManager.Instance.IsInProgress,
                    roomState.ActMusicRestoreAttempted);
            if (decision == BossBurstCombatEndMusicDecision.SuppressAndRestoreActMusic)
            {
                roomState.ActMusicRestoreAttempted = true;
                runState = roomState.RunState;
            }

            return decision;
        }
    }

    public static bool ShouldSuppressCombatEndMusicAfterFailure()
    {
        try
        {
            NCombatRoom? sceneRoom = NCombatRoom.Instance;
            if (sceneRoom == null)
            {
                return false;
            }

            lock (Gate)
            {
                if (!Rooms.TryGetValue(sceneRoom, out RoomState? roomState))
                {
                    return false;
                }

                return BossBurstPresentationPolicy.ResolveCombatEndMusic(
                        roomState.HasParticipant,
                        ReferenceEquals(roomState.ModelRoom, roomState.RunState.CurrentRoom),
                        roomState.ModelRoom.RoomType == RoomType.Boss,
                        CombatManager.Instance.IsInProgress,
                        roomState.ActMusicRestoreAttempted)
                    != BossBurstCombatEndMusicDecision.PassThrough;
            }
        }
        catch
        {
            return false;
        }
    }

    public static void Clear(NCombatRoom sceneRoom)
    {
        lock (Gate)
        {
            Rooms.Remove(sceneRoom);
        }
    }

    private sealed record ParticipantMarker(WeakReference<NCombatRoom> SceneRoom);

    private sealed class RoomState(CombatRoom modelRoom, IRunState runState)
    {
        public CombatRoom ModelRoom { get; } = modelRoom;
        public IRunState RunState { get; } = runState;
        public HashSet<ulong> ParticipantIds { get; } = [];
        public bool HasParticipant => ParticipantIds.Count > 0;
        public bool BossMusicStopClaimed { get; set; }
        public bool ActMusicRestoreAttempted { get; set; }
    }
}
