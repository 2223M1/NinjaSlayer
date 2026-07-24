using Godot;
using NinjaSlayer.Code.Transition;

namespace NinjaSlayer.Code.Nodes;

public partial class NinjaSlayerTransitionPrewarmPlayer : VideoStreamPlayer
{
    public const string NodeName = "NinjaSlayerTransitionPrewarmPlayer";
    private const double PrewarmSeconds = 0.5;
    private const double PrewarmTimeoutSeconds = 5.0;

    private bool _configured;
    private bool _finished;
    private bool _playbackStarted;
    private double _elapsed;

    internal long Generation { get; private set; }

    internal void Configure(long generation)
    {
        Generation = generation;
        _configured = true;
    }

    public override void _Ready()
    {
        if (!_configured)
        {
            QueueFree();
            return;
        }

        Volume = 0f;
        MouseFilter = MouseFilterEnum.Ignore;
        SelfModulate = Colors.Transparent;
        CustomMinimumSize = Vector2.One;
        Size = Vector2.One;
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_finished)
        {
            return;
        }

        _elapsed += Math.Max(delta, 0.0);
        try
        {
            if (!_playbackStarted)
            {
                TransitionVideoLoadPollResult loadResult =
                    NinjaSlayerTransitionVideo.PollPreloadedStream(out VideoStream? stream, out string? diagnostic);
                if (loadResult == TransitionVideoLoadPollResult.Waiting)
                {
                    CheckTimeout();
                    return;
                }

                if (loadResult == TransitionVideoLoadPollResult.Failed || stream is null)
                {
                    FinishAsFailure(diagnostic ?? "the preloaded stream was unavailable");
                    return;
                }

                Stream = stream;
                _ = GetVideoTexture();
                Play();
                _playbackStarted = true;
                return;
            }

            _ = GetVideoTexture();
            if (StreamPosition >= PrewarmSeconds)
            {
                _finished = true;
                NinjaSlayerTransitionVideoPrewarmer.Complete(this, Generation);
                return;
            }

            if (!IsPlaying())
            {
                FinishAsFailure("the decoder stopped before enough frames were produced");
                return;
            }

            CheckTimeout();
        }
        catch (Exception ex)
        {
            FinishAsFailure(ex.Message);
        }
    }

    public override void _ExitTree()
    {
        NinjaSlayerTransitionVideoPrewarmer.NotifyExited(this, Generation);
    }

    internal void StopAndRelease()
    {
        _finished = true;
        SetProcess(false);
        try
        {
            Stop();
        }
        finally
        {
            Stream = null;
            if (IsInsideTree())
            {
                QueueFree();
            }
            else
            {
                Dispose();
            }
        }
    }

    private void CheckTimeout()
    {
        if (_elapsed >= PrewarmTimeoutSeconds)
        {
            FinishAsFailure($"the {PrewarmTimeoutSeconds:0.#}s prewarm timeout elapsed");
        }
    }

    private void FinishAsFailure(string diagnostic)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        NinjaSlayerTransitionVideoPrewarmer.Fail(this, Generation, diagnostic);
    }
}
