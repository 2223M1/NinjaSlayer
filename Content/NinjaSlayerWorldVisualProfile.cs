using Godot;
using STS2RitsuLib.Scaffolding.Characters.Visuals.Definition;
using STS2RitsuLib.Scaffolding.Visuals.Definition;

namespace NinjaSlayer.Content;

public static class NinjaSlayerWorldVisualProfile
{
    public static readonly CharacterWorldProceduralVisualSet Profile =
        CharacterWorldProceduralVisualSetBuilder.Create()
            .Merchant(cues => cues.Single("idle", Merchant.IdleTexturePath, Merchant.BodyStyle()))
            .RestSite(cues => cues.Single("relaxed", RestSite.IdleTexturePath, RestSite.BodyStyle()))
            .Build();

    public static class Merchant
    {
        public const float BodyScale = 1.1f;
        public const float BodyPositionX = -40f;
        public const float BodyPositionY = -100f;
        public const string IdleTexturePath =
            "res://NinjaSlayer/images/characters/ninja_slayer/merchant/ninja_slayer_merchant_idle.png";

        public static VisualNodeStyle BodyStyle() =>
            VisualNodeStyle.Create()
                .WithPosition(new Vector2(BodyPositionX, BodyPositionY))
                .WithScale(BodyScale);
    }

    public static class RestSite
    {
        // Compensate centered scaling so the 1111x1415 texture keeps its current top-left position.
        public const float BodyScale = 0.47f;
        public const float BodyPositionX = 49.995f;
        public const float BodyPositionY = -16.325f;
        public const string IdleTexturePath =
            "res://NinjaSlayer/images/characters/ninja_slayer/rest_site/ninja_slayer_rest_idle.png";

        public static VisualNodeStyle BodyStyle() =>
            VisualNodeStyle.Create()
                .WithPosition(new Vector2(BodyPositionX, BodyPositionY))
                .WithScale(BodyScale);
    }
}
