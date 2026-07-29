using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Content;

public static class NinjaSlayerPowerAssets
{
    public static PowerAssetProfile For(Type powerType)
    {
        string path = NinjaSlayerAssetPaths.PowerImage($"{powerType.Name}.png");
        return new PowerAssetProfile(IconPath: path, BigIconPath: path);
    }
}
