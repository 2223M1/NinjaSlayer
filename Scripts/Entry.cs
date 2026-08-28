using System.Linq;
using System.Reflection;
using HarmonyLib;
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

            int expectedRequiredPatchCount = requiredPatcher.RegisteredPatchCount
                + requiredPatcher.RegisteredDynamicPatchCount;
            bool requiredPatchFailure = false;
            bool requiredPatchesApplied = RitsuLibFramework.ApplyRequiredPatcher(
                requiredPatcher,
                disableMod: () => requiredPatchFailure = true,
                failureMessage: "Required NinjaSlayer patches failed; initialization will abort.");
            if (!requiredPatchesApplied
                || requiredPatchFailure
                || requiredPatcher.AppliedPatchCount != expectedRequiredPatchCount)
            {
                LogPatchFailure(requiredPatcher);
                throw new InvalidOperationException(
                    "Required NinjaSlayer patch installation failed or was incomplete: "
                    + $"applied {requiredPatcher.AppliedPatchCount}/{expectedRequiredPatchCount} patches.");
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
                .Card<NinjaSlayerCardPool, OneBodyOneSoul>()
                .Card<NinjaSlayerRedesignCardPool, OneBodyOneSoul>()
                .Card<NinjaSlayerCardPool, ZazenDrink>()
                .Card<NinjaSlayerRedesignCardPool, ZazenDrink>()
                .HealthBarForecast<KarateHealthBarForecastSource>("karate")
                .Apply();

            RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraight, CollapseFist>();
            RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraightRedesignV1, CollapseFistRedesignV1>();
        }
        catch (Exception exception)
        {
            var failures = new List<Exception> { exception };
            foreach (IDisposable? subscription in new IDisposable?[]
                     {
                         _runRulesSubscription,
                         _preparedSafetyLifecycle
                     })
            {
                try
                {
                    subscription?.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    failures.Add(cleanupFailure);
                }
            }
            _runRulesSubscription = null;
            _preparedSafetyLifecycle = null;

            Exception? rollbackFailure = RollbackPatcherVerified(requiredPatcher, nameof(Entry));
            if (rollbackFailure is not null)
            {
                failures.Add(rollbackFailure);
            }

            Logger.Error(
                rollbackFailure is null
                    ? $"NinjaSlayer required initialization failed; required patches were verified as rolled back: {exception}"
                    : $"NinjaSlayer required initialization failed and required patch rollback was incomplete: {exception}");
            if (failures.Count == 1)
            {
                throw;
            }

            throw new AggregateException(
                "NinjaSlayer required initialization and cleanup failed.",
                failures);
        }

        InstallOptionalPresentations();
        InstallOptionalTelemetry();

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

    private static void InstallOptionalTelemetry()
    {
        try
        {
            NinjaSlayerBalanceTelemetry.Register();
        }
        catch (Exception exception)
        {
            Logger.Warn(
                $"NinjaSlayer telemetry registration failed; identity patches were skipped: {exception}");
            return;
        }

        TryInstallOptionalPatches(
            "telemetry-identity",
            patcher =>
            {
                patcher.RegisterPatch<NinjaSlayerTelemetryIdentityLaunchPatch>();
                patcher.RegisterPatch<NinjaSlayerTelemetryIdentityCleanupPatch>();
            });
    }

    private static void InstallOptionalPresentations()
    {
        TryInstallOptionalPatches(
            "KaratePreviewPatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<KarateCardPreviewTargetPatch>();
                patcher.RegisterPatch<KarateCardPreviewAllEnemiesPatch>();
                patcher.RegisterPatch<KarateCardPreviewClearPatch>();
                patcher.RegisterPatch<KarateHealthBarTextPreviewPatch>();
            });
        TryInstallOptionalPatches(
            "TypographyPatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<NinjaSlayerCardTitleTypographyPatch>();
                patcher.RegisterPatch<NinjaSlayerInspectRelicTypographyPatch>();
            });
        TryInstallOptionalPatches(
            "ChadoPresentationPatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<ChadoEnergyCostVisualPatch>();
                patcher.RegisterPatch<ChadoCardNodeLifecyclePatch>();
            });
        TryInstallOptionalPatches(
            "CinematicInfrastructurePatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<CombatCinematicLayoutPatch>();
                patcher.RegisterPatch<ScreenShakeSuppressionPatch>();
                patcher.RegisterPatch<ScreenRumbleCinematicSuppressionPatch>();
                patcher.RegisterPatch<ScreenTraumaCinematicSuppressionPatch>();
                patcher.RegisterPatch<NinjaSlayerTransitionPreloadPatch>();
            });
        TryInstallOptionalPatches(
            nameof(BossBurstPresentationPatchGroup),
            patcher => patcher.RegisterPatches<BossBurstPresentationPatchGroup>());
        TryInstallOptionalPatches(
            "PreparedUiPatchGroup",
            patcher => patcher.RegisterPatch<PreparedDrawPileDisplayOrderPatch>());
        TryInstallOptionalPatches(
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
        TryInstallOptionalPatches(
            "FinisherPresentationPatchGroup",
            patcher =>
            {
                patcher.RegisterPatch<NinjaSlayerFinisherDeathStartPatch>();
                patcher.RegisterPatch<NinjaSlayerFinisherDamageNumberPatch>();
                patcher.RegisterPatch<NinjaSlayerFinisherCardVisualPatch>();
            });
        TryInstallOptionalPatches(
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
#if NINJASLAYER_TRANSITION_LOAD_LIMIT
                patcher.RegisterPatch<NinjaSlayerTransitionAssetLoadConcurrencyPatch>();
#endif
#if NINJASLAYER_TRANSITION_FINALIZE_BATCHING
                patcher.RegisterPatch<NinjaSlayerTransitionAssetFinalizePatch>();
#endif
            });
    }

    private static void TryInstallOptionalPatches(
        string patcherName,
        Action<ModPatcher> registerPatches,
        Func<DynamicPatchInfo[]>? dynamicPatchFactory = null)
    {
        ModPatcher patcher = RitsuLibFramework.CreatePatcher(NinjaSlayerIds.ModId, patcherName);
        Exception installFailure;
        MethodBase[] dynamicTargets = [];
        try
        {
            registerPatches(patcher);
            DynamicPatchInfo[] dynamicPatches = dynamicPatchFactory?.Invoke() ?? [];
            dynamicTargets = dynamicPatches
                .Select(patch => (MethodBase)patch.OriginalMethod)
                .Distinct()
                .ToArray();
            int expectedPatchCount = patcher.RegisteredPatchCount + dynamicPatches.Length;
            bool staticPatchesApplied = patcher.PatchAll();
            bool dynamicPatchesApplied = staticPatchesApplied
                && (dynamicPatches.Length == 0
                    || patcher.ApplyDynamicPatches(
                        dynamicPatches,
                        rollbackOnCriticalFailure: true));
            if (dynamicPatchesApplied && patcher.AppliedPatchCount == expectedPatchCount)
            {
                return;
            }

            installFailure = new InvalidOperationException(
                $"Optional NinjaSlayer patch transaction '{patcherName}' was incompletely installed: "
                + $"applied {patcher.AppliedPatchCount}/{expectedPatchCount} patches.");
        }
        catch (Exception exception)
        {
            installFailure = exception;
        }

        Exception? rollbackFailure = RollbackPatcherVerified(
            patcher,
            patcherName,
            dynamicTargets);
        if (rollbackFailure is not null)
        {
            throw new AggregateException(
                $"Optional NinjaSlayer patch transaction '{patcherName}' failed and could not be rolled back.",
                installFailure,
                rollbackFailure);
        }

        Logger.Warn(
            $"Optional NinjaSlayer patch transaction '{patcherName}' was not installed and was rolled back: "
            + installFailure);
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

    private static Exception? RollbackPatcherVerified(
        ModPatcher patcher,
        string patcherName,
        IReadOnlyCollection<MethodBase>? additionalTargets = null)
    {
        var failures = new List<Exception>();
        try
        {
            patcher.UnpatchAll();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (patcher.AppliedPatchCount != 0)
        {
            failures.Add(new InvalidOperationException(
                $"Patch transaction '{patcherName}' retained "
                + $"{patcher.AppliedPatchCount} applied patch(es) after rollback."));
        }

        var inspectedTargets = new HashSet<MethodBase>();
        void InspectTarget(MethodBase target)
        {
            if (!inspectedTargets.Add(target))
            {
                return;
            }

            if (Harmony.GetPatchInfo(target)?.Owners.Contains(patcher.PatcherId) == true)
            {
                failures.Add(new InvalidOperationException(
                    $"Patch transaction '{patcherName}' retained Harmony ownership of "
                    + $"{target.DeclaringType?.FullName}.{target.Name}."));
            }
        }

        foreach (ModPatchInfo patch in patcher.RegisteredPatches)
        {
            MethodBase? target;
            try
            {
                target = PatchTargetMethodResolver.Resolve(patch);
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Could not resolve rollback target for patch '{patch.Id}'.",
                    exception));
                continue;
            }

            if (target is null)
            {
                continue;
            }

            InspectTarget(target);
        }

        if (additionalTargets is not null)
        {
            foreach (MethodBase target in additionalTargets)
            {
                InspectTarget(target);
            }
        }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(
                $"Patch transaction '{patcherName}' rollback was incomplete.",
                failures)
        };
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
