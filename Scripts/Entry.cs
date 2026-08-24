using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Combat;
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
    private static IDisposable? _runRulesSubscription;
    private static PreparedSafetyLifecycle? _preparedSafetyLifecycle;
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
        ModPatcher requiredPatcher = RitsuLibFramework.CreatePatcher(
            NinjaSlayerIds.ModId,
            nameof(Entry));
        try
        {
            requiredPatcher.RegisterPatches<GameplayPatchGroup>();
            requiredPatcher.RegisterPatches<OrobasSeaGlassPatchGroup>();
            requiredPatcher.RegisterPatches<CardResolutionPatchGroup>();
            requiredPatcher.RegisterPatches<PreparedGameplayPatchGroup>();
            requiredPatcher.RegisterPatches<ReporterPassPatchGroup>();
            requiredPatcher.RegisterPatches<NancyCandidateFilterPatchGroup>();
            requiredPatcher.RegisterPatches<FinisherCorePatchGroup>();
            requiredPatcher.RegisterPatches<FeedbackPatchGroup>();
            requiredPatcher.RegisterPatches<TelemetryIdentityPatchGroup>();

            bool requiredPatchFailure = false;
            bool requiredPatchesApplied = RitsuLibFramework.ApplyRequiredPatcher(
                requiredPatcher,
                disableMod: () => requiredPatchFailure = true,
                failureMessage: "Required NinjaSlayer patches failed; initialization will abort.");
            if (!requiredPatchesApplied || requiredPatchFailure)
            {
                LogPatchFailure(requiredPatcher);
                throw new InvalidOperationException("Required NinjaSlayer patch installation failed.");
            }

            if (!GameCompatibility.Finisher.CanProtectLethalDamage(out string finisherReason))
            {
                throw new InvalidOperationException(
                    $"Required NinjaSlayer finisher contract is unavailable: {finisherReason}");
            }

            _preparedSafetyLifecycle = PreparedSafetyLifecycle.Subscribe();
            RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);
            ModTypeDiscoveryHub.RegisterModAssembly(NinjaSlayerIds.ModId, assembly);

            using (RitsuLibFramework.BeginModDataRegistration(NinjaSlayerIds.ModId))
            {
                NinjaSlayerSettings.Register(NinjaSlayerIds.ModId);
                NinjaSlayerRunData.Register(NinjaSlayerIds.ModId);
            }
            _runRulesSubscription = NinjaSlayerRunRulesRuntime.Subscribe();

            RitsuLibFramework.CreateContentPack(NinjaSlayerIds.ModId)
                .Character<NinjaSlayerCharacter>(ConfigureStartingDeck)
                .Character<NinjaSlayerRedesignCharacter>(ConfigureRedesignStartingDeck)
                .HealthBarForecast<KarateHealthBarForecastSource>("karate")
                .Apply();

            RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraight, CollapseFist>();
            RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraightRedesignV1, CollapseFistRedesignV1>();
            NinjaSlayerBalanceTelemetry.Register();
        }
        catch (Exception exception)
        {
            _runRulesSubscription?.Dispose();
            _runRulesSubscription = null;
            _preparedSafetyLifecycle?.Dispose();
            _preparedSafetyLifecycle = null;
            TryRollbackPatcher(requiredPatcher, nameof(Entry));
            Logger.Error($"NinjaSlayer required initialization failed and patches were rolled back: {exception}");
            throw;
        }

        InstallOptionalPresentations();
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

    private static void ConfigureRedesignStartingDeck<TCharacter>(CharacterRegistrationEntry<TCharacter> character)
        where TCharacter : CharacterModel
    {
        character
            .AddStartingCard<StrikeNinjaSlayerRedesignV1>(4, 0)
            .AddStartingCard<DefendNinjaSlayerRedesignV1>(4, 1)
            .AddStartingCard<KarateStraightRedesignV1>(1, 2)
            .AddStartingCard<TurtleShellRedesignV1>(1, 3);
    }

    private static void InstallOptionalPresentations()
    {
        TryInstallOptionalPresentation(
            nameof(KaratePreviewPatchGroup),
            patcher => patcher.RegisterPatches<KaratePreviewPatchGroup>());
        TryInstallOptionalPresentation(
            nameof(TypographyPatchGroup),
            patcher => patcher.RegisterPatches<TypographyPatchGroup>());
        TryInstallOptionalPresentation(
            nameof(ChadoPresentationPatchGroup),
            patcher => patcher.RegisterPatches<ChadoPresentationPatchGroup>());
        TryInstallOptionalPresentation(
            nameof(CinematicInfrastructurePatchGroup),
            patcher => patcher.RegisterPatches<CinematicInfrastructurePatchGroup>());
        TryInstallOptionalPresentation(
            nameof(BossBurstPresentationPatchGroup),
            patcher => patcher.RegisterPatches<BossBurstPresentationPatchGroup>());
        TryInstallOptionalPresentation(
            nameof(PreparedUiPatchGroup),
            patcher => patcher.RegisterPatches<PreparedUiPatchGroup>());
        TryInstallOptionalPresentation(
            nameof(RapidCardResolutionPatchGroup),
            patcher =>
            {
                patcher.RegisterPatches<CombatPresentationPacingPatchGroup>();
                patcher.RegisterPatches<RapidCardResolutionPatchGroup>();
            },
            () =>
            [
                .. CombatPresentationPacingPatch.CreateDynamicPatches(),
                .. RapidCardResolutionStateMachinePatch.CreateDynamicPatches()
            ]);
        TryInstallOptionalPresentation(
            nameof(FinisherPresentationPatchGroup),
            patcher => patcher.RegisterPatches<FinisherPresentationPatchGroup>());
        TryInstallOptionalPresentation(
            nameof(TransitionCorePatchGroup),
            patcher =>
            {
                patcher.RegisterPatches<TransitionCorePatchGroup>();
                patcher.RegisterPatches<TransitionPresentationPatchGroup>();
                patcher.RegisterPatches<TransitionSmoothingPatchGroup>();
            },
            NinjaSlayerTransitionGcDeferralPatch.CreateDynamicPatches);
    }

    private static void TryInstallOptionalPresentation(
        string patcherName,
        Action<ModPatcher> registerPatches,
        Func<DynamicPatchInfo[]>? dynamicPatchFactory = null)
    {
        ModPatcher patcher = RitsuLibFramework.CreatePatcher(NinjaSlayerIds.ModId, patcherName);
        try
        {
            registerPatches(patcher);
            DynamicPatchInfo[] dynamicPatches = dynamicPatchFactory?.Invoke() ?? [];
            bool staticPatchesApplied = patcher.PatchAll();
            bool dynamicPatchesApplied = staticPatchesApplied
                && (dynamicPatches.Length == 0
                    || patcher.ApplyDynamicPatches(
                        dynamicPatches,
                        rollbackOnCriticalFailure: true));
            int expectedPatchCount = patcher.RegisteredPatchCount + dynamicPatches.Length;
            if (dynamicPatchesApplied && patcher.AppliedPatchCount == expectedPatchCount)
            {
                return;
            }

            TryRollbackPatcher(patcher, patcherName);
            Logger.Warn(
                $"Optional NinjaSlayer presentation '{patcherName}' was not installed: " +
                $"applied {patcher.AppliedPatchCount}/{expectedPatchCount} patches.");
        }
        catch (Exception exception)
        {
            TryRollbackPatcher(patcher, patcherName);
            Logger.Warn(
                $"Optional NinjaSlayer presentation '{patcherName}' was not installed: {exception}");
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

    private static void TryRollbackPatcher(ModPatcher patcher, string patcherName)
    {
        try
        {
            patcher.UnpatchAll();
        }
        catch (Exception rollbackException)
        {
            Logger.Error($"Patch rollback failed: {patcherName}; {rollbackException}");
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
