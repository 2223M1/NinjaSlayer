using Godot;

namespace NinjaSlayer.Code.Nodes;

internal sealed class SpinExposureRenderer(Node owner) : IDisposable
{
    internal static float BodyHeight(Texture2D texture) => texture.ResourcePath switch
    {
        string path when path.Contains("one_body_one_soul", StringComparison.Ordinal)
            || path.Contains("one_soul_", StringComparison.Ordinal) => 2664.026f,
        string path when path.Contains("/hell_tornado/full_naraku_", StringComparison.Ordinal) => 1632f,
        _ => 816f
    };

    private const string MaterialPath = "res://NinjaSlayer/materials/vfx/ninja_slayer_spin_motion_blur_mat.tres";
    private const string ExposureShaderPath = "res://NinjaSlayer/shaders/vfx/spin_angular_exposure.gdshader";
    private readonly Node _owner = owner;
    private readonly float[] _ratios = new float[25];
    private Sprite2D? _renderBody;
    private ShaderMaterial? _material;
    private ShaderMaterial? _exposureMaterial;
    private ShaderMaterial? _filterMaterial;
    private ShaderMaterial? _diffusionMaterial;
    private SubViewport? _exposure;
    private SubViewport? _filteredExposure;
    private SubViewport? _diffuseExposure;
    private Sprite2D? _exposureSprite;
    private float _captureMarginY;
    private Rect2? _planarCapture;

    internal void Apply(Sprite2D sprite, float axis, float ratio, ReadOnlySpan<float> angles,
        float bodyHeight, bool premultiplied = false)
    {
        if (!Prepare(sprite, planar: false)) return;
        Rect2 rect = sprite.GetRect();
        float travel = 0f;
        for (int i = 0; i < angles.Length; i++)
        {
            _ratios[i] = MathF.Cos(Mathf.DegToRad(angles[i]));
            if (i > 0) travel += Math.Abs(angles[i] - angles[i - 1]);
        }
        float radians = Mathf.DegToRad(travel);
        var (radius, verticalRadius, radiancePower) = ExposureShape(bodyHeight, radians);
        _captureMarginY = Math.Max(_captureMarginY, bodyHeight * .125f);
        _captureMarginY = Math.Max(_captureMarginY, MathF.Ceiling(verticalRadius / 32f) * 32f);
        float sweepRadius = Math.Max(Math.Abs(rect.Position.X - axis), Math.Abs(rect.End.X - axis));
        Rect2 captureRect = new(axis - sweepRadius, rect.Position.Y - _captureMarginY,
            sweepRadius * 2f, rect.Size.Y + 2f * _captureMarginY);
        _exposureMaterial!.SetShaderParameter("body_premultiplied", premultiplied);
        _exposureMaterial.SetShaderParameter("sample_ratios", _ratios);
        _exposureMaterial.SetShaderParameter("axis_x", axis);
        _exposureMaterial.SetShaderParameter("draw_bounds", new Vector2(captureRect.Position.X, captureRect.End.X));
        float a = axis + (captureRect.Position.X - radius - axis) / ratio;
        float b = axis + (captureRect.End.X + radius - axis) / ratio;
        CaptureAndFilter(sprite, captureRect, radius, verticalRadius, radiancePower);
        SetOutput(axis, ratio, new(Math.Min(a, b), Math.Max(a, b)),
            new(rect.Position.Y - verticalRadius, rect.End.Y + verticalRadius), radius, radiancePower);
    }

