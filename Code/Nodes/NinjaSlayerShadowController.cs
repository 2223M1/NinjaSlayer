using Godot;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NinjaSlayerShadowController : Node
{
    private const float AirborneFadeDistance = 300f;
    private const float MinimumAirborneScale = 0.18f;
    private const float FallenShadowWidthMultiplier = 1.25f;
    private const float FallenShadowHeightMultiplier = 0.65f;

    private Sprite2D? _shadow;
    private Node2D? _airborneAnchor;
    private Vector2 _groundPosition;
    private Color _groundModulate;
    private float _groundAnchorY;
    private Vector2 _authoredScale = Vector2.One * 0.5f;
    private float _scaleMultiplier = 1f;
    private bool _deathFallOverride;
    private float _deathFallProgress;
    private float _deathFallDirection = -1f;

    public override void _Ready()
    {
        Node? rig = GetParent();
        _shadow = rig?.GetNodeOrNull<Sprite2D>(NinjaSlayerVisualRig.ShadowNodeName);
        _airborneAnchor = rig?.GetNodeOrNull<Node2D>(NinjaSlayerVisualRig.AirborneAnchorName);
        if (_shadow == null || _airborneAnchor == null)
        {
            SetProcess(false);
            return;
        }

        _groundPosition = _shadow.Position;
        _groundModulate = _shadow.Modulate;
        _groundAnchorY = _airborneAnchor.Position.Y;
        _authoredScale = new Vector2(Mathf.Abs(_shadow.Scale.X), Mathf.Abs(_shadow.Scale.Y));
        ApplyPresentation();
    }

    public override void _Process(double delta)
    {
        ApplyPresentation();
    }

    public void SetScaleMultiplier(float multiplier)
    {
        _scaleMultiplier = Mathf.Max(0f, multiplier);
        ApplyPresentation();
    }

    internal void SetAuthoredPresentation(Vector2 position, Vector2 scale)
    {
        _groundPosition = position;
        _authoredScale = new Vector2(Mathf.Abs(scale.X), Mathf.Abs(scale.Y));
        ApplyPresentation();
    }

    public void SetDeathFall(float progress, float direction)
    {
        _deathFallOverride = true;
        _deathFallProgress = Mathf.Clamp(progress, 0f, 1f);
        _deathFallDirection = Mathf.Sign(direction == 0f ? -1f : direction);
        ApplyPresentation();
    }

    public void ClearDeathFall()
    {
        _deathFallOverride = false;
        _deathFallProgress = 0f;
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        if (_shadow == null
            || _airborneAnchor == null
            || !GodotObject.IsInstanceValid(_shadow)
            || !GodotObject.IsInstanceValid(_airborneAnchor))
        {
            return;
        }

        if (_deathFallOverride)
        {
            Vector2 baseScale = _authoredScale * _scaleMultiplier;
            float widthMultiplier = Mathf.Lerp(1f, FallenShadowWidthMultiplier, _deathFallProgress);
            float heightMultiplier = Mathf.Lerp(1f, FallenShadowHeightMultiplier, _deathFallProgress);
            float textureWidth = _shadow.Texture?.GetWidth() ?? 0f;
            float extension = textureWidth * baseScale.X * (widthMultiplier - 1f) * 0.5f;
            _shadow.Position = _groundPosition + Vector2.Right * (_deathFallDirection * extension);
            _shadow.Scale = new Vector2(
                baseScale.X * widthMultiplier,
                baseScale.Y * heightMultiplier);
            _shadow.Modulate = _groundModulate;
            return;
        }

        float altitude = Mathf.Max(0f, _groundAnchorY - _airborneAnchor.Position.Y);
        float airborneProgress = Mathf.Clamp(altitude / AirborneFadeDistance, 0f, 1f);
        float scaleMultiplier = Mathf.Lerp(1f, MinimumAirborneScale, airborneProgress);
        _shadow.Position = _groundPosition;
        _shadow.Scale = _authoredScale * _scaleMultiplier * scaleMultiplier;
        _shadow.Modulate = new Color(
            _groundModulate.R,
            _groundModulate.G,
            _groundModulate.B,
            _groundModulate.A * (1f - airborneProgress));
    }
}
