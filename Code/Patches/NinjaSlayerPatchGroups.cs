using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class GameplayPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerAnimationPatch>();
        patcher.RegisterPatch<NinjaSlayerDebuffShakePatch>();
        patcher.RegisterPatch<NinjaSlayerMapHistoryIconPatch>();
        patcher.RegisterPatch<NinjaSlayerSurroundedFacingPatch>();
        patcher.RegisterPatch<NinjaSlayerAttackFacingPatch>();
        patcher.RegisterPatch<NinjaSlayerDeathAnimPatch>();
        patcher.RegisterPatch<NinjaSlayerAbandonDeathCapturePatch>();
        patcher.RegisterPatch<NinjaSlayerAbandonVisualCreationPatch>();
        patcher.RegisterPatch<NinjaSlayerAbandonMerchantDeathPatch>();
        patcher.RegisterPatch<NinjaSlayerAbandonGameOverDeathFeedbackPatch>();
        patcher.RegisterPatch<NarakuLifeHealthBarLayoutPatch>();
        patcher.RegisterPatch<ArchitectDialogueSuppressionPatch>();
        patcher.RegisterPatch<ArchitectExecutionStartPatch>();
        patcher.RegisterPatch<ArchitectDeathAnimationPatch>();
        patcher.RegisterPatch<NinjaSlayerReviveAnimPatch>();
        patcher.RegisterPatch<NinjaSlayerIncomingDamageCapturePatch>();
        patcher.RegisterPatch<BlackFlameDamagePatch>();
        patcher.RegisterPatch<AncientEntranceEventOptionPatch>();
        patcher.RegisterPatch<AncientEntranceCreatureVisibilityPatch>();
        patcher.RegisterPatch<BossGreetingMusicPatch>();
        patcher.RegisterPatch<CardTransformShineSfxPatch>();
        patcher.RegisterPatch<NinjaSlayerSwipePowerStealPatch>();
        patcher.RegisterPatch<YamotoKokiAllyLayoutPatch>();
        patcher.RegisterPatch<YamotoKokiDynamicAllyLayoutPatch>();
        patcher.RegisterPatch<YamotoKokiFinishedCombatRestorePatch>();
        patcher.RegisterPatch<EnemyAttackDodgeScopePatch>();
        patcher.RegisterPatch<EnemyAttackDodgeAnimationPatch>();
        patcher.RegisterPatch<AllyDodgeImpactPatch>();
        patcher.RegisterPatch<AttackIntentDamagePreviewPatch>();
        patcher.RegisterPatch<YamotoKokiIntentUpdatePatch>();
        patcher.RegisterPatch<YamotoKokiIntentGenerationPatch>();
        patcher.RegisterPatch<YamotoKokiLastEnemyDeathIntentPatch>();
        patcher.RegisterPatch<YamotoKokiOrigamiMissileHitSparkPatch>();
        patcher.RegisterPatch<NinjaSlayerEnemyAttackVfxBaselinePatch>();
        patcher.RegisterPatch<TargetedRelicFlashAnchorPatch>();
    }
}

internal sealed class OrobasSeaGlassPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher) =>
        patcher.RegisterPatch<OrobasSeaGlassCharacterPatch>();
}

internal sealed class BossBurstPresentationPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<BossDeathPresentationPatch>();
        patcher.RegisterPatch<BossBurstCombatEndMusicPatch>();
        patcher.RegisterPatch<BossBurstSingleDeathFadePatch>();
        patcher.RegisterPatch<BossBurstGroupedDeathFadePatch>();
        patcher.RegisterPatch<BossBurstDeathFadePlaybackPatch>();
    }
}

internal sealed class CardResolutionPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<CardPlayResolutionBeforePatch>();
        patcher.RegisterPatch<CardPlayResolutionAfterPatch>();
        patcher.RegisterPatch<CardResolutionCleanupPatch>();
    }
}

internal sealed class ReporterPassPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher) => patcher.RegisterPatch<ReporterPassEventOptionPatch>();
}

