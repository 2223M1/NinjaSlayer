using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace NinjaSlayer.Code.Nodes;

internal sealed class NinjaSlayerHoverTipSuppression : IDisposable
{
    private static int _activeLeases;
    private static bool _previousBlockState;

    private bool _disposed;

    private NinjaSlayerHoverTipSuppression()
    {
    }

    public static NinjaSlayerHoverTipSuppression Acquire()
    {
        if (_activeLeases == 0)
        {
            _previousBlockState = NHoverTipSet.shouldBlockHoverTips;
            NHoverTipSet.Clear();
            NHoverTipSet.shouldBlockHoverTips = true;
        }

        _activeLeases++;
        return new NinjaSlayerHoverTipSuppression();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeLeases--;
        if (_activeLeases == 0)
        {
            NHoverTipSet.shouldBlockHoverTips = _previousBlockState;
        }
    }
}