    internal void ApplyPlanar(Sprite2D sprite, Vector4[] samplesX, Vector4[] samplesY,
        Rect2 bounds, Vector2 pivot, float radians, Vector3 sweep, float bodyHeight, bool premultiplied = false)
    {
        if (!Prepare(sprite, planar: true)) return;
        float motion = radians + new Vector2(sweep.X, sweep.Y).Length() / bodyHeight;
        var (radius, verticalRadius, radiancePower) = ExposureShape(bodyHeight, motion);
        Rect2 padded = bounds.Grow(Math.Max(bodyHeight * .125f, MathF.Ceiling(verticalRadius / 32f) * 32f));
        _planarCapture = _planarCapture?.Merge(padded) ?? padded;
        Rect2 capture = _planarCapture.Value;
        _exposureMaterial!.SetShaderParameter("samples_x", samplesX);
        _exposureMaterial.SetShaderParameter("body_premultiplied", premultiplied);
        _exposureMaterial.SetShaderParameter("samples_y", samplesY);
        _exposureMaterial.SetShaderParameter("draw_rect", new Vector4(capture.Position.X, capture.Position.Y, capture.Size.X, capture.Size.Y));
        foreach (ShaderMaterial material in new[] { _material!, _filterMaterial!, _diffusionMaterial! })
        {
            material.SetShaderParameter("planar_pivot", pivot);
            material.SetShaderParameter("planar_sweep", sweep);
        }
        CaptureAndFilter(sprite, capture, radius, verticalRadius, radiancePower);
        SetOutput(0f, 1f, new(capture.Position.X - radius, capture.End.X + radius),
            new(capture.Position.Y, capture.End.Y), radius, radiancePower);
    }

    private static (float Horizontal, float Vertical, float Radiance) ExposureShape(float bodyHeight, float radians) =>
        (bodyHeight * .026f * radians * 2.8f, bodyHeight * .004f * radians * 2.8f,
            1f + 2f * (1f - MathF.Exp(-radians)));

