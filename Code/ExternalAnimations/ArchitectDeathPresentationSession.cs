using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class ArchitectDeathPresentationSession : IDisposable
{
    public const float DurationSeconds = 1.5f;

    private static readonly Dictionary<ulong, ArchitectDeathPresentationSession> Pending = [];

    private readonly NCreature _architect;
    private readonly TaskCompletionSource _deathStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _visualsCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    private ArchitectDeathPresentationSession(NCreature architect)
    {
        _architect = architect;
    }

    public static ArchitectDeathPresentationSession Register(NCreature architect)
    {
        ulong id = architect.GetInstanceId();
        if (Pending.Remove(id, out ArchitectDeathPresentationSession? previous))
        {
            previous.Dispose();
        }

        var session = new ArchitectDeathPresentationSession(architect);
        Pending[id] = session;
        return session;
    }

    public static bool TryConsume(NCreature architect, out ArchitectDeathPresentationSession? session)
    {
        if (!Pending.Remove(architect.GetInstanceId(), out session) || session._disposed)
        {
            session = null;
            return false;
        }

        return true;
    }

    public Task StartDeathAnimation(bool shouldRemove)
    {
        _architect.DisableInteractionForDeath();
        _architect.AnimHideIntent();
        _architect.AnimDisableUi();
        Task task = WaitForVisualsAndRemove(shouldRemove);
        _deathStarted.TrySetResult();
        return task;
    }

    public async Task WaitUntilDeathStarts(Task killTask, CancellationToken cancellationToken)
    {
        Task completed = await Task.WhenAny(_deathStarted.Task, killTask)
            .WaitAsync(cancellationToken);
        if (completed == killTask && !_deathStarted.Task.IsCompleted)
        {
            await killTask;
            throw new InvalidOperationException("Architect death completed without starting its presentation.");
        }

        await _deathStarted.Task.WaitAsync(cancellationToken);
    }

    public void CompleteVisuals() => _visualsCompleted.TrySetResult();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ulong id = _architect.GetInstanceId();
        if (Pending.TryGetValue(id, out ArchitectDeathPresentationSession? pending)
            && ReferenceEquals(pending, this))
        {
            Pending.Remove(id);
        }

        _deathStarted.TrySetCanceled();
        _visualsCompleted.TrySetResult();
    }

    private async Task WaitForVisualsAndRemove(bool shouldRemove)
    {
        try
        {
            await _visualsCompleted.Task;
        }
        finally
        {
            if (shouldRemove && GodotObject.IsInstanceValid(_architect))
            {
                _architect.QueueFreeSafely();
            }
        }
    }
}
