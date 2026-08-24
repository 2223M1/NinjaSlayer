using System.Reflection;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal readonly record struct RuntimePatchTarget(string IdSuffix, MethodInfo Method);
}
