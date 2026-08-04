using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Code.Prepared;
using NinjaSlayer.Content;
using NinjaSlayer.Relics;
using STS2RitsuLib;
using STS2RitsuLib.Audio;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Scripts;

[ModInitializer(nameof(Init))]
public class Entry
{
    public static readonly Logger Logger = RitsuLibFramework.CreateLogger(NinjaSlayerIds.ModId);
    private static CoreActivationLease? _coreActivation;
    private static readonly Type[] GodotSceneScriptTypes =
    [
        typeof(NinjaSlayerSpinPivot),
        typeof(NinjaSlayerSpinMotionBlur),
        typeof(NinjaSlayerShadowController),
        typeof(NarakuVisualOverlay),
        typeof(NinjaSlayerTransitionOverlay),
        typeof(NNinjaSlayerGroundFireVfx),
        typeof(NYamotoKokiIaiPetalsVfx),
        typeof(NYamotoKokiIaiImpactVfx)
    ];

    public static void Init()
    {
        GC.KeepAlive(GodotSceneScriptTypes);
        Log.Info("Initializing NinjaSlayer...");

        var assembly = Assembly.GetExecutingAssembly();
        if (!TryActivateCoreCapabilities(out CoreActivationLease activation, out string failedCapabilityId))
        {
            PublishCapabilityStatus(
                NinjaSlayerCapabilityIds.CoreContent,
                CapabilityStatusEvaluator.DisabledByDependency(failedCapabilityId));
            Logger.Error(
                $"NinjaSlayer content registration was skipped because required capability " +
                $"'{failedCapabilityId}' is unavailable.");
            return;
        }

        _coreActivation = activation;
        try
        {
            RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);
            ModTypeDiscoveryHub.RegisterModAssembly(NinjaSlayerIds.ModId, assembly);

            using (RitsuLibFramework.BeginModDataRegistration(NinjaSlayerIds.ModId))
            {
                NinjaSlayerRunData.Register(NinjaSlayerIds.ModId);
            }

            NinjaSlayerBalanceTelemetry.Register();

            RitsuLibFramework.CreateContentPack(NinjaSlayerIds.ModId)
                .Character<NinjaSlayerCharacter>(ConfigureStartingDeck)
                .Apply();

            RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraight, CollapseFist>();
        }
        catch (Exception exception)
        {
            activation.Rollback(NinjaSlayerCapabilityIds.CoreContent);
            _coreActivation = null;
            PublishCapabilityStatus(
                NinjaSlayerCapabilityIds.CoreContent,
                new CapabilityStatus(
                    CapabilityState.Disabled,
                    $"Content registration failed before activation completed: {exception.Message}",
                    0));
            Logger.Error($"NinjaSlayer content registration failed and core patches were rolled back: {exception}");
            return;
        }

        PublishCapabilityStatus(
            NinjaSlayerCapabilityIds.CoreContent,
            new CapabilityStatus(
                CapabilityState.Enabled,
                "All required patches, lifecycle subscriptions, and content registrations succeeded.",
                activation.InstalledPatchCount));

        InstallOptionalBaseCapabilities();
        InstallFinisherCapability();
        InstallTransitionCapability();
        InstallFeedbackCapability();
        InstallCapability<TelemetryIdentityPatchGroup>(NinjaSlayerCapabilityIds.TelemetryIdentity);

