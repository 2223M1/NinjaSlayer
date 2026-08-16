using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class FriendlyCompanionTargetPolicyTests
{
    [Fact]
    public void AllowsOnlyCreatureBasedAllyEffects()
    {
        Assert.All(
            new[]
            {
                "Blaze", "Concoct", "Coordinate", "DemonicShield",
                "Fade", "Intercept", "Lift", "Mimic"
            },
            typeName => Assert.True(FriendlyCompanionTargetPolicy.SupportsCard(typeName)));
        Assert.All(
            new[] { "BelieveInYou", "Ignition", "Largesse", "Soulbound", "Tutor" },
            typeName => Assert.False(FriendlyCompanionTargetPolicy.SupportsCard(typeName)));

        Assert.All(
            new[]
            {
                "BlockPotion", "DexterityPotion", "FlexPotion", "FyshOil",
                "HeartOfIron", "LiquidBronze", "LuckyTonic", "MazalethsGift",
                "RegenPotion", "ShipInABottle", "SpeedPotion", "StrengthPotion"
            },
            typeName => Assert.True(FriendlyCompanionTargetPolicy.SupportsPotion(typeName)));
        Assert.All(
            new[] { "EnergyPotion", "FocusPotion", "PotionOfCapacity", "SwiftPotion" },
            typeName => Assert.False(FriendlyCompanionTargetPolicy.SupportsPotion(typeName)));
    }
}
