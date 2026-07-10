using System.Reflection;

namespace NinjaSlayer.Content;

public static class NinjaSlayerVersion
{
    public static string Current { get; } =
        typeof(NinjaSlayerVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(NinjaSlayerVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
