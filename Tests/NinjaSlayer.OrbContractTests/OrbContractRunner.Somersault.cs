using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private void VerifySomersault(OrbCombat combat, NCreature actor, NCreature target, Marker2D targetCenter)
    {
        VerifyFastOrbitExposure();
        Node2D pose = actor.Visuals.GetNode<Node2D>("%AimPose");
        Node2D anchor = actor.Visuals.GetNode<Node2D>("AirborneAnchor");
        Marker2D center = actor.Visuals.GetNode<Marker2D>("%CenterPos");
        Sprite2D source = actor.Visuals.GetNode<Sprite2D>("%Visuals");
        Node2D stage = actor.GetParent<Node2D>();
        Transform2D stageBaseline = stage.Transform;
        Vector2 targetBaseline = target.Position, targetCore = targetCenter.Position;
        void Call(string method, params object?[] args) => AccessTools.Method(pose.GetType(), method).Invoke(pose, args);
        var contour = (System.Numerics.Vector2[])AccessTools.Field(typeof(ShurikenOrb).Assembly
            .GetType("NinjaSlayer.Code.Combat.CombatBodyContours"), "NinjaSlayer").GetValue(null)!;
        try
        {
            foreach (float scale in new[] { .75f, 1f, 1.5f })
            foreach (float facing in new[] { -1f, 1f })
            {
                Call("Reset");
                stage.Scale = Vector2.One * scale;
                target.Position = new(700f * facing, 0f);
                targetCenter.Position = new(0f, -300f);
                anchor.Transform = Transform2D.Identity;
                Call("SyncNow");
                Vector2 root = actor.Position;
                Call("BeginAction", combat.Enemy, false, false);
                float ground = (actor.Visuals.GetGlobalTransformWithCanvas() * new Vector2(0f, -6.45f)).Y;
                for (int frame = 0; frame <= 24; frame++)
                {
                    float p = frame / 24f;
                    Call("ApplySomersault", p);
                    Call("SetTravel", Vector2.Right * facing * (120f * p * p), p);
                    Require(actor.Position.IsEqualApprox(root), "Somersault moved the health/status root.");
                    Require(Math.Abs(pose.Scale.X - 1f) < .001f && Math.Abs(pose.Scale.Y - 1f) < .001f,
                        "Somersault introduced body compression.");
                    float bottom = contour.Max(point => (source.GetGlobalTransformWithCanvas() * new Vector2(point.X, point.Y)).Y);
                    Require(bottom <= ground + .5f, "Scaled somersault penetrated the ground.");
                }
                Require(Math.Abs(center.GetGlobalTransformWithCanvas().Origin.Y - targetCenter.GetGlobalTransformWithCanvas().Origin.Y) < .5f,
                    "Somersault impact did not align the actual VFX cores.");
                Vector2 peakCore = center.GetGlobalTransformWithCanvas().Origin;
                Call("BeginReturn"); Call("ApplyReturn", 0f);
                Require(center.GetGlobalTransformWithCanvas().Origin.DistanceTo(peakCore) < .5f,
                    "Somersault snapped when its return began.");
                for (int frame = 1; frame <= 12; frame++)
                {
                    pose._Process(1d / 60d);
                    Call("ApplyReturn", frame / 12f);
                }
                Call("Reset"); Call("SyncNow");
                Require(actor.Position.IsEqualApprox(root) && pose.Transform.IsEqualApprox(Transform2D.Identity),
                    "Somersault return left a transform contribution.");
            }
        }
        finally
        {
            Call("Reset");
            stage.Transform = stageBaseline;
            target.Position = targetBaseline;
            targetCenter.Position = targetCore;
            anchor.Transform = Transform2D.Identity;
            Call("SyncNow");
        }
        GD.Print("PASS special heavy: scaled grounding, both directions, core alignment, UI stability and continuous return.");
    }

    private void VerifyFastOrbitExposure()
    {
        Type type = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.FreeControlMotionBlur", true)!;
        var blur = (Node2D)Activator.CreateInstance(type)!;
        var body = new Sprite2D { Texture = ImageTexture.CreateFromImage(Image.CreateEmpty(8, 8, false, Image.Format.Rgba8)) };
        var stage = new Node2D();
        AddChild(stage);
        stage.AddChild(body);
        stage.AddChild(blur);
        var record = type.GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(method => method.Name == "RecordHistory" && method.GetParameters().Length == 6);
        Vector2 pivot = new(20f, -40f);
        Transform2D authored = new(0f, new Vector2(35f, 65f));
        const double frame = 1d / 60d;
        const double speed = 12000d * Math.PI / 180d;
        void Sample(double time, double angle)
        {
            body.Transform = new Transform2D((float)angle, pivot) * new Transform2D(0f, -pivot) * authored;
            record.Invoke(blur, [body, time, Transform2D.Identity, pivot, authored, angle]);
        }
        try
        {
            Sample(0d, 0d);
            Sample(frame, speed * frame);
            var rowsX = (Vector4[])AccessTools.Field(type, "_rowsX").GetValue(blur)!;
            var rowsY = (Vector4[])AccessTools.Field(type, "_rowsY").GetValue(blur)!;
            Require(blur.Visible, "Maximum-speed orbit did not produce an exposure.");
            Require(body.Material is ShaderMaterial output && output.Shader.ResourcePath.EndsWith("/horizontal_motion_blur.gdshader", StringComparison.Ordinal),
                "Planar exposure did not replace the body with the cylindrical spin output material.");
            object renderer = AccessTools.Field(type, "_renderer").GetValue(blur)!;
            foreach ((string field, string shader) in new[] {
                ("_filterMaterial", "spin_exposure_filter.gdshader"),
                ("_diffusionMaterial", "spin_exposure_diffusion.gdshader") })
            {
                var material = (ShaderMaterial)AccessTools.Field(renderer.GetType(), field).GetValue(renderer)!;
                Require(material.Shader.ResourcePath.EndsWith("/" + shader, StringComparison.Ordinal),
                    "Planar motion bypassed the shared cylindrical exposure filter/diffusion.");
            }
            for (int i = 1; i <= 5; i++)
            {
                double expected = speed * (1001d / 24000d) * i / (rowsX.Length - 1d);
                double actual = Math.Atan2(rowsY[i].X, rowsX[i].X);
                Require(Math.Abs(actual - expected) < .001d,
                    "Maximum-speed exposure reversed to the short arc between frames.");
            }
            body.Texture = ImageTexture.CreateFromImage(Image.CreateEmpty(8, 8, false, Image.Format.Rgba8));
            Sample(frame * 2d, speed * frame * 2d);
            Require(blur.Visible && body.Material != null, "An idle texture frame erased the motion exposure.");
            Sample(frame * 2d + .06d, speed * frame * 2d);
            Require(!blur.Visible, "Paused orbit retained motion blur after its exposure window elapsed.");
            Require(body.Material == null, "Stopped planar exposure left its replacement material on the body.");
            AccessTools.Method(type, "ClearHistory").Invoke(blur, null);
            var planar = type.GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Single(method => method.Name == "RecordHistory" && method.GetParameters().Length == 3);
            body.Transform = Transform2D.Identity;
            planar.Invoke(blur, [body, 1d, Vector2.Zero]);
            body.Position = new(20f, -10f);
            planar.Invoke(blur, [body, 1.02d, body.Position]);
            Require(blur.Visible && body.Material is ShaderMaterial,
                "Planar translation without a turn lost its exposure.");
            rowsX = (Vector4[])AccessTools.Field(type, "_rowsX").GetValue(blur)!;
            rowsY = (Vector4[])AccessTools.Field(type, "_rowsY").GetValue(blur)!;
            Require(Math.Abs(rowsX[^1].Z - 20f) < .001f && Math.Abs(rowsY[^1].Z + 10f) < .001f,
                "Planar motion ignored the actual two-dimensional translation path.");
            planar.Invoke(blur, [body, 1.08d, body.Position]);
            Require(!blur.Visible && body.Material == null, "Stopped translation retained its trail.");
        }
        finally { stage.Free(); }
        GD.Print("PASS orbit exposure: 12000 degrees/s at 60fps preserves direction; hit-stop history settles.");
    }
}
