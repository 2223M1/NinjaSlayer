using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

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
