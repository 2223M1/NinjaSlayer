using Godot;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class SawatariWeaponVisuals
{
    private readonly float[] _weaponVisibility = new float[3];
    private Polygon2D? _bowMesh;
    private Polygon2D? _stringMesh;
    private static readonly Vector2 BowRest = new(-204.5f, -17.5f);
    private static readonly Vector2 ArrowRest = new(30.959879f, 4.226345f);
    private static readonly Vector2 InnerGrip = new(-31f, -8.5f);
    private static readonly Shader HandClip = new()
    {
        Code = """
            shader_type canvas_item;
            uniform vec2 hand;
            uniform vec2 inward;
            varying vec2 point;
            void vertex() { point = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy; }
            void fragment() { COLOR.a *= 1.0 - smoothstep(-0.5, 0.5, dot(point - hand, inward)); }
            """
    };

    private void SwitchWeapon(WeaponPose pose)
    {
        if (_shownPose == pose && CombatActionTimingRuntime.VisualSeconds(.24f) > 0f) return;
        bool initial = _shownPose == null;
        _shownPose = pose;
        _swapTween?.Kill();
        float duration = CombatActionTimingRuntime.VisualSeconds(.24f);
        float[] start = (float[])_weaponVisibility.Clone();
        void Apply(float seconds)
        {
            for (int i = 0; i < 3; i++)
            {
                float p = i == (int)pose
                    ? Mathf.SmoothStep(0, 1, Mathf.Clamp((seconds - .08f) / .16f, 0, 1))
                    : Mathf.SmoothStep(0, 1, Mathf.Clamp(seconds / .08f, 0, 1));
                _weaponVisibility[i] = Mathf.Lerp(start[i], i == (int)pose ? 1f : 0f, p);
            }
            ApplyWeaponVisibility();
        }
        if (initial || duration <= 0f) { Apply(.24f); return; }
        _swapTween = CreateTween();
        _swapTween.TweenMethod(Callable.From<float>(Apply), 0f, .24f, duration);
    }

    private void ApplyWeaponVisibility()
    {
        float bamboo = _weaponVisibility[(int)WeaponPose.Bamboo];
        _bamboo.Visible = bamboo > 0f;
        _bamboo.Position = new Vector2(-73.5f + 360f * (1f - bamboo), -5.5f);
        ClipAtHand(_bamboo, _weapons, InnerGrip, Vector2.Right, bamboo);
        float bow = _weaponVisibility[(int)WeaponPose.Bow];
        _ranged.Visible = bow > 0f;
        _ranged.Position = (InnerGrip + Vector2.Right * 160f).Lerp(BowRest, bow);
        _ranged.Skew = .18f * (1f - bow);
        foreach (CanvasItem item in _ranged.GetChildren().OfType<CanvasItem>())
            ClipAtHand(item, _weapons, InnerGrip, Vector2.Right, bow);
        float knives = _weaponVisibility[(int)WeaponPose.Knives];
        for (int i = 0; i < 2; i++)
        {
            _hands[i].Visible = knives > 0f && ((_monster.HeldMachetes | _incomingMachetes) & (1 << i)) != 0;
            if (_knives[i] is not { } knife || knife.GetParent() != _hands[i]) continue;
            knife.Position = Vector2.Right * (270f * (1f - knives));
            ClipAtHand(knife, _hands[i], Vector2.Zero, Vector2.Right, knives);
        }
    }

    private static void ClipAtHand(CanvasItem item, Node2D space, Vector2 hand, Vector2 inward, float visibility)
    {
        if (visibility >= 1f || visibility <= 0f) { item.Material = null; return; }
        if (item.Material is not ShaderMaterial material)
            item.Material = material = new ShaderMaterial { Shader = HandClip };
        material.SetShaderParameter("hand", space.ToGlobal(hand));
        material.SetShaderParameter("inward", space.GlobalTransform.BasisXform(inward).Normalized());
    }

    private Polygon2D BuildBowMesh(string spriteName)
    {
        var sprite = _ranged.GetNode<Sprite2D>(spriteName);
        var mesh = new Polygon2D { Name = spriteName + "Motion", Texture = sprite.Texture, ZIndex = sprite.ZIndex };
        var uv = new Vector2[38];
        var polygons = new Godot.Collections.Array();
        Vector2 size = sprite.Texture.GetSize();
        for (int row = 0; row <= 18; row++)
        {
            uv[row * 2] = new Vector2(0, size.Y * row / 18f);
            uv[row * 2 + 1] = new Vector2(size.X, size.Y * row / 18f);
            if (row < 18) polygons.Add(new int[] { row * 2, row * 2 + 1, row * 2 + 3, row * 2 + 2 });
        }
        mesh.UV = uv;
        mesh.Polygons = polygons;
        _ranged.AddChild(mesh);
        sprite.Hide();
        return mesh;
    }

    private void ApplyBowDraw(float draw)
    {
        _bowMesh ??= BuildBowMesh("Bow");
        _stringMesh ??= BuildBowMesh("String");
        Deform(_bowMesh, new Vector2(78, -28), false);
        Deform(_stringMesh, new Vector2(120.5f, -29.5f), true);
        if (_arrow is { } arrow) arrow.Position = ArrowRest - Vector2.Right * (24f / .52f * (1f - draw));
        void Deform(Polygon2D mesh, Vector2 offset, bool isString)
        {
            Vector2 size = mesh.Texture.GetSize();
            Vector2[] points = mesh.UV;
            for (int i = 0; i < points.Length; i++)
            {
                points[i] += offset - size * .5f;
                float y = points[i].Y;
                float envelope = Mathf.Clamp(1f - Mathf.Abs(y - ArrowRest.Y) / (y < ArrowRest.Y ? 269f : 201f), 0, 1);
                points[i].X -= (1f - draw) * (isString ? 24f / .52f * envelope : 6f * (1f - envelope));
            }
            mesh.Polygon = points;
        }
    }

    private async Task DrawBow(Node2D target, Action release)
    {
        _stringTween?.Kill();
        _actionTween?.Kill();
        float duration = CombatActionTimingRuntime.VisualSeconds(.2f);
        void Apply(float p)
        {
            if (!GodotObject.IsInstanceValid(target)) return;
            Vector2 direction = _weapons.ToLocal(target.GlobalPosition) - _ranged.Position;
            _ranged.Rotation = direction.Angle() - Mathf.Pi;
            ApplyBowDraw(Mathf.SmoothStep(0, 1, p));
        }
        if (duration <= 0f) { Apply(1); release(); return; }
        Apply(0);
        _actionTween = CreateTween();
        _actionTween.TweenMethod(Callable.From<float>(Apply), 0f, 1f, duration);
        _actionTween.TweenCallback(Callable.From(release));
        await TweenPlayback.AwaitCompletion(_actionTween, this);
    }

    private void ReleaseString()
    {
        float duration = CombatActionTimingRuntime.VisualSeconds(.12f);
        if (duration <= 0f) { ApplyBowDraw(0); return; }
        _stringTween = CreateTween();
        _stringTween.TweenMethod(Callable.From<float>(p =>
            ApplyBowDraw(Mathf.Cos(p * Mathf.Tau * 1.5f) * Mathf.Exp(-p * 5f))), 0f, 1f, duration);
        _stringTween.TweenCallback(Callable.From(() => ApplyBowDraw(0)));
    }
}
