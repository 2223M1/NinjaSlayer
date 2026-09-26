using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Monsters;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private sealed partial class TheaterRuntime
    {
        private readonly JsonArray _auditEvents = [];
        private readonly JsonArray _auditMotionEvents = [];
        private readonly JsonArray _auditDeathEvents = [];
        private readonly Dictionary<Creature, NCreature> _auditNodes = [];
        private Transform2D _auditRender = Transform2D.Identity;

        private async Task CompanionFacingPreview()
        {
            AddActor("koki", await PlayerCmd.AddPet<YamotoKokiMonster>(_player));
            AddActor("yukano", await PlayerCmd.AddPet<YukanoMonster>(_player));
            string[] allies = ["ally_sawatari", "koki", "yukano"];
            Node("ninja").Position = new(-500, 200);
            for (int i = 0; i < allies.Length; i++) Node(allies[i]).Position = new(-180 + i * 260, 200);
            Node("enemy").Position = new(700, 200);
            Node("extra1").Position = new(-780, 200);
            Type facing = ProductType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerFacingState");
            await Wait(.5);
            var observations = new JsonArray();
            foreach (string speed in new[] { "Normal", "Fast" })
            {
                SaveManager.Instance.PrefsSave.FastMode = Enum.Parse<FastModeType>(speed);
                foreach (bool surrounded in new[] { false, true })
                {
                    if (surrounded)
                    {
                        await Power("extra1", "BackAttackLeftPower", 1);
                        await Power("enemy", "BackAttackRightPower", 1);
                        await Wait(.3);
                    }
                    foreach (bool left in new[] { true, false })
                    {
                        InvokeMethod(facing, null, "SetFacing", Node("ninja"), left);
                        await Wait(.075);
                        foreach (string name in allies)
                        {
                            var turn = Node(name).Visuals.GetNode("CombatFacingTurn");
                            float duration = (float)AccessTools.Field(turn.GetType(), "_duration").GetValue(turn)!;
                            Require(Math.Abs(duration - .15f) < .001f,
                                $"{name}: {speed} turn did not retain the 0.15s column-spin window.");
                            Require(AccessTools.Field(turn.GetType(), "_blur").GetValue(turn) is Node,
                                $"{name}: turn did not use the shared exposure renderer.");
                        }
                        await Wait(.15);
                        foreach (string name in allies)
                        {
                            var body = Node(name).Body;
                            bool shownLeft = name == "ally_sawatari" ? !((Sprite2D)body).FlipH : body.Transform.Determinant() < 0f;
                            Require(shownLeft == (left ^ surrounded), $"{name}: companion facing rule differs from the owner/flank state.");
                            observations.Add(new JsonObject { ["actor"] = name, ["speed"] = speed,
                                ["surrounded"] = surrounded, ["ownerLeft"] = left, ["shownLeft"] = shownLeft });
                        }
                        await Wait(.4);
                    }
                    if (surrounded)
                    {
                        await RemovePower(Actor("extra1"), "BackAttackLeftPower");
                        await RemovePower(Actor("enemy"), "BackAttackRightPower");
                        await Wait(.3);
                    }
                }
            }
            var enemyTurn = InvokeMethod(ProductType("NinjaSlayer.Code.Nodes.CombatFacingTurn"), null, "Ensure", Node("enemy"))!;
            foreach (bool left in new[] { false, true })
            {
                InvokeMethod(enemyTurn.GetType(), enemyTurn, "SetFacing", left, false);
                await Wait(.3);
                Require(!((Sprite2D)Node("enemy").Body).FlipH == left, "Enemy Sawatari turn ended with the wrong orientation.");
            }
            File.WriteAllText(Path.Combine(_directory, "companion-facing.json"), observations.ToJsonString());
        }

        private async Task PrepareFinisherAudit()
        {
            var setup = _script.Audit ?? throw new InvalidDataException("Missing audit setup.");
            var profile = AccessTools.Property(ProductType("NinjaSlayer.Code.ExternalAnimations.FinisherTimeline"),
                "PreviewProfile");
            profile.SetValue(null, Enum.Parse(profile.PropertyType, setup.CameraProfile));
            if (setup.Monster != "SawatariMonster")
            {
                Type type = typeof(MonsterModel).Assembly.GetTypes().Concat(typeof(DarkNinjaMonster).Assembly.GetTypes())
                    .Single(t => t.Name == setup.Monster && typeof(MonsterModel).IsAssignableFrom(t));
                var model = (MonsterModel)AccessTools.Method(typeof(ModelDb), "Monster", [], [type]).Invoke(null, null)!;
                Creature old = Actor("enemy");
                var oldNode = Node("enemy");
                Vector2 slot = oldNode.Position;
                Creature target = _combat.CreateCreature(model.ToMutable(), CombatSide.Enemy, null);
                await CreatureCmd.Add(target);
                oldNode.Hide(); _room.RemoveCreatureNode(oldNode); oldNode.QueueFree();
                MegaCrit.Sts2.Core.Combat.CombatManager.Instance.RemoveCreature(old);
                if (_combat.ContainsCreature(old)) _combat.RemoveCreature(old);
                target.GetCreatureNode()!.Position = slot;
                _enemy = setup.Monster;
                AddActor(_enemy, target);
            }
            await CreatureCmd.SetCurrentHp(Actor("enemy"), setup.TargetHp);
            for (int index = 1; index < setup.Targets; index++)
            {
                Creature extra = _combat.CreateCreature(Actor("enemy").Monster!.CanonicalInstance.ToMutable(), CombatSide.Enemy, null);
                await CreatureCmd.Add(extra);
                await CreatureCmd.SetCurrentHp(extra, setup.TargetHp);
                AddActor("extra" + index, extra);
            }
            if (setup.PlayerHp is { } hp) await CreatureCmd.SetCurrentHp(_player.Creature, hp);
            if (setup.Form != "normal") await Form(setup.Form);
            if (setup.Companion == "sawatari")
            {
                Creature companion = await PlayerCmd.AddPet<SawatariMonster>(_player);
                AddActor("ally_sawatari", companion);
                // The real event initializes weapons explicitly; AddPet does not call AfterAddedToRoom.
                InvokeMethod(ProductType("NinjaSlayer.Code.Nodes.SawatariWeaponVisuals"), null, "Create", companion.Monster!);
            }
            else if (setup.Companion is { } companion)
            {
                if (companion == "koki") await RelicCmd.Obtain<YamotoKokiCuteRelic>(_player);
                else if (companion == "yukano") await RelicCmd.Obtain<YukanoCompanionRelic>(_player);
                await Entrance(companion);
            }
            if (setup.Orb is { } orb)
            {
                Type type = typeof(OrbModel).Assembly.GetTypes().Single(t => t.Name == orb && typeof(OrbModel).IsAssignableFrom(t));
                var canonical = (OrbModel)AccessTools.Method(typeof(ModelDb), "Orb", [], [type]).Invoke(null, null)!;
                await OrbCmd.Channel(_choice, canonical.ToMutable(), _player);
            }
            if (setup.Mirror) await SwapSides();
            SaveManager.Instance.PrefsSave.FastMode = Enum.Parse<FastModeType>(setup.Mode);
            FinisherSmokeObserver.Reset();
            FinisherMotionObservationPatch.Record = (name, gate, distance) =>
            {
                if (_start == 0) return;
                _auditMotionEvents.Add(new JsonObject { ["event"] = name, ["seconds"] = Seconds,
                    ["gate"] = gate, ["distance"] = distance, ["frame"] = Engine.GetProcessFrames() });
            };
            FinisherAuditObservationPatch.Record = (name, session) =>
            {
                if (_start == 0) return;
                _auditEvents.Add(new JsonObject { ["event"] = name, ["seconds"] = Seconds,
                    ["qpc"] = Stopwatch.GetTimestamp(), ["frame"] = Engine.GetProcessFrames(),
                    ["scenario"] = AccessTools.Property(session.GetType(), "Scenario").GetValue(session)?.ToString(),
                    ["ranged"] = (bool)AccessTools.Property(session.GetType(), "IsRanged").GetValue(session)!,
                    ["primaryCallsBefore"] = (int)AccessTools.Field(session.GetType(), "_primaryDamageCalls").GetValue(session)! });
            };
            await Wait(.5);
            var initialBodies = _actors.Values.Distinct().Select(c => (Creature: c, Node: c.GetCreatureNode()))
                .Where(entry => entry.Node != null && GodotObject.IsInstanceValid(entry.Node))
                .ToDictionary(entry => entry.Creature, entry => entry.Node!.Body.Transform);
            foreach (Creature creature in initialBodies.Keys)
                _auditNodes[creature] = creature.GetCreatureNode()!;
            AlabamaDeathObservationPatch.Record = (name, node) =>
            {
                if (_start == 0 || !initialBodies.TryGetValue(node.Entity, out Transform2D initial)) return;
                Transform2D actual = node.Body.Transform;
                var recovery = node.Body.GetNodeOrNull<Node>("AlabamaDeathRecovery");
                Transform2D shown = recovery == null ? actual : (Transform2D)AccessTools.Property(
                    recovery.GetType(), "RenderTransform").GetValue(recovery)!;
                _auditDeathEvents.Add(new JsonObject
                {
                    ["event"] = name, ["seconds"] = Seconds, ["frame"] = Engine.GetProcessFrames(),
                    ["creature"] = node.Entity.Monster?.GetType().Name,
                    ["rotation"] = node.Body.Rotation, ["scaleX"] = node.Body.Scale.X,
                    ["scaleY"] = node.Body.Scale.Y, ["mode"] = node.Body.ProcessMode.ToString(),
                    ["visibleRotation"] = shown.Rotation, ["visibleScaleX"] = shown.Scale.X,
                    ["visibleScaleY"] = shown.Scale.Y,
                    ["positionError"] = actual.Origin.DistanceTo(initial.Origin),
                    ["basisError"] = Math.Max(actual.X.DistanceTo(initial.X), actual.Y.DistanceTo(initial.Y))
                });
            };
        }

        private void SampleFinisherAudit(JsonObject row)
        {
            _auditRender = Transform2D.Identity;
            var scene = _room.SceneContainer;
            row["scene"] = new JsonObject { ["x"] = scene.Position.X, ["y"] = scene.Position.Y,
                ["scaleX"] = scene.Scale.X, ["scaleY"] = scene.Scale.Y };
            var cameraType = ProductType("NinjaSlayer.Code.ExternalAnimations.CombatCinematicCameraLease");
            if (AccessTools.Field(cameraType, "_active").GetValue(null) is { } camera)
            {
                Vector2 raw = (Vector2)AccessTools.Property(cameraType, "CurrentPosition").GetValue(camera)!;
                float scale = (float)AccessTools.Property(cameraType, "CurrentScale").GetValue(camera)!;
                Vector2 shake = (Vector2)AccessTools.Field(cameraType, "_shakeOffset").GetValue(camera)!;
                if (AccessTools.Field(cameraType, "_combatCanvas").GetValue(camera) is CanvasLayer canvas)
                    _auditRender = canvas.Transform;
                row["scene"] = new JsonObject { ["x"] = raw.X + shake.X, ["y"] = raw.Y + shake.Y,
                    ["scaleX"] = scale, ["scaleY"] = scale };
                row["camera"] = new JsonObject { ["x"] = raw.X, ["y"] = raw.Y,
                    ["scale"] = scale, ["shakeX"] = shake.X, ["shakeY"] = shake.Y };
            }
            var bodies = new JsonObject();
            foreach (var (name, actor) in _actors)
            {
                var node = actor.GetCreatureNode() ?? _auditNodes.GetValueOrDefault(actor);
                if (node == null || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) continue;
                Node2D body = node.Visuals.GetCurrentBody();
                if (!GodotObject.IsInstanceValid(body) || !body.IsInsideTree()) continue;
                var recovery = body.GetNodeOrNull<Node>("AlabamaDeathRecovery");
                Transform2D rendered = recovery == null ? body.Transform : (Transform2D)AccessTools.Property(
                    recovery.GetType(), "RenderTransform").GetValue(recovery)!;
                var t = _auditRender * body.GetGlobalTransformWithCanvas() * body.Transform.AffineInverse() * rendered;
                Vector2 core = node.GetTransform() * node.GetGlobalTransformWithCanvas().AffineInverse()
                    * node.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
                bodies[name] = new JsonObject { ["localX"] = body.Position.X, ["localY"] = body.Position.Y,
                    ["coreX"] = core.X, ["coreY"] = core.Y, ["rootX"] = node.Position.X, ["rootY"] = node.Position.Y,
                    ["x"] = t.Origin.X, ["y"] = t.Origin.Y, ["rotation"] = body.Rotation,
                    ["scaleX"] = body.Scale.X, ["scaleY"] = body.Scale.Y,
                    ["visibleRotation"] = rendered.Rotation, ["visibleScaleX"] = rendered.Scale.X,
                    ["visibleScaleY"] = rendered.Scale.Y,
                    ["deathRecovery"] = recovery == null ? null : JsonValue.Create((float)AccessTools.Property(
                        recovery.GetType(), "Progress").GetValue(recovery)!),
                    ["basisXx"] = t.X.X, ["basisXy"] = t.X.Y,
                    ["basisYx"] = t.Y.X, ["basisYy"] = t.Y.Y,
                    ["z"] = EffectiveZ(body),
                    ["mode"] = body.ProcessMode.ToString(), ["nodeMode"] = node.ProcessMode.ToString(),
                    ["visible"] = body.IsVisibleInTree() };
                if (body.GetNodeOrNull<Marker2D>("AlabamaGroundContact") is { } contact)
                {
                    Vector2 head = node.GetGlobalTransformWithCanvas().AffineInverse()
                        * contact.GetGlobalTransformWithCanvas().Origin;
                    bodies[name]!["headContactX"] = head.X;
                    bodies[name]!["headContactY"] = head.Y;
                }
            }
            row["bodies"] = bodies;
            var aim = Node("ninja").Visuals.GetNode<Node2D>("%AimPose");
            var recoveries = new JsonArray();
            foreach (object motion in (System.Collections.IEnumerable)AccessTools.Field(aim.GetType(), "_presentations").GetValue(aim)!)
            {
                Type type = motion.GetType();
                if (AccessTools.Field(type, "Kind").GetValue(motion)!.ToString() != "Recovery") continue;
                recoveries.Add(new JsonObject
                {
                    ["elapsed"] = (float)AccessTools.Field(type, "Elapsed").GetValue(motion)!,
                    ["duration"] = (float)AccessTools.Field(type, "Duration").GetValue(motion)!,
                    ["rotation"] = (float)AccessTools.Field(type, "Rotation").GetValue(motion)!,
                    ["planarBlur"] = (bool)AccessTools.Field(type, "PlanarBlur").GetValue(motion)!,
                    ["stretch"] = AccessTools.Field(type, "Stretch").GetValue(motion)!.ToString()
                });
            }
            row["recoveries"] = recoveries;
            row["returnBlurVisible"] = AccessTools.Field(aim.GetType(), "_somersaultBlur").GetValue(aim)
                is CanvasItem returnBlur && returnBlur.IsVisibleInTree();
            Vector2 travel = (Vector2)AccessTools.Property(aim.GetType(), "Travel").GetValue(aim)!;
            row["ordinaryTravel"] = new JsonObject { ["x"] = travel.X, ["y"] = travel.Y };
            var registry = ProductType("NinjaSlayer.Code.ExternalAnimations.FinisherSessionRegistry");
            if (InvokeMethod(registry, null, "GetActiveSession") is not { } session) return;
            var squashes = (System.Collections.IDictionary)AccessTools.Field(session.GetType(), "_deathSquashStates").GetValue(session)!;
            var ratios = new JsonArray();
            foreach (System.Collections.DictionaryEntry entry in squashes)
            {
                var body = (Node2D)entry.Key;
                if (!GodotObject.IsInstanceValid(body)) continue;
                Transform2D original = (Transform2D)AccessTools.Property(entry.Value!.GetType(), "OriginalTransform").GetValue(entry.Value)!;
                ratios.Add(body.Transform.Determinant() / original.Determinant());
            }
            object ledger = AccessTools.Field(session.GetType(), "_ledger").GetValue(session)!;
            Creature[] victims = ((IEnumerable<Creature>)AccessTools.Property(ledger.GetType(), "Victims").GetValue(ledger)!).ToArray();
            var actorNode = (MegaCrit.Sts2.Core.Nodes.Combat.NCreature)AccessTools.Field(session.GetType(), "_actorNode").GetValue(session)!;
            row["impactPose"] = new JsonObject
            {
                ["actorFrozen"] = !actorNode.CanProcess(),
                ["squashAreaRatios"] = ratios,
                ["squashEligibleVictims"] = victims.Count(v => v.Player?.Character is not NinjaSlayer.Content.INinjaSlayerCharacter
                    && v.Monster?.GetType().Assembly != typeof(SawatariMonster).Assembly)
            };
            Vector2 combo = (Vector2)AccessTools.Field(session.GetType(), "_comboTravel").GetValue(session)!;
            row["comboTravel"] = new JsonObject { ["x"] = combo.X, ["y"] = combo.Y };
            row["finisherClock"] = new JsonObject
            {
                ["profile"] = AccessTools.Field(session.GetType(), "_previewProfile").GetValue(session)!.ToString(),
                ["activeSeconds"] = (float)AccessTools.Field(session.GetType(), "_activeSeconds").GetValue(session)!,
                ["impactOrigin"] = AccessTools.Field(session.GetType(), "_impactStartedAt").GetValue(session) is float origin
                    ? JsonValue.Create(origin) : null
            };
            if (AccessTools.Field(session.GetType(), "_ranged").GetValue(session) is { } ranged)
            {
                var visuals = (IEnumerable<Node>)AccessTools.Property(ranged.GetType(), "Visuals").GetValue(ranged)!;
                var positions = new JsonArray();
                foreach (var visual in visuals.Where(GodotObject.IsInstanceValid))
                    foreach (var item in visual.GetChildren().Prepend(visual).OfType<Node2D>())
                    {
                        Vector2 p = _auditRender * item.GetGlobalTransformWithCanvas().Origin;
                        positions.Add(new JsonObject { ["name"] = item.Name.ToString(), ["x"] = p.X, ["y"] = p.Y,
                            ["mode"] = item.ProcessMode.ToString(), ["parent"] = item.GetParent()?.Name.ToString() });
                    }
                row["rangedVisuals"] = positions;
            }
        }

        private void WriteFinisherAudit()
        {
            File.WriteAllText(Path.Combine(_directory, "finisher-events.json"), _auditEvents.ToJsonString());
            File.WriteAllText(Path.Combine(_directory, "animation-events.json"), _auditMotionEvents.ToJsonString());
            File.WriteAllText(Path.Combine(_directory, "alabama-death-events.json"), _auditDeathEvents.ToJsonString());
            var releases = _auditDeathEvents.Where(e => e!["event"]!.GetValue<string>() == "pose_release").ToArray();
            foreach (JsonNode? release in releases)
                Require(release!["positionError"]!.GetValue<float>() < .02f
                    && release["basisError"]!.GetValue<float>() < .001f
                    && release["mode"]!.GetValue<string>() != "Disabled",
                    "Alabama death handoff did not release its owned logical baseline.");
            var sessions = new JsonArray();
            foreach (var s in FinisherSmokeObserver.Snapshots())
                sessions.Add(new JsonObject { ["id"] = s.SessionId, ["scenario"] = s.Scenario,
                    ["hits"] = s.ResolvedHits, ["completed"] = s.CompletionObserved,
                    ["released"] = s.ResourcesReleased, ["error"] = s.CompletionFailure?.ToString(),
                    ["kills"] = s.SuccessfulKills.Values.Sum() });
            File.WriteAllText(Path.Combine(_directory, "finisher-sessions.json"), sessions.ToJsonString());
            if (_script.Audit?.ExpectedHits is { } expectedHits)
            {
                var session = FinisherSmokeObserver.Snapshots().Single();
                Require(session.ResolvedHits == expectedHits && session.CompletionObserved
                    && session.ResourcesReleased && session.CompletionFailure == null
                    && session.SuccessfulKills.Values.Sum() == 1,
                    "Combo finisher lost its expected hit count, single death or lifecycle cleanup.");
                var impact = _auditEvents.Single(e => e!["event"]!.GetValue<string>() == "PlayEnhancedDoomPoseImpact")!;
                Require(impact["primaryCallsBefore"]!.GetValue<int>() == expectedHits,
                    "Combo Doom began before the final actual damage wave.");
                int damageWaves = _damage.Count(e => e!["before"]!.GetValue<int>() > e["after"]!.GetValue<int>()
                    && e["after"]!.GetValue<int>() > 0);
                Require(damageWaves == expectedHits, "Combo damage ended before every hit resolved.");
                var begin = _auditEvents.Single(e => e!["event"]!.GetValue<string>() == "Begin")!;
                if (expectedHits > 1 && begin["primaryCallsBefore"]!.GetValue<int>() < expectedHits
                    && _script.Audit.CameraProfile != "A")
                {
                    float baseline = _motion.First()!["scene"]!["scaleX"]!.GetValue<float>();
                    float preZoom = _motion.Where(e => e!["finisherClock"] is { } clock
                            && clock["impactOrigin"] == null && e["camera"] != null)
                        .Select(e => e!["camera"]!["scale"]!.GetValue<float>() / baseline).DefaultIfEmpty(1f).Max();
                    Require(preZoom > 1.01f && preZoom <= 1.3501f,
                        "Combo finisher lost its first-stage zoom or reached the final zoom before impact.");
                }
                var heldFrames = _motion.Where(e => e!["finisherClock"] is { } clock
                        && clock["impactOrigin"] is { } origin
                        && clock["activeSeconds"]!.GetValue<float>() - origin.GetValue<float>() is > .10f and < .40f)
                    .ToArray();
                Require(heldFrames.Length > 0 && heldFrames.All(e => e!["impactPose"]!["actorFrozen"]!.GetValue<bool>()),
                    "A combo actor continued its recovery during the shared Doom hold.");
                bool ranged = impact["ranged"]!.GetValue<bool>();
                Require(heldFrames.All(e => e!["impactPose"]!["squashAreaRatios"] is JsonArray ratios
                    && (ranged || e["impactPose"]!["squashEligibleVictims"]!.GetValue<int>() == 0 ? ratios.Count == 0 : ratios.Count > 0
                        && ratios.All(r => Math.Abs(r!.GetValue<float>() - .60f) < .005f))),
                    "Finisher squash ignored ranged/mod-character exclusions or native melee feedback.");
            }
        }
    }
}

