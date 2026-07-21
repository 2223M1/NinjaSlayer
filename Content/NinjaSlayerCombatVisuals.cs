using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCombatVisuals
{
    public const float CloseRangeApproachGap = 20f;
    public const float AttackLungeDistance = 90f;
    public const float SlowAttackLungeDistance = 120f;
    public const float SlowAttackLungeDuration = 0.25f;
    public static readonly Vector2 BodySpriteBasePosition = new(-160f, -190f);
    public const float BodySpriteBaseScale = 0.33f;
    public const float NarakuFormYOffset = -50f;
    public const float ShadowNormalScale = 0.5f;
    public const float ShadowNarakuScale = 1f;

    public static float GetShadowScale(Creature creature) =>
        NinjaSlayerFormState.IsFullyReleasedNaraku(creature)
            ? ShadowNarakuScale
            : ShadowNormalScale;
}
