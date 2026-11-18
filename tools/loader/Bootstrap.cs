using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace NinjaSlayer.Loader;

[ModInitializer(nameof(Initialize))]
public static class Bootstrap
{
    private const string ModId = "NinjaSlayer";

    public static void Initialize()
    {
        try
        {
            string loaderDirectory = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location)
                ?? throw new InvalidOperationException("NinjaSlayer loader has no assembly directory.");
            Guid hostMvid = typeof(ModManager).Assembly.ManifestModule.ModuleVersionId;
            SemanticVersion hostVersion = ReleaseInfoManager.Instance.SemVer
                ?? throw new InvalidDataException("NinjaSlayer could not read the native STS2 release version.");
            if (hostVersion.Prerelease is not null)
                throw new InvalidDataException($"NinjaSlayer does not support STS2 prerelease {hostVersion}.");
            string gameApiVersion = $"{hostVersion.Major}.{hostVersion.Minor}.{hostVersion.Patch}";
            BundleVariant variant = VariantBundleContract.Select(loaderDirectory, gameApiVersion);
            AssemblyLoadContext loadContext = AssemblyLoadContext.GetLoadContext(typeof(Bootstrap).Assembly)
                ?? AssemblyLoadContext.Default;
            Assembly implementation = loadContext.LoadFromAssemblyPath(variant.AssemblyPath);
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

            ModManager.AssociateAssemblyWithMod(ModId, implementation);

            Log.Info(
                $"[NinjaSlayer.Loader] Loaded {variant.Channel} implementation for STS2 {variant.GameApiVersion} " +
                $"(host MVID {hostMvid:D}).");
        }
        catch (Exception exception)
        {
            Log.Error($"[NinjaSlayer.Loader] NinjaSlayer initialization failed: {exception}");
            throw;
        }
    }
}
