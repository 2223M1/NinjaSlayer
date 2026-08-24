using System.Reflection;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    public static string SupportedGameVersion => string.Join(
        "; ",
        GeneratedGameHostContracts.All.Select(profile => profile.GameVersion));

    internal readonly record struct RuntimePatchTarget(string IdSuffix, MethodInfo Method);
}
