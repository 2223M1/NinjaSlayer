using Godot;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class BossCapturedFragmentRenderSurface : IDisposable
{
    internal const string ShaderPath =
        "res://NinjaSlayer/shaders/vfx/boss_dismemberment_clip.gdshader";
    private const int RenderGridSize = 10;
    private static readonly StringName ControlOffsetsParameter = new("control_offsets");

    private readonly MeshInstance2D _meshNode;
    private readonly ShaderMaterial _material;
    private readonly SoftFragmentBody _fragmentBody;
    private readonly BossFragmentPoint _fragmentRestCenter;
    private readonly BossFragmentPoint[] _residuals =
        new BossFragmentPoint[SoftFragmentBody.ParticleCount];
    private readonly Godot.Collections.Array<Vector2> _controlOffsets =
        new(new Vector2[SoftFragmentBody.ParticleCount]);
    private float _previousRotation;
    private int _applyFailureLogged;
    private int _consecutivePoseFailures;
    private SoftFragmentBody _body;
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
        _fragmentBody = body;
        _fragmentRestCenter = body.RestCenter;
        _body = body;
        Descriptor = descriptor;
    }

    public Node2D Anchor { get; }
    public SoftFragmentBody Body => _body;
    public SoftFragmentBody FragmentBody => _fragmentBody;
    public BossCapturedFragmentDescriptor Descriptor { get; }

    internal sealed class PreparedResource : IDisposable
    {
        private int _consumed;
        private int _disposed;

        internal PreparedResource(
            BossCapturedFragmentDescriptor descriptor,
            SoftFragmentBody body,
            ArrayMesh mesh,
            ShaderMaterial material)
        {
            Descriptor = descriptor;
            Body = body;
            Mesh = mesh;
            Material = material;
        }

        internal BossCapturedFragmentDescriptor Descriptor { get; }
        internal SoftFragmentBody Body { get; }
        internal ArrayMesh Mesh { get; }
        internal ShaderMaterial Material { get; }

        internal void MarkConsumed() => Interlocked.Exchange(ref _consumed, 1);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0
                || Volatile.Read(ref _consumed) != 0)
            {
                return;
            }

            Material.Dispose();
            Mesh.Dispose();
        }
    }

    internal static bool TryPrepare(
        BossCapturedFragmentDescriptor descriptor,
        Transform2D bodyToPresentation,
        Shader shader,
        float mass,
        float collisionMargin,
        out PreparedResource? prepared)
    {
        prepared = null;
        ArrayMesh? mesh = null;
        ShaderMaterial? material = null;
        try
        {
            BossFragmentCell cell = descriptor.Cell;
            Rect2 cellBounds = ToRect2(BossDismembermentMath.BoundsOf(cell.Vertices));
            Rect2 partBounds = descriptor.Part.SourceBounds;
            Rect2 renderBounds = descriptor.IsLocalSplit
                ? Intersect(cellBounds, partBounds)
                : partBounds;
            if (!HasRenderableBounds(descriptor, cellBounds, partBounds, renderBounds))
            {
                return false;
            }

            BossFragmentPoint[] restGrid = BuildRestGrid(cellBounds, bodyToPresentation);
            SoftBodyHullPoint[] restHull = BuildRestHull(cell, cellBounds, bodyToPresentation);
            BossFragmentPoint restCenter = Average(restGrid);
            var body = new SoftFragmentBody(
                descriptor.FragmentIndex,
                restGrid,
                restHull,
                restCenter,
                compressedScale: 1f,
                mass,
                collisionMargin);

            material = new ShaderMaterial { Shader = shader };
            ConfigureMaterial(material, descriptor, cellBounds);
            mesh = BuildMesh(
                renderBounds,
                partBounds,
                descriptor.AtlasUvRect,
                bodyToPresentation,
                body.RestCenter);
            prepared = new PreparedResource(descriptor, body, mesh, material);
            return true;
        }
        catch (Exception exception)
        {
            material?.Dispose();
            mesh?.Dispose();
            Scripts.Entry.Logger.Warn(
                $"Captured boss fragment preparation failed: {exception.Message}");
            return false;
        }
    }

    internal static bool TryInstantiate(
        Node parent,
        PreparedResource prepared,
        Texture2D captureTexture,
        int zIndex,
        out BossCapturedFragmentRenderSurface? surface)
    {
        surface = null;
        Node2D? anchor = null;
        try
        {
            BossCapturedFragmentDescriptor descriptor = prepared.Descriptor;
            anchor = new Node2D
            {
                Name = $"BossBodyFragment_{descriptor.FragmentIndex}",
                ZAsRelative = false,
                ZIndex = zIndex + Math.Clamp(descriptor.Part.DrawOrder, 0, 48),
                Visible = false
            };
            parent.AddChildSafely(anchor);
            EnsureInsideTree(anchor, "captured fragment anchor");

            var meshNode = new MeshInstance2D
            {
                Name = "CapturedFragmentMesh",
                Mesh = prepared.Mesh,
                Texture = captureTexture,
                Material = prepared.Material,
                ZAsRelative = true,
                ZIndex = 0
            };
            anchor.AddChildSafely(meshNode);
            EnsureInsideTree(meshNode, "captured fragment mesh");

            surface = new BossCapturedFragmentRenderSurface(
                anchor,
                meshNode,
                prepared.Material,
                prepared.Body,
                descriptor);
            prepared.MarkConsumed();
            return true;
        }
        catch (Exception exception)
        {
            if (anchor != null && GodotObject.IsInstanceValid(anchor))
            {
                anchor.QueueFreeSafely();
            }

            Scripts.Entry.Logger.Warn(
                $"Captured boss fragment instantiation failed: {exception.Message}");
            return false;
        }
    }

    internal void BindToSharedBody(SoftFragmentBody body, Rect2 deformationBounds) =>
        BindBody(body, deformationBounds);

    internal void BindToFragmentBody()
    {
        Rect2 cellBounds = ToRect2(BossDismembermentMath.BoundsOf(Descriptor.Cell.Vertices));
        BindBody(_fragmentBody, cellBounds);
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
                _body,
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
            for (int index = 0; index < _residuals.Length; index++)
            {
                _controlOffsets[index] = new Vector2(_residuals[index].X, _residuals[index].Y);
            }

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

    private void BindBody(SoftFragmentBody body, Rect2 deformationBounds)
    {
        _body = body;
        _meshNode.Position = new Vector2(
            _fragmentRestCenter.X - body.RestCenter.X,
            _fragmentRestCenter.Y - body.RestCenter.Y);
        _material.SetShaderParameter("cell_bounds_min", deformationBounds.Position);
        _material.SetShaderParameter("cell_bounds_size", deformationBounds.Size);
        _previousRotation = 0f;
        _consecutivePoseFailures = 0;
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

    private static SoftBodyHullPoint[] BuildRestHull(
        BossFragmentCell cell,
        Rect2 cellBounds,
        Transform2D bodyToPresentation) =>
        cell.Vertices
            .Select(point =>
            {
                Vector2 mapped = bodyToPresentation * ToVector2(point);
                return new SoftBodyHullPoint(
                    new BossFragmentPoint(mapped.X, mapped.Y),
                    Math.Clamp((point.X - cellBounds.Position.X) / cellBounds.Size.X, 0f, 1f),
                    Math.Clamp((point.Y - cellBounds.Position.Y) / cellBounds.Size.Y, 0f, 1f));
            })
            .ToArray();

    private static bool HasRenderableBounds(
        BossCapturedFragmentDescriptor descriptor,
        Rect2 cellBounds,
        Rect2 partBounds,
        Rect2 renderBounds) =>
        cellBounds.Size.X > 1f
        && cellBounds.Size.Y > 1f
        && renderBounds.Size.X > 1f
        && renderBounds.Size.Y > 1f
        && partBounds.Size.X > 1f
        && partBounds.Size.Y > 1f
        && descriptor.AtlasUvRect.Size.X > 0f
        && descriptor.AtlasUvRect.Size.Y > 0f;

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

    private static BossFragmentPoint Average(BossFragmentPoint[] points)
    {
        float x = 0f;
        float y = 0f;
        for (int index = 0; index < points.Length; index++)
        {
            x += points[index].X;
            y += points[index].Y;
        }

        float inverseCount = 1f / Math.Max(1, points.Length);
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
