using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Powers;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static FreeInputContractRoom? _flameScene;
    private static readonly List<(int Targets, int Hits, Task Playback)> FlamePoses = [];

    // TestMode normally hides the room and all creature nodes. Expose the visual stage for the
    // flame wave; card movement before and after it still uses the host's headless hand.
    private static void EnterFlameScene() => _hurtRoom = _flameScene;
    private static void ExitFlameScene() => _hurtRoom = null;
    // The target doubles have no native monster animation or controller navigation. Their
    // host Kill command still runs after the mod's complete impact animation.
    private static bool SkipFixtureDeathAnim(NCreature __instance) => __instance.Visuals is not AimContractVisuals;
    private static bool SkipFixtureNavigation(NCombatRoom __instance) => __instance != _flameScene;
    private static bool ResolveFlameActor(Creature __instance, ref NCreature? __result)
    {
        if (_hurtRoom == null) return true;
        __result = _hurtRoom.GetCreatureNode(__instance);
        return false;
    }

    private static void ObserveFlamePose(object __instance, IReadOnlyList<NCreature> targetNodes, Task __result) =>
        FlamePoses.Add((targetNodes.Count, (int)AccessTools.Field(__instance.GetType(), "_primaryDamageCalls").GetValue(__instance)!, __result));

    private async Task VerifyFlamePose()
    {
        var hooks = RitsuLibFramework.CreatePatcher("NinjaSlayer.Contracts.FlamePose", "Damage");
        hooks.RegisterPatch<NinjaSlayerFinisherPrimaryDamagePatch>();
        hooks.RegisterPatch<NinjaSlayerFinisherLethalDamagePatch>();
        Require(hooks.PatchAll(), "Could not install the finisher damage hooks.");
        var observe = new Harmony("NinjaSlayer.Contracts.FlamePose.Stage");
        var product = typeof(BlackFlame).Assembly;
        Type cinematic(string name) => product.GetType("NinjaSlayer.Code.ExternalAnimations." + name, true)!;
        try
        {
            const string rigPath = "res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn";
            const string formTexture = "res://NinjaSlayer/images/characters/ninja_slayer/naraku_idle/NinjaSlayer_naraku_idle_0001.png";
            RitsuLibFramework.EnsureGodotScriptsRegistered(product, RitsuLibFramework.CreateLogger("Black Flame finisher"));
            PreloadManager.Cache.SetAsset(rigPath, GD.Load(rigPath));
            PreloadManager.Cache.SetAsset(formTexture, GD.Load(formTexture));
            observe.Patch(AccessTools.Method(cinematic("FinisherEligibilityService"), "CreateActionSession"),
                prefix: new HarmonyMethod(GetType(), nameof(EnterFlameScene)));
            observe.Patch(AccessTools.Method(cinematic("FinisherSessionRegistry"), "UnregisterSession"),
                postfix: new HarmonyMethod(GetType(), nameof(ExitFlameScene)));
            observe.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)),
                prefix: new HarmonyMethod(GetType(), nameof(ResolveFlameActor)));
            observe.Patch(AccessTools.Method(typeof(NCreature), nameof(NCreature.StartDeathAnim)),
                prefix: new HarmonyMethod(GetType(), nameof(SkipFixtureDeathAnim)));
            observe.Patch(AccessTools.Method(typeof(NCombatRoom), "UpdateCreatureNavigation"),
                prefix: new HarmonyMethod(GetType(), nameof(SkipFixtureNavigation)));
            observe.Patch(AccessTools.Method(cinematic("FinisherSession"), "PlayEnhancedDoomPoseImpact"),
                postfix: new HarmonyMethod(GetType(), nameof(ObserveFlamePose)));
            foreach (bool form in new[] { false, true })
            {
                using var combat = new OrbCombat(ninjaSlayer: true);
                combat.CreateRun("flame-pose");
                var enemies = form ? new[] { combat.Enemy, combat.AddEnemy() } : new[] { combat.Enemy };
                foreach (var enemy in enemies) enemy.SetCurrentHpInternal(4);
                combat.Enemy.SetCurrentHpInternal(13); // Strike (6) + Karate (3) + one flame (4).
                var actor = combat.Player.Creature;
                await PowerCmd.Apply<KaratePower>(Choice, actor, 3, actor, null);
                if (form) await PowerCmd.Apply<NarakuFormPower>(Choice, actor, 1, actor, null);
                else AddCard<BlackFlame>(combat);
                var room = _flameScene = new FreeInputContractRoom { Size = new(1280, 720) };
                try
                {
                    foreach (string name in new[] { "SceneContainer", "CombatVfxContainer", "BackCombatVfxContainer" })
                    {
                        var layer = new Control { Size = room.Size };
                        room.AddChild(layer);
                        AccessTools.Property(typeof(NCombatRoom), name).SetValue(room, layer);
                    }
                    AddChild(room);
                    var bg = new Control { Name = "BgContainer" };
                    room.SceneContainer.AddChild(bg);
                    bg.Owner = room;
                    bg.UniqueNameInOwner = true;
                    int x = 220;
                    foreach (Creature entity in enemies.Prepend(actor))
                    {
                        NCreature node;
                        if (entity == actor)
                        {
                            node = new AimContractCreature { Position = new(x, 420) };
                            var rig = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(rigPath)!;
                            AccessTools.Property(typeof(NCreature), "Entity").SetValue(node, entity);
                            AccessTools.Property(typeof(NCreature), "Visuals").SetValue(node, rig);
                            node.AddChild(rig); room.SceneContainer.AddChild(node);
                            ((List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(room)!).Add(node);
                        }
                        else
                        {
                            AddStabActor(room, room.SceneContainer, entity, new(x, 420));
                            node = room.GetCreatureNode(entity)!;
                            var bounds = new Control { Position = new(-32, -120), Size = new(64, 120) };
                            node.Visuals.AddChild(bounds);
                            AccessTools.Property(typeof(NCreatureVisuals), "Bounds").SetValue(node.Visuals, bounds);
                        }
                        AccessTools.Property(typeof(NCreature), "Hitbox").SetValue(node, node.Visuals.Bounds);
                        x += 360;
                    }
                    FlamePoses.Clear();
                    await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
                    Require(enemies.All(e => e.IsDead) && FlamePoses.Count == 1 && FlamePoses[0].Targets == enemies.Length
                        && FlamePoses[0].Hits == 1 && FlamePoses[0].Playback.IsCompletedSuccessfully,
                        $"Expected one completed pose for {enemies.Length} flame victim(s): {string.Join(", ", FlamePoses)}.");
                }
                finally { _hurtRoom = null; _flameScene = null; room.Free(); }
            }
        }
        finally { observe.UnpatchAll(observe.Id); hooks.UnpatchAll(); FlamePoses.Clear(); }
        GD.Print("PASS headless finisher animation: held flame and form, one primary impact, single/group pose completed.");
    }
}
