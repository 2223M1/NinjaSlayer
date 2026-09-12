using Godot;

namespace NinjaSlayer.Code.Nodes;

// The same 25-tap shutter as the axial spin, sampled around the physical grip.
internal sealed partial class FreeControlMotionBlur : Node2D
{
    private readonly Vector4[] _rowsX = new Vector4[25], _rowsY = new Vector4[25];
    private Sprite2D? _image;
    private ShaderMaterial? _material;
    private float _previousTime;

    internal void Record(Sprite2D body, Vector2 center, Transform2D space, float time,
        Vector2 velocity, float angular, bool physical)
    {
        float dt = time - _previousTime;
        if (dt <= 0f) return;
        _previousTime = time;
        Visible = physical && Math.Abs(angular) > Mathf.DegToRad(250f) && body.IsVisibleInTree();
        if (!Visible) return;
        if (_image == null)
        {
            _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://NinjaSlayer/shaders/vfx/free_rotation_exposure.gdshader") };
            _image = new Sprite2D { Material = _material, ZIndex = -1, ShowBehindParent = true };
            AddChild(_image);
        }
        _image.Texture = body.Texture;
        _image.Centered = body.Centered; _image.Offset = body.Offset;
        _image.GlobalTransform = body.GlobalTransform;
        _image.Modulate = body.Modulate;
        Rect2 rect = body.GetRect(), bounds = rect;
        Transform2D bodyInSpace = space.AffineInverse() * body.GetGlobalTransformWithCanvas();
        Transform2D inverse = bodyInSpace.AffineInverse();
        for (int i = 0; i < 25; i++)
        {
            float age = (float)(1001d / 24000d) * i / 24f;
            Transform2D sweep = new(-angular * age, center - velocity * age);
            sweep *= new Transform2D(0f, -center);
            Transform2D relative = inverse * sweep * bodyInSpace;
            Transform2D sample = relative.AffineInverse();
            _rowsX[i] = new(sample.X.X, sample.Y.X, sample.Origin.X, 0f);
            _rowsY[i] = new(sample.X.Y, sample.Y.Y, sample.Origin.Y, 0f);
            bounds = bounds.Merge(relative * rect);
        }
        _material!.SetShaderParameter("samples_x", _rowsX);
        _material.SetShaderParameter("samples_y", _rowsY);
        _material.SetShaderParameter("sprite_rect", new Vector4(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y));
        _material.SetShaderParameter("draw_rect", new Vector4(bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y));
        _material.SetShaderParameter("flipped", new Vector2(body.FlipH ? 1 : 0, body.FlipV ? 1 : 0));
        _material.SetShaderParameter("radiance_power", 1f + 2f * (1f - MathF.Exp(-Math.Abs(angular) * .0417f)));
    }
}
