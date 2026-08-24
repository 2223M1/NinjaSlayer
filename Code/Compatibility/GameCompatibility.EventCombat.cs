using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class EventCombat
    {
        private static readonly FieldInfo ActRooms = AccessTools.Field(typeof(ActModel), "_rooms")
            ?? throw new MissingFieldException(typeof(ActModel).FullName, "_rooms");
#if NINJASLAYER_CHANNEL_STABLE
        private static readonly FieldInfo EmbeddedCombatState =
            AccessTools.Field(typeof(EventModel), "_combatStateForCombatLayout")
            ?? throw new MissingFieldException(typeof(EventModel).FullName, "_combatStateForCombatLayout");
#else
        private static readonly FieldInfo CombatSynchronizer =
            AccessTools.Field(typeof(EventModel), "_combatSynchronizer")
            ?? throw new MissingFieldException(typeof(EventModel).FullName, "_combatSynchronizer");
        private static readonly PropertyInfo EmbeddedCombatState =
            AccessTools.Property(CombatSynchronizer.FieldType, "CombatStateForLayout")
            ?? throw new MissingMemberException(
                CombatSynchronizer.FieldType.FullName,
                "CombatStateForLayout");
#endif

        public static CombatState? GetEmbeddedCombatState(EventModel eventModel)
        {
#if NINJASLAYER_CHANNEL_STABLE
            return EmbeddedCombatState.GetValue(eventModel) as CombatState;
#else
            object? synchronizer = CombatSynchronizer.GetValue(eventModel);
            return synchronizer == null
                ? null
                : EmbeddedCombatState.GetValue(synchronizer) as CombatState;
#endif
        }

        public static int CountValidEvents(ActModel act, RunState runState) =>
            GetRoomSet(act).events.Count(eventModel =>
                eventModel.IsAllowed(runState)
                && !runState.VisitedEventIds.Contains(eventModel.Id));

        public static IReadOnlyList<EventModel> GetCurrentEvents(ActModel act) =>
            GetRoomSet(act).events;

        public static void SetAncient(ActModel act, AncientEventModel ancient) =>
            GetRoomSet(act).Ancient = ancient;

        public static EncounterModel? GetPreviousNormalEncounter(ActModel act)
        {
            RoomSet rooms = GetRoomSet(act);
            if (rooms.normalEncounters.Count == 0 || rooms.normalEncountersVisited <= 0)
            {
                return null;
            }

            int index = (rooms.normalEncountersVisited - 1) % rooms.normalEncounters.Count;
            return rooms.normalEncounters[index];
        }

        private static RoomSet GetRoomSet(ActModel act) =>
            ActRooms.GetValue(act) as RoomSet
            ?? throw new InvalidOperationException($"Act {act.Id} has no room set.");
    }
}
