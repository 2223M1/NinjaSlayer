using Godot;
using MegaCrit.Sts2.Core.Assets;
using NinjaSlayer.Cards;

namespace NinjaSlayer.Code.Nodes;

// Cosmetic exposure samples: this effect never selects targets or uses combat RNG.
internal sealed partial class NShurikenScatterVfx : Node2D
{
    private readonly record struct Piece(Vector2 Velocity, float Delay, float Scale, float Angle, float Spin);
    private static int _sequence;
    private readonly Piece[] _pieces = new Piece[18];
    private Texture2D _texture = null!;
    private float _elapsed;

    public override void _Ready()
    {
        _texture = PreloadManager.Cache.GetTexture2D(ShurikenCombat.ProjectileTexturePath);
        var random = new Random(unchecked(0x53434154 + _sequence++));
        for (int i = 0; i < _pieces.Length; i++)
        {
            float angle = random.NextSingle() * Mathf.Tau;
            _pieces[i] = new Piece(Vector2.Right.Rotated(angle) * (900f + 1500f * random.NextSingle()),
                random.NextSingle() * 0.075f, (32f + 34f * random.NextSingle()) / ShurikenCombat.SourceVisibleDiameter,
                random.NextSingle() * Mathf.Tau, (random.Next(2) == 0 ? -1f : 1f) * (30f + 30f * random.NextSingle()));
        }
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed >= 0.435f) { QueueFree(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (Piece piece in _pieces)
        {
            float time = _elapsed - piece.Delay;
            if (time < 0f || time > 0.36f) continue;
            float alpha = 1f - Mathf.SmoothStep(0.20f, 0.36f, time);
            for (int sample = 7; sample >= 0; sample--)
            {
                float age = sample * 0.006f;
                float t = time - age;
                if (t < 0f) continue;
                DrawSetTransform(piece.Velocity * t, piece.Angle + piece.Spin * t, Vector2.One * piece.Scale);
                DrawTexture(_texture, -_texture.GetSize() * 0.5f,
                    new Color(1f, 1f, 1f, alpha * (sample == 0 ? 0.8f : 0.055f)));
            }
        }
    }
}
