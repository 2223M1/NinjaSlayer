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
    private readonly BossFragmentPoint[] _residuals =
        new BossFragmentPoint[SoftFragmentBody.ParticleCount];
    private readonly Godot.Collections.Array<Vector2> _controlOffsets =
        new(new Vector2[SoftFragmentBody.ParticleCount]);
    private float _previousRotation;
    private int _applyFailureLogged;
    private int _consecutivePoseFailures;
    private bool _disposed;

    private BossCapturedFragmentRenderSurface(
        Node2D anchor,
        MeshInstance2D meshNode,
        ShaderMaterial material,
        SoftFragmentBody body,
        BossCapturedFragmentDescriptor descriptor)
    {
        Anchor = anchor;
        _meshNode = meshNode;
        _material = material;
        Body = body;
        Descriptor = descriptor;
    }

    public Node2D Anchor { get; }
    public SoftFragmentBody Body { get; }
    public BossCapturedFragmentDescriptor Descriptor { get; }
    public float MaximumResidual { get; private set; }
    public float RmsResidual { get; private set; }
    public float RmsResidualRatio => RmsResidual / Math.Max(1f, Body.ShortDimension);

    public static bool TryCreate(
        Node parent,
        BossCapturedFragmentDescriptor descriptor,
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
            BossFragmentCell cell = descriptor.Cell;
            Rect2 cellBounds = ToRect2(BossDismembermentMath.BoundsOf(cell.Vertices));
            Rect2 partBounds = descriptor.Part.SourceBounds;
            Rect2 renderBounds = descriptor.IsLocalSplit
                ? Intersect(cellBounds, partBounds)
                : partBounds;
            if (cellBounds.Size.X <= 1f
                || cellBounds.Size.Y <= 1f
                || renderBounds.Size.X <= 1f
                || renderBounds.Size.Y <= 1f
                || partBounds.Size.X <= 1f
                || partBounds.Size.Y <= 1f
                || descriptor.AtlasUvRect.Size.X <= 0f
                || descriptor.AtlasUvRect.Size.Y <= 0f)
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
                descriptor.FragmentIndex,
                restGrid,
                restHull,
                initialCenter ?? restCenter,
                initialScale,
                mass,
                collisionMargin);

            anchor = new Node2D
            {
                Name = $"BossBodyFragment_{descriptor.FragmentIndex}",
                ZAsRelative = false,
                ZIndex = zIndex + Math.Clamp(descriptor.Part.DrawOrder, 0, 48),
                Visible = false
            };
            parent.AddChildSafely(anchor);
            EnsureInsideTree(anchor, "captured fragment anchor");

            var material = new ShaderMaterial { Shader = shader };
            ConfigureMaterial(
                material,
                descriptor,
                cellBounds);
            var meshNode = new MeshInstance2D
            {
                Name = "CapturedFragmentMesh",
                Mesh = BuildMesh(
                    renderBounds,
                    partBounds,
                    descriptor.AtlasUvRect,
                    bodyToPresentation,
                    body.RestCenter),
                Texture = captureTexture,
                Material = material,
                ZAsRelative = true,
                ZIndex = 0
            };
            anchor.AddChildSafely(meshNode);
            EnsureInsideTree(meshNode, "captured fragment mesh");

            var created = new BossCapturedFragmentRenderSurface(
                anchor,
                meshNode,
                material,
                body,
                descriptor);
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

            Scripts.Entry.Logger.Warn(
                $"Captured boss fragment initialization failed: {exception.Message}");
            return false;
        }
    }

    public bool ApplyFrame()
    {
        if (_disposed
            || !GodotObject.IsInstanceValid(Anchor)
            || !Anchor.IsInsideTree()
            || !GodotObject.IsInstanceValid(_meshNode)
            || !_meshNode.IsInsideTree())
        {
            return false;
        }

        if (!SoftBodyRenderPoseResolver.TryResolve(
                Body,
                _previousRotation,
                _residuals,
                out SoftBodyRenderPose pose))
        {
            _consecutivePoseFailures++;
            return _consecutivePoseFailures < 3;
        }

        try
        {
            _consecutivePoseFailures = 0;
            _previousRotation = pose.RotationRadians;
            Anchor.Position = new Vector2(pose.Position.X, pose.Position.Y);
            Anchor.Rotation = pose.RotationRadians;
            Anchor.Scale = Vector2.One * pose.UniformScale;
            MaximumResidual = pose.MaximumResidual;
            double residualSquared = 0d;
            for (int index = 0; index < _residuals.Length; index++)
            {
                _controlOffsets[index] = new Vector2(_residuals[index].X, _residuals[index].Y);
                residualSquared += _residuals[index].X * _residuals[index].X
                    + _residuals[index].Y * _residuals[index].Y;
            }

            RmsResidual = (float)Math.Sqrt(residualSquared / _residuals.Length);
            _material.SetShaderParameter(ControlOffsetsParameter, _controlOffsets);
            return true;
        }
        catch (Exception exception)
        {
            _consecutivePoseFailures++;
            if (Interlocked.Exchange(ref _applyFailureLogged, 1) == 0)
            {
                Scripts.Entry.Logger.Warn(
                    $"Captured boss fragment {Body.Id} render binding failed: {exception.Message}");
            }

            return _consecutivePoseFailures < 3;
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
                result[row * SoftFragmentBody.GridSize + column] =
                    new BossFragmentPoint(mapped.X, mapped.Y);
            }
        }

        return result;
    }

    private static ArrayMesh BuildMesh(
        Rect2 renderBounds,
        Rect2 partBounds,
        Rect2 atlasUvRect,
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
                Vector2 bodyPoint = renderBounds.Position + renderBounds.Size * new Vector2(u, v);
                Vector2 mapped = bodyToPresentation * bodyPoint;
                int index = row * RenderGridSize + column;
                vertices[index] = mapped - new Vector2(restCenter.X, restCenter.Y);
                Vector2 partUv = new(
                    (bodyPoint.X - partBounds.Position.X) / partBounds.Size.X,
                    (bodyPoint.Y - partBounds.Position.Y) / partBounds.Size.Y);
                uvs[index] = atlasUvRect.Position + partUv * atlasUvRect.Size;
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

    private static Rect2 Intersect(Rect2 first, Rect2 second)
    {
        Vector2 minimum = new(
            Math.Max(first.Position.X, second.Position.X),
            Math.Max(first.Position.Y, second.Position.Y));
        Vector2 maximum = new(
            Math.Min(first.End.X, second.End.X),
            Math.Min(first.End.Y, second.End.Y));
        Vector2 size = maximum - minimum;
        return size.X > 0f && size.Y > 0f
            ? new Rect2(minimum, size)
            : default;
    }

    private static void ConfigureMaterial(
        ShaderMaterial material,
        BossCapturedFragmentDescriptor descriptor,
        Rect2 cellBounds)
    {
        IReadOnlyList<BossFragmentPoint> seeds = descriptor.AllSeeds;
        int seedCount = descriptor.IsLocalSplit
            ? Math.Min(seeds.Count, BossDismembermentMath.MaximumPieces)
            : 0;
        material.SetShaderParameter("seed_count", seedCount);
        material.SetShaderParameter("cell_seed", ToVector2(descriptor.Cell.Seed));
        material.SetShaderParameter("cell_bounds_min", cellBounds.Position);
        material.SetShaderParameter("cell_bounds_size", cellBounds.Size);
        material.SetShaderParameter("part_bounds_min", descriptor.Part.SourceBounds.Position);
        material.SetShaderParameter("part_bounds_size", descriptor.Part.SourceBounds.Size);
        material.SetShaderParameter("atlas_content_min", descriptor.AtlasUvRect.Position);
        material.SetShaderParameter("atlas_content_size", descriptor.AtlasUvRect.Size);
        for (int index = 0; index < BossDismembermentMath.MaximumPieces; index++)
        {
            material.SetShaderParameter(
                $"seed_{index}",
                index < seeds.Count ? ToVector2(seeds[index]) : Vector2.Zero);
        }
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

    private static Rect2 ToRect2(BossFragmentRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Vector2 ToVector2(BossFragmentPoint point) => new(point.X, point.Y);

    private static void EnsureInsideTree(Node node, string label)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
        {
            throw new InvalidOperationException($"The {label} could not enter the scene tree.");
        }
    }
}
