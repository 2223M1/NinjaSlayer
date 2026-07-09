namespace NinjaSlayer.Content;

public static class NinjaSlayerAssetPaths
{
    public const string Root = "res://NinjaSlayer";
    public const string ImagesRoot = Root + "/images";
    public const string CardImagesRoot = ImagesRoot + "/cards";
    public const string RelicImagesRoot = ImagesRoot + "/relics";
    public const string PowerImagesRoot = ImagesRoot + "/powers";
    public const string FmodRoot = Root + "/audio/fmod";

    public static string InMod(string relativePath) => Root + "/" + Normalize(relativePath);

    public static string Image(string relativePath) => ImagesRoot + "/" + Normalize(relativePath);

    public static string CardImage(string relativePath) => CardImagesRoot + "/" + Normalize(relativePath);

    public static string RelicImage(string relativePath) => RelicImagesRoot + "/" + Normalize(relativePath);

    public static string PowerImage(string relativePath) => PowerImagesRoot + "/" + Normalize(relativePath);

    public static string Fmod(string relativePath) => FmodRoot + "/" + Normalize(relativePath);

    private static string Normalize(string relativePath) => relativePath.Replace('\\', '/').TrimStart('/');
}
