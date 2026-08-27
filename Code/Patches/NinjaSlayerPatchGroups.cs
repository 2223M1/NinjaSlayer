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
