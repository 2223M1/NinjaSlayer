using Godot;
using HarmonyLib;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyEntangledSpinExposure()
    {
        string extension = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(
            "res://../../addons/spine/spine_godot_extension.gdextension"));
        GDExtensionManager.LoadExtension(extension);
        Require(ClassDB.ClassExists("SpineSprite"), "Live enemy spin contracts require native Spine.");
        var assembly = typeof(ShurikenOrb).Assembly;
        Type blurType = assembly.GetType("NinjaSlayer.Code.Nodes.EntangledSpinMotionBlur", true)!;
        Type projectionType = assembly.GetType("NinjaSlayer.Code.ExternalAnimations.VerticalAxisSpinProjection", true)!;
        string? directory = System.Environment.GetEnvironmentVariable("NINJASLAYER_SPIN_RENDER_DIR");
        async Task Frame()
        {
            if (directory != null) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            else await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        foreach (string kind in new[] { "PNG", "Spine", "Composite" })
        {
            bool native = kind == "Spine";
            var viewport = new SubViewport { Size = new(1200, 800), TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            var stage = new Node2D();
            viewport.AddChild(stage);
            Node2D body;
            if (native)
            {
                body = (Node2D)ClassDB.Instantiate("SpineSprite").AsGodotObject();
                string scene = Godot.FileAccess.GetFileAsString("res://scenes/creature_visuals/fogmog.tscn");
                foreach (System.Text.RegularExpressions.Match match in
                    System.Text.RegularExpressions.Regex.Matches(scene, "path=\"([^\"]+\\.tres)\""))
                {
                    Resource resource = GD.Load(match.Groups[1].Value);
                    if (resource.IsClass("SpineSkeletonDataResource")) body.Call("set_skeleton_data_res", resource);
                }
            }
            else if (kind == "PNG")
                body = new Sprite2D { Texture = GD.Load<Texture2D>("res://NinjaSlayer/images/monsters/dark_ninja.png") };
            else
            {
                body = new Node2D();
                body.AddChild(new Sprite2D { Texture = GD.Load<Texture2D>("res://NinjaSlayer/images/monsters/dark_ninja.png") });
            }
            body.Position = new(800f, native ? 200f : 450f);
            body.Scale = Vector2.One * .65f;
            body.RotationDegrees = 180f;
            stage.AddChild(body);
            await Frame();
            await Frame();
            if (native)
            {
                using Variant state = body.Call("get_animation_state");
                using Variant track = state.AsGodotObject().Call("set_animation", "hurt", false, 0);
                track.AsGodotObject().Call("set_track_time", .1f);
                track.AsGodotObject().Call("set_time_scale", 0f);
                await Frame();
                await Frame();
            }
            Rect2 bounds = kind switch
            {
                "PNG" => ((Sprite2D)body).GetRect(),
                "Composite" => body.GetChild<Sprite2D>(0).GetRect(),
                _ => body.Call("get_skeleton").AsGodotObject().Call("get_bounds").AsRect2()
            };
            Transform2D baseline = body.Transform;
            Node parent = body.GetParent();
            int children = body.GetChildCount(true);
            Material material = body.Material;
            Node.ProcessModeEnum processMode = body.ProcessMode;
            Image? original = directory != null ? viewport.GetTexture().GetImage() : null;
            var blur = (Node2D)AccessTools.Method(blurType, "Create").Invoke(null, [body, bounds])!;
            blur.SetProcess(false);
            void Sync() => AccessTools.Method(blurType, "SyncNow").Invoke(blur, null);
            void Stop() => AccessTools.Method(blurType, "Stop").Invoke(blur, null);
            Vector2 center = body.GetGlobalTransformWithCanvas() * bounds.GetCenter();
            const float sharedAxis = 530f;
            object projection = AccessTools.Method(projectionType, "CaptureCurrent")
                .Invoke(null, [body, sharedAxis, center])!;
            try
            {
                for (int frame = 0; frame <= 36; frame++)
                {
                    double now = frame / 60d;
                    if (frame > 0) blur._Process(1d / 60d);
                    static double Angle(double time)
                    {
                        double p = Math.Clamp(time / .6d, 0d, 1d);
                        return 1800d * (p + 1.2d * p * p + .2d * p * p * p);
                    }
                    float degrees = (float)Angle(now);
                    Func<double, double> before = age => Angle(now - age);
                    AccessTools.Method(projectionType, "ApplyDegrees").Invoke(projection, [degrees, before]);
                    AccessTools.Method(blurType, "Record").Invoke(blur, [projection, degrees, before]);
                    Sync();
                    await Frame();
                    Require(body.GetParent() == parent && body.GetChildCount(true) == children
                        && body.Material == material && body.ProcessMode == processMode,
                        "Live spin capture changed the original body hierarchy, materials or processing.");
                    if (frame < 3) continue;
                    Sprite2D display = blur.GetNode<Sprite2D>("BodyExposure");
                    var exposureMaterial = (ShaderMaterial)display.Material;
                    float axis = exposureMaterial.GetShaderParameter("axis_x").AsSingle();
                    float drawnAxis = (display.GetGlobalTransformWithCanvas() * new Vector2(axis, 0f)).X;
                    Require(Math.Abs(drawnAxis - sharedAxis) < .01f,
                        "Alabama exposure used the victim's own center instead of the shared orbit axis.");
                    float ratio = exposureMaterial.GetShaderParameter("current_ratio").AsSingle();
                    Vector2 projectedCenter = body.GetGlobalTransformWithCanvas() * bounds.GetCenter();
                    Require(Math.Abs(projectedCenter.X - (sharedAxis + (center.X - sharedAxis) * ratio)) < .05f,
                        "The original victim stopped orbiting the shared axis.");
                    if (directory != null && frame is 12 or 24 or 35)
                    {
                        using Image rendered = viewport.GetTexture().GetImage();
                        rendered.SavePng($"{directory}/entangled-{kind.ToLowerInvariant()}-{frame}.png");
                        int opaque = 0, soft = 0, left = 0, right = 0;
                        for (int y = 0; y < 800; y += 2)
                        for (int x = 0; x < 1200; x += 2)
                        {
                            float alpha = rendered.GetPixel(x, y).A;
                            if (alpha > .8f) opaque++;
                            if (alpha > .02f && alpha < .8f) soft++;
                            if (alpha > .1f && x < sharedAxis - 40f) left++;
                            if (alpha > .1f && x > sharedAxis + 40f) right++;
                        }
                        Require(opaque > 1500 && soft > 500 && left > 500 && right > 500,
                            $"Live entangled render lost the orbit or soft exposure: {opaque}/{soft}/{left}/{right}.");
                    }
                }
                var frozenTransform = body.Transform;
                stage.ProcessMode = ProcessModeEnum.Disabled;
                Sync();
                await Frame();
                Require(body.Transform.IsEqualApprox(frozenTransform), "Paused enemy exposure advanced the pose.");
                stage.ProcessMode = ProcessModeEnum.Inherit;
                AccessTools.Method(projectionType, "Restore").Invoke(projection, null);
                Stop();
                Stop();
                Require(body.Transform.IsEqualApprox(baseline), "Spin completion failed to restore the victim transform.");
                await Frame();
                await Frame();
                Require(!GodotObject.IsInstanceValid(blur), "Spin completion left capture viewports in the scene.");
                if (original != null)
                {
                    using Image restored = viewport.GetTexture().GetImage();
                    Require(original.GetData().SequenceEqual(restored.GetData()),
                        "The original victim did not render identically after blur cleanup.");
                    original.Dispose();
                }
                // Early cancellation and target removal must restore drawing without a final spin callback.
                var cancelled = (Node)AccessTools.Method(blurType, "Create").Invoke(null, [body, bounds])!;
                cancelled.Free();
                Require(body.Transform.IsEqualApprox(baseline), "Cancelling capture changed the original pose.");
                var removed = (Node)AccessTools.Method(blurType, "Create").Invoke(null, [body, bounds])!;
                body.Free();
                await Frame();
                await Frame();
                Require(!GodotObject.IsInstanceValid(removed), "Removing the victim left an active capture behind.");
                GD.Print($"PASS entangled {kind}: live body, shared axis, 3000-12000 dps, pause, exact restore, cancellation and removal.");
            }
            finally
            {
                if (GodotObject.IsInstanceValid(blur)) Stop();
                viewport.QueueFree();
                await Frame();
            }
        }
    }
}
