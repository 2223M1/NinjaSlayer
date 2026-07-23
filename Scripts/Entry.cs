using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Compatibility;
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
        InstallCapability<TelemetryIdentityPatchGroup>(NinjaSlayerCapabilityIds.TelemetryIdentity);

        RegisterFmodBanksIfPresent();
    }

    private static void InstallBaseCapabilities()
    {
        InstallCapability<GameplayPatchGroup>(NinjaSlayerCapabilityIds.Gameplay);
        InstallCapability<CardResolutionPatchGroup>(NinjaSlayerCapabilityIds.CardResolution);
        InstallCapability<ReporterPassPatchGroup>(
            NinjaSlayerCapabilityIds.ReporterPass,
            GameCompatibility.ReporterPass.GetProbes());
        InstallCapability<NancyCandidateFilterPatchGroup>(NinjaSlayerCapabilityIds.NancyCandidateFilter);
        InstallCapability<NancyLoadedRunRepairPatchGroup>(
            NinjaSlayerCapabilityIds.NancyLoadedRunRepair,
            NancyCompatibility.GetLoadedRunRepairProbes());
        InstallCapability<KaratePreviewPatchGroup>(
            NinjaSlayerCapabilityIds.KaratePreview,
            GameCompatibility.KarateHealthBar.GetProbes());
        InstallCapability<TypographyPatchGroup>(
            NinjaSlayerCapabilityIds.Typography,
            GameCompatibility.Typography.GetProbes());
        InstallCapability<ChadoPresentationPatchGroup>(NinjaSlayerCapabilityIds.ChadoPresentation);
        InstallCapability<CinematicInfrastructurePatchGroup>(NinjaSlayerCapabilityIds.CinematicInfrastructure);

        CapabilityStatus preparedSafety = InstallCapability<PreparedSafetyPatchGroup>(
            NinjaSlayerCapabilityIds.PreparedSafety,
            GameCompatibility.Prepared.GetSafetyProbes());
        if (!preparedSafety.IsOperational)
        {
            DisableByDependency(
                NinjaSlayerCapabilityIds.PreparedGameplay,
                NinjaSlayerCapabilityIds.PreparedSafety);
            DisableByDependency(
                NinjaSlayerCapabilityIds.PreparedUi,
                NinjaSlayerCapabilityIds.PreparedGameplay);
            return;
        }

        CapabilityStatus prepared = InstallCapability<PreparedGameplayPatchGroup>(
            NinjaSlayerCapabilityIds.PreparedGameplay,
            GameCompatibility.Prepared.GetGameplayProbes());
        if (prepared.IsOperational)
        {
            InstallCapability<PreparedUiPatchGroup>(
                NinjaSlayerCapabilityIds.PreparedUi,
                GameCompatibility.Prepared.GetUiProbes());
        }
        else
        {
            DisableByDependency(NinjaSlayerCapabilityIds.PreparedUi, NinjaSlayerCapabilityIds.PreparedGameplay);
        }
    }

    private static void InstallFinisherCapability()
    {
        CapabilityStatus finisher = InstallCapability<FinisherCorePatchGroup>(
            NinjaSlayerCapabilityIds.FinisherCore,
            GameCompatibility.Finisher.GetProbes());

        if (!finisher.IsOperational)
        {
            DisableByDependency(
                NinjaSlayerCapabilityIds.FinisherPresentation,
                NinjaSlayerCapabilityIds.FinisherCore);
            DisableByDependency(
                NinjaSlayerCapabilityIds.FinisherTornadoCadence,
                NinjaSlayerCapabilityIds.FinisherCore);
            return;
        }

        InstallCapability<FinisherPresentationPatchGroup>(NinjaSlayerCapabilityIds.FinisherPresentation);
        InstallCapability<FinisherCadencePatchGroup>(
            NinjaSlayerCapabilityIds.FinisherTornadoCadence,
            GameCompatibility.TornadoCadence.GetProbes());
    }

    private static void InstallTransitionCapability()
    {
        CapabilityStatus transition = InstallCapability<TransitionCorePatchGroup>(
            NinjaSlayerCapabilityIds.TransitionCore,
            GameCompatibility.Transition.GetProbes());

        if (!transition.IsOperational)
        {
            DisableByDependency(
                NinjaSlayerCapabilityIds.TransitionLoadSmoothing,
                NinjaSlayerCapabilityIds.TransitionCore);
            return;
        }

        InstallCapability<TransitionSmoothingPatchGroup>(
            NinjaSlayerCapabilityIds.TransitionLoadSmoothing,
            GameCompatibility.AssetLoading.GetProbes());
    }

    private static CapabilityStatus InstallCapability<TPatchGroup>(
        string capabilityId,
        IReadOnlyList<CapabilityProbe>? probes = null)
        where TPatchGroup : IModPatches
    {
        CapabilityProbe[] probeSnapshot = probes?.ToArray() ?? [];
        if (probeSnapshot.Any(probe => probe.IsRequired && !probe.IsAvailable))
        {
            CapabilityStatus disabled = CapabilityStatusEvaluator.EvaluatePatchResult(
                probeSnapshot,
                patchAllSucceeded: false,
                registeredPatchCount: 0,
                appliedPatchCount: 0);
            PublishCapabilityStatus(capabilityId, disabled);
            return disabled;
        }

        ModPatcher patcher = RitsuLibFramework.CreatePatcher(NinjaSlayerIds.ModId, capabilityId);
        bool patchAllSucceeded;
        try
        {
            patcher.RegisterPatches<TPatchGroup>();
            patchAllSucceeded = patcher.PatchAll();
        }
        catch (Exception exception)
        {
            patchAllSucceeded = false;
            try
            {
                patcher.UnpatchAll();
            }
            catch (Exception rollbackException)
            {
                Logger.Error($"Capability rollback failed: {capabilityId}; {rollbackException}");
            }

            Logger.Error($"Capability installation threw: {capabilityId}; {exception}");
        }

        CapabilityStatus status = CapabilityStatusEvaluator.EvaluatePatchResult(
            probeSnapshot,
            patchAllSucceeded,
            patcher.RegisteredPatchCount,
            patcher.AppliedPatchCount);
        if (!patchAllSucceeded)
        {
            LogPatchFailure(patcher);
        }

        PublishCapabilityStatus(capabilityId, status);
        return status;
    }

    private static void InstallFeedbackCapability()
    {
        InstallCapability<FeedbackPatchGroup>(
            NinjaSlayerCapabilityIds.Feedback,
            GameCompatibility.Feedback.GetProbes());
    }

    private static void DisableByDependency(string capabilityId, string dependencyId)
    {
        CapabilityStatus status = CapabilityStatusEvaluator.DisabledByDependency(dependencyId);
        PublishCapabilityStatus(capabilityId, status);
    }

    private static void PublishCapabilityStatus(string capabilityId, CapabilityStatus status)
    {
        NinjaSlayerCapabilityRegistry.Current.Publish(capabilityId, status);
        Version? gameVersion = typeof(MegaCrit.Sts2.Core.Nodes.NGame).Assembly.GetName().Version;
        Version? ritsuVersion = typeof(RitsuLibFramework).Assembly.GetName().Version;
        string message =
            $"NinjaSlayer capability {status.State.ToString().ToLowerInvariant()}: {capabilityId}; " +
            $"patches={status.InstalledPatchCount}; reason={status.Reason}; " +
            $"game={gameVersion}; RitsuLib={ritsuVersion}.";

        if (status.State == CapabilityState.Enabled)
        {
            Logger.Info(message);
        }
        else
        {
            Logger.Warn(message);
        }
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
