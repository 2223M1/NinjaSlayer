using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class BossBurstDeathFadeRegistry
{
    private static readonly ConditionalWeakTable<NMonsterDeathVfx, Marker> SuppressedPlayback = new();

    public static void MarkPlaybackSuppressed(NMonsterDeathVfx vfx) =>
        SuppressedPlayback.GetOrCreateValue(vfx);

    public static bool ConsumePlaybackSuppression(NMonsterDeathVfx vfx) =>
        SuppressedPlayback.Remove(vfx);

    private sealed class Marker;
}