    private bool Prepare(Sprite2D sprite, bool planar)
    {
        if (!ReferenceEquals(sprite, _renderBody)) { Reset(); _renderBody = sprite; }
        if (_material == null || sprite.Material != _material)
        {
            // Host hit/finisher materials take precedence until their owner releases them.
            if (sprite.Material != null)
            {
                if (_exposure != null) _exposure.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                if (_filteredExposure != null) _filteredExposure.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                if (_diffuseExposure != null) _diffuseExposure.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                return false;
            }
            _material ??= (ShaderMaterial)GD.Load<ShaderMaterial>(MaterialPath).Duplicate();
            sprite.Material = _material;
        }
        // Capture uncompressed local space. A second pass must blur across the
        // axis too: more angular projections alone leave its face/cloth sharp.
        if (_exposure == null)
        {
            _exposureMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(planar
                ? "res://NinjaSlayer/shaders/vfx/spin_planar_exposure.gdshader" : ExposureShaderPath) };
            _exposureSprite = new Sprite2D { Material = _exposureMaterial };
            _exposure = new SubViewport
            {
                Name = "AngularExposure", TransparentBg = true, Disable3D = true,
                GuiDisableInput = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
            };
            _owner.AddChild(_exposure);
            _exposure.AddChild(_exposureSprite);
            _filterMaterial = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://NinjaSlayer/shaders/vfx/spin_exposure_filter.gdshader")
            };
            _filteredExposure = new SubViewport
            {
                Name = "FilteredExposure", TransparentBg = true, Disable3D = true,
                GuiDisableInput = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
            };
            _owner.AddChild(_filteredExposure);
            _filteredExposure.AddChild(new Sprite2D
            {
                Texture = _exposure.GetTexture(), Centered = false, Material = _filterMaterial
            });
            _diffusionMaterial = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://NinjaSlayer/shaders/vfx/spin_exposure_diffusion.gdshader")
            };
            _diffuseExposure = new SubViewport
            {
                Name = "DiffuseExposure", TransparentBg = true, Disable3D = true,
                GuiDisableInput = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
            };
            _owner.AddChild(_diffuseExposure);
            _diffuseExposure.AddChild(new Sprite2D
            {
                Texture = _filteredExposure.GetTexture(), Centered = false, Material = _diffusionMaterial
            });
            _material.SetShaderParameter("exposure_texture", _diffuseExposure.GetTexture());
        }
        foreach (ShaderMaterial material in new[] { _material!, _filterMaterial!, _diffusionMaterial! })
            material.SetShaderParameter("planar_motion", planar);
        return true;
    }

    private void CaptureAndFilter(Sprite2D sprite, Rect2 captureRect, float radius,
        float verticalRadius, float radiancePower)
    {
        Rect2 rect = sprite.GetRect();
        // Axial captures retain their existing resolution. Dense planar exposure
        // needs only screen-pixel resolution; its angular filter already removes detail.
        float captureScale = .5f;
        if (_planarCapture != null)
        {
            Transform2D canvas = sprite.GetGlobalTransformWithCanvas();
            captureScale = Math.Min(captureScale, Math.Max(canvas.X.Length(), canvas.Y.Length()));
        }
        captureScale = Math.Min(captureScale, 2048f / Math.Max(captureRect.Size.X, captureRect.Size.Y));
        Vector2I size = new((int)MathF.Ceiling((captureRect.Size.X * captureScale - .01f) / 16f) * 16,
            (int)MathF.Ceiling((captureRect.Size.Y * captureScale - .01f) / 16f) * 16);
        if (_exposure!.Size != size) _exposure.Size = size;
        if (_filteredExposure!.Size != size) _filteredExposure.Size = size;
        if (_diffuseExposure!.Size != size) _diffuseExposure.Size = size;
        _exposure.CanvasTransform = new Transform2D(new Vector2(captureScale, 0f),
            new Vector2(0f, captureScale), -captureRect.Position * captureScale);
        _exposureSprite!.Texture = sprite.Texture;
        _exposureSprite.Centered = sprite.Centered;
        _exposureSprite.Offset = sprite.Offset;
        _exposureMaterial!.SetShaderParameter("body_texture", sprite.Texture);
        Vector4 spriteRect = new(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
        _exposureMaterial.SetShaderParameter("sprite_rect", spriteRect);
        _exposureMaterial.SetShaderParameter("flipped", new Vector2(sprite.FlipH ? 1 : 0, sprite.FlipV ? 1 : 0));
        _exposureMaterial.SetShaderParameter("radiance_power", radiancePower);
        _exposure.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        // Filter the footprint between spatial taps before widening the streak.
        // Otherwise thin mask/cloth lines repeat as a comb at high angular speed.
        _filterMaterial!.SetShaderParameter("sample_step", radius / 128f / size.X * captureScale);
        _filteredExposure.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _diffusionMaterial!.SetShaderParameter("radius", verticalRadius * captureScale / size.Y);
        _diffuseExposure.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

        _material!.SetShaderParameter("sprite_rect", spriteRect);
        Vector4 captured = new(captureRect.Position.X, captureRect.Position.Y, size.X / captureScale, size.Y / captureScale);
        _material.SetShaderParameter("capture_rect", captured);
        _filterMaterial.SetShaderParameter("capture_rect", captured);
        _diffusionMaterial.SetShaderParameter("capture_rect", captured);
    }

    private void SetOutput(float axis, float ratio, Vector2 xBounds, Vector2 yBounds, float radius, float radiancePower)
    {
        _material!.SetShaderParameter("axis_x", axis);
        _material.SetShaderParameter("draw_bounds", xBounds);
        _material.SetShaderParameter("draw_y_bounds", yBounds);
        _material.SetShaderParameter("current_ratio", ratio);
        _material.SetShaderParameter("blur_radius", radius);
        _material.SetShaderParameter("radiance_power", radiancePower);
    }

    internal void Reset()
    {
        if (_exposure != null) _exposure.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (_filteredExposure != null) _filteredExposure.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (_diffuseExposure != null) _diffuseExposure.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (GodotObject.IsInstanceValid(_renderBody) && _renderBody!.Material == _material)
            _renderBody.Material = null;
        _renderBody = null;
        _planarCapture = null;
    }

    public void Dispose()
    {
        Reset();
        foreach (SubViewport? viewport in new[] { _exposure, _filteredExposure, _diffuseExposure })
            if (GodotObject.IsInstanceValid(viewport) && !viewport!.IsQueuedForDeletion()) viewport.QueueFree();
        _material?.Dispose();
        _exposureMaterial?.Dispose();
        _filterMaterial?.Dispose();
        _diffusionMaterial?.Dispose();
    }
}