[HarmonyPatch]
internal static class AlabamaDeathObservationPatch
{
    internal static Action<string, NCreature>? Record;
    static MethodBase TargetMethod() => AccessTools.Method(typeof(DarkNinjaMonster).Assembly.GetType(
        "NinjaSlayer.Code.ExternalAnimations.AlabamaDropAnimation", true), "ReleaseVictimBeforeDeath");
    static void Prefix(NCreature creatureNode, out bool __state) =>
        __state = creatureNode.Body.GetNodeOrNull<Marker2D>("AlabamaGroundContact")
            ?.HasMeta("alabama_release_before_death") == true;
    static void Postfix(NCreature creatureNode, bool __state)
    {
        if (__state) Record?.Invoke("pose_release", creatureNode);
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.StartDeathAnim))]
internal static class AlabamaDeathStartObservationPatch
{
    [HarmonyPriority(Priority.Last)]
    static void Prefix(NCreature __instance) => AlabamaDeathObservationPatch.Record?.Invoke("death_start", __instance);
}

[HarmonyPatch]
internal static class AlabamaRecoveryObservationPatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(DarkNinjaMonster).Assembly.GetType(
        "NinjaSlayer.Code.Nodes.NinjaSlayerAimPose+VisualMotion", true), "Dispose");
    static void Prefix(object __instance)
    {
        Type type = __instance.GetType();
        if (AccessTools.Field(type, "Kind").GetValue(__instance)!.ToString() != "Recovery") return;
        FinisherMotionObservationPatch.Record?.Invoke("recovery_dispose",
            (float)AccessTools.Field(type, "Duration").GetValue(__instance)!,
            (float)AccessTools.Field(type, "Elapsed").GetValue(__instance)!);
    }
}

