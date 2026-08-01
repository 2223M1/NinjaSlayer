using System.Reflection;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    public static string SupportedGameVersion => string.Join(
        "; ",
        GeneratedGameHostContracts.All.Select(profile => profile.GameVersion));

    internal readonly record struct RuntimePatchTarget(string IdSuffix, MethodInfo Method);

    private static CapabilityProbe RequiredMember(string name, MemberInfo? member, string memberDescription) =>
        CapabilityProbe.Required(
            name,
            member != null,
            member != null ? "available" : $"{memberDescription} is unavailable");
}
