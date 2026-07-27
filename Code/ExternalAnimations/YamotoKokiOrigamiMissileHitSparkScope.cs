using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class YamotoKokiOrigamiMissileHitSparkScope
{
    private static readonly AsyncLocal<Frame?> Current = new();

    public static IDisposable Enter(Creature target)
    {
        var frame = new Frame(Current.Value, target);
        Current.Value = frame;
        return frame;
    }

    public static bool TryCreate(
        Creature target,
        bool requireInteractable,
        out NHitSparkVfx? hitSpark)
    {
        Frame? frame = Current.Value;
        if (frame == null || frame.IsDisposed || frame.Consumed || frame.Target != target)
        {
            hitSpark = null;
            return false;
        }

        hitSpark = NYamotoKokiOrigamiMissileHitSparkVfx.Create(target, requireInteractable);
        if (hitSpark == null)
        {
            return false;
        }

        frame.Consumed = true;
        return true;
    }

    private sealed class Frame(Frame? previous, Creature target) : IDisposable
    {
        public Frame? Previous { get; } = previous;
        public Creature Target { get; } = target;
        public bool Consumed { get; set; }
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            if (ReferenceEquals(Current.Value, this))
            {
                Current.Value = Previous is { IsDisposed: false } ? Previous : null;
            }
        }
    }
}
