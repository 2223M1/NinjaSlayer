using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private sealed partial class TheaterRuntime
    {
        private async Task OverheadCheck()
        {
            await Entrance("koki");
            await Entrance("yukano");
            var friend = await PlayerCmd.AddPet<SawatariMonster>(_player);
            AddActor("sawatari-ally", friend);
            string[] names = ["koki", "yukano", "sawatari", "sawatari-ally"];
            foreach (string name in names)
            {
                var monster = Actor(name).Monster!;
                MoveState move = name switch
                {
                    "koki" => (MoveState)monster.MoveStateMachine!.States[YamotoKokiMonster.IaiSlashMoveId],
                    "yukano" => (MoveState)monster.MoveStateMachine!.States[YukanoMonster.ArrowMoveId],
                    _ => monster.MoveStateMachine!.States.Values.OfType<MoveState>().First(state => state.Intents.Count > 0)
                };
                monster.SetMoveImmediate(move, true);
                InvokeMethod(ProductType("NinjaSlayer.Code.Combat.YamotoKokiIntentLifecycle"), null, "BeginCombat", Actor(name));
                await Node(name).RefreshIntents();
            }
            await Wait(1.5);
            var gaps = new Dictionary<string, float>();
            foreach (string name in names) gaps[name] = float.PositiveInfinity;
            double sampleUntil = Seconds + 2.1; // Native intent bob completes a cycle in 2s.
            while (Seconds < sampleUntil)
            {
                foreach (string name in names)
                {
                    NCreature actor = Node(name);
                    Node2D visual = actor.Visuals.GetNode<Node2D>("%Visuals");
                    Sprite2D body = visual as Sprite2D ?? visual.GetNode<Sprite2D>("Sprite");
                    // Uppermost source alpha row, independent of TalkPos/IntentPos.
                    float topPixel = name == "koki" ? -252f : name == "yukano" ? -606.5f : -260.5f;
                    float top = (body.GetGlobalTransformWithCanvas() * new Vector2(0, topPixel)).Y;
                    Require(actor.IntentContainer.GetChildCount() > 0, name + " has no actual intent.");
                    foreach (var intent in actor.IntentContainer.GetChildren().OfType<NIntent>())
                    {
                        var icon = intent.GetNode<Sprite2D>("%Intent");
                        float bottom = (icon.GetGlobalTransformWithCanvas() * icon.GetRect()).End.Y;
                        var value = intent.GetNode<RichTextLabel>("%Value");
                        if (!string.IsNullOrEmpty(value.Text))
                            bottom = Math.Max(bottom, (value.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, value.Size)).End.Y);
                        gaps[name] = Math.Min(gaps[name], top - bottom);
                        Require(top - bottom >= 4f, $"{name} intent has insufficient head clearance: gap={top - bottom:0.##}.");
                    }
                }
                await _driver.WaitFrames(1);
            }
            _driver._tree.Root.GetTexture().GetImage().SavePng(Path.Combine(_directory, "companion-intents.png"));
            var report = new JsonObject();
            foreach (var (name, gap) in gaps) report[name] = gap;
            File.WriteAllText(Path.Combine(_directory, "overhead-gaps.json"), report.ToJsonString());
            foreach (string form in new[] { "normal", "semi", "full", "soul" })
            {
                await Form(form);
                await Wait(.35);
                foreach (bool mirrored in new[] { false, true })
                {
                    Call(Pose, "RequestTurn", mirrored);
                    await Wait(.2);
                    var actor = Node("ninja");
                    var bubble = NSpeechBubbleVfx.Create("DOMO, NINJA SLAYER DESU.", actor.Entity, 1.8)!;
                    _room.SceneContainer.AddChild(bubble);
                    await Wait(.65);
                    var source = actor.Visuals.GetNode<Sprite2D>("%Visuals");
                    var overlay = actor.Visuals.FindChild("NarakuVisualOverlay", true, false) as Sprite2D;
                    Sprite2D body = overlay is { Visible: true } ? overlay : source;
                    var kind = ProductType("NinjaSlayer.Content.NinjaSlayerFormKind");
                    string key = form switch { "semi" => "Naraku", "full" => "FullyReleasedNaraku", "soul" => "OneBodyOneSoul", _ => "Normal" };
                    var contour = (System.Numerics.Vector2[])InvokeMethod(ProductType("NinjaSlayer.Code.Combat.CombatBodyContours"),
                        null, "ForForm", Enum.Parse(kind, key))!;
                    float top = contour.Min(p => (body.GetGlobalTransformWithCanvas()
                        * (new Vector2(body.FlipH ? -p.X : p.X, body.FlipV ? -p.Y : p.Y) + body.Offset)).Y);
                    var sprite = bubble.GetNode<Sprite2D>("%Bubble");
                    float bottom = (sprite.GetGlobalTransformWithCanvas() * sprite.GetRect()).End.Y;
                    Require(top - bottom >= 8f, $"{form} speech bubble covers head: gap={top - bottom:0.##}.");
                    _driver._tree.Root.GetTexture().GetImage().SavePng(Path.Combine(_directory, $"speech-{form}-{mirrored}.png"));
                    bubble.QueueFree();
                }
            }
            await Form("normal");
            Call(Pose, "RequestTurn", false);
            Cover("overhead-clearance");
        }
    }
}
