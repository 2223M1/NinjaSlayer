using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Transition;

internal static class NinjaSlayerTransitionPaths
{
    public static bool IsModPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (path == NinjaSlayerAssetProfile.CharacterSelectTransitionMaterialPath)
        {
            return true;
        }

        return path.Contains("ninja_slayer", StringComparison.OrdinalIgnoreCase)
            && path.Contains("transition", StringComparison.OrdinalIgnoreCase);
    }
}