[HarmonyPatch]
internal static class FinisherMotionObservationPatch
{
    internal static Action<string, float, float>? Record;
    static IEnumerable<MethodBase> TargetMethods()
    {
        var assembly = typeof(DarkNinjaMonster).Assembly;
        yield return AccessTools.Method(assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherSession", true), "PlayAimedAction");
        yield return AccessTools.Method(assembly.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerRapidAnimationCoordinator", true), "PlayAttackToPeak");
    }
    static void Prefix(MethodBase __originalMethod, object[] __args)
    {
        bool finisher = __originalMethod.Name == "PlayAimedAction";
        Record?.Invoke((finisher ? "finisher" : "ordinary") + "_start",
            (float)__args[finisher ? 0 : 2], (float)__args[1]);
    }
    static void Postfix(MethodBase __originalMethod, object[] __args, ref Task __result)
    {
        bool finisher = __originalMethod.Name == "PlayAimedAction";
        __result = Complete(__result, (finisher ? "finisher" : "ordinary") + "_peak",
            (float)__args[finisher ? 0 : 2], (float)__args[1]);
    }
    static async Task Complete(Task task, string name, float gate, float distance)
    {
        await task;
        Record?.Invoke(name, gate, distance);
    }
}

[HarmonyPatch]
internal static class FinisherAuditObservationPatch
{
    internal static Action<string, object>? Record;
    static IEnumerable<MethodBase> TargetMethods()
    {
        Type type = typeof(DarkNinjaMonster).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherSession", true)!;
        foreach (string name in new[] { "Begin", "NotifyPrimaryDamage", "NotifyProtectedDamageConfirmed",
            "StartFinalZoom", "PlayEnhancedDoomPoseImpact", "PlayDoomPoseImpact", "StartCompletion",
            "NotifyDeathAnimationStarting", "ReturnToBaseline", "PlayAimedAction", "BeginComboRecovery" })
            yield return AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
    }
    static void Prefix(object __instance, MethodBase __originalMethod) => Record?.Invoke(__originalMethod.Name, __instance);
}
