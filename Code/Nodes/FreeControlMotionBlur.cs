using Godot;

namespace NinjaSlayer.Code.Nodes;

// Planar trajectories feed the same exposure/filter/diffusion renderer as axial spin.
internal sealed partial class FreeControlMotionBlur : Node2D
{
    private const int SampleCount = 129;
    private readonly Vector4[] _rowsX = new Vector4[SampleCount], _rowsY = new Vector4[SampleCount];
    private SpinExposureRenderer? _renderer;
    private float _previousTime;
    private readonly List<(double Time, ExposurePose Pose)> _history = [];
    private Sprite2D? _historyBody;
    private Vector2 _historySize;
    internal bool SourcePremultiplied { get; init; }
    internal float? SourceBodyHeight { get; init; }

    internal void RecordHistory(Sprite2D body, double time, Vector2 canvasPivot) =>
        RecordHistory(body, time, Transform2D.Identity, canvasPivot, body.GetGlobalTransformWithCanvas(), 0d);

    internal void RecordHistory(Sprite2D body, double time, Transform2D space,
        Vector2 pivot, Transform2D authored, double angle)
    {
        var pose = new ExposurePose(space, pivot, authored, angle);
        Transform2D current = body.GetGlobalTransformWithCanvas();
        // Idle-frame texture swaps belong to the same motion exposure.
        if (!ReferenceEquals(body, _historyBody) || body.Texture.GetSize() != _historySize || _history.Count > 0
            && (time < _history[^1].Time || time - _history[^1].Time > .1d
                || Math.Sign(current.Determinant()) != Math.Sign(_history[^1].Pose.Transform.Determinant())))
            _history.Clear();
        _historyBody = body;
        _historySize = body.Texture.GetSize();
        if (_history.Count > 0 && time == _history[^1].Time) _history[^1] = (time, pose);
        else _history.Add((time, pose));
        const double shutter = 1001d / 24000d;
        while (_history.Count > 2 && _history[1].Time < time - shutter) _history.RemoveAt(0);
        Visible = _history.Count > 1 && body.IsVisibleInTree();
        if (!Visible) { _renderer?.Reset(); return; }
        Rect2 rect = body.GetRect();
        Transform2D inverse = current.AffineInverse();
        Vector2 localPivot = inverse * space * pivot;
        Rect2 bounds = FullTurnBounds(rect, localPivot);
        float swept = 0f, signedSweep = 0f, previousAngle = 0f;
        Vector2 translation = Vector2.Zero;
        for (int i = 0; i < SampleCount; i++)
        {
            double sampleTime = Math.Max(_history[0].Time, time - shutter * i / (SampleCount - 1d));
            int at = 0;
            while (at + 1 < _history.Count && _history[at + 1].Time < sampleTime) at++;
            ExposurePose sample = _history[at].Pose;
            if (at + 1 < _history.Count)
                sample = sample.InterpolateWith(_history[at + 1].Pose,
                    (float)((sampleTime - _history[at].Time) / (_history[at + 1].Time - _history[at].Time)));
            Transform2D relative = inverse * sample.Transform;
            Transform2D lookup = relative.AffineInverse();
            _rowsX[i] = new(lookup.X.X, lookup.Y.X, lookup.Origin.X, 0f);
            _rowsY[i] = new(lookup.X.Y, lookup.Y.Y, lookup.Origin.Y, 0f);
            float step = Mathf.Wrap(previousAngle - relative.Rotation, -Mathf.Pi, Mathf.Pi);
            swept += Math.Abs(step);
            signedSweep += step;
            previousAngle = relative.Rotation;
            translation = localPivot - inverse * sample.Space * sample.Pivot;
            bounds = bounds.Merge(relative * rect);
        }
        Visible = swept > Mathf.DegToRad(2f) || current.BasisXform(translation).Length() > 1f;
        if (Visible) ApplyExposure(body, bounds, localPivot, swept, new(translation.X, translation.Y, signedSweep));
        else _renderer?.Reset();
    }

    // Keep the unwrapped turn separate: at high speed a frame can exceed 180 degrees.
    // Interpolating only the final matrix would then reverse the exposure direction.
    private readonly record struct ExposurePose(Transform2D Space, Vector2 Pivot, Transform2D Authored, double Angle)
    {
        internal Transform2D Transform => Space * new Transform2D((float)(Angle % Mathf.Tau), Pivot)
            * new Transform2D(0f, -Pivot) * Authored;

        internal ExposurePose InterpolateWith(ExposurePose next, float weight) => new(
            Space.InterpolateWith(next.Space, weight), Pivot.Lerp(next.Pivot, weight),
            Authored.InterpolateWith(next.Authored, weight), Angle + (next.Angle - Angle) * weight);
    }

    internal void ClearHistory()
    {
        _history.Clear();
        _renderer?.Reset();
        Hide();
    }

    private static Rect2 FullTurnBounds(Rect2 rect, Vector2 pivot)
    {
        float radius = Math.Max(Math.Max(pivot.DistanceTo(rect.Position), pivot.DistanceTo(rect.End)),
            Math.Max(pivot.DistanceTo(new(rect.Position.X, rect.End.Y)), pivot.DistanceTo(new(rect.End.X, rect.Position.Y))));
        return new(pivot - Vector2.One * radius, Vector2.One * (radius * 2f));
    }

    private void ApplyExposure(Sprite2D body, Rect2 bounds, Vector2 pivot, float radians, Vector3 sweep)
    {
        _renderer ??= new SpinExposureRenderer(this);
        _renderer.ApplyPlanar(body, _rowsX, _rowsY, bounds, pivot, radians, sweep,
            SourceBodyHeight ?? SpinExposureRenderer.BodyHeight(body.Texture), SourcePremultiplied);
    }

    internal void Record(Sprite2D body, Vector2 center, Transform2D space, float time,
        Vector2 velocity, float angular, bool physical)
    {
        float dt = time - _previousTime;
        if (dt <= 0f) return;
        _previousTime = time;
        Visible = physical && Math.Abs(angular) > Mathf.DegToRad(250f) && body.IsVisibleInTree();
        if (!Visible) { _renderer?.Reset(); return; }
        Rect2 rect = body.GetRect();
        Transform2D bodyInSpace = space.AffineInverse() * body.GetGlobalTransformWithCanvas();
        Transform2D inverse = bodyInSpace.AffineInverse();
        Rect2 bounds = FullTurnBounds(rect, inverse * center);
        for (int i = 0; i < SampleCount; i++)
        {
            float age = (float)(1001d / 24000d) * i / (SampleCount - 1f);
            Transform2D sweep = new(-angular * age, center - velocity * age);
            sweep *= new Transform2D(0f, -center);
            Transform2D relative = inverse * sweep * bodyInSpace;
            Transform2D sample = relative.AffineInverse();
            _rowsX[i] = new(sample.X.X, sample.Y.X, sample.Origin.X, 0f);
            _rowsY[i] = new(sample.X.Y, sample.Y.Y, sample.Origin.Y, 0f);
            bounds = bounds.Merge(relative * rect);
        }
        float shutter = (float)(1001d / 24000d);
        Vector2 translation = inverse.BasisXform(velocity * shutter);
        ApplyExposure(body, bounds, inverse * center, Math.Abs(angular) * shutter,
            new(translation.X, translation.Y, angular * shutter * Math.Sign(bodyInSpace.Determinant())));
    }

    public override void _ExitTree()
    {
        _renderer?.Dispose();
        _renderer = null;
    }
}
