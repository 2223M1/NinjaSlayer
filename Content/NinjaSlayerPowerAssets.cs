using NinjaSlayer.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Content;

public static class NinjaSlayerPowerAssets
{
    // ponytail: shared stand-in shown until a power gets dedicated art. Swap in real PNGs by
    // dropping res://NinjaSlayer/images/powers/{PowerClassName}.png and they take over automatically.
    private const string PlaceholderIcon = "res://NinjaSlayer/images/powers/soar_power.png";

    public static PowerAssetProfile For(Type powerType) => new(
        IconPath: IconPathFor(powerType),
        BigIconPath: IconPathFor(powerType));

    private static string IconPathFor(Type powerType)
    {
        if (powerType == typeof(NinjaSlayerSoarPower))
        {
            return "res://NinjaSlayer/images/powers/soar_power.png";
        }

        string path = $"res://NinjaSlayer/images/powers/{powerType.Name}.png";
        return Godot.FileAccess.FileExists(path) ? path : PlaceholderIcon;
    }
}
