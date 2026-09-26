using global::AutoAnthony;
using ChaosCardGenerator;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.AutoAnthony;

public static class Bridge
{
    internal const int CardCount = 90;
    internal static readonly Type[] CardTypes = typeof(Bridge).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(NinjaChaosCard).IsAssignableFrom(t))
        .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();

    public static void Register(ModPatcher patcher)
    {
        if (CardTypes.Length != CardCount) throw new InvalidOperationException("Generated card slots are incomplete.");
        ExternalComponentCharacterApi.Register(new(NinjaComponentSources.ProfileId, GeneratedCharacter.Ironclad, NinjaSlayer.Scripts.NinjaSlayerIds.EnergyColorName));
        ExternalComponentCharacterApi.RegisterRuntime(new(NinjaComponentSources.ProfileId, CardCount,
            slot => CardTypes[slot], () => NinjaAnthonyRun.Active, () => ModelDb.CardPool<NinjaSlayerCardPool>()));
        NinjaAnthonyRun.Register(patcher);
        NinjaComponentCardPatches.Register(patcher);
        patcher.RegisterPatch<InitializeComponents>();
    }

    internal static void RegisterComponents()
    {
        var recipes = NinjaComponentSources.Sources.Select(source => NinjaComponentSources.Recipe(source,
            ModelDb.GetById<CardModel>(ModelDb.GetId(source.Card)))).ToArray();
        var catalog = new ImmutableComponentCatalog(GeneratedCharacter.Ironclad, recipes);
        CardNameGenerator.RegisterExternalParts("ninjaslayer.components", NinjaComponentSources.Sources.Select(source =>
        {
            int lastSpace = source.English.LastIndexOf(' ');
            return new ComponentCardNameParts(source.Card.Name,
                source.Chinese.Chunk(2).Select(chars => new string(chars)).ToArray(),
                lastSpace < 0 ? "" : source.English[..(lastSpace + 1)], source.English[(lastSpace + 1)..], "");
        }));
        var request = new ComponentProfileRequest(NinjaComponentSources.ProfileId, GeneratedCharacter.Ironclad, false);
        var profile = new ComponentGenerationProfile(NinjaComponentSources.ProfileId, GeneratedCharacter.Ironclad, false,
            catalog, catalog, catalog, () => ComponentApi.CreateNativeOccurrencePolicy(catalog, false), ComponentApi.DefaultValuePolicy);
        ComponentPackageApi.Register(new("ninjaslayer.components", request, profile,
            LocalizedTexts: catalog.Atoms
                .Where(atom => !ExternalOperationTextRegistry.TryGet(atom.Template, atom.ChineseText, out _))
                .Select(atom => new ComponentLocalizedText(atom.Template, atom.ChineseText,
                    atom.LocalizedText!.RenderEnglish(atom.RuntimeSpec!)!)).ToArray(),
            Localizations: catalog.Atoms.Select(atom => new ComponentLocalizationRegistration(atom.SemanticId!,
                atom.LocalizedText!.ChineseTemplate, atom.LocalizedText.EnglishTemplate!)).ToArray(),
            Valuations: NinjaComponentValuation.FromSources(recipes)));
        var handler = new NinjaComponentRuntime();
        AutoAnthonyNativeCardApi.RegisterExternalAdapter(new NinjaNativeCardAdapter());
        ComponentRuntimeApi.RegisterPackage("ninjaslayer.components", catalog.Atoms
            .Where(atom => atom.RuntimeSpec!.Opcode == NinjaComponentSources.Opcode)
            .Select(atom => atom.RuntimeSpec!.Variant).Distinct()
            .Select(variant => new ComponentRuntimeRoute(NinjaComponentSources.Opcode, variant, handler)));
        NinjaAnthonyRun.InstallLibraryPreview();
    }

    private sealed class InitializeComponents : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_components";
        public static string Description => "Register component sources before Anthony freezes its profiles.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(ModelDb), nameof(ModelDb.InitIds))];
        [HarmonyPriority(Priority.First)]
        public static void Prefix() => RegisterComponents();
    }
}
