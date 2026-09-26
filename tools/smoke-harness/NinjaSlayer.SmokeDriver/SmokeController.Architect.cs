using Godot;
using HarmonyLib;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    internal bool IsArchitectPreview => _theater?.IsArchitectPreview == true;
    internal bool UseFullArchitectGreeting { get; private set; }
    internal Task PlayArchitectFullGreeting(Player player) => _theater!.PlayArchitectFullGreeting(player);

    private sealed partial class TheaterRuntime
    {
        internal bool IsArchitectPreview => _script.Purpose == "architect";

        internal async Task PlayArchitectFullGreeting(Player player)
        {
            var room = NCombatRoom.Instance!;
            var ui = NRun.Instance!.GlobalUi;
            ICombatState state = player.Creature.CombatState!;
            Require(state.Enemies.Any(c => c.Monster is Architect), "Full greeting must run in the Architect room.");
            Type greeting = typeof(BossGreetingCinematic);
            uint seed = (uint)InvokeMethod(greeting, null, "StableHash", _script.Seed)!;
            Type contextType = AccessTools.Inner(greeting, "BossGreetingSession");
            var context = (IDisposable)Activator.CreateInstance(contextType, room, ui, seed)!;
            Cover("architect-full-greeting-start");
            try
            {
                await (Task)InvokeMethod(greeting, null, "PlayInternal", state,
                    new List<Player> { player }, room, ui, context)!;
                Cover("architect-full-greeting-complete");
            }
            finally
            {
                context.Dispose();
                InvokeMethod(contextType, context, "RestoreCameraAndScreenShakeTarget");
                player.Creature.GetCreatureNode()?.Visuals.Show();
            }
        }

        private async Task ArchitectComparison()
        {
            Require(IsArchitectPreview, "Architect comparison requires its dedicated script.");
            await ReplaceEnemy(nameof(Architect));
            Creature coral = _combat.CreateCreature(ModelDb.Monster<SkulkingColony>().ToMutable(), CombatSide.Enemy, null);
            await CreatureCmd.Add(coral);
            NCreature architect = Node("enemy"), reference = coral.GetCreatureNode()!;
            await Wait(.6);
            Vector2 originalPosition = architect.Position;
            Transform2D originalBodyTransform = architect.Body.Transform;
            bool ninjaVisible = Node("ninja").Visible;
            try
            {
                Node("ninja").Hide();
                foreach (NCreature node in new[] { architect, reference })
                {
                    node.AnimHideIntent();
                    node.AnimDisableUi();
                    using Variant skeleton = node.Body.Call("get_skeleton");
                    using GodotObject native = skeleton.AsGodotObject();
                    Rect2 bounds = CharacterBounds(native);
                    float height = bounds.Size.Y * Mathf.Abs(node.Body.GlobalScale.Y);
                    Vector2 scale = node.Body.GlobalScale * (330f / height);
                    node.Body.TopLevel = true;
                    node.Body.GlobalScale = scale;
                }
                architect.Body.GlobalPosition = new(390f, 730f);
                reference.Body.GlobalPosition = new(1080f, 730f);
                await Wait(.7);
                PlayDeath(architect.Body, "ninjaslayer_soft_death");
                PlayDeath(reference.Body, "die");
                await Wait(4.15);
                Cover("architect-coral-native-tracks");
            }
            finally
            {
                reference.Hide(); _room.RemoveCreatureNode(reference); reference.QueueFree();
                CombatManager.Instance.RemoveCreature(coral);
                if (_combat.ContainsCreature(coral)) _combat.RemoveCreature(coral);
                architect.Position = originalPosition;
                architect.Body.TopLevel = false;
                architect.Body.Transform = originalBodyTransform;
                using Variant state = architect.Body.Call("get_animation_state");
                using GodotObject native = state.AsGodotObject();
                native.Call("clear_tracks");
                // Clearing tracks leaves unkeyed bones, constraints and slot
                // deforms in the last death pose. Reset all of them before idle.
                using Variant skeleton = architect.Body.Call("get_skeleton");
                using GodotObject skeletonObject = skeleton.AsGodotObject();
                skeletonObject.Call("set_to_setup_pose");
                using Variant idle = native.Call("set_animation", "idle_loop", true, 0);
                using GodotObject idleTrack = idle.AsGodotObject();
                idleTrack.Call("set_mix_duration", 0f);
                architect.Body.Call("update_skeleton", 0f);
                Node("ninja").Visible = ninjaVisible;
            }
            await Wait(.5);
        }

        private static Rect2 CharacterBounds(GodotObject skeleton)
        {
            var hidden = new List<(GodotObject Slot, Variant Attachment)>();
            var slots = skeleton.Call("get_slots").AsGodotArray<GodotObject>();
            try
            {
                foreach (GodotObject slot in slots)
                {
                    using Variant data = slot.Call("get_data");
                    using GodotObject slotData = data.AsGodotObject();
                    string name = slotData.Call("get_slot_name").AsString();
                    if (name.Contains("pen", StringComparison.Ordinal) || name.Contains("flame", StringComparison.Ordinal)
                        || name is "book" or "shadow")
                    {
                        hidden.Add((slot, slot.Call("get_attachment")));
                        slot.Call("set_attachment", default(Variant));
                    }
                }
                return skeleton.Call("get_bounds").AsRect2();
            }
            finally
            {
                foreach (var (slot, attachment) in hidden)
                {
                    slot.Call("set_attachment", attachment);
                    attachment.Dispose();
                }
                foreach (GodotObject slot in slots) slot.Dispose();
            }
        }

        private void SampleArchitect(JsonObject row)
        {
            if (_player.Creature.GetCreatureNode() is { } ninja)
            {
                Vector2 core = ninja.GetParent<CanvasItem>().GetGlobalTransformWithCanvas().AffineInverse()
                    * ninja.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
                var blur = ninja.Visuals.FindChild("SpinMotionBlur", true, false);
                row["eventActor"] = new JsonObject
                {
                    ["rootX"] = ninja.Position.X, ["visualX"] = ninja.Visuals.Position.X,
                    ["coreX"] = core.X, ["coreY"] = core.Y, ["frozen"] = !ninja.CanProcess(),
                    ["spinExposure"] = blur != null && (bool)AccessTools.Field(blur.GetType(), "_hasExposure").GetValue(blur)!
                };
            }
            if (!_actors.TryGetValue(nameof(Architect), out Creature? actor)
                || actor.GetCreatureNode() is not { } node) return;
            using Variant state = node.Body.Call("get_animation_state");
            // The live animation shares these Godot wrappers with the production
            // controller. Observing a frame must not dispose its animation state.
            GodotObject native = state.AsGodotObject();
            using Variant track = native.Call("get_current", 0);
            GodotObject? entry = track.AsGodotObject();
            if (entry == null) return;
            using Variant animation = entry.Call("get_animation");
            GodotObject clip = animation.AsGodotObject();
            Material? material = node.Visuals.SpineBody!.GetNormalMaterial();
            Node? recovery = node.Body.GetNodeOrNull("AlabamaDeathRecovery");
            Transform2D renderTransform = recovery == null ? node.Body.Transform
                : (Transform2D)AccessTools.Property(recovery.GetType(), "RenderTransform").GetValue(recovery)!;
            row["architectTrack"] = new JsonObject
            {
                ["name"] = clip.Call("get_name").AsString(),
                ["time"] = entry.Call("get_track_time").AsSingle(),
                ["duration"] = entry.Call("get_animation_end").AsSingle(),
                ["visible"] = node.Body.Visible,
                ["recoveryProgress"] = recovery == null ? null
                    : (float)AccessTools.Property(recovery.GetType(), "Progress").GetValue(recovery)!,
                ["renderRotation"] = renderTransform.Rotation,
                ["white"] = material is ShaderMaterial shader
                    && shader.Shader.ResourcePath.EndsWith("boss_death_whiteout.gdshader", StringComparison.Ordinal)
                    ? shader.GetShaderParameter("white_mix").AsSingle() : 0f
            };
        }

        private static void PlayDeath(Node2D body, string animation)
        {
            using Variant state = body.Call("get_animation_state");
            using GodotObject native = state.AsGodotObject();
            native.Call("clear_tracks");
            native.Call("set_time_scale", 1f);
            using Variant track = native.Call("set_animation", animation, false, 0);
            using GodotObject entry = track.AsGodotObject();
            entry.Call("set_mix_duration", .05f);
            entry.Call("set_time_scale", 1f);
        }

        private async Task ArchitectExecution(TheaterStep step)
        {
            Require(IsArchitectPreview, "Architect execution requires its dedicated script.");
            if (step.Card != null)
            {
                await CardPileCmd.RemoveFromDeck(_player.Deck.Cards.ToArray(), showPreview: false);
                CardModel selected = CreateCard(step.Card);
                await CardPileCmd.Add(_player.RunState.CreateCard(selected.CanonicalInstance, _player), PileType.Deck);
            }
            _driver.PreviewEntranceVariant = step.Mode == null ? null
                : Enum.Parse<AncientEntranceAnimation.EntranceVariant>(step.Mode);
            _driver.UseFullArchitectGreeting = step.Greeting == "full";
            int hp = _player.Creature.CurrentHp;
            CardModel[] deck = _player.Deck.Cards.ToArray();
            Vector2 playerSlot = Node("ninja").Position;
            var probabilities = Enumerable.Range(0, 1000)
                .Select(i => AncientEntranceAnimation.FromRoll((i + .5f) / 1000f)).GroupBy(v => v)
                .ToDictionary(g => g.Key, g => g.Count());
            Require(probabilities[AncientEntranceAnimation.EntranceVariant.SlideFromLeft] == 500
                && probabilities.Where(p => p.Key != AncientEntranceAnimation.EntranceVariant.SlideFromLeft).All(p => p.Value == 100),
                "Entrance probabilities must remain 50% short and 10% per long entrance.");
            NCombatRoom room;
            if (step.Actor == "native")
            {
                Cover("architect-room-enter");
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<TheArchitect>());
                room = NCombatRoom.Instance!;
                AddActor(nameof(Architect), room.CreatureNodes.Single(n => n.Entity.Monster is Architect).Entity);
                var architectEvent = ((MegaCrit.Sts2.Core.Rooms.EventRoom)_player.RunState.CurrentRoom!).LocalMutableEvent;
                await _driver.WaitUntilAsync(() => architectEvent.CurrentOptions.Count > 0,
                    "Architect did not offer a dialogue option", _cancel);
                Require(room.GetNodeOrNull("NinjaSlayerArchitectExecution") == null,
                    "Architect executed before Continue.");
                await architectEvent.CurrentOptions.Single().Chosen();
                await architectEvent.CurrentOptions.Single().Chosen();
            }
            else
            {
                var model = (TheArchitect)ModelDb.Event<TheArchitect>().ToMutable();
                AccessTools.Property(typeof(EventModel), nameof(EventModel.Owner)).SetValue(model, _player);
                AccessTools.Property(typeof(EventModel), nameof(EventModel.Rng)).SetValue(model, new Rng(252509));
                if (_driver.UseFullArchitectGreeting) await PlayArchitectFullGreeting(_player);
                await ArchitectExecutionCinematic.Play(model);
                room = _room;
            }
            var controller = room.GetNode<ArchitectExecutionCinematic>("NinjaSlayerArchitectExecution");
            await _driver.WaitUntilAsync(() => (bool)AccessTools.Field(typeof(ArchitectExecutionCinematic), "_completed")
                .GetValue(controller)!, "Architect cinematic did not complete", _cancel);
            Require(_player.Deck.Cards.SequenceEqual(deck) && _player.Creature.CurrentHp == hp,
                "Architect visual execution changed the deck or player HP.");
            Require(Node("ninja").Position.X > playerSlot.X + 800f, "Ninja Slayer did not finish exiting the stage.");
            _driver.PreviewEntranceVariant = null;
            _driver.UseFullArchitectGreeting = false;
            await Wait(1.2);
            Cover("architect-production-execution");
        }
    }
}

[HarmonyPatch(typeof(ArchitectExecutionCinematic), "PlayGreetingBow")]
internal static class ArchitectFullGreetingPreview
{
    private static bool Prefix(Creature owner, ref Task __result)
    {
        if (SmokeController.Current is not { UseFullArchitectGreeting: true } driver) return true;
        __result = driver.PlayArchitectFullGreeting(owner.Player!);
        return false;
    }
}

// Keep the isolated recording scene alive after the unmodified production
// cinematic finishes; no victory/history changes are needed in a preview.
[HarmonyPatch(typeof(ArchitectExecutionCinematic), "CompleteEventCore")]
internal static class ArchitectPreviewCompletion
{
    private static bool Prefix(ref Task __result)
    {
        if (SmokeController.Current?.IsArchitectPreview != true) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