internal sealed class NancyCandidateFilterPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher) => patcher.RegisterPatch<NancyLeeCandidatePatch>();
}

internal sealed class NancyLoadedRunRepairPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher) => patcher.RegisterPatch<NancyLeeLoadedRunPatch>();
}

internal sealed class KaratePreviewPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<KarateCardPreviewTargetPatch>();
        patcher.RegisterPatch<KarateCardPreviewClearPatch>();
        patcher.RegisterPatch<KarateHealthBarTextPreviewPatch>();
    }
}

internal sealed class TypographyPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerCardTitleTypographyPatch>();
        patcher.RegisterPatch<NinjaSlayerInspectRelicTypographyPatch>();
    }
}

internal sealed class ChadoPresentationPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<ChadoEnergyCostVisualPatch>();
        patcher.RegisterPatch<ChadoCardNodeLifecyclePatch>();
    }
}

internal sealed class CinematicInfrastructurePatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<CombatCinematicLayoutPatch>();
        patcher.RegisterPatch<ScreenShakeSuppressionPatch>();
        patcher.RegisterPatch<ScreenRumbleCinematicSuppressionPatch>();
        patcher.RegisterPatch<ScreenTraumaCinematicSuppressionPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionPreloadPatch>();
    }
}

internal sealed class PreparedGameplayPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher) => patcher.RegisterPatch<PreparedDrawPatch>();
}

internal sealed class PreparedSafetyPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<PreparedPileChangeSafetyPatch>();
        patcher.RegisterPatch<PreparedRunLoadedSafetyPatch>();
        patcher.RegisterPatch<PreparedCombatStartSafetyPatch>();
    }
}

internal sealed class PreparedUiPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher) => patcher.RegisterPatch<PreparedDrawPileDisplayOrderPatch>();
}

internal sealed class FinisherCorePatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerFinisherAttackCommandPatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherLethalDamagePatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherPrimaryDamagePatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherAfterCardPlayedPatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherCardPlayCleanupPatch>();
        patcher.RegisterPatch<NinjaSlayerDeathCompletionPatch>();
    }
}

internal sealed class FinisherPresentationPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerFinisherDeathStartPatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherDamageNumberPatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherCardVisualPatch>();
    }
}

internal sealed class FinisherCadencePatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher) => patcher.RegisterPatch<TornadoFistFinisherCadencePatch>();
}

internal sealed class TransitionCorePatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerTransitionSfxPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionPatch>();
        patcher.RegisterPatch<NinjaSlayerRoomFadeInGatePatch>();
        patcher.RegisterPatch<NinjaSlayerFadeInGatePatch>();
    }
}

internal sealed class TransitionPresentationPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerTransitionRunPresentationPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionTeardownPresentationPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionCombatPresentationPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionAncientSetupPresentationPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionAncientHealPresentationPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionRunMusicPresentationPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionParameterizedSfxPresentationPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionLoopSfxPresentationPatch>();
    }
}

internal sealed class TransitionSmoothingPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerTransitionAssetLoadConcurrencyPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionAssetFinalizePatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionGcDeferralPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionRunSceneTracePatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionRunInitializationTracePatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionSceneTreeTracePatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionEventSceneTracePatch>();
    }
}

internal sealed class FeedbackPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerFeedbackOpenerPatch>();
        patcher.RegisterPatch<NinjaSlayerFeedbackOpenPatch>();
        patcher.RegisterPatch<NinjaSlayerFeedbackConfirmPatch>();
        patcher.RegisterPatch<NinjaSlayerFeedbackSendPatch>();
        patcher.RegisterPatch<NinjaSlayerFeedbackClosePatch>();
    }
}

internal sealed class TelemetryIdentityPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerTelemetryIdentityLaunchPatch>();
        patcher.RegisterPatch<NinjaSlayerTelemetryIdentityCleanupPatch>();
    }
}
