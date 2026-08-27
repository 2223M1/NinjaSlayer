using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Code.Prepared;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
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
            requiredPatcher.RegisterPatch<NinjaSlayerAnimationPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerDebuffShakePatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerSurroundedFacingPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerAttackFacingPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerDeathAnimPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerAbandonDeathCapturePatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerAbandonVisualCreationPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerAbandonMerchantDeathPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerAbandonGameOverDeathFeedbackPatch>();
            requiredPatcher.RegisterPatch<NarakuLifeHealthBarLayoutPatch>();
            requiredPatcher.RegisterPatch<ArchitectDialogueSuppressionPatch>();
            requiredPatcher.RegisterPatch<ArchitectExecutionStartPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerReviveAnimPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerIncomingDamageCapturePatch>();
            requiredPatcher.RegisterPatch<BlackFlameDamagePatch>();
            requiredPatcher.RegisterPatch<KarateDamageWavePatch>();
            requiredPatcher.RegisterPatch<AncientEntranceEventOptionPatch>();
            requiredPatcher.RegisterPatch<AncientEntranceCreatureVisibilityPatch>();
            requiredPatcher.RegisterPatch<BossGreetingMusicPatch>();
            requiredPatcher.RegisterPatch<CardTransformShineSfxPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerSwipePowerStealPatch>();
            requiredPatcher.RegisterPatch<YamotoKokiAllyLayoutPatch>();
            requiredPatcher.RegisterPatch<YamotoKokiDynamicAllyLayoutPatch>();
            requiredPatcher.RegisterPatch<YamotoKokiFinishedCombatRestorePatch>();
            requiredPatcher.RegisterPatch<EventValidationRunGenerationPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerSingleplayerRunRulesCharacterPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerRunRulesCharacterPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerCanonicalCharacterIdPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerRunProgressIdentityPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerGameOverBadgeIdentityPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerCombatProgressIdentityPatch>();
            requiredPatcher.RegisterPatch<AetherEnergyPower.EnergyGainPatch>();
            requiredPatcher.RegisterPatch<SawatariActRoomGenerationPatch>();
            requiredPatcher.RegisterPatch<SawatariUnknownRoomTypeCapturePatch>();
            requiredPatcher.RegisterPatch<SawatariUnknownRoomRollPatch>();
            requiredPatcher.RegisterPatch<SawatariPullEventPatch>();
            requiredPatcher.RegisterPatch<SawatariRoomVisitPatch>();
            requiredPatcher.RegisterPatch<SawatariCombatEndGatePatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionInteractionPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionCardSelectedPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionCardDeselectedPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionPotionSelectedPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionPotionDeselectedPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionTargetManagerPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionCardCanPlayPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionCardAutoPlayPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionControllerCardTargetPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionPotionThrowPatch>();
            requiredPatcher.RegisterPatch<FriendlyCompanionPotionTargetPatch>();
            requiredPatcher.RegisterPatch<SawatariDuelDeathAnimationPatch>();
            requiredPatcher.RegisterPatch<SawatariDuelRewardsPatch>();
            requiredPatcher.RegisterPatch<EnemyAttackDodgeScopePatch>();
            requiredPatcher.RegisterPatch<EnemyAttackDodgeAnimationPatch>();
            requiredPatcher.RegisterPatch<AttackEvasionFeedbackScopePatch>();
            requiredPatcher.RegisterPatch<EvasionMoveScopePatch>();
            requiredPatcher.RegisterPatch<EvasionTargetHitVfxPatch>();
            requiredPatcher.RegisterPatch<EvasionSideHitVfxPatch>();
            requiredPatcher.RegisterPatch<EvasionFmodHitSfxPatch>();
            requiredPatcher.RegisterPatch<EvasionTemporaryHitSfxPatch>();
            requiredPatcher.RegisterPatch<EvasionCustomHitVfxPatch>();
            requiredPatcher.RegisterPatch<AttackEvasionDamagePatch>();
            requiredPatcher.RegisterPatch<EvasionPowerApplyPatch>();
            requiredPatcher.RegisterPatch<EvasionPowerModifyAmountPatch>();
            requiredPatcher.RegisterPatch<AttackIntentDamagePreviewPatch>();
            requiredPatcher.RegisterPatch<YamotoKokiIntentUpdatePatch>();
            requiredPatcher.RegisterPatch<YamotoKokiIntentGenerationPatch>();
            requiredPatcher.RegisterPatch<YamotoKokiLastEnemyDeathIntentPatch>();
            requiredPatcher.RegisterPatch<YamotoKokiOrigamiMissileHitSparkPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerEnemyAttackVfxBaselinePatch>();
            requiredPatcher.RegisterPatch<TargetedRelicFlashAnchorPatch>();
            requiredPatcher.RegisterPatch<OrobasSeaGlassCharacterPatch>();
            requiredPatcher.RegisterPatch<CardPlayResolutionBeforePatch>();
            requiredPatcher.RegisterPatch<CardPlayResolutionAfterPatch>();
            requiredPatcher.RegisterPatch<CardResolutionCleanupPatch>();
            requiredPatcher.RegisterPatch<PreparedDrawPatch>();
            requiredPatcher.RegisterPatch<ReporterPassEventOptionPatch>();
            requiredPatcher.RegisterPatch<NancyLeeCandidatePatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFinisherAttackCommandPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFinisherLethalDamagePatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFinisherPrimaryDamagePatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFinisherAfterCardPlayedPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFinisherCardPlayCleanupPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerDeathCompletionPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFeedbackOpenerPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFeedbackOpenPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFeedbackConfirmPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFeedbackSendPatch>();
            requiredPatcher.RegisterPatch<NinjaSlayerFeedbackClosePatch>();

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

            if (!FinisherProtectionService.CanProtectLethalDamage(out string finisherReason))
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
        try
        {
            NinjaSlayerBalanceTelemetry.Register();

            ModPatcher telemetryPatcher = RitsuLibFramework.CreatePatcher(
                NinjaSlayerIds.ModId,
                "telemetry-identity");
            try
            {
                telemetryPatcher.RegisterPatch<NinjaSlayerTelemetryIdentityLaunchPatch>();
                telemetryPatcher.RegisterPatch<NinjaSlayerTelemetryIdentityCleanupPatch>();
                bool patchesApplied = telemetryPatcher.PatchAll();
                int expectedPatchCount = telemetryPatcher.RegisteredPatchCount;
                if (!patchesApplied || telemetryPatcher.AppliedPatchCount != expectedPatchCount)
                {
                    TryRollbackPatcher(telemetryPatcher, telemetryPatcher.PatcherName);
                    Logger.Warn(
                        "Optional NinjaSlayer telemetry identity patches were not installed: " +
                        $"applied {telemetryPatcher.AppliedPatchCount}/{expectedPatchCount} patches.");
                }
            }
            catch (Exception exception)
            {
                TryRollbackPatcher(telemetryPatcher, telemetryPatcher.PatcherName);
                Logger.Warn(
                    $"Optional NinjaSlayer telemetry identity patches were not installed: {exception}");
            }
        }
        catch (Exception exception)
        {
            Logger.Warn(
                $"NinjaSlayer telemetry registration failed; identity patches were skipped: {exception}");
        }

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
            "KaratePreviewPatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<KarateCardPreviewTargetPatch>();
                patcher.RegisterPatch<KarateCardPreviewAllEnemiesPatch>();
                patcher.RegisterPatch<KarateCardPreviewClearPatch>();
                patcher.RegisterPatch<KarateHealthBarTextPreviewPatch>();
            });
        TryInstallOptionalPresentation(
            "TypographyPatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<NinjaSlayerCardTitleTypographyPatch>();
                patcher.RegisterPatch<NinjaSlayerInspectRelicTypographyPatch>();
            });
        TryInstallOptionalPresentation(
            "ChadoPresentationPatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<ChadoEnergyCostVisualPatch>();
                patcher.RegisterPatch<ChadoCardNodeLifecyclePatch>();
            });
        TryInstallOptionalPresentation(
            "CinematicInfrastructurePatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<CombatCinematicLayoutPatch>();
                patcher.RegisterPatch<ScreenShakeSuppressionPatch>();
                patcher.RegisterPatch<ScreenRumbleCinematicSuppressionPatch>();
                patcher.RegisterPatch<ScreenTraumaCinematicSuppressionPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionPreloadPatch>();
            });
        TryInstallOptionalPresentation(
            nameof(BossBurstPresentationPatchGroup),
            patcher => patcher.RegisterPatches<BossBurstPresentationPatchGroup>());
        TryInstallOptionalPresentation(
            "PreparedUiPatchGroup",
            patcher => patcher.RegisterPatch<PreparedDrawPileDisplayOrderPatch>());
        TryInstallOptionalPresentation(
            "RapidCardResolutionPatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<RapidCardResolutionScopePatch>();
                patcher.RegisterPatch<RapidPowerCardFlyPatch>();
                patcher.RegisterPatch<RapidMultiCardPlayPatch>();
            },
            () =>
            [
                .. CombatPresentationPacingPatch.CreateDynamicPatches(),
                .. RapidCardResolutionStateMachinePatch.CreateDynamicPatches()
            ]);
        TryInstallOptionalPresentation(
            "FinisherPresentationPatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<NinjaSlayerFinisherDeathStartPatch>();
                patcher.RegisterPatch<NinjaSlayerFinisherDamageNumberPatch>();
                patcher.RegisterPatch<NinjaSlayerFinisherCardVisualPatch>();
            });
        TryInstallOptionalPresentation(
            nameof(TransitionCorePatchGroup),
            patcher =>
            {
                patcher.RegisterPatches<TransitionCorePatchGroup>();
                patcher.RegisterPatch<NinjaSlayerTransitionRunPresentationPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionTeardownPresentationPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionCombatPresentationPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionAncientSetupPresentationPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionAncientHealPresentationPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionRunMusicPresentationPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionParameterizedSfxPresentationPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionLoopSfxPresentationPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionAssetLoadConcurrencyPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionAssetFinalizePatch>();
            });
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
