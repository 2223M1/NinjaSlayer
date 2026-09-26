using Godot;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class AlabamaDeathRecovery : Node
{
    private Node2D _body = null!;
    private Transform2D _baseline;
    private Transform2D _impact;
    private Vector2 _pivot;
    private float _endRotation;
    private float _elapsed;
    private bool _subscribed;

    internal Transform2D RenderTransform { get; private set; }
    internal float Progress => Mathf.Clamp(_elapsed / AlabamaDropAnimation.StandUpDuration, 0f, 1f);

    internal static void Attach(Node2D body, Transform2D impact, Vector2 pivot)
    {
        var recovery = new AlabamaDeathRecovery
        {
            Name = nameof(AlabamaDeathRecovery),
            ProcessMode = ProcessModeEnum.Pausable,
            _body = body,
            _baseline = body.Transform,
            _impact = impact,
            _pivot = pivot,
            _endRotation = impact.Rotation + Mathf.PosMod(body.Transform.Rotation - impact.Rotation, Mathf.Tau)
        };
        body.AddChild(recovery);
        recovery.Apply();
    }

    public override void _Ready()
    {
        RenderingServer.FramePreDraw += Apply;
        _subscribed = true;
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (Progress >= 1f)
        {
            RestoreRendering();
            QueueFree();
        }
    }

    private void Apply()
    {
        if (!_subscribed || !GodotObject.IsInstanceValid(_body)) return;
        float p = 1f - Mathf.Pow(1f - Progress, 2f);
        Vector2 pivot = (_impact * _pivot).Lerp(_baseline * _pivot, p);
        var pose = new Transform2D(Mathf.Lerp(_impact.Rotation, _endRotation, p),
            _impact.Scale.Lerp(_baseline.Scale, p), Mathf.Lerp(_impact.Skew, _baseline.Skew, p), Vector2.Zero);
        pose.Origin = pivot - pose.BasisXform(_pivot);
        // Native death owns the node transform. Compose the departing grab only
        // at render time so its rotation cannot reset death flight or Spine tracks.
        RenderTransform = _body.Transform * _baseline.AffineInverse() * pose;
        RenderingServer.CanvasItemSetTransform(_body.GetCanvasItem(), RenderTransform);
    }

    private void RestoreRendering()
    {
        if (_subscribed) RenderingServer.FramePreDraw -= Apply;
        _subscribed = false;
        if (GodotObject.IsInstanceValid(_body))
            RenderingServer.CanvasItemSetTransform(_body.GetCanvasItem(), _body.Transform);
    }

    public override void _ExitTree() => RestoreRendering();
}
