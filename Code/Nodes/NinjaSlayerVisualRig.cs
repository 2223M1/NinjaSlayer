using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.Nodes;

public static class NinjaSlayerVisualRig
{
    public const string AirborneAnchorName = "AirborneAnchor";
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
}
