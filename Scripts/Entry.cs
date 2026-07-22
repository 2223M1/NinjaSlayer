using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Relics;
using STS2RitsuLib;
using STS2RitsuLib.Audio;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Scripts;

[ModInitializer(nameof(Init))]
public class Entry
{
    public static readonly Logger Logger = RitsuLibFramework.CreateLogger(NinjaSlayerIds.ModId);
    private static readonly Type[] GodotSceneScriptTypes =
    [
        typeof(NinjaSlayerSpinPivot),
        typeof(NinjaSlayerSpinMotionBlur),
        typeof(NinjaSlayerShadowController),
        typeof(NarakuVisualOverlay),
        typeof(NinjaSlayerTransitionOverlay),
        typeof(NNinjaSlayerGroundFireVfx)
    ];

    public static void Init()
    {
        GC.KeepAlive(GodotSceneScriptTypes);
        Log.Info("Mod initialized!");

        var assembly = Assembly.GetExecutingAssembly();
        RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);
        ModTypeDiscoveryHub.RegisterModAssembly(NinjaSlayerIds.ModId, assembly);

        using (RitsuLibFramework.BeginModDataRegistration(NinjaSlayerIds.ModId))
        {
            NinjaSlayerRunData.Register(NinjaSlayerIds.ModId);
        }

        NinjaSlayerBalanceTelemetry.Register();

        RitsuLibFramework.CreateContentPack(NinjaSlayerIds.ModId)
            .Character<NinjaSlayerCharacter>(character => character
                .AddStartingCard<StrikeNinjaSlayer>(4, 0)
                .AddStartingCard<DefendNinjaSlayer>(4, 1)
                .AddStartingCard<Meditation>(1, 2)
                .AddStartingCard<KarateStraight>(1, 3))
            .Character<NinjaSlayerDebugCharacter>(character => character
                .AddStartingCard<StrikeNinjaSlayer>(4, 0)
                .AddStartingCard<DefendNinjaSlayer>(4, 1)
                .AddStartingCard<Meditation>(1, 2)
                .AddStartingCard<KarateStraight>(1, 3)
                .AddStartingRelic<ChadoBreathingRelic>(1, 0))
            .Apply();

        RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraight, CollapseFist>();

        InstallBaseCapabilities();
        InstallFinisherCapability();
        InstallTransitionCapability();
        InstallFeedbackCapability();

        RegisterFmodBanksIfPresent();
    }

    private static void InstallBaseCapabilities()
    {
        NinjaSlayerPatchCapabilities.GameplayEnabled =
            InstallCapability<GameplayPatchGroup>("gameplay");
        NinjaSlayerPatchCapabilities.CardResolutionEnabled =
            InstallCapability<CardResolutionPatchGroup>("card-resolution");
        InstallCapability<EventCompatibilityPatchGroup>("event-compatibility");
        InstallCapability<CombatUiPatchGroup>("combat-ui");
        InstallCapability<CinematicInfrastructurePatchGroup>("cinematic-infrastructure");

        if (PreparedDrawCompatibility.CanInstall(out string missingMember))
        {
            NinjaSlayerPatchCapabilities.PreparedEnabled =
                InstallCapability<PreparedPatchGroup>("prepared");
            if (NinjaSlayerPatchCapabilities.PreparedEnabled)
            {
                InstallCapability<PreparedUiPatchGroup>("prepared-ui");
            }
        }
        else
        {
            Logger.Warn($"NinjaSlayer capability disabled: prepared; missing={missingMember}.");
        }

        if (!ReporterPassEventOptionPatch.IsAvailable)
        {
            Logger.Warn("Reporter Pass event option disabled: EventModel.SetEventFinished is unavailable.");
        }
    }

    private static void InstallFinisherCapability()
    {
        NinjaSlayerPatchCapabilities.FinisherEnabled =
            InstallCapability<FinisherCorePatchGroup>("finisher-core");

        if (!NinjaSlayerPatchCapabilities.FinisherEnabled)
        {
            return;
        }

        InstallCapability<FinisherPresentationPatchGroup>("finisher-presentation");

        if (TornadoFistFinisherCadencePatch.CanInstall(out string missingMember))
        {
            InstallCapability<FinisherCadencePatchGroup>("finisher-tornado-cadence");
        }
        else
        {
            Logger.Warn(
                $"NinjaSlayer capability disabled: finisher-tornado-cadence; missing={missingMember}.");
        }
    }

    private static void InstallTransitionCapability()
    {
        NinjaSlayerPatchCapabilities.TransitionEnabled =
            InstallCapability<TransitionCorePatchGroup>("transition-core");

        if (!NinjaSlayerPatchCapabilities.TransitionEnabled)
        {
            return;
        }

        bool assetFinalizeAvailable = NinjaSlayerTransitionAssetFinalizePatch.CanInstall(out string assetMissing);
        bool gcDeferralAvailable = NinjaSlayerTransitionGcDeferralPatch.CanInstall(out string gcMissing);
        if (!assetFinalizeAvailable || !gcDeferralAvailable)
        {
            string missing = string.IsNullOrEmpty(assetMissing) ? gcMissing : assetMissing;
            Logger.Warn($"NinjaSlayer capability disabled: transition-load-smoothing; missing={missing}.");
            return;
        }

        NinjaSlayerPatchCapabilities.TransitionLoadSmoothingEnabled =
            InstallCapability<TransitionSmoothingPatchGroup>("transition-load-smoothing");
    }

    private static bool InstallCapability<TPatchGroup>(string capability)
        where TPatchGroup : IModPatches
    {
        ModPatcher patcher = RitsuLibFramework.CreatePatcher(NinjaSlayerIds.ModId, capability);
        patcher.RegisterPatches<TPatchGroup>();
        if (patcher.PatchAll())
        {
            Logger.Info($"NinjaSlayer capability enabled: {capability}.");
            return true;
        }

        LogPatchFailure(patcher);
        Version? gameVersion = typeof(MegaCrit.Sts2.Core.Nodes.NGame).Assembly.GetName().Version;
        Version? ritsuVersion = typeof(RitsuLibFramework).Assembly.GetName().Version;
        Logger.Warn(
            $"NinjaSlayer capability disabled: {capability}; " +
            $"game={gameVersion}, RitsuLib={ritsuVersion}.");
        return false;
    }

    private static void InstallFeedbackCapability()
    {
        if (!NinjaSlayerFeedbackConfirmPatch.IsAvailable)
        {
            Logger.Warn(
                "NinjaSlayer capability disabled: feedback; " +
                "NSendFeedbackScreen.SendButtonSelected is unavailable.");
            return;
        }

        NinjaSlayerPatchCapabilities.FeedbackEnabled =
            InstallCapability<FeedbackPatchGroup>("feedback");
    }

    private static void LogPatchFailure(ModPatcher patcher)
    {
        Logger.Error(
            $"PatchAll failed for {patcher.PatcherName}: applied {patcher.AppliedPatchCount}/{patcher.RegisteredPatchCount}.");

        foreach (ModPatchInfo patch in patcher.RegisteredPatches)
        {
            string paramList = patch.ParameterTypes is { Length: > 0 } types
                ? string.Join(", ", types.Select(t => t.Name))
                : "(none)";

            Logger.Error(
                $"  patch id={patch.Id}, critical={patch.IsCritical}, target={patch.TargetType?.Name}.{patch.MethodName}({paramList})");
        }
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
