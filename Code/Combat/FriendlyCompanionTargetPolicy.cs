namespace NinjaSlayer.Code.Combat;

internal static class FriendlyCompanionTargetPolicy
{
    public static bool SupportsCard(string typeName) => typeName is
        "Blaze" or
        "Concoct" or
        "Coordinate" or
        "DemonicShield" or
        "Fade" or
        "Intercept" or
        "Lift" or
        "Mimic";

    public static bool SupportsPotion(string typeName) => typeName is
        "BlockPotion" or
        "DexterityPotion" or
        "FlexPotion" or
        "FyshOil" or
        "HeartOfIron" or
        "LiquidBronze" or
        "LuckyTonic" or
        "MazalethsGift" or
        "RegenPotion" or
        "ShipInABottle" or
        "SpeedPotion" or
        "StrengthPotion";
}
