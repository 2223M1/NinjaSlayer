using STS2RitsuLib;

namespace NinjaSlayer.Code.Prepared;

internal sealed class PreparedSafetyLifecycle : IDisposable
{
    private readonly IDisposable[] _subscriptions;
    private int _disposed;

    private PreparedSafetyLifecycle(IDisposable[] subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public int SubscriptionCount => _subscriptions.Length;

    public static PreparedSafetyLifecycle Subscribe()
    {
        var subscriptions = new List<IDisposable>(3);
        try
        {
            subscriptions.Add(RitsuLibFramework.SubscribeLifecycle<CardMovedBetweenPilesEvent>(
                evt => PreparedSafetyService.CompletePileChange(
                    evt.CombatState,
                    evt.Card,
                    evt.PreviousPile),
                replayCurrentState: false));
            subscriptions.Add(RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(
                evt => PreparedSafetyService.RecoverAfterRunLoaded(evt.RunState),
                replayCurrentState: false));
            subscriptions.Add(RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(
                evt => PreparedSafetyService.RecoverBeforeCombatStart(evt.CombatState),
                replayCurrentState: false));
            return new PreparedSafetyLifecycle(subscriptions.ToArray());
        }
        catch
        {
            for (int index = subscriptions.Count - 1; index >= 0; index--)
            {
                subscriptions[index].Dispose();
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        for (int index = _subscriptions.Length - 1; index >= 0; index--)
        {
            _subscriptions[index].Dispose();
        }
    }
}
