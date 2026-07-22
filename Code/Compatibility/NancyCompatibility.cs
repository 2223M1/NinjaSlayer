using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace NinjaSlayer.Code.Compatibility;

internal static class NancyCompatibility
{
    // RitsuLib 0.4.62 appends registered act ancients in this Harmony owner.
    public const string RitsuLibContentRegistryHarmonyId =
        "com.ritsukage.sts2-RitsuLib.framework-content-registry";

    private static readonly FieldInfo? Rooms = AccessTools.Field(typeof(ActModel), "_rooms");

    public static IReadOnlyList<CapabilityProbe> GetLoadedRunRepairProbes()
    {
        bool roomsAvailable = Rooms is
        {
            DeclaringType: not null,
            FieldType: not null
        } && Rooms.DeclaringType == typeof(ActModel)
          && Rooms.FieldType == typeof(RoomSet);
        return
        [
            CapabilityProbe.Required(
                "ActModel.rooms",
                roomsAvailable,
                roomsAvailable
                    ? "ActModel._rooms is a RoomSet"
                    : "ActModel._rooms is unavailable or no longer a RoomSet")
        ];
    }

    public static bool TryGetRooms(ActModel act, out RoomSet? rooms)
    {
        rooms = Rooms?.GetValue(act) as RoomSet;
        return rooms != null;
    }
}
