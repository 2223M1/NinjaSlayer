using System.Reflection;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    public const string SupportedGameVersion = "0.109.x";

    private static CapabilityProbe RequiredMember(string name, MemberInfo? member, string memberDescription) =>
        CapabilityProbe.Required(
            name,
            member != null,
            member != null ? "available" : $"{memberDescription} is unavailable");
}
