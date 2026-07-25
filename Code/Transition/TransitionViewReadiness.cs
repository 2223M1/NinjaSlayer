namespace NinjaSlayer.Code.Transition;

internal sealed class TransitionViewReadiness
{
    private readonly TaskCompletionSource<bool> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> Completion => _completion.Task;

    public bool TryMarkReady() => _completion.TrySetResult(true);

    public bool TryMarkUnavailable() => _completion.TrySetResult(false);

    public Task<bool> WaitAsync(CancellationToken cancellationToken = default) =>
        Completion.WaitAsync(cancellationToken);
}
