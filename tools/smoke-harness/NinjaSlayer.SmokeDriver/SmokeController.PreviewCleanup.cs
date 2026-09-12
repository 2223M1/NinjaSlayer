using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Nodes;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private AutoSlayer? _previewAutoSlayer;
    private bool _previewStopping;
    private readonly TaskCompletionSource _previewStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool IsPreview => _configuration.Phase is SmokePhase.ActionPreview or SmokePhase.TornadoPreview;
    internal bool UseNativePreviewCharacter => IsPreview && _configuration.PreviewCleanupBaseline;

    internal async Task CompletePreviewRunOnCancellation(Task run)
    {
        try { await run; }
        catch (OperationCanceledException) when (_previewStopping) { }
    }

    internal bool CapturePreviewExit(int code)
    {
        if (!_previewStopping) return false;
        if (code == 0) _previewStopped.TrySetResult();
        else _previewStopped.TrySetException(new InvalidOperationException($"Preview AutoSlay exited with {code}."));
        return true;
    }

    private async Task FinishPreviewAsync()
    {
        _previewStopping = true;
        _previewAutoSlayer!.Stop();
        await WaitTaskAsync(_previewStopped.Task, "Preview AutoSlay did not stop", TimeSpan.FromSeconds(10));
        // Native exit callbacks must run while their game-owned UI is alive.
        _checkpoints.Write("preview.native-quit");
        _previewStopping = false;
        NGame.Instance!.Quit();
    }
}
