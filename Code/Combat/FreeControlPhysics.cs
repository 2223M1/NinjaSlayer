using System.Numerics;
using Box2D.NET;
using static Box2D.NET.B2Bodies;
using static Box2D.NET.B2Geometries;
using static Box2D.NET.B2Hulls;
using static Box2D.NET.B2Joints;
using static Box2D.NET.B2MathFunction;
using static Box2D.NET.B2Shapes;
using static Box2D.NET.B2Types;
using static Box2D.NET.B2Worlds;

namespace NinjaSlayer.Code.Combat;

// Owns a private solver because the game's PhysicsServer2D is a Dummy server.
internal sealed class FreeControlPhysics : IDisposable
{
    internal const float StepSeconds = 1f / 60f;
    private const float PixelsPerMeter = 100f;
    private B2WorldId _world;
    private readonly B2BodyId _body, _cursor;
    private B2JointId _grip;
    private readonly List<B2ShapeId> _shapes = [];
    private readonly Dictionary<ulong, (B2BodyId Body, Vector2[] Points)> _enemies = [];
    private Vector2[] _hull = [];
    private Vector2 _gripLocal;
    private bool _disposed;
    internal bool Ragging { get; private set; }
    internal bool Grabbing { get; private set; }
    internal bool Grounded { get; private set; }
    internal float WallNormal { get; private set; }
    internal readonly Dictionary<ulong, (Vector2 Point, Vector2 Velocity)> Contacts = [];
    internal Vector2 Position => Pixels(b2Body_GetPosition(_body));
    internal float Rotation => b2Rot_GetAngle(b2Body_GetRotation(_body));
    internal Vector2 Velocity { get => Pixels(b2Body_GetLinearVelocity(_body)); set => b2Body_SetLinearVelocity(_body, Meters(value)); }
    internal float AngularVelocity { get => b2Body_GetAngularVelocity(_body); set => b2Body_SetAngularVelocity(_body, value); }
    internal Vector2 GripPoint => Pixels(b2Body_GetWorldPoint(_body, Meters(_gripLocal)));

    internal FreeControlPhysics(Vector2 core, Vector2 arenaStart, Vector2 arenaEnd, Vector2[] hull)
    {
        B2WorldDef world = b2DefaultWorldDef();
        world.gravity = new(0f, FreeControlMotor.Gravity / PixelsPerMeter);
        world.maximumLinearSpeed = 10000f;
        _world = b2CreateWorld(world);
        B2BodyDef body = b2DefaultBodyDef();
        body.type = B2BodyType.b2_dynamicBody;
        body.position = Meters(core);
        body.motionLocks.angularZ = true;
        body.gravityScale = 0f;
        body.enableSleep = false;
        body.isBullet = true;
        body.allowFastRotation = true;
        _body = b2CreateBody(_world, body);
        body = b2DefaultBodyDef();
        body.type = B2BodyType.b2_kinematicBody;
        _cursor = b2CreateBody(_world, body);
        UpdateHull(hull);
        Vector2 middle = (arenaStart + arenaEnd) * .5f, size = arenaEnd - arenaStart;
        Boundary(new(middle.X, arenaEnd.Y + 50f), new(size.X + 200f, 100f));
        Boundary(new(middle.X, arenaStart.Y - 50f), new(size.X + 200f, 100f));
        Boundary(new(arenaStart.X - 50f, middle.Y), new(100f, size.Y + 200f));
        Boundary(new(arenaEnd.X + 50f, middle.Y), new(100f, size.Y + 200f));
    }

    private static B2Vec2 Meters(Vector2 pixels) => new(pixels.X / PixelsPerMeter, pixels.Y / PixelsPerMeter);
    private static Vector2 Pixels(B2Vec2 meters) => new(meters.X * PixelsPerMeter, meters.Y * PixelsPerMeter);
    private void Boundary(Vector2 center, Vector2 size)
    {
        B2BodyDef body = b2DefaultBodyDef(); body.position = Meters(center);
        B2ShapeDef shape = b2DefaultShapeDef();
        shape.filter.categoryBits = 1; shape.filter.maskBits = 2;
        shape.material.friction = .65f;
        b2CreatePolygonShape(b2CreateBody(_world, body), shape, b2MakeBox(size.X / 200f, size.Y / 200f));
    }

    internal void SetTransform(Vector2 position, float rotation) => b2Body_SetTransform(_body, Meters(position), b2MakeRot(rotation));

    internal void UpdateHull(Vector2[] points)
    {
        if (_hull.Length == points.Length && !points.Where((p, i) => Vector2.DistanceSquared(p, _hull[i]) > .0625f).Any()) return;
        // Adjacent convex pieces share only their fan edge, so mass is counted once.
        var polygons = new List<B2Polygon>();
        for (int start = 1; start < points.Length - 1; start += 6)
        {
            B2Vec2[] part = new B2Vec2[Math.Min(7, points.Length - start) + 1];
            part[0] = Meters(points[0]);
            for (int i = 1; i < part.Length; i++) part[i] = Meters(points[start + i - 1]);
            B2Hull hull = b2ComputeHull(part, part.Length);
            // Edge-on sprite projection temporarily has no area; retain its last solid collider.
            if (hull.count < 3) continue;
            polygons.Add(b2MakePolygon(hull, 0f));
        }
        if (polygons.Count == 0) return;
        Vector2 velocity = Velocity;
        foreach (B2ShapeId shape in _shapes) b2DestroyShape(shape, false);
        _shapes.Clear();
        B2ShapeDef definition = b2DefaultShapeDef();
        definition.filter.categoryBits = 2; definition.filter.maskBits = Ragging ? 5u : 1u;
        definition.material.friction = Ragging ? .65f : 0f;
        definition.material.restitution = Ragging ? .25f : 0f;
        foreach (B2Polygon polygon in polygons) _shapes.Add(b2CreatePolygonShape(_body, definition, polygon));
        b2Body_ApplyMassFromShapes(_body);
        Velocity = velocity;
        _hull = (Vector2[])points.Clone();
    }

