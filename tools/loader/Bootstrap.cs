using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace NinjaSlayer.Loader;

[ModInitializer(nameof(Initialize))]
public static class Bootstrap
{
    private const string ModId = "NinjaSlayer";
    private static Assembly? _implementationAssembly;

    public static void Initialize()
    {
        try
        {
            string loaderDirectory = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location)
                ?? throw new InvalidOperationException("NinjaSlayer loader has no assembly directory.");
            Guid hostMvid = typeof(ModManager).Assembly.ManifestModule.ModuleVersionId;
            BundleVariant variant = VariantBundleContract.Select(loaderDirectory, hostMvid);
            AssemblyLoadContext loadContext = AssemblyLoadContext.GetLoadContext(typeof(Bootstrap).Assembly)
                ?? AssemblyLoadContext.Default;
            Assembly implementation = loadContext.LoadFromAssemblyPath(variant.AssemblyPath);
            MethodInfo? associateAssembly = typeof(ModManager).GetMethod(
                "AssociateAssemblyWithMod",
                BindingFlags.Public | BindingFlags.Static,
                null,
                [typeof(string), typeof(Assembly)],
                null);
            var initializers = implementation.GetTypes()
                .Select(type => (Type: type, Attribute: type.GetCustomAttribute<ModInitializerAttribute>()))
                .Where(candidate => candidate.Attribute is not null)
                .ToArray();
            if (initializers.Length != 1)
            {
                throw new InvalidDataException(
                    $"Expected one NinjaSlayer implementation initializer, found {initializers.Length}.");
            }
            (Type type, ModInitializerAttribute? attribute) = initializers[0];
            MethodInfo initializer = type.GetMethod(
                attribute!.initializerMethod,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(type.FullName, attribute.initializerMethod);
            initializer.Invoke(null, null);

            if (associateAssembly is not null)
            {
                associateAssembly.Invoke(null, [ModId, implementation]);
            }
            else
            {
                // Stable assigns Mod.assembly after initializers return, so replace it from this callback.
                _implementationAssembly = implementation;
                ModManager.OnModDetected += AssociateLegacyAssembly;
            }

            Log.Info(
                $"[NinjaSlayer.Loader] Loaded {variant.Channel} implementation for STS2 {variant.GameApiVersion} " +
                $"({variant.ModuleMvid:D}).");
        }
        catch (Exception exception)
        {
            ModManager.OnModDetected -= AssociateLegacyAssembly;
            _implementationAssembly = null;
            Log.Error($"[NinjaSlayer.Loader] NinjaSlayer initialization failed: {exception}");
            throw;
        }
    }

    private static void AssociateLegacyAssembly(Mod mod)
    {
        if (!string.Equals(mod.manifest?.id, ModId, StringComparison.Ordinal))
        {
            return;
        }

        ModManager.OnModDetected -= AssociateLegacyAssembly;
        Assembly implementation = _implementationAssembly
            ?? throw new InvalidOperationException("NinjaSlayer implementation assembly was not loaded.");
        _implementationAssembly = null;
        FieldInfo assemblyField = mod.GetType().GetField(
            "assembly",
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(mod.GetType().FullName, "assembly");
        if (assemblyField.FieldType != typeof(Assembly))
        {
            throw new InvalidDataException("The stable Mod.assembly contract has changed.");
        }

        assemblyField.SetValue(mod, implementation);
        Log.Info("[NinjaSlayer.Loader] Associated the implementation assembly with the stable host mod.");
    }
}
