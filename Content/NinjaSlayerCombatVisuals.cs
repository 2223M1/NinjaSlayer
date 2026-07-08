using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCombatVisuals
{
    public const float AttackLungeDistance = 90f;
    public static readonly Vector2 BodySpriteBasePosition = new(-160f, -190f);
    public const float BodySpriteBaseScale = 0.33f;
    public const float NarakuFormYOffset = -50f;
    public const float ShadowNormalScale = 0.5f;
    public const float ShadowNarakuScale = 1f;

    public static float GetShadowScale(Creature creature) =>
        creature.HasPower<NarakuPower>()
            ? ShadowNarakuScale
            : ShadowNormalScale;
}
