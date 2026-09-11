using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Orbs;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifySpinExposure(NCreatureVisuals rig, int form)
    {
        Node blur = rig.GetNode("SpinMotionBlur");
        Sprite2D source = rig.GetNode<Sprite2D>("%Visuals");
        Sprite2D overlay = rig.GetNode<Sprite2D>("AirborneAnchor/AimPose/NarakuVisualOverlay");
        Node2D anchor = rig.GetNode<Node2D>("AirborneAnchor");
        var projectionType = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.VerticalAxisSpinProjection", true)!;
        Transform2D baseline = source.Transform, anchorBaseline = anchor.Transform;
        var processes = new List<(Node Node, bool Processing)>();
        void StopProcesses(Node node)
        {
            processes.Add((node, node.IsProcessing()));
            node.SetProcess(false);
            foreach (Node child in node.GetChildren()) StopProcesses(child);
        }
        StopProcesses(rig);
        string? directory = System.Environment.GetEnvironmentVariable("NINJASLAYER_SPIN_RENDER_DIR");
        SubViewport? viewport = directory != null ? (SubViewport)rig.GetViewport() : null;
        Transform2D canvasBaseline = viewport?.CanvasTransform ?? Transform2D.Identity;
        if (directory != null)
        {
            System.IO.Directory.CreateDirectory(directory);
            viewport!.CanvasTransform = new Transform2D(0f, new Vector2(720, 650) - rig.GlobalPosition);
        }
        void Blur(string method) => AccessTools.Method(blur.GetType(), method).Invoke(blur, null);
        object Capture(float axisOffset = 0f)
        {
            Node2D focus = rig.GetNode<Node2D>("%CinematicFocus");
            Vector2 point = focus.GetGlobalTransformWithCanvas().Origin;
            return AccessTools.Method(projectionType, "CaptureCurrent").Invoke(null, [source, point.X + axisOffset, point])!;
        }
        void Apply(object projection, float degrees, Func<double, double>? before = null) =>
            AccessTools.Method(projectionType, "ApplyDegrees").Invoke(projection, [degrees, before]);
        void Restore(object projection) => AccessTools.Method(projectionType, "Restore").Invoke(projection, null);
        try
        {
            anchor.Transform = Transform2D.Identity;
            foreach (int fps in new[] { 30, 60, 120, 144 })
            foreach (float speed in new[] { 2400f, 4500f, 12000f })
            {
                Blur("Reset");
                source.Transform = baseline;
                object projection = Capture();
                Apply(projection, 0);
                // End at an edge-on phase to check expansion beyond the 18% body rect.
                double duration = (360d + 90d) / speed;
                for (double t = 1d / fps; t < duration + 1d / fps; t += 1d / fps)
                {
                    double now = Math.Min(duration, t);
                    double previous = Math.Max(0d, t - 1d / fps);
                    blur._Process(now - previous);
                    Apply(projection, (float)(now * speed), age => Math.Max(0d, now - age) * speed);
                }
                Blur("SyncNow");
                Sprite2D active = overlay.Visible ? overlay : source;
                Require(active.Material is ShaderMaterial, $"Form {form}: spin exposure has no material.");
                var material = (ShaderMaterial)active.Material;
                var exposure = blur.GetNode<SubViewport>("AngularExposure");
                var exposureMaterial = (ShaderMaterial)exposure.GetChild<Sprite2D>(0).Material;
                float[] ratios = exposureMaterial.GetShaderParameter("sample_ratios").AsFloat32Array();
                Require(ratios.Length == 25 && ratios.Any(r => Math.Abs(r) > .5f),
                    $"Form {form}: exposure lost the swept body at {fps} fps / {speed} dps.");
                Vector2 bounds = material.GetShaderParameter("draw_bounds").AsVector2();
                Require(bounds.Y - bounds.X > active.GetRect().Size.X * 2f,
                    "Spin geometry failed to expand beyond the current silhouette.");
                Blur("SyncNow");
                Require(ratios.SequenceEqual(exposureMaterial.GetShaderParameter("sample_ratios").AsFloat32Array()),
                    "PreDraw advanced the exposure a second time.");
                if (viewport != null && fps == 60)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using Image rendered = viewport.GetTexture().GetImage();
                    Require(!rendered.IsEmpty(), "Spin scene produced an empty render.");
                    int colored = 0, translucent = 0, solid = 0;
                    for (int y = 40; y < 620; y += 2)
                    for (int x = 40; x < 1350; x += 2)
                    {
                        Color pixel = rendered.GetPixel(x, y);
                        if (pixel.A > .02f && Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > .1f) colored++;
                        if (pixel.A > .02f && pixel.A < .8f && pixel.R > .1f) translucent++;
                        if (pixel.A > .95f && pixel.R > .1f) solid++;
                    }
                    rendered.SavePng($"{directory}/form-{form}-{speed}-edge.png");
                    GD.Print($"Spin pixels form {form}, {speed} dps: colored={colored}, soft={translucent}, solid={solid}.");
                    Require(colored > 1000 && translucent > 0, "Spin exposure lost its color or soft texture edges.");
                    Require(solid > 1000, "Fast rotation faded the swept body instead of retaining its silhouette.");
                    if (form == 0 && speed == 12000f)
                    {
                        using Image texture = source.Texture.GetImage();
                        Transform2D world = source.GetGlobalTransformWithCanvas();
                        float axis = material.GetShaderParameter("axis_x").AsSingle();
                        float axisCanvas = (world * new Vector2(axis, 0f)).X;
                        float unprojectedScale = world.X.Length() / .18f;
                        float blurRadius = material.GetShaderParameter("blur_radius").AsSingle();
                        Rect2 rect = source.GetRect();
                        float verticalRadius = material.GetShaderParameter("draw_y_bounds").AsVector2().Y - rect.End.Y;
                        int rowRadius = (int)Math.Ceiling(verticalRadius / rect.Size.Y * texture.GetHeight());
                        for (int y = 500; y < 610; y += 12)
                        {
                            float localY = (world.AffineInverse() * new Vector2(axisCanvas, y)).Y;
                            int row = (int)((localY - rect.Position.Y) / rect.Size.Y * texture.GetHeight());
                            if (source.FlipV) row = texture.GetHeight() - 1 - row;
                            Require(row >= 0 && row < texture.GetHeight(), "Spin bound fixture missed the source body.");
                            float radius = 0f;
                            for (int neighbor = Math.Max(0, row - rowRadius); neighbor <= Math.Min(texture.GetHeight() - 1, row + rowRadius); neighbor++)
                            for (int x = 0; x < texture.GetWidth(); x++)
                            {
                                if (texture.GetPixel(x, neighbor).A <= .01f) continue;
                                float u = (float)x / texture.GetWidth();
                                if (source.FlipH) u = 1f - u;
                                radius = Math.Max(radius, Math.Abs(rect.Position.X + u * rect.Size.X - axis) * unprojectedScale);
                            }
                            for (int x = 40; x < 1350; x++)
                                if (Math.Abs(x - axisCanvas) > radius + blurRadius * 1.03125f * unprojectedScale + 4f)
                                    Require(rendered.GetPixel(x, y).A <= .02f,
                                        "Exposure exceeded the cylindrical sweep plus its bounded streak filter.");
                        }
                        Node freeze = rig.GetParent();
                        var priorMode = freeze.ProcessMode;
                        freeze.ProcessMode = ProcessModeEnum.Disabled;
                        material.SetShaderParameter("blur_radius", 0f);
                        // Compare against the unfiltered angular capture, not the
                        // prefilter needed to prevent sparse spatial tap aliasing.
                        material.SetShaderParameter("exposure_texture", exposure.GetTexture());
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using Image angularOnly = viewport.GetTexture().GetImage();
                        material.SetShaderParameter("blur_radius", blurRadius);
                        material.SetShaderParameter("exposure_texture", blur.GetNode<SubViewport>("DiffuseExposure").GetTexture());
                        freeze.ProcessMode = priorMode;
                        double originalDetail = TextureDetail(angularOnly, (int)axisCanvas - 60, 400, 120, 205);
                        double smearDetail = TextureDetail(rendered, (int)axisCanvas - 60, 400, 120, 205);
                        double originalVertical = TextureDetail(angularOnly, (int)axisCanvas - 60, 400, 120, 205, vertical: true);
                        double diffuseVertical = TextureDetail(rendered, (int)axisCanvas - 60, 400, 120, 205, vertical: true);
                        GD.Print($"Spin core detail: angular={originalDetail:F2}, smeared={smearDetail:F2}; vertical={originalVertical:F2}, diffused={diffuseVertical:F2}.");
                        Require(smearDetail < originalDetail * .35,
                            "Fast spin still draws sharp face/cloth outlines near the rotation axis.");
                        Require(diffuseVertical < originalVertical * .35,
                            "Fast spin only made horizontal bands without diffusing their vertical edges.");
                    }
                }
                Node freezeNode = rig.GetParent();
                var mode = freezeNode.ProcessMode;
                freezeNode.ProcessMode = ProcessModeEnum.Disabled;
                Blur("SyncNow");
                Require(ratios.SequenceEqual(exposureMaterial.GetShaderParameter("sample_ratios").AsFloat32Array()),
                    "Finisher freeze altered the exposure.");
                freezeNode.ProcessMode = mode;
                var hostMaterial = new CanvasItemMaterial();
                active.Material = hostMaterial;
                Blur("SyncNow");
                Require(active.Material == hostMaterial, "Blur replaced a host-owned material.");
                Blur("Reset");
                Require(active.Material == hostMaterial, "Blur cleanup overwrote a host-owned material.");
                Require(exposure.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled,
                    "Blur reset left its private render target running.");
                Require(blur.GetNode<SubViewport>("FilteredExposure").RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled,
                    "Blur reset left its prefilter running.");
                Require(blur.GetNode<SubViewport>("DiffuseExposure").RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled,
                    "Blur reset left its diffusion target running.");
                active.Material = null;
                hostMaterial.Dispose();
                Restore(projection);
            }
            source.Transform = baseline;
            object tailProjection = Capture();
            Apply(tailProjection, 0f);
            blur._Process(.05);
            Apply(tailProjection, 180f, age => 180d - 3600d * age);
            Blur("SyncNow");
            Restore(tailProjection);
            blur._Process(.05);
            Blur("SyncNow");
            Require(source.Material == null && overlay.Material == null, "Normal stop retained an exposure material.");
            foreach (float offset in new[] { -160f, 160f })
            {
                Blur("Reset");
                source.Transform = baseline;
                source.RotationDegrees = 180f;
                Vector2 point = rig.GetNode<Node2D>("%CinematicFocus").GetGlobalTransformWithCanvas().Origin;
                Vector2 localPoint = source.GetGlobalTransformWithCanvas().AffineInverse() * point;
                float sharedAxis = point.X + offset;
                object sharedProjection = Capture(offset);
                Apply(sharedProjection, 0f);
                blur._Process(.05);
                Apply(sharedProjection, 225f, age => 225d - 4500d * age);
                Blur("SyncNow");
                Sprite2D active = overlay.Visible ? overlay : source;
                var sharedMaterial = (ShaderMaterial)active.Material;
                float axis = sharedMaterial.GetShaderParameter("axis_x").AsSingle();
                float renderedAxis = (active.GetGlobalTransformWithCanvas() * new Vector2(axis, 0f)).X;
                Require(Math.Abs(renderedAxis - sharedAxis) < .01f,
                    $"Ninja Slayer form {form} blurred around its own body instead of Alabama's shared axis.");
                float ratio = sharedMaterial.GetShaderParameter("current_ratio").AsSingle();
                float actualPoint = (source.GetGlobalTransformWithCanvas() * localPoint).X;
                Require(Math.Abs(actualPoint - (sharedAxis + (point.X - sharedAxis) * ratio)) < .05f,
                    "Ninja Slayer exposure changed the existing entangled orbit.");
                Restore(sharedProjection);
            }
            foreach (float facing in new[] { -1f, 1f })
            foreach (float inverted in new[] { 0f, 180f })
            {
                Blur("Reset");
                anchor.Scale = new(facing, 1.2f);
                anchor.RotationDegrees = 6f;
                source.Transform = baseline;
                source.RotationDegrees = inverted;
                object projection = Capture();
                Apply(projection, 0f);
                blur._Process(.05);
                Apply(projection, 225f, age => 225d - 4500d * age);
                Blur("SyncNow");
                Sprite2D active = overlay.Visible ? overlay : source;
                var material = (ShaderMaterial)active.Material;
                Vector2 originalBounds = material.GetShaderParameter("draw_bounds").AsVector2();
                Vector2 parentPosition = anchor.Position;
                anchor.Position += new Vector2(230f, -150f);
                Blur("SyncNow");
                Require(material.GetShaderParameter("draw_bounds").AsVector2().IsEqualApprox(originalBounds),
                    "Moving the actor left old exposure geometry behind in world space.");
                Require(active.Visible && active.Material == material, "Mirrored/inverted form lost its exposure.");
                anchor.Position = parentPosition;
                Restore(projection);
            }
            Blur("Reset");
            anchor.Transform = Transform2D.Identity;
            source.Transform = baseline;
            if (viewport != null && form == 0)
            {
                string frames = $"{directory}/motion";
                System.IO.Directory.CreateDirectory(frames);
                Color clear = RenderingServer.GetDefaultClearColor();
                viewport.TransparentBg = false;
                RenderingServer.SetDefaultClearColor(new Color(.12f, .11f, .14f));
                object projection = Capture();
                Apply(projection, 0f);
                Vector2I? captureSize = null;
                try
                {
                    for (int frame = 0; frame <= 600; frame++)
                    {
                        double now = frame / 60d;
                        if (frame > 0) blur._Process(1d / 60d);
                        if (frame <= 540)
                            Apply(projection, (float)SpinClipAngle(now), age => SpinClipAngle(Math.Max(0d, now - age)));
                        if (frame == 540) Restore(projection);
                        Blur("SyncNow");
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using Image rendered = viewport.GetTexture().GetImage();
                        rendered.SavePng($"{frames}/{frame:D4}.png");
                        var capture = blur.GetNode<SubViewport>("AngularExposure");
                        captureSize ??= capture.Size;
                        Require(capture.Size == captureSize.Value, "Spinning reallocated the local exposure target.");
                        if (frame >= 544)
                            Require(source.Transform.IsEqualApprox(baseline) && source.Material == null && overlay.Material == null,
                                "Deceleration failed to return to a clean, uncompressed body.");
                    }
                }
                finally
                {
                    Restore(projection);
                    Blur("Reset");
                    viewport.TransparentBg = true;
                    RenderingServer.SetDefaultClearColor(clear);
                }
                GD.Print("PASS continuous spin render: rest, acceleration, 2400/4500/12000 dps, deceleration and rest.");
            }
            blur.SetProcess(true);
            var creature = ((NCreature)rig.GetParent()).Entity;
            SoarSpinAnimation.StartAirborneSpin(creature, 12000f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.12f), SceneTreeTimer.SignalName.Timeout);
            Blur("SyncNow");
            var degreesByCreature = (System.Collections.IDictionary)AccessTools.Field(typeof(SoarSpinAnimation), "spinDegrees").GetValue(null)!;
            Require((float)degreesByCreature[creature]! > 720f, "The live looping Tween lost complete turns.");
            Require((overlay.Visible ? overlay : source).Material is ShaderMaterial, "Live Soar Tween did not drive exposure.");
            if (viewport != null)
            {
                ulong start = Time.GetTicksUsec();
                for (int frame = 0; frame < 60; frame++)
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GD.Print($"Spin live render form {form}: {(Time.GetTicksUsec() - start) / 60000d:F2} ms/frame (60 frames, no image readback).");
            }
            SoarSpinAnimation.ResetSpinVisual(creature);
            Require(source.Material == null && overlay.Material == null, "Spin reset retained its material.");
            await (Task)AccessTools.Method(typeof(SoarSpinAnimation), "PlayCueSpin").Invoke(null, [creature, .24f])!;
            await ToSignal(GetTree().CreateTimer(.06f), SceneTreeTimer.SignalName.Timeout);
            Blur("SyncNow");
            Require(source.Transform.IsEqualApprox(baseline), "Continuous fallback cue did not restore its authored body transform.");
            GD.Print($"PASS spin exposure form {form}: 30/60/120/144 fps, unwrapped fast turns, expanded alpha, freeze, material takeover and stop.");
        }
        finally
        {
            Blur("Reset");
            source.Transform = baseline;
            anchor.Transform = anchorBaseline;
            if (viewport != null) viewport.CanvasTransform = canvasBaseline;
            foreach (var (node, processing) in processes) node.SetProcess(processing);
        }
    }

    private static double TextureDetail(Image image, int x0, int y0, int width, int height, bool vertical = false)
    {
        double detail = 0;
        int dx = vertical ? 0 : 1, dy = vertical ? 1 : 0;
        for (int y = y0; y < y0 + height; y++)
        for (int x = x0; x < x0 + width; x++)
        {
            Color a = image.GetPixel(x - dx, y - dy), b = image.GetPixel(x, y), c = image.GetPixel(x + dx, y + dy);
            detail += Math.Abs(a.R * a.A - 2 * b.R * b.A + c.R * c.A)
                + Math.Abs(a.G * a.A - 2 * b.G * b.A + c.G * c.A)
                + Math.Abs(a.B * a.A - 2 * b.B * b.A + c.B * c.A);
        }
        return detail;
    }

    private static double SpinClipAngle(double time)
    {
        double[] times = [0, 1, 3, 4, 5, 6, 7, 8, 9, 10];
        double[] speeds = [0, 0, 2400, 2400, 4500, 4500, 12000, 12000, 0, 0];
        double angle = 0;
        for (int i = 1; i < times.Length; i++)
        {
            double duration = times[i] - times[i - 1];
            double elapsed = Math.Clamp(time - times[i - 1], 0, duration);
            angle += speeds[i - 1] * elapsed + (speeds[i] - speeds[i - 1]) * elapsed * elapsed / (2 * duration);
        }
        return angle;
    }
}