        RegisterFmodBanksIfPresent();
        Log.Info("NinjaSlayer initialized.");
    }

    private static void ConfigureStartingDeck<TCharacter>(CharacterRegistrationEntry<TCharacter> character)
        where TCharacter : CharacterModel
    {
        character
            .AddStartingCard<StrikeNinjaSlayer>(4, 0)
            .AddStartingCard<DefendNinjaSlayer>(4, 1)
            .AddStartingCard<Meditation>(1, 2)
            .AddStartingCard<KarateStraight>(1, 3);
    }

    private static bool TryActivateCoreCapabilities(
        out CoreActivationLease activation,
        out string failedCapabilityId)
    {
        var candidate = new CoreActivationLease();
        failedCapabilityId = NinjaSlayerCapabilityIds.Gameplay;
        IReadOnlyList<CapabilityProbe> gameplayProbes = GameCompatibility.MapHistory.GetProbes()
            .Concat(GameCompatibility.EnemyAttackDodge.GetProbes())
            .ToArray();
        if (!TryInstallRequiredCapability<GameplayPatchGroup>(
                failedCapabilityId,
                candidate,
                gameplayProbes))
        {
            candidate.Rollback(failedCapabilityId);
            activation = null!;
            return false;
        }

        failedCapabilityId = NinjaSlayerCapabilityIds.OrobasSeaGlass;
        if (!TryInstallRequiredCapability<OrobasSeaGlassPatchGroup>(
                failedCapabilityId,
                candidate,
                GameCompatibility.OrobasSeaGlass.GetProbes()))
        {
            candidate.Rollback(failedCapabilityId);
            activation = null!;
            return false;
        }

        failedCapabilityId = NinjaSlayerCapabilityIds.CardResolution;
        if (!TryInstallRequiredCapability<CardResolutionPatchGroup>(failedCapabilityId, candidate))
        {
            candidate.Rollback(failedCapabilityId);
            activation = null!;
            return false;
        }

        failedCapabilityId = NinjaSlayerCapabilityIds.PreparedSafety;
        try
        {
            PreparedSafetyLifecycle preparedSafety = PreparedSafetyLifecycle.Subscribe();
            candidate.Track(failedCapabilityId, preparedSafety, preparedSafety.SubscriptionCount);
            PublishCapabilityStatus(
                failedCapabilityId,
                new CapabilityStatus(
                    CapabilityState.Enabled,
                    $"All {preparedSafety.SubscriptionCount} lifecycle subscriptions installed.",
                    preparedSafety.SubscriptionCount));
        }
        catch (Exception exception)
        {
            PublishCapabilityStatus(
                failedCapabilityId,
                new CapabilityStatus(
                    CapabilityState.Disabled,
                    $"Lifecycle subscription failed and was rolled back: {exception.Message}",
                    0));
            candidate.Rollback(failedCapabilityId);
            activation = null!;
            return false;
        }

        failedCapabilityId = NinjaSlayerCapabilityIds.PreparedGameplay;
        if (!TryInstallRequiredCapability<PreparedGameplayPatchGroup>(
                failedCapabilityId,
                candidate,
                GameCompatibility.Prepared.GetGameplayProbes()))
        {
            candidate.Rollback(failedCapabilityId);
            activation = null!;
            return false;
        }

        activation = candidate;
        return true;
    }

    private static void InstallOptionalBaseCapabilities()
    {
        InstallCapability<ReporterPassPatchGroup>(
            NinjaSlayerCapabilityIds.ReporterPass,
            GameCompatibility.ReporterPass.GetProbes());
        InstallCapability<NancyCandidateFilterPatchGroup>(NinjaSlayerCapabilityIds.NancyCandidateFilter);
        InstallCapability<KaratePreviewPatchGroup>(
            NinjaSlayerCapabilityIds.KaratePreview,
            GameCompatibility.KarateHealthBar.GetProbes());
        InstallCapability<TypographyPatchGroup>(
            NinjaSlayerCapabilityIds.Typography,
            GameCompatibility.Typography.GetProbes());
        InstallCapability<ChadoPresentationPatchGroup>(NinjaSlayerCapabilityIds.ChadoPresentation);
        InstallCapability<CinematicInfrastructurePatchGroup>(NinjaSlayerCapabilityIds.CinematicInfrastructure);
        InstallCapability<BossBurstPresentationPatchGroup>(
            NinjaSlayerCapabilityIds.BossBurstPresentation,
            GameCompatibility.BossBurst.GetProbes());

        if (NinjaSlayerPatchCapabilities.PreparedGameplayEnabled)
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

        InstallCapability<FinisherPresentationPatchGroup>(
            NinjaSlayerCapabilityIds.FinisherPresentation,
            GameCompatibility.Finisher.GetPresentationProbes());
        InstallCapability<FinisherCadencePatchGroup>(
            NinjaSlayerCapabilityIds.FinisherTornadoCadence,
            GameCompatibility.TornadoCadence.GetProbes(),
            TornadoFistFinisherCadencePatch.CreateDynamicPatches);
    }

    private static void InstallTransitionCapability()
    {
        CapabilityStatus transition = InstallCapability<TransitionCorePatchGroup>(
            NinjaSlayerCapabilityIds.TransitionCore,
            GameCompatibility.Transition.GetProbes());

        if (!transition.IsOperational)
        {
            DisableByDependency(
                NinjaSlayerCapabilityIds.TransitionPresentation,
                NinjaSlayerCapabilityIds.TransitionCore);
            DisableByDependency(
                NinjaSlayerCapabilityIds.TransitionLoadSmoothing,
                NinjaSlayerCapabilityIds.TransitionCore);
            return;
        }

        InstallCapability<TransitionPresentationPatchGroup>(
            NinjaSlayerCapabilityIds.TransitionPresentation,
            GameCompatibility.TransitionPresentation.GetProbes());
        InstallCapability<TransitionSmoothingPatchGroup>(
            NinjaSlayerCapabilityIds.TransitionLoadSmoothing,
            GameCompatibility.AssetLoading.GetProbes(),
            NinjaSlayerTransitionGcDeferralPatch.CreateDynamicPatches);
    }

    private static bool TryInstallRequiredCapability<TPatchGroup>(
        string capabilityId,
        CoreActivationLease activation,
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
            return false;
        }

        ModPatcher patcher = RitsuLibFramework.CreatePatcher(NinjaSlayerIds.ModId, capabilityId);
        bool succeeded = false;
        try
        {
            patcher.RegisterPatches<TPatchGroup>();
            succeeded = RitsuLibFramework.ApplyRequiredPatcher(
                patcher,
                disableMod: () => succeeded = false,
                failureMessage:
                    $"Required NinjaSlayer capability '{capabilityId}' failed; content registration will be skipped.");
        }
        catch (Exception exception)
        {
            TryRollbackPatcher(patcher, capabilityId);
            Logger.Error($"Required capability installation threw: {capabilityId}; {exception}");
        }

        CapabilityStatus status = CapabilityStatusEvaluator.EvaluatePatchResult(
            probeSnapshot,
            succeeded,
            patcher.RegisteredPatchCount,
            patcher.AppliedPatchCount);
        if (!succeeded)
        {
            LogPatchFailure(patcher);
            PublishCapabilityStatus(capabilityId, status);
            return false;
        }

        activation.Track(capabilityId, patcher);
        PublishCapabilityStatus(capabilityId, status);
        return true;
    }

    private static CapabilityStatus InstallCapability<TPatchGroup>(
        string capabilityId,
        IReadOnlyList<CapabilityProbe>? probes = null,
        Func<DynamicPatchInfo[]>? dynamicPatchFactory = null)
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
            if (patchAllSucceeded && dynamicPatchFactory is not null)
            {
                DynamicPatchInfo[] dynamicPatches = dynamicPatchFactory();
                patchAllSucceeded = patcher.ApplyDynamicPatches(
                    dynamicPatches,
                    rollbackOnCriticalFailure: true);
            }
        }
        catch (Exception exception)
        {
            patchAllSucceeded = false;
            TryRollbackPatcher(patcher, capabilityId);
            Logger.Error($"Capability installation threw: {capabilityId}; {exception}");
        }

        int registeredPatchCount = patcher.RegisteredPatchCount + patcher.RegisteredDynamicPatchCount;
        CapabilityStatus status = CapabilityStatusEvaluator.EvaluatePatchResult(
            probeSnapshot,
            patchAllSucceeded,
            registeredPatchCount,
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
            $"Patch installation failed for {patcher.PatcherName}: applied {patcher.AppliedPatchCount}/" +
            $"{patcher.RegisteredPatchCount + patcher.RegisteredDynamicPatchCount}.");

        foreach (ModPatchInfo patch in patcher.RegisteredPatches)
        {
            string paramList = patch.ParameterTypes is { Length: > 0 } types
                ? string.Join(", ", types.Select(t => t.Name))
                : "(none)";

            Logger.Error(
                $"  patch id={patch.Id}, critical={patch.IsCritical}, target={patch.TargetType?.Name}.{patch.MethodName}({paramList})");
        }
    }

    private static void TryRollbackPatcher(ModPatcher patcher, string capabilityId)
    {
        try
        {
            patcher.UnpatchAll();
        }
        catch (Exception rollbackException)
        {
            Logger.Error($"Capability rollback failed: {capabilityId}; {rollbackException}");
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

    private sealed class CoreActivationLease
    {
        private readonly List<ActivationResource> _resources = [];
        private bool _rolledBack;

        public int InstalledPatchCount => _resources.Sum(resource => resource.InstalledCount);

        public void Track(string capabilityId, ModPatcher patcher) =>
            _resources.Add(new ActivationResource(
                capabilityId,
                patcher.AppliedPatchCount,
                () => TryRollbackPatcher(patcher, capabilityId)));

        public void Track(string capabilityId, IDisposable subscription, int installedCount) =>
            _resources.Add(new ActivationResource(
                capabilityId,
                installedCount,
                subscription.Dispose));

        public void Rollback(string failedCapabilityId)
        {
            if (_rolledBack)
            {
                return;
            }

            _rolledBack = true;
            for (int index = _resources.Count - 1; index >= 0; index--)
            {
                ActivationResource resource = _resources[index];
                try
                {
                    resource.Release();
                }
                catch (Exception exception)
                {
                    Logger.Error(
                        $"Core activation rollback failed for {resource.CapabilityId}: {exception}");
                }

                PublishCapabilityStatus(
                    resource.CapabilityId,
                    CapabilityStatusEvaluator.RolledBack(failedCapabilityId));
            }

            _resources.Clear();
        }

        private sealed record ActivationResource(
            string CapabilityId,
            int InstalledCount,
            Action Release);
    }
}
