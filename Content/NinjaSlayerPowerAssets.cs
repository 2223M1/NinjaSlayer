using NinjaSlayer.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Content;

public static class NinjaSlayerPowerAssets
{
    // ponytail: shared stand-in shown until a power gets dedicated art.
    private static readonly string PlaceholderIcon = NinjaSlayerAssetPaths.PowerImage("soar_power.png");

    public static PowerAssetProfile For(Type powerType) => new(
        IconPath: IconPathFor(powerType),
        BigIconPath: IconPathFor(powerType));

    private static string IconPathFor(Type powerType)
    {
        if (powerType == typeof(HellTornadoPower))
        {
            return NinjaSlayerAssetPaths.PowerImage("soar_power.png");
        }

        string path = NinjaSlayerAssetPaths.PowerImage($"{powerType.Name}.png");
        return Godot.FileAccess.FileExists(path) ? path : PlaceholderIcon;
    }
}
