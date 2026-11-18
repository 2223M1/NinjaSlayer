#if NINJASLAYER_CHANNEL_PREVIEW
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyCompanionSelectionAndWuppy()
    {
        using var f = new DarkStrikeFixture(multiplayer: true);
        var local = f.Combat.Player;
        var remote = f.Other!;
        var wuppy = await PlayerCmd.AddPet<Wuppy>(local);
        var ally = await PlayerCmd.AddPet<YamotoKokiMonster>(remote);
        var ordinary = await PlayerCmd.AddPet<Wuppy>(remote);
        var osty = await PlayerCmd.AddPet<Osty>(local);
        osty.SetCurrentHpInternal(0);
        var patch = new Harmony("NinjaSlayer.Contract.RemoteSelection");
        var room = new FreeInputContractRoom(); _hurtRoom = room;
        AccessTools.Property(typeof(NCombatRoom), "Mode").SetValue(room, CombatRoomMode.ActiveCombat);
        var scene = new Control(); room.AddChild(scene);
        AccessTools.Property(typeof(NCombatRoom), "SceneContainer").SetValue(room, scene);
        var ui = new ControllerCombatUi(); var hand = new NPlayerHand(); var holder = new Control();
        room.AddChild(ui); ui.AddChild(holder);
        AccessTools.Property(typeof(NPlayerHand), "CardHolderContainer").SetValue(hand, holder);
        AccessTools.Property(typeof(NCombatUi), "Hand").SetValue(ui, hand);
        AccessTools.Property(typeof(NCombatRoom), "Ui").SetValue(room, ui);
        var nodes = (List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(room)!;
        var display = new NCreatureStateDisplay();
        NCreature Actor(Creature entity)
        {
            var n = new AimContractCreature();
            var v = new AimContractVisuals();
            var bounds = new Control { Size = new(160, 120) };
            var center = new Marker2D(); var body = new Sprite2D();
            var height = new Node2D { Name = "HeightControl" };
            var hit = new Control { Size = bounds.Size };
            v.AddChild(bounds); v.AddChild(center); v.AddChild(body); v.AddChild(height);
            height.Owner = v; height.UniqueNameInOwner = true;
            AccessTools.Field(typeof(NCreatureVisuals), "_body").SetValue(v, body);
            AccessTools.Property(typeof(NCreatureVisuals), "Bounds").SetValue(v, bounds);
            AccessTools.Property(typeof(NCreatureVisuals), "VfxSpawnPosition").SetValue(v, center);
            AccessTools.Property(typeof(NCreature), "Entity").SetValue(n, entity);
            AccessTools.Property(typeof(NCreature), "Visuals").SetValue(n, v);
            AccessTools.Property(typeof(NCreature), "Hitbox").SetValue(n, hit);
            AccessTools.Field(typeof(NCreature), "_stateDisplay").SetValue(n, display);
            n.AddChild(v); n.AddChild(hit); scene.AddChild(n); nodes.Add(n); AimActors.Add(entity,n);
            return n;
        }
        try
        {
            patch.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)), prefix: new HarmonyMethod(GetType(), nameof(ResolveAimActor)));
            // Keep native Wuppy placement and height tweens; omit only the unrelated HUD bounds rebuild.
            patch.Patch(AccessTools.Method(typeof(NCreature), "UpdateBounds", [typeof(Node)]), prefix: new HarmonyMethod(GetType(), nameof(SkipCompanionFixtureHud)));
            patch.Patch(AccessTools.Method(typeof(NCreature), "UpdateInteractability"), transpiler: new HarmonyMethod(typeof(FriendlyCompanionRemoteSelectionPatch), "Transpiler"));
            patch.Patch(AccessTools.Method(typeof(NCombatRoom), "PositionPlayersAndPets"),
                prefix: new HarmonyMethod(typeof(YamotoKokiAllyLayoutPatch), "Prefix"), postfix: new HarmonyMethod(typeof(YamotoKokiAllyLayoutPatch), "Postfix"),
                transpiler: new HarmonyMethod(typeof(YamotoKokiAllyLayoutPatch), "Transpiler"));
            var owner = Actor(local.Creature); Actor(remote.Creature); var pet = Actor(wuppy); var otherPet = Actor(ordinary);
            AddChild(room);
            YamotoKokiDynamicAllyLayoutPatch.Prefix(room, wuppy, out var state);
            Require(state == null, "Native-only pet party requested a mod reflow.");
            YamotoKokiDynamicAllyLayoutPatch.Prefix(room, ally, out state);
            Require(state != null, "The first friendly companion did not request a new slot.");
            var companion = Actor(ally);
            YamotoKokiDynamicAllyLayoutPatch.Prefix(room, f.Monster.Creature, out state);
            Require(state == null, "Enemy spawn disrupted friendly slots.");
            companion.SetIsInteractable(true, false); otherPet.SetIsInteractable(true, false);
            void Check(bool allow)
            {
                Require(companion.IsInteractable == allow && companion.Hitbox.MouseFilter == (allow ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore)
                    && companion.Hitbox.FocusMode == (allow ? Control.FocusModeEnum.All : Control.FocusModeEnum.None)
                    && !otherPet.IsInteractable, "Remote hitbox/focus or unrelated pet eligibility incorrect.");
                if (allow)
                {
                    companion.Hitbox.GrabFocus();
                    Require(room.GetViewport().GuiGetFocusOwner() == companion.Hitbox, "Remote companion could not take controller focus.");
                }
            }
            Check(false);
            var support = f.Combat.State.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Coordinate>(local);
            FriendlyCompanionCardSelectedPatch.Postfix(support); Check(true);
            companion.SetIsInteractable(false, false); Check(false);
            companion.SetIsInteractable(true, false); Check(true);
            FriendlyCompanionCardSelectedPatch.Postfix(f.Combat.State.CreateCard<StrikeIronclad>(local)); Check(false);
            var potion = ModelDb.Potion<BlockPotion>().ToMutable(); potion.Owner = local;
            FriendlyCompanionPotionSelectedPatch.Postfix(potion); Check(true);
            var targeting = typeof(FriendlyCompanionControllerTargetPatch).Assembly.GetType("NinjaSlayer.Code.Patches.FriendlyCompanionTargeting", true)!;
            var resolvePotion = AccessTools.Method(targeting, "ResolveControllerPotionAllies");
            var candidates = (IReadOnlyList<Creature>)resolvePotion.Invoke(null, [f.Combat.State, local.Creature])!;
            Require(candidates.Contains(ally) && candidates.Contains(remote.Creature) && !candidates.Contains(ordinary) && !candidates.Contains(wuppy),
                "Directional potion targeting offered an ordinary pet or skipped an eligible remote companion.");
            FriendlyCompanionPotionDeselectedPatch.Postfix(); Check(false);
            async Task Settle() => await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
            void Placed(float x, float height)
            {
                Require(pet.Position.DistanceTo(owner.Position + Vector2.Right * (owner.Hitbox.Size.X + x)) < .1f
                    && Math.Abs(pet.Visuals.VfxSpawnPosition.Position.Y - (Wuppy.boundsOffset.Y + height)) < .1f,
                    $"Wuppy missed native owner offset {x}/{height}: {pet.Position-owner.Position}, center {pet.Visuals.VfxSpawnPosition.Position}.");
            }
            NCombatRoom.PositionPlayersAndPets(nodes.ToList(), 1, false); await Settle(); Placed(50,-50);
            osty.SetMaxHpInternal(10); osty.SetCurrentHpInternal(10); Actor(osty);
            NCombatRoom.PositionPlayersAndPets(nodes.ToList(), 1, false); await Settle(); Placed(-25,-250);
            YamotoKokiDynamicAllyLayoutPatch.Prefix(room, companion, out state);
            Require(state != null, "Removing the last companion skipped layout restoration.");
            nodes.Remove(companion); companion.Hide();
            YamotoKokiDynamicAllyLayoutPatch.Postfix(room, state); await Settle(); Placed(-25,-250);
            GD.Print("PASS two-owner mouse/controller targeting, unsupported selection/cancel, native disabled interaction, local Wuppy with/without Osty and last-companion removal.");
        }
        finally
        {
            FriendlyCompanionCardDeselectedPatch.Postfix(); AimActors.Clear(); _hurtRoom = null;
            room.Free(); hand.Free(); display.Free(); patch.UnpatchAll(patch.Id);
        }
    }

    private static bool SkipCompanionFixtureHud() => false;
}
#endif
