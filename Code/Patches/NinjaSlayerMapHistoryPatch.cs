using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerMapHistoryIconPatch : IPatchMethod
{
    private static readonly FieldInfo MapPointRunState =
        AccessTools.Field(typeof(NMapPoint), "_runState")
        ?? throw new MissingFieldException(typeof(NMapPoint).FullName, "_runState");

    public static string PatchId => "ninjaslayer_map_history_icon";
    public static string Description =>
        "Align traveled unknown-room icons with NinjaSlayer's visited map coordinates.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NNormalMapPoint), "UpdateIcon")
    ];

    public static void Postfix(NNormalMapPoint __instance)
    {
        RunState? runState = MapPointRunState.GetValue(__instance) switch
        {
            null => null,
            RunState value => value,
            _ => throw new InvalidOperationException(
                "NMapPoint._runState has an unexpected runtime type.")
        };
        if (__instance.Point.PointType != MapPointType.Unknown
            || __instance.State != MapPointState.Traveled
            || runState is null
            || LocalContext.GetMe(runState)?.Character is not INinjaSlayerCharacter
            || runState.CurrentActIndex < 0
            || runState.CurrentActIndex >= runState.MapPointHistory.Count)
        {
            return;
        }

        IReadOnlyList<MapCoord> visited = runState.VisitedMapCoords;
        int visitedIndex = IndexOf(visited, __instance.Point.coord);
        if (visitedIndex < 0 || visited.Any(coord => !runState.Map.HasPoint(coord)))
        {
            return;
        }

        IReadOnlyList<MapPointHistoryEntry> history = runState.MapPointHistory[runState.CurrentActIndex];
        int historyIndex = VisitedMapHistoryAlignment.ResolveHistoryIndex(
            visited.Select(coord => (int)runState.Map.GetPoint(coord)!.PointType).ToArray(),
            history.Select(entry => (int)entry.MapPointType).ToArray(),
            visitedIndex);
        if (historyIndex < 0
            || historyIndex >= history.Count
            || history[historyIndex].Rooms.FirstOrDefault() is not { } room)
        {
            return;
        }

        TextureRect? icon = __instance.GetNodeOrNull<TextureRect>("%Icon");
        TextureRect? outline = __instance.GetNodeOrNull<TextureRect>("%Outline");
        if (icon == null || outline == null)
        {
            return;
        }

        icon.Texture = ResourceLoader.Load<Texture2D>(UnknownIconPath(room.RoomType));
        outline.Texture = ResourceLoader.Load<Texture2D>(UnknownOutlinePath(room.RoomType));
    }

    private static int IndexOf(IReadOnlyList<MapCoord> coordinates, MapCoord target)
    {
        for (int i = 0; i < coordinates.Count; i++)
        {
            if (coordinates[i] == target)
            {
                return i;
            }
        }

        return -1;
    }

    private static string UnknownIconPath(RoomType roomType)
    {
        string suffix = roomType switch
        {
            RoomType.Treasure => "unknown_chest",
            RoomType.Monster => "unknown_monster",
            RoomType.Shop => "unknown_shop",
            RoomType.Elite => "unknown_elite",
            _ => "unknown"
        };
        return ImageHelper.GetImagePath($"atlases/ui_atlas.sprites/map/icons/map_{suffix}.tres");
    }

    private static string UnknownOutlinePath(RoomType roomType)
    {
        string filename = roomType switch
        {
            RoomType.Treasure => "map_chest",
            RoomType.Monster => "map_monster",
            RoomType.Shop => "map_shop",
            RoomType.Elite => "map_elite",
            _ => "map_unknown"
        };
        return ImageHelper.GetImagePath($"atlases/compressed.sprites/map/{filename}_outline.tres");
    }
}
