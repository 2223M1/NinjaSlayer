using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace NinjaSlayer.Code;

internal static class FreeControlDependency
{
    // Both the standalone mod and universal loader load from their own package directory.
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Mod dependencies must be loaded before host type discovery.")]
    internal static void Load()
    {
        Assembly owner = typeof(FreeControlDependency).Assembly;
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(owner)!;
        Assembly? physics = context.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Box2D.NET");
        physics ??= string.IsNullOrEmpty(owner.Location)
            ? context.LoadFromAssemblyName(new AssemblyName("Box2D.NET")) // Godot editor loads managed assemblies from streams.
            : context.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(owner.Location)!, "Box2D.NET.dll"));
        if (physics.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            != "1.0.0+5efc96def866edbb4e5a9368d84de5bf8c2dcaca")
            throw new InvalidOperationException("NinjaSlayer requires its packaged Box2D.NET 3.1.654 dependency.");
    }
}
