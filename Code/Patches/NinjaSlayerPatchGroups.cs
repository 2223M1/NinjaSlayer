using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class GameplayPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerAnimationPatch>();
        patcher.RegisterPatch<NinjaSlayerDebuffShakePatch>();
        patcher.RegisterPatch<NinjaSlayerSurroundedFacingPatch>();
        patcher.RegisterPatch<NinjaSlayerAttackFacingPatch>();
        patcher.RegisterPatch<NinjaSlayerDeathAnimPatch>();
        patcher.RegisterPatch<BossDeathPresentationPatch>();
        patcher.RegisterPatch<BossDeathFadeStartPatch>();
        patcher.RegisterPatch<ArchitectDialogueSuppressionPatch>();
        patcher.RegisterPatch<ArchitectExecutionStartPatch>();
        patcher.RegisterPatch<NinjaSlayerReviveAnimPatch>();
        patcher.RegisterPatch<NinjaSlayerIncomingDamageCapturePatch>();
        patcher.RegisterPatch<BlackFlameDamagePatch>();
        patcher.RegisterPatch<AncientEntranceEventOptionPatch>();
        patcher.RegisterPatch<AncientEntranceCreatureVisibilityPatch>();
        patcher.RegisterPatch<BossGreetingMusicPatch>();
        patcher.RegisterPatch<CardTransformShineSfxPatch>();
        patcher.RegisterPatch<NinjaSlayerSwipePowerStealPatch>();
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

internal sealed class NancyCompatibilityPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NancyLeeCandidatePatch>();
        patcher.RegisterPatch<NancyLeeLoadedRunPatch>();
    }
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

internal sealed class PreparedPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<PreparedDrawPatch>();
        patcher.RegisterPatch<PreparedPileExitPatch>();
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
    }
}

internal sealed class FinisherPresentationPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
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

internal sealed class TransitionSmoothingPatchGroup : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<NinjaSlayerTransitionAssetFinalizePatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionGcDeferralPatch>();
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
