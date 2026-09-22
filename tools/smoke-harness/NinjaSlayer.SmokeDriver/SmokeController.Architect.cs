using Godot;
using HarmonyLib;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    internal bool IsArchitectPreview => _theater?.IsArchitectPreview == true;

    private sealed partial class TheaterRuntime
    {
        internal bool IsArchitectPreview => _script.Purpose == "architect";

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
            if (!_actors.TryGetValue(nameof(Architect), out Creature? actor)
                || actor.GetCreatureNode() is not { } node) return;
            using Variant state = node.Body.Call("get_animation_state");
            using GodotObject native = state.AsGodotObject();
            using Variant track = native.Call("get_current", 0);
            using GodotObject? entry = track.AsGodotObject();
            if (entry == null) return;
            using Variant animation = entry.Call("get_animation");
            using GodotObject clip = animation.AsGodotObject();
            Material? material = node.Visuals.SpineBody!.GetNormalMaterial();
            row["architectTrack"] = new JsonObject
            {
                ["name"] = clip.Call("get_name").AsString(),
                ["time"] = entry.Call("get_track_time").AsSingle(),
                ["duration"] = entry.Call("get_animation_end").AsSingle(),
                ["visible"] = node.Body.Visible,
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

        private async Task ArchitectExecution()
        {
            Require(IsArchitectPreview, "Architect execution requires its dedicated script.");
            var model = (TheArchitect)ModelDb.Event<TheArchitect>().ToMutable();
            AccessTools.Property(typeof(EventModel), nameof(EventModel.Owner)).SetValue(model, _player);
            Require(ArchitectExecutionCinematic.TryStart(model), "Production Architect cinematic did not start.");
            var controller = _room.GetNode<ArchitectExecutionCinematic>("NinjaSlayerArchitectExecution");
            await _driver.WaitUntilAsync(() => (bool)AccessTools.Field(typeof(ArchitectExecutionCinematic), "_completed")
                .GetValue(controller)!, "Architect cinematic did not complete", _cancel);
            await Wait(1.2);
            Cover("architect-production-execution");
        }
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
