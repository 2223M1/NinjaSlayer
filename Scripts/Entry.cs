using System.Reflection;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using STS2RitsuLib;
using STS2RitsuLib.Audio;
using STS2RitsuLib.Content;
using STS2RitsuLib.Interop;

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
        typeof(NinjaSlayerNParticlesContainer)
    ];

    public static void Init()
    {
        var harmony = new Harmony("sts2.2223M1.NinjaSlayer");
        harmony.PatchAll();

        GC.KeepAlive(GodotSceneScriptTypes);
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);
        Log.Info("Mod initialized!");

        var assembly = Assembly.GetExecutingAssembly();
        RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);
        ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);
        RegisterStartingDeckOrder();
        RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraight, CollapseFist>();
        RegisterFmodBanksIfPresent();
    }

    private static void RegisterStartingDeckOrder()
    {
        ModContentRegistry content = RitsuLibFramework.GetContentRegistry(ModId);
        content.RegisterCharacterStarterCard(typeof(NinjaSlayerCharacter), typeof(StrikeNinjaSlayer), 4, 0);
        content.RegisterCharacterStarterCard(typeof(NinjaSlayerCharacter), typeof(DefendNinjaSlayer), 5, 1);
        content.RegisterCharacterStarterCard(typeof(NinjaSlayerCharacter), typeof(KarateStraight), 1, 2);
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
    }
}
