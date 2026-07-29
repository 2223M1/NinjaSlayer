using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class BossBurstDeathFadeRegistry
{
    private static readonly object Gate = new();
    private static readonly ConditionalWeakTable<NMonsterDeathVfx, Marker> SuppressedPlayback = new();

    public static void MarkPlaybackSuppressed(NMonsterDeathVfx vfx)
    {
        ArgumentNullException.ThrowIfNull(vfx);
        lock (Gate)
        {
            SuppressedPlayback.Remove(vfx);
            SuppressedPlayback.Add(vfx, new Marker());
        }
    }

    public static bool ConsumePlaybackSuppression(NMonsterDeathVfx vfx)
    {
        ArgumentNullException.ThrowIfNull(vfx);
        lock (Gate)
        {
            return SuppressedPlayback.Remove(vfx);
        }
    }

    private sealed class Marker;
}
