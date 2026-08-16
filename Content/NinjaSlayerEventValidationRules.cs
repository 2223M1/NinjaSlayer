using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Events;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models;

namespace NinjaSlayer.Content;

[RegisterSingleton]
public sealed class NinjaSlayerEventValidationRules : NinjaSlayerSingletonTemplate
{
    private static readonly IReadOnlySet<RoomType> EventRoomOnly =
        new HashSet<RoomType> { RoomType.Event };

    public NinjaSlayerEventValidationRules() : base(HookedSingletonModel.HookType.Run)
    {
    }

    public override IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(
        IReadOnlySet<RoomType> roomTypes)
    {
        if (CurrentRunState is not RunState runState)
        {
            return roomTypes;
        }

        EventModel[] candidates = GetCandidates(runState);
        return NinjaSlayerRunData.IsEventValidationEnabled(runState)
            && roomTypes.Contains(RoomType.Event)
            && candidates.Length > 0
            ? EventRoomOnly
            : roomTypes;
    }

    public override EventModel ModifyNextEvent(EventModel currentEvent)
    {
        if (CurrentRunState is not RunState runState
            || !NinjaSlayerRunData.IsEventValidationEnabled(runState))
        {
            return currentEvent;
        }

        EventModel[] candidates = GetCandidates(runState);
        return candidates.Length == 0
            ? currentEvent
            : runState.Rng.UpFront.NextItem(candidates)!;
    }

    private static EventModel[] GetCandidates(RunState runState) =>
        GameCompatibility.EventCombat.GetCurrentEvents(runState.Act)
            .Where(eventModel => eventModel is
                YamotoKokiCuteEvent or YukanoEvent or NarakuEvent or DarkNinjaEvent
                && eventModel.IsAllowed(runState)
                && !runState.VisitedEventIds.Contains(eventModel.Id))
            .ToArray();
}
