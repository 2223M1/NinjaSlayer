using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class NinjaSlayerFormShatter : Node2D
{
    private readonly List<Shard> _shards = [];
    private readonly List<Snapshot> _pending = [];
    private Creature _actor = null!;
    private NCombatRoom _room = null!;
    private NinjaSlayerFormKind _oldForm;
    private float _elapsed;
    private bool _hasPending;

    internal static void Schedule(Creature actor, NinjaSlayerFormKind oldForm, Sprite2D body)
    {
        if (actor.IsDead || !CombatManager.Instance.IsInProgress || CombatManager.Instance.IsOverOrEnding
            || NCombatRoom.Instance is not { } room || !room.IsInsideTree()) return;
        string name = "FormShatter" + actor.GetCreatureNode()!.GetInstanceId();
        var node = room.CombatVfxContainer.GetNodeOrNull<NinjaSlayerFormShatter>(name);
        if (node?.IsQueuedForDeletion() == true)
        {
            room.CombatVfxContainer.RemoveChild(node);
            node = null;
        }
        if (CombatActionTimingRuntime.CurrentSpeed == CombatActionSpeed.Instant)
        {
            node?.QueueFree();
            return;
        }
        if (node == null)
        {
            node = new NinjaSlayerFormShatter { Name = name, _actor = actor, _room = room, ZIndex = 20 };
            room.CombatVfxContainer.AddChild(node);
        }
        // Keep the appearance that was actually visible before this frame's first
        // change. Multiple Power mutations in one frame produce one final swap.
        if (node._hasPending) return;
        node._hasPending = true;
        node._oldForm = oldForm;
        IEnumerable<Sprite2D> layers = NinjaSlayerHellTornadoVisual.Get(actor) is { Active: true } tornado
            ? [tornado.GetNode<Sprite2D>("OrbitingBody"), tornado.GetNode<Sprite2D>("FixedHead")]
            : [body];
        Transform2D inverse = node.GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 origin = inverse * actor.GetCreatureNode()!.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
        foreach (var layer in layers)
        {
            if (layer.Texture == null) continue;
            node._pending.Add(new(layer.Texture, inverse * layer.GetGlobalTransformWithCanvas(), layer.GetRect(),
                layer.FlipH, layer.FlipV, layer.Modulate * layer.SelfModulate, origin));
        }
    }

    public override void _Ready() => RenderingServer.FramePreDraw += Flush;

    private void Flush()
    {
        if (!_hasPending) return;
        _hasPending = false;
        bool changed = !_actor.IsDead && NinjaSlayerFormState.GetPresentation(_actor).Kind != _oldForm;
        if (changed)
        {
            ClearShards();
            foreach (Snapshot snapshot in _pending) BuildShards(snapshot, _pending.Count == 1 ? 8 : 4);
            _elapsed = 0f;
            if (_shards.Count > 0) SfxCmd.Play("event:/sfx/block_break", .8f);
        }
        _pending.Clear();
    }

    private void BuildShards(Snapshot snapshot, int rows)
    {
        using Image image = snapshot.Texture.GetImage();
        Rect2 used = image.GetUsedRect();
        if (used.Size.X <= 0f || used.Size.Y <= 0f) return;
        const int columns = 8;
        Vector2 size = snapshot.Texture.GetSize();
        Vector2[] points = new Vector2[(columns + 1) * (rows + 1)];
        for (int y = 0; y <= rows; y++)
        for (int x = 0; x <= columns; x++)
        {
            float jitterX = x > 0 && x < columns ? MathF.Sin(x * 12.37f + y * 8.1f) * .24f : 0f;
            float jitterY = y > 0 && y < rows ? MathF.Sin(x * 7.1f + y * 3.17f) * .24f : 0f;
            points[y * (columns + 1) + x] = used.Position + used.Size * new Vector2((x + jitterX) / columns, (y + jitterY) / rows);
        }
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            int at = y * (columns + 1) + x;
            AddTriangle([points[at], points[at + 1], points[at + columns + 2]]);
            AddTriangle([points[at], points[at + columns + 2], points[at + columns + 1]]);
        }

        void AddTriangle(Vector2[] uv)
        {
            var polygon = new Vector2[3];
            for (int i = 0; i < 3; i++)
            {
                Vector2 p = uv[i];
                if (snapshot.FlipH) p.X = size.X - p.X;
                if (snapshot.FlipV) p.Y = size.Y - p.Y;
                polygon[i] = snapshot.Transform * (snapshot.Rect.Position + p);
            }
            Vector2 center = (polygon[0] + polygon[1] + polygon[2]) / 3f;
            for (int i = 0; i < 3; i++) polygon[i] -= center;
            var piece = new Polygon2D { Polygon = polygon, UV = uv, Texture = snapshot.Texture,
                Position = center, Color = snapshot.Color, Antialiased = true };
            AddChild(piece);
            Vector2 outward = (center - snapshot.Origin).Normalized();
            int index = _shards.Count;
            float speed = 70f + 55f * (.5f + .5f * MathF.Sin(index * 5.17f));
            _shards.Add(new(piece, center, outward * speed, MathF.Sin(index * 3.71f) * 1.1f));
        }
    }

    public override void _Process(double delta)
    {
        if (_actor.IsDead || !ReferenceEquals(_room, NCombatRoom.Instance))
        {
            QueueFree();
            return;
        }
        _elapsed += (float)delta;
        float p = Mathf.Clamp(_elapsed / .083f, 0f, 1f);
        float burst = 1f - MathF.Pow(1f - p, 3f);
        float drift = Math.Max(0f, _elapsed - .083f) * .55f;
        float alpha = 1f - Mathf.SmoothStep(.083f, .25f, _elapsed);
        foreach (Shard shard in _shards)
        {
            shard.Node.Position = shard.Origin + shard.Travel * (burst + drift);
            shard.Node.Rotation = shard.Turn * (burst + drift);
            shard.Node.Modulate = new Color(1f, 1f, 1f, alpha);
        }
        if (_elapsed >= .25f && !_hasPending) QueueFree();
    }

    private void ClearShards()
    {
        foreach (Shard shard in _shards) { RemoveChild(shard.Node); shard.Node.QueueFree(); }
        _shards.Clear();
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= Flush;
        _pending.Clear();
        _shards.Clear();
    }

    private sealed record Snapshot(Texture2D Texture, Transform2D Transform, Rect2 Rect, bool FlipH, bool FlipV, Color Color, Vector2 Origin);
    private sealed record Shard(Polygon2D Node, Vector2 Origin, Vector2 Travel, float Turn);
}
