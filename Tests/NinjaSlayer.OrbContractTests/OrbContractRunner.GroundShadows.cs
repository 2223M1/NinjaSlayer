using Godot;
using MegaCrit.Sts2.Core.Assets;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static void VerifyAimedGroundShadow(NCreatureVisuals rig, int form)
    {
        Node controller = rig.GetNode("ShadowController");
        AccessTools.Method(controller.GetType(), "SyncNow").Invoke(controller, null);
        Sprite2D shadow = rig.GetNode<Sprite2D>("Shadow");
        Require(shadow.Position.IsFinite() && shadow.Scale.IsFinite() && shadow.Scale.X > .01f,
            $"Form {form} lost its ground shadow under affine kick/aim deformation.");
        Require(Math.Abs(shadow.Position.Y + 20.625f) < .1f,
            $"Form {form} lifted the ground shadow with the body.");
        Require(shadow.FlipH == (rig.GetNode<Node2D>("AirborneAnchor").Scale.X < 0f),
            $"Form {form} left its painted footprint facing the wrong way.");
    }

    private async Task VerifyGroundShadows()
    {
        var type = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.NinjaSlayerShadowController", true)!;
        var actionType = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Combat.ShadowActionKind", true)!;
        string? renderDirectory = System.Environment.GetEnvironmentVariable("NINJASLAYER_SHADOW_RENDER_DIR");
        if (renderDirectory != null) System.IO.Directory.CreateDirectory(renderDirectory);
        foreach (string name in new[] { "ninja_slayer", "yamoto_koki", "dark_ninja", "sawatari", "yukano" })
        {
            string scenePath = $"res://NinjaSlayer/scenes/creature_visuals/{name}.tscn";
            PreloadManager.Cache.SetAsset(scenePath, GD.Load<PackedScene>(scenePath));
            NCreatureVisuals rig = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(
                scenePath)!;
            SubViewport? viewport = null;
            if (renderDirectory != null)
            {
                viewport = new SubViewport { Size = new(900, 600), TransparentBg = true,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                AddChild(viewport);
                viewport.AddChild(rig);
                rig.Position = new(450f, 420f);
            }
            else AddChild(rig);
            try
            {
                Node controller = rig.GetNode("ShadowController");
                Require(controller.GetType() == type, $"{name}: packaged shadow script did not bind.");
                void Invoke(string method, params object?[] args) => AccessTools.Method(type, method).Invoke(controller, args);
                Sprite2D shadow = rig.GetNode<Sprite2D>("Shadow");
                Marker2D ground = rig.GetNode<Marker2D>("GroundContact");
                Node2D visuals = rig.GetNode<Node2D>("%Visuals");
                Sprite2D body = visuals as Sprite2D ?? visuals.GetNode<Sprite2D>("Sprite");
                Node2D moving = rig.GetNodeOrNull<Node2D>("AirborneAnchor") ?? visuals;
                Transform2D baseline = moving.Transform;
                Invoke("SyncNow");
                Transform2D idle = shadow.Transform;
                Color idleColor = shadow.Modulate;
                Vector2 groundPosition = ground.Position;
                Rect2 groundRect = (Rect2)AccessTools.Property(type, "GroundReferenceRect").GetValue(controller)!;
                if (viewport != null)
                    await CaptureGroundShadow(viewport, $"{renderDirectory}/{name}-idle.png", requireGroundPixels: true);
                foreach (float degrees in new[] { -90f, -45f, 45f, 90f, 180f })
                {
                    moving.RotationDegrees = degrees;
                    Invoke("SyncNow");
                    Require(!shadow.Transform.IsEqualApprox(idle), $"{name}: rotation {degrees} did not change the shadow.");
                    Require(shadow.Scale.X > 0 && shadow.Scale.Y > 0, $"{name}: shadow collapsed during rotation.");
                    Require(shadow.Position.Y == idle.Origin.Y, $"{name}: ground shadow moved vertically with the body.");
                    Require(ground.Position == groundPosition, $"{name}: drawing moved the ground pivot.");
                    Require((Rect2)AccessTools.Property(type, "GroundReferenceRect").GetValue(controller)! == groundRect,
                        $"{name}: dynamic drawing changed the fire-wall reference.");
                    if (viewport != null && degrees == 90f)
                        await CaptureGroundShadow(viewport, $"{renderDirectory}/{name}-turned.png", requireGroundPixels: false);
                }
                moving.Transform = baseline;
                moving.Position += new Vector2(55f, -180f);
                Invoke("SyncNow");
                Require(Math.Abs(shadow.Position.X - idle.Origin.X - 55f) < .1f, $"{name}: airborne shadow did not follow horizontally.");
                Require(shadow.Modulate.A < idleColor.A && shadow.Modulate.A > 0f, $"{name}: altitude did not fade the shadow continuously.");
                moving.Transform = baseline;
                Color originalBodyColor = body.Modulate;
                body.Modulate = new Color(1f, .1f, .1f, .2f);
                Invoke("SyncNow");
                Require(shadow.Modulate == idleColor, $"{name}: body flash/fade contaminated shadow color.");
                body.Modulate = originalBodyColor;
                object spinOwner = new();
                foreach (float angle in new[] { 0f, Mathf.Pi / 2, Mathf.Pi, Mathf.Pi * 1.5f, Mathf.Tau })
                {
                    Invoke("SetSpin", spinOwner, angle);
                    Invoke("SyncNow");
                    Require(shadow.Scale.X >= idle.X.Length() * .54f, $"{name}: pillar spin collapsed its shadow.");
                    if (Math.Abs(MathF.Cos(angle)) < .01f)
                        Require(shadow.Scale.X < idle.X.Length() * .6f, $"{name}: pillar spin did not narrow its shadow.");
                }
                Invoke("ClearSpin", spinOwner);
                Invoke("BeginAction", Enum.Parse(actionType, "Hurt"), 0f, .3f, false);
                controller._Process(.05);
                Vector2 hurtScale = shadow.Scale;
                bool paused = GetTree().Paused;
                try
                {
                    GetTree().Paused = true;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Require(shadow.Scale.IsEqualApprox(hurtScale), $"{name}: paused action advanced its shadow.");
                }
                finally { GetTree().Paused = paused; }
                Invoke("BeginAction", Enum.Parse(actionType, "SlowAttack"), .2f, .2f, true);
                Invoke("SyncNow");
                Require(shadow.Scale.IsEqualApprox(hurtScale), $"{name}: hurt-to-counter handoff jumped to idle.");
                controller._Process(.2);
                Require(shadow.Scale.X > idle.X.Length(), $"{name}: Slow Attack did not reach the native shadow accent.");
                Invoke("BeginReturn", .2f);
                controller._Process(.2);
                Invoke("SyncNow");
                Require(shadow.Transform.IsEqualApprox(idle), $"{name}: action return drifted from idle.");
                Invoke("SetDeathFall", 1f, -1f);
                Require(shadow.Scale.X >= idle.X.Length() * 1.60f && Math.Abs(shadow.Skew) > .5f,
                    $"{name}: grounded death did not spread the painted shadow.");
                Invoke("ClearDeathFall");
                Require(shadow.Transform.IsEqualApprox(idle), $"{name}: death cleanup left a stretched shadow.");
                if (name == "dark_ninja")
                {
                    body.Texture = GD.Load<Texture2D>("res://NinjaSlayer/images/monsters/dark_ninja.png");
                    shadow.Texture = GD.Load<Texture2D>("res://NinjaSlayer/images/shadows/dark_ninja_combat_shadow.png");
                    Invoke("SetAuthoredPresentation", new Vector2(5f, -20.625f), new Vector2(.57f, .275f));
                    Require(shadow.Scale.IsEqualApprox(new(.57f, .275f)), "Dark combat pose did not adopt its authored shadow.");
                    Require(ground.Position == groundPosition, "Dark pose switch moved the fixed ground anchor.");
                }
                else
                {
                    if (name == "sawatari")
                    {
                        // The independent body crop has an offset foot pivot. Mirror the
                        // sprite origin too, as the live weapon rig does when it plants the foot.
                        body.FlipH = !body.FlipH;
                        body.Position = new(-body.Position.X, body.Position.Y);
                    }
                    else moving.Scale = new(-baseline.Scale.X, baseline.Scale.Y);
                    Invoke("SetMirrored", true);
                    Require(shadow.FlipH, $"{name}: facing did not mirror the painted contact pattern.");
                    float axis = name == "yamoto_koki" ? groundPosition.X : 0f;
                    Require(Math.Abs(shadow.Position.X - (2f * axis - idle.Origin.X)) < .1f,
                        $"{name}: mirrored shadow center did not follow the foot support.");
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"PASS ground shadow {name}: rotation, altitude, color isolation, pillar spin, action handoff, restoration, fixed ground reference and facing/pose.");
            }
            finally { rig.Free(); viewport?.Free(); }
        }
    }

    private async Task CaptureGroundShadow(SubViewport viewport, string path, bool requireGroundPixels)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = viewport.GetTexture().GetImage();
        Require(!image.IsEmpty(), "Actual scene renderer produced an empty image.");
        int shadowPixels = 0;
        for (int y = 380; y < 418; y++)
        for (int x = 280; x < 630; x++)
        {
            Color pixel = image.GetPixel(x, y);
            if (pixel.A > .02f && pixel.A < .5f && pixel.R < .03f && pixel.G < .03f && pixel.B < .03f)
                shadowPixels++;
        }
        if (requireGroundPixels) Require(shadowPixels > 100, $"Actual scene has no visible painted ground shadow: {path} ({shadowPixels} pixels).");
        Require(image.SavePng(path) == Error.Ok, $"Could not save actual scene capture {path}.");
    }
}
