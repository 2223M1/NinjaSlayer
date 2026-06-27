using System.Reflection;
using Godot.Bridge;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using STS2RitsuLib;
using STS2RitsuLib.Audio;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Patching.Core;

namespace NinjaSlayer.Scripts;

[ModInitializer(nameof(Init))]
public class Entry
{
    public const string ModId = "NinjaSlayer";
    public static readonly Logger Logger = RitsuLibFramework.CreateLogger(ModId);
    private static readonly Type[] GodotSceneScriptTypes =
    [
        typeof(NinjaSlayerNEnergyCounter),
        typeof(NinjaSlayerMegaLabel),
        typeof(NinjaSlayerNParticlesContainer),
        typeof(NinjaSlayerSpinPivot),
        typeof(NarakuVisualOverlay),
        typeof(NinjaSlayerTransitionOverlay)
    ];

    public static void Init()
    {
        GC.KeepAlive(GodotSceneScriptTypes);
        Log.Info("Mod initialized!");

        var assembly = Assembly.GetExecutingAssembly();
        RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);
        ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);

        RitsuLibFramework.CreateContentPack(ModId)
            .Character<NinjaSlayerCharacter>(character => character
                .AddStartingCard<StrikeNinjaSlayer>(4, 0)
                .AddStartingCard<DefendNinjaSlayer>(4, 1)
                .AddStartingCard<Meditation>(1, 2)
                .AddStartingCard<KarateStraight>(1, 3))
            .Apply();

        RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraight, CollapseFist>();

        var patcher = RitsuLibFramework.CreatePatcher(ModId, "core-patches");
        patcher.RegisterPatch<NinjaSlayerAnimationPatch>();
        patcher.RegisterPatch<NinjaSlayerDeathAnimPatch>();
        patcher.RegisterPatch<ReporterPassEventOptionPatch>();
        patcher.RegisterPatch<NarakuLifeHealthBarPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionSfxPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionPreloadPatch>();
        if (!patcher.PatchAll())
        {
            throw new InvalidOperationException("Critical NinjaSlayer patches failed to apply.");
        }

        RegisterFmodBanksIfPresent();
        ScriptManagerBridge.LookupScriptsInAssembly(assembly);
    }

    private static void RegisterFmodBanksIfPresent()
    {
        if (!Godot.FileAccess.FileExists(NinjaSlayerAudio.BankPath) || !Godot.FileAccess.FileExists(NinjaSlayerAudio.GuidMappingsPath))
        {
            Log.Warn($"FMOD bank files are missing. Expected {NinjaSlayerAudio.BankPath} and {NinjaSlayerAudio.GuidMappingsPath}. Audio events will remain unavailable until exported FMOD bank files are added.");
            return;
        }

        FmodStudioDeferredBankRegistration.RegisterBank(NinjaSlayerAudio.BankPath);
        FmodStudioDeferredBankRegistration.RegisterStudioGuidMappings(NinjaSlayerAudio.GuidMappingsPath);
        Logger.Info($"FMOD bank registered: {NinjaSlayerAudio.BankPath}");
    }
}
