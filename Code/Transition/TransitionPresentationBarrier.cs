namespace NinjaSlayer.Code.Transition;

internal enum TransitionPresentationDisposition
{
    Pending,
    Released,
    Discarded
}

internal sealed class TransitionPresentationBarrier
{
    private readonly object _sync = new();
    private readonly List<PendingOperation> _pending = [];
    private TransitionPresentationDisposition _disposition;

    public TransitionPresentationDisposition Disposition
    {
        get
        {
            lock (_sync)
            {
                return _disposition;
            }
        }
    }

    public bool TryDefer(Func<Task> operation, out Task completion)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_sync)
        {
            if (_disposition == TransitionPresentationDisposition.Released)
            {
                completion = Task.CompletedTask;
                return false;
            }

            if (_disposition == TransitionPresentationDisposition.Discarded)
            {
                completion = Task.CompletedTask;
                return true;
            }

            var pending = new PendingOperation(operation);
            _pending.Add(pending);
            completion = pending.Completion;
            return true;
        }
    }

    public bool Release() => Complete(TransitionPresentationDisposition.Released);

    public bool Discard() => Complete(TransitionPresentationDisposition.Discarded);

    private bool Complete(TransitionPresentationDisposition disposition)
    {
        PendingOperation[] pending;
        lock (_sync)
        {
            if (_disposition != TransitionPresentationDisposition.Pending)
            {
                return false;
            }

            _disposition = disposition;
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (PendingOperation operation in pending)
        {
            if (disposition == TransitionPresentationDisposition.Released)
            {
                operation.Start();
            }
            else
            {
                operation.Discard();
            }
        }

        return true;
    }

    private sealed class PendingOperation(Func<Task> operation)
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public void Start()
        {
            Task task;
            try
            {
                task = operation()
                    ?? throw new InvalidOperationException("Deferred presentation operation returned null.");
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
                return;
            }

            _ = CompleteAsync(task);
        }

        public void Discard() => _completion.TrySetResult();

        private async Task CompleteAsync(Task task)
        {
            try
            {
                await task;
                _completion.TrySetResult();
            }
            catch (OperationCanceledException ex)
            {
                _completion.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
            }
        }
    }
}
