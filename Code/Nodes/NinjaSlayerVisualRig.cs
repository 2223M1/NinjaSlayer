using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Nodes;

public static class NinjaSlayerVisualRig
{
    public const string AirborneAnchorName = "AirborneAnchor";
    public const string CinematicFocusName = "CinematicFocus";
    public const string ShadowNodeName = "Shadow";
    public const float SpinTextureSize = 1800f;
    public const float SpinPivotX = 1480f;

    public static float SpinPivotDeltaX => SpinPivotX - SpinTextureSize / 2f;

    /// <summary>
    /// Bottom of the tornado-fist vertical spin axis in centered-texture local pixels.
    /// </summary>
    public static Vector2 SpinAxisBottomOffset => new(SpinPivotDeltaX, SpinTextureSize / 2f);

    public static Node2D? GetAirborneAnchor(NCreatureVisuals? visuals)
    {
        return visuals?.GetNodeOrNull<Node2D>(AirborneAnchorName);
    }

    public static Sprite2D? GetBodySprite(NCreatureVisuals? visuals)
    {
        return visuals?.GetNodeOrNull<Sprite2D>("%Visuals");
    }

    public static Node2D? GetCinematicFocus(NCreatureVisuals? visuals)
    {
        return visuals?.GetNodeOrNull<Node2D>($"%{CinematicFocusName}");
    }

    public static void SyncShadowScale(Creature creature)
    {
        var visuals = NCombatRoom.Instance?.GetCreatureNode(creature)?.Visuals;
        var shadow = visuals?.GetNodeOrNull<Sprite2D>(ShadowNodeName);
        if (shadow == null)
        {
            return;
        }

        float scale = NinjaSlayerCombatVisuals.GetShadowScale(creature);
        shadow.Scale = new Vector2(scale, scale);
    }
}