    internal void UpdateEnemies(IEnumerable<(ulong Id, Vector2[] Polygon)> enemies)
    {
        var current = new HashSet<ulong>();
        foreach ((ulong id, Vector2[] points) in enemies)
        {
            current.Add(id);
            if (_enemies.TryGetValue(id, out var old))
            {
                if (old.Points.SequenceEqual(points)) continue;
                b2DestroyBody(old.Body);
            }
            B2BodyId body = b2CreateBody(_world, b2DefaultBodyDef());
            B2ShapeDef shape = b2DefaultShapeDef();
            shape.filter.categoryBits = 4; shape.filter.maskBits = 2;
            shape.material.friction = .65f;
            B2Vec2[] vertices = points.Select(Meters).ToArray();
            B2Hull hull = b2ComputeHull(vertices, vertices.Length);
            b2CreatePolygonShape(body, shape, b2MakePolygon(hull, 0f));
            _enemies[id] = (body, (Vector2[])points.Clone());
        }
        foreach (ulong id in _enemies.Keys.Where(id => !current.Contains(id)).ToArray())
        { b2DestroyBody(_enemies[id].Body); _enemies.Remove(id); }
    }

    internal void Grab(Vector2 localPoint)
    {
        if (Grabbing) return;
        Ragging = true;
        ConfigureBody();
        _gripLocal = localPoint;
        b2Body_SetTransform(_cursor, b2Body_GetWorldPoint(_body, Meters(localPoint)), b2MakeRot(0f));
        B2RevoluteJointDef joint = b2DefaultRevoluteJointDef();
        joint.@base.bodyIdA = _cursor; joint.@base.bodyIdB = _body;
        joint.@base.localFrameB.p = Meters(localPoint);
        joint.enableMotor = joint.enableLimit = joint.enableSpring = false;
        _grip = b2CreateRevoluteJoint(_world, joint);
        Grabbing = true;
    }

    internal void Release()
    {
        if (!Grabbing) return;
        b2DestroyJoint(_grip, true); _grip = default; Grabbing = false;
        b2Body_SetLinearVelocity(_cursor, default);
    }

    internal void ResumeWalking()
    {
        Release(); Ragging = false; AngularVelocity = 0f; ConfigureBody();
    }

    private void ConfigureBody()
    {
        b2Body_SetMotionLocks(_body, new(false, false, !Ragging));
        b2Body_SetGravityScale(_body, Ragging ? 1f : 0f);
        b2Body_SetLinearDamping(_body, Ragging ? .3f : 0f);
        b2Body_SetAngularDamping(_body, Ragging ? .25f : 0f);
        foreach (B2ShapeId shape in _shapes)
        {
            B2Filter filter = b2Shape_GetFilter(shape); filter.maskBits = Ragging ? 5u : 1u;
            b2Shape_SetFilter(shape, filter);
            b2Shape_SetFriction(shape, Ragging ? .65f : 0f);
            b2Shape_SetRestitution(shape, Ragging ? .25f : 0f);
        }
    }

    internal void Step(Vector2 pointer)
    {
        if (Grabbing)
            b2Body_SetLinearVelocity(_cursor, Meters((pointer - Pixels(b2Body_GetPosition(_cursor))) / StepSeconds));
        else if (Ragging)
        {
            Vector2 speed = Velocity;
            if (speed.LengthSquared() > 2400f * 2400f) Velocity = Vector2.Normalize(speed) * 2400f;
            AngularVelocity = Math.Clamp(AngularVelocity, -MathF.Tau * 20f, MathF.Tau * 20f);
        }
        Vector2 velocity = Velocity, center = Pixels(b2Body_GetWorldCenterOfMass(_body));
        float spin = AngularVelocity;
        b2World_Step(_world, StepSeconds, 4);
        Grounded = false; WallNormal = 0f; Contacts.Clear();
        B2ContactData[] contacts = new B2ContactData[b2Body_GetContactCapacity(_body)];
        int count = b2Body_GetContactData(_body, contacts, contacts.Length);
        for (int i = 0; i < count; i++)
        {
            B2ContactData contact = contacts[i];
            bool isA = b2Shape_GetBody(contact.shapeIdA).Equals(_body);
            B2BodyId other = b2Shape_GetBody(isA ? contact.shapeIdB : contact.shapeIdA);
            B2Vec2 normal = contact.manifold.normal * (isA ? -1f : 1f);
            for (int j = 0; j < contact.manifold.pointCount; j++)
            {
                B2ManifoldPoint point = contact.manifold.points[j];
                if (point.separation > .01f && point.totalNormalImpulse <= 0f) continue;
                if (normal.Y < -.7f) Grounded = true;
                if (Math.Abs(normal.X) > .7f) WallNormal = normal.X;
                Vector2 position = Pixels(point.point), radius = position - center;
                foreach (var enemy in _enemies)
                    if (enemy.Value.Body.Equals(other)) Contacts[enemy.Key] = (position, velocity + new Vector2(-radius.Y, radius.X) * spin);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; b2DestroyWorld(_world); _world = default;
    }
}
