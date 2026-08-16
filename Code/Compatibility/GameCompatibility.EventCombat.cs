using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    public static EncounterModel ResolveEventCombatEncounter(
        EncounterModel canonicalEncounter)
    {
#if NINJASLAYER_CHANNEL_STABLE
        return canonicalEncounter.ToMutable();
#else
        return canonicalEncounter;
#endif
    }

    internal static class EventCombat
    {
        private static readonly FieldInfo? ActRooms = AccessTools.Field(typeof(ActModel), "_rooms");
        private static readonly MethodInfo? RunManagerGenerateRooms = AccessTools.Method(
            typeof(RunManager),
            nameof(RunManager.GenerateRooms),
            Type.EmptyTypes);
#if NINJASLAYER_CHANNEL_STABLE
        private static readonly FieldInfo? EmbeddedCombatState =
            AccessTools.Field(typeof(EventModel), "_combatStateForCombatLayout");
#else
        private static readonly FieldInfo? CombatSynchronizer =
            AccessTools.Field(typeof(EventModel), "_combatSynchronizer");
        private static readonly PropertyInfo? EmbeddedCombatState = CombatSynchronizer == null
            ? null
            : AccessTools.Property(CombatSynchronizer.FieldType, "CombatStateForLayout");
#endif

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            RequiredMember("event-combat.act-rooms", ActRooms, "ActModel._rooms"),
            RequiredMember(
                "event-combat.generate-rooms",
                RunManagerGenerateRooms,
                "RunManager.GenerateRooms()"),
#if NINJASLAYER_CHANNEL_STABLE
            RequiredMember(
                "event-combat.embedded-state",
                EmbeddedCombatState,
                "EventModel._combatStateForCombatLayout")
#else
            RequiredMember(
                "event-combat.synchronizer",
                CombatSynchronizer,
                "EventModel._combatSynchronizer"),
            RequiredMember(
                "event-combat.embedded-state",
                EmbeddedCombatState,
                "EventCombatSynchronizer.CombatStateForLayout")
#endif
        ];

        public static CombatState? GetEmbeddedCombatState(EventModel eventModel)
        {
#if NINJASLAYER_CHANNEL_STABLE
            return EmbeddedCombatState?.GetValue(eventModel) as CombatState;
#else
            object? synchronizer = CombatSynchronizer?.GetValue(eventModel);
            return synchronizer == null
                ? null
                : EmbeddedCombatState?.GetValue(synchronizer) as CombatState;
#endif
        }

        public static int CountValidEvents(ActModel act, RunState runState) =>
            GetRoomSet(act)?.events.Count(eventModel =>
                eventModel.IsAllowed(runState)
                && !runState.VisitedEventIds.Contains(eventModel.Id)) ?? 0;

        public static IReadOnlyList<EventModel> GetCurrentEvents(ActModel act) =>
            GetRoomSet(act)?.events ?? [];

        public static void SetAncient(ActModel act, AncientEventModel ancient)
        {
            if (GetRoomSet(act) is { } rooms)
            {
                rooms.Ancient = ancient;
            }
        }

        public static EncounterModel? GetPreviousNormalEncounter(ActModel act)
        {
            RoomSet? rooms = GetRoomSet(act);
            if (rooms == null || rooms.normalEncounters.Count == 0 || rooms.normalEncountersVisited <= 0)
            {
                return null;
            }

            int index = (rooms.normalEncountersVisited - 1) % rooms.normalEncounters.Count;
            return rooms.normalEncounters[index];
        }

        private static RoomSet? GetRoomSet(ActModel act) => ActRooms?.GetValue(act) as RoomSet;
    }
}
