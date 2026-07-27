using Godot;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class BossCapturedFragmentRenderSurface : IDisposable
{
    private const int RenderGridSize = 10;
    private static readonly StringName ControlOffsetsParameter = new("control_offsets");

    private readonly MeshInstance2D _meshNode;
    private readonly ShaderMaterial _material;
    private readonly BossFragmentPoint[] _residuals = new BossFragmentPoint[SoftFragmentBody.ParticleCount];
    // Godot shader uniform arrays require a Variant Array, not a packed Vector2 array.
    private readonly Godot.Collections.Array<Vector2> _controlOffsets =
        new(new Vector2[SoftFragmentBody.ParticleCount]);
    private float _previousRotation;
    private int _applyFailureLogged;
    private bool _disposed;

    private BossCapturedFragmentRenderSurface(
        Node2D anchor,
        MeshInstance2D meshNode,
        ShaderMaterial material,
        SoftFragmentBody body)
    {
        Anchor = anchor;
        _meshNode = meshNode;
        _material = material;
        Body = body;
    }

    public Node2D Anchor { get; }
    public SoftFragmentBody Body { get; }
    public float MaximumResidual { get; private set; }

    public static bool TryCreate(
        Node parent,
        int id,
        BossFragmentCell cell,
        IReadOnlyList<BossFragmentPoint> allSeeds,
        Rect2 textureBounds,
        Transform2D bodyToPresentation,
        Texture2D captureTexture,
        Shader shader,
        BossFragmentPoint? initialCenter,
        float initialScale,
        float mass,
        float collisionMargin,
        int zIndex,
        out BossCapturedFragmentRenderSurface? surface)
    {
        surface = null;
        Node2D? anchor = null;
        try
        {
            Rect2 cellBounds = BoundsOf(cell.Vertices);
            if (cellBounds.Size.X <= 1f || cellBounds.Size.Y <= 1f)
            {
                return false;
            }

            BossFragmentPoint[] restGrid = BuildRestGrid(cellBounds, bodyToPresentation);
            SoftBodyHullPoint[] restHull = cell.Vertices
                .Select(point =>
                {
                    Vector2 mapped = bodyToPresentation * ToVector2(point);
                    return new SoftBodyHullPoint(
                        new BossFragmentPoint(mapped.X, mapped.Y),
                        Math.Clamp((point.X - cellBounds.Position.X) / cellBounds.Size.X, 0f, 1f),
                        Math.Clamp((point.Y - cellBounds.Position.Y) / cellBounds.Size.Y, 0f, 1f));
                })
                .ToArray();
            BossFragmentPoint restCenter = Average(restGrid);
            var body = new SoftFragmentBody(
                id,
                restGrid,
                restHull,
                initialCenter ?? restCenter,
                initialScale,
                mass,
                collisionMargin);

            anchor = new Node2D
            {
                Name = "BossBodyFragment",
                ZAsRelative = false,
                ZIndex = zIndex,
                Visible = false
            };
            parent.AddChildSafely(anchor);
            if (!GodotObject.IsInstanceValid(anchor) || !anchor.IsInsideTree())
            {
                throw new InvalidOperationException("The captured fragment anchor could not enter the scene tree.");
            }

            var material = new ShaderMaterial { Shader = shader };
            ConfigureMaterial(material, cell, allSeeds, cellBounds, textureBounds);
            var meshNode = new MeshInstance2D
            {
                Name = "CapturedFragmentMesh",
                Mesh = BuildMesh(cellBounds, textureBounds, bodyToPresentation, body.RestCenter),
                Texture = captureTexture,
                Material = material,
                ZAsRelative = true,
                ZIndex = 0
            };
            anchor.AddChildSafely(meshNode);
            if (!GodotObject.IsInstanceValid(meshNode) || !meshNode.IsInsideTree())
            {
                throw new InvalidOperationException("The captured fragment mesh could not enter the scene tree.");
            }

            var created = new BossCapturedFragmentRenderSurface(anchor, meshNode, material, body);
            if (!created.ApplyFrame())
            {
                created.Dispose();
                return false;
            }

            anchor.Visible = true;
            surface = created;
            return true;
        }
        catch (Exception exception)
        {
            if (anchor != null && GodotObject.IsInstanceValid(anchor))
            {
                anchor.QueueFreeSafely();
            }

            Scripts.Entry.Logger.Warn($"Captured boss fragment initialization failed: {exception.Message}");
            return false;
        }
    }

    public bool ApplyFrame()
    {
        if (_disposed
            || !GodotObject.IsInstanceValid(Anchor)
            || !Anchor.IsInsideTree()
            || !GodotObject.IsInstanceValid(_meshNode)
            || !_meshNode.IsInsideTree()
            || !SoftBodyRenderPoseResolver.TryResolve(
                Body,
                _previousRotation,
                _residuals,
                out SoftBodyRenderPose pose))
        {
            return false;
        }

        try
        {
            _previousRotation = pose.RotationRadians;
            Anchor.Position = new Vector2(pose.Position.X, pose.Position.Y);
            Anchor.Rotation = pose.RotationRadians;
            Anchor.Scale = Vector2.One * pose.UniformScale;
            MaximumResidual = pose.MaximumResidual;
            for (int index = 0; index < _residuals.Length; index++)
            {
                _controlOffsets[index] = new Vector2(_residuals[index].X, _residuals[index].Y);
            }

            _material.SetShaderParameter(
                ControlOffsetsParameter,
                _controlOffsets);
            return true;
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _applyFailureLogged, 1) == 0)
            {
                Scripts.Entry.Logger.Warn(
                    $"Captured boss fragment {Body.Id} render binding failed: {exception.Message}");
            }

            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (GodotObject.IsInstanceValid(Anchor))
        {
            Anchor.QueueFreeSafely();
        }
    }

    private static BossFragmentPoint[] BuildRestGrid(
        Rect2 cellBounds,
        Transform2D bodyToPresentation)
    {
        var result = new BossFragmentPoint[SoftFragmentBody.ParticleCount];
        for (int row = 0; row < SoftFragmentBody.GridSize; row++)
        {
            for (int column = 0; column < SoftFragmentBody.GridSize; column++)
            {
                float u = column / (float)(SoftFragmentBody.GridSize - 1);
                float v = row / (float)(SoftFragmentBody.GridSize - 1);
                Vector2 bodyPoint = cellBounds.Position + cellBounds.Size * new Vector2(u, v);
                Vector2 mapped = bodyToPresentation * bodyPoint;
                result[row * SoftFragmentBody.GridSize + column] = new BossFragmentPoint(mapped.X, mapped.Y);
            }
        }

        return result;
    }

    private static ArrayMesh BuildMesh(
        Rect2 cellBounds,
        Rect2 textureBounds,
        Transform2D bodyToPresentation,
        BossFragmentPoint restCenter)
    {
        var vertices = new Vector2[RenderGridSize * RenderGridSize];
        var uvs = new Vector2[vertices.Length];
        for (int row = 0; row < RenderGridSize; row++)
        {
            for (int column = 0; column < RenderGridSize; column++)
            {
                float u = column / (float)(RenderGridSize - 1);
                float v = row / (float)(RenderGridSize - 1);
                Vector2 bodyPoint = cellBounds.Position + cellBounds.Size * new Vector2(u, v);
                Vector2 mapped = bodyToPresentation * bodyPoint;
                int index = row * RenderGridSize + column;
                vertices[index] = mapped - new Vector2(restCenter.X, restCenter.Y);
                uvs[index] = new Vector2(
                    (bodyPoint.X - textureBounds.Position.X) / textureBounds.Size.X,
                    (bodyPoint.Y - textureBounds.Position.Y) / textureBounds.Size.Y);
            }
        }

        var indices = new int[(RenderGridSize - 1) * (RenderGridSize - 1) * 6];
        int write = 0;
        for (int row = 0; row < RenderGridSize - 1; row++)
        {
            for (int column = 0; column < RenderGridSize - 1; column++)
            {
                int topLeft = row * RenderGridSize + column;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + RenderGridSize;
                int bottomRight = bottomLeft + 1;
                indices[write++] = topLeft;
                indices[write++] = topRight;
                indices[write++] = bottomRight;
                indices[write++] = topLeft;
                indices[write++] = bottomRight;
                indices[write++] = bottomLeft;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static void ConfigureMaterial(
        ShaderMaterial material,
        BossFragmentCell cell,
        IReadOnlyList<BossFragmentPoint> allSeeds,
        Rect2 cellBounds,
        Rect2 textureBounds)
    {
        material.SetShaderParameter("seed_count", Math.Min(allSeeds.Count, BossDismembermentMath.MaximumPieces));
        material.SetShaderParameter("cell_seed", ToVector2(cell.Seed));
        material.SetShaderParameter("cell_bounds_min", cellBounds.Position);
        material.SetShaderParameter("cell_bounds_size", cellBounds.Size);
        material.SetShaderParameter("texture_bounds_min", textureBounds.Position);
        material.SetShaderParameter("texture_bounds_size", textureBounds.Size);
        for (int index = 0; index < BossDismembermentMath.MaximumPieces; index++)
        {
            material.SetShaderParameter(
                $"seed_{index}",
                index < allSeeds.Count ? ToVector2(allSeeds[index]) : Vector2.Zero);
        }
    }

    private static Rect2 BoundsOf(IReadOnlyList<BossFragmentPoint> points)
    {
        float minX = points[0].X;
        float minY = points[0].Y;
        float maxX = minX;
        float maxY = minY;
        for (int index = 1; index < points.Count; index++)
        {
            minX = Math.Min(minX, points[index].X);
            minY = Math.Min(minY, points[index].Y);
            maxX = Math.Max(maxX, points[index].X);
            maxY = Math.Max(maxY, points[index].Y);
        }

        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private static BossFragmentPoint Average(IReadOnlyList<BossFragmentPoint> points)
    {
        float x = 0f;
        float y = 0f;
        for (int index = 0; index < points.Count; index++)
        {
            x += points[index].X;
            y += points[index].Y;
        }

        float inverseCount = 1f / Math.Max(1, points.Count);
        return new BossFragmentPoint(x * inverseCount, y * inverseCount);
    }

    private static Vector2 ToVector2(BossFragmentPoint point) => new(point.X, point.Y);
}
