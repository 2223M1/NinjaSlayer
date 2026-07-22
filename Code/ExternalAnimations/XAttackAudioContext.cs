namespace NinjaSlayer.Code.ExternalAnimations;

public static class XAttackAudioContext
{
    private static readonly AsyncLocal<int> SuppressionDepth = new();

    public static bool SuppressAutomaticSfx => SuppressionDepth.Value > 0;

    public static IDisposable Suppress()
    {
        SuppressionDepth.Value++;
        return new SuppressionLease();
    }

    private sealed class SuppressionLease : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SuppressionDepth.Value = Math.Max(0, SuppressionDepth.Value - 1);
        }
    }
}
