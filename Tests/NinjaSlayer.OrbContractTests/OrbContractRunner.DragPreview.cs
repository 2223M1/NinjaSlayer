using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task RenderDragPreviews(OrbCombat combat, NCreature actor, NCreature target,
        Marker2D targetCenter, NCreatureVisuals rig, Creature second, NCreature secondNode,
        Marker2D secondCenter, Node drag)
    {
        string directory = System.Environment.GetEnvironmentVariable("NINJASLAYER_DRAG_RENDER_DIR")!;
        string? selectedProfile = System.Environment.GetEnvironmentVariable("NINJASLAYER_DRAG_RENDER_PROFILE");
        Require(selectedProfile == null || ChargePreviews.Any(p => p.Name == selectedProfile), "Unknown charge preview profile.");
        var assembly = typeof(ShurikenOrb).Assembly;
        Node2D pose = rig.GetNode<Node2D>("%AimPose");
        Node2D anchor = rig.GetNode<Node2D>("AirborneAnchor");
        Type type = pose.GetType();
        void Call(string name, params object?[] args) => AccessTools.Method(type, name).Invoke(pose, args);
        void Face(bool left) => AccessTools.Method(assembly.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerFacingState"),
            "SetFacing").Invoke(null, [actor, left]);
        Type coordinator = assembly.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerRapidAnimationCoordinator", true)!;
        void FinishGameplay() => AccessTools.Method(coordinator, "CardGameplaySettled").Invoke(null, [combat.Player.Creature]);
        void CancelAction() => AccessTools.Method(coordinator, "CancelAndRestore").Invoke(null, [combat.Player.Creature]);
        var profile = AccessTools.Property(type, "ChargeProfile");
        object savedProfile = profile.GetValue(pose)!;
        var viewport = (SubViewport)rig.GetViewport();
        Transform2D canvas = viewport.CanvasTransform;
        Color oldClear = RenderingServer.GetDefaultClearColor();
        bool transparent = viewport.TransparentBg;
        var mode = SaveManager.Instance.PrefsSave.FastMode;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        viewport.CanvasTransform = Transform2D.Identity;
        viewport.TransparentBg = false;
        RenderingServer.SetDefaultClearColor(new Color("b8bdc5"));
        var floor = new ColorRect { Position = new(0f, 642f), Size = new(1400f, 258f),
            Color = new("a5abb4"), ZIndex = -5 };
        actor.GetParent().AddChild(floor);
        var platform = new ColorRect { Position = new(875f, 472f), Size = new(450f, 170f),
            Color = new("a5abb4"), ZIndex = -4, Visible = false };
        actor.GetParent().AddChild(platform);
        var label = new Label { Position = new(44f, 38f), ZIndex = 100 };
        label.AddThemeColorOverride("font_color", new Color("20242b"));
        label.AddThemeFontSizeOverride("font_size", 24);
        actor.GetParent().AddChild(label);
        var cursor = new Polygon2D { Polygon = [Vector2.Zero, new(0f, 20f), new(5f, 15f), new(10f, 23f),
                new(14f, 21f), new(9f, 13f), new(17f, 13f)], Color = new("fff6d4"), ZIndex = 100, Visible = false };
        actor.GetParent().AddChild(cursor);
        var ray = new Line2D { Width = 1.5f, DefaultColor = new Color(.12f, .16f, .2f, .32f), ZIndex = -1, Visible = false };
        actor.GetParent().AddChild(ray);
        NCreatureVisuals TargetVisual(string name, NCreature parent, Marker2D marker)
        {
            string path = $"res://NinjaSlayer/scenes/creature_visuals/{name}.tscn";
            PreloadManager.Cache.SetAsset(path, GD.Load<PackedScene>(path));
            NCreatureVisuals visual = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(path)!;
            parent.Visuals.AddChild(visual);
            marker.Position = visual.VfxSpawnPosition.Position;
            return visual;
        }
        NCreatureVisuals rightVisual = TargetVisual("dark_ninja", target, targetCenter);
        NCreatureVisuals leftVisual = TargetVisual("sawatari", secondNode, secondCenter);
        var processes = new List<(Node Node, bool Enabled)>();
        void Enable(Node node)
        {
            processes.Add((node, node.IsProcessing()));
            node.SetProcess(true);
            foreach (Node child in node.GetChildren()) Enable(child);
        }
        Enable(rig);
        async Task Capture(string clip, int frame)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = viewport.GetTexture().GetImage();
            Require(!image.IsEmpty(), "Drag preview rendered an empty viewport.");
            image.SavePng($"{directory}/{clip}/{frame:D4}.png");
        }
        object? rapidScope = null;
        try
        {
            foreach (var (name, values) in ChargePreviews)
            {
                if (selectedProfile != null && selectedProfile != name) continue;
                System.IO.Directory.CreateDirectory($"{directory}/{name}");
                CancelAction();
                Call("Reset");
                anchor.Transform = Transform2D.Identity;
                actor.Position = new(480f, 650f);
                target.Position = new(1080f, 650f);
                Face(false);
                second.SetCurrentHpInternal(0);
                leftVisual.Visible = false;
                profile.SetValue(pose, Activator.CreateInstance(profile.PropertyType, values.Cast<object>().ToArray()));
                var card = combat.State.CreateCard<TornadoFistRedesignV1>(combat.Player);
                MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand.GetPile(combat.Player).AddInternal(card, -1, silent: true);
                combat.Player.PlayerCombatState!.GainEnergy(4);
                Type rapid = assembly.GetType("NinjaSlayer.Code.Lifecycle.RapidCardPresentationContext", true)!;
                rapidScope = AccessTools.Method(rapid, "Begin").Invoke(null, [card])!;
                label.Text = $"{name.ToUpperInvariant()}  |  IDLE";
                for (int frame = 0; frame < 480; frame++)
                {
                    if (frame == 54 || frame == 318)
                    {
                        Call("Drag", drag, card, new Vector2(1100f, 300f), null);
                        label.Text = $"{name.ToUpperInvariant()}  |  CHARGE";
                    }
                    if (frame == 96 || frame == 360) label.Text = $"{name.ToUpperInvariant()}  |  HOLD";
                    if (frame == 186)
                    {
                        Call("EndDrag", drag, true);
                        XAttackComboMovement.BeginCombo(combat.Player.Creature, .15f);
                        label.Text = $"{name.ToUpperInvariant()}  |  RELEASE / ATTACK";
                    }
                    if (frame == 258)
                    {
                        await XAttackComboMovement.EndCombo(combat.Player.Creature);
                        FinishGameplay();
                        label.Text = $"{name.ToUpperInvariant()}  |  RETURN";
                    }
                    if (frame == 408)
                    {
                        Call("EndDrag", drag, false);
                        label.Text = $"{name.ToUpperInvariant()}  |  CANCEL";
                    }
                    if (frame is 288 or 438) label.Text = $"{name.ToUpperInvariant()}  |  IDLE";
                    await Capture(name, frame);
                    if (frame == 196)
                    {
                        Require(((Vector2)AccessTools.Field(type, "_chargeScale").GetValue(pose)!).IsEqualApprox(Vector2.One),
                            "The real attack Tween retained charge compression after its first peak.");
                    }
                    if (frame is 300 or 470)
                        Require(pose.Transform.IsEqualApprox(Transform2D.Identity)
                            && actor.Position.DistanceTo(new Vector2(480f, 650f)) < .01f,
                            "The filmed attack/cancel did not return to the authored baseline.");
                }
                GD.Print($"PASS actual drag render: {name}, 480 frames at 60 fps, production charge/launch/return/cancellation.");
                AccessTools.Method(rapidScope.GetType(), "RestoreCallerContext").Invoke(rapidScope, null);
                rapidScope = null;
            }
            profile.SetValue(pose, savedProfile);
            if (selectedProfile != null) return;
            foreach (bool bothSides in new[] { false, true })
            {
                string clip = bothSides ? "aim-both-sides" : "aim-one-side";
                System.IO.Directory.CreateDirectory($"{directory}/{clip}");
                CancelAction();
                actor.Position = new(650f, 650f);
                target.Position = new(1110f, 490f);
                secondNode.Position = new(180f, 650f);
                second.SetCurrentHpInternal(bothSides ? 1000 : 0);
                leftVisual.Visible = bothSides;
                platform.Visible = true;
                Face(false);
                var card = combat.State.CreateCard<SatsubatsuRedesignV1>(combat.Player);
                cursor.Visible = ray.Visible = true;
                for (int frame = 0; frame < 480; frame++)
                {
                    float time = frame / 60f;
                    (float Time, Vector2 Point)[] path =
                    [
                        (0f, new(1180f, 470f)), (.7f, new(1180f, 470f)),
                        (1.5f, new(950f, 60f)), (2.2f, new(900f, 750f)),
                        (3f, new(1120f, 320f)), (3.5f, new(100f, 100f)),
                        (4.1f, new(100f, 740f)), (4.6f, new(180f, 503f)),
                        (5.2f, new(660f, 120f)), (5.25f, new(620f, 120f)),
                        (5.3f, new(675f, 120f)), (5.7f, new(950f, 300f)),
                        (6.3f, new(950f, 300f)), (7f, new(950f, 300f)), (8f, new(950f, 300f))
                    ];
                    int segment = 1;
                    while (segment < path.Length - 1 && time > path[segment].Time) segment++;
                    var a = path[segment - 1];
                    var b = path[segment];
                    Vector2 pointer = a.Point.Lerp(b.Point, Mathf.Clamp((time - a.Time) / (b.Time - a.Time), 0f, 1f));
                    Creature? hovered = time >= 2.8f && time < 3.25f ? combat.Enemy
                        : bothSides && time >= 4.4f && time < 4.9f ? second : null;
                    cursor.Position = hovered?.GetCreatureNode()?.Visuals.VfxSpawnPosition.GlobalPosition ?? pointer;
                    label.Text = $"{(bothSides ? "TWO SIDES" : "RIGHT TARGET / EMPTY LEFT")}  |  "
                        + (time < 3.2f ? "AIM LIMIT / LOCK" : time < 5f ? "HALF TURN / LEFT LIMIT" : time < 6.5f ? "QUICK REVERSAL" : "CANCEL");
                    if (frame < 390) Call("Drag", drag, card, pointer, hovered);
                    if (frame == 390) Call("EndDrag", drag, false);
                    ray.Points = [rig.VfxSpawnPosition.GlobalPosition, cursor.Position];
                    await Capture(clip, frame);
                }
                GD.Print($"PASS actual aim render: {clip}, 480 frames at 60 fps, legal bounds, locked cores, reversals and cancellation.");
            }
        }
        finally
        {
            if (rapidScope != null) AccessTools.Method(rapidScope.GetType(), "RestoreCallerContext").Invoke(rapidScope, null);
            CancelAction();
            Call("Reset");
            profile.SetValue(pose, savedProfile);
            foreach (var (node, enabled) in processes)
                if (GodotObject.IsInstanceValid(node)) node.SetProcess(enabled);
            rightVisual.Free();
            leftVisual.Free();
            floor.Free();
            platform.Free();
            label.Free();
            cursor.Free();
            ray.Free();
            viewport.CanvasTransform = canvas;
            viewport.TransparentBg = transparent;
            RenderingServer.SetDefaultClearColor(oldClear);
            SaveManager.Instance.PrefsSave.FastMode = mode;
        }
    }
}
