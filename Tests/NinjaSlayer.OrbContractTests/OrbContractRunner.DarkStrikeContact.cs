using Godot;
#if NINJASLAYER_CHANNEL_PREVIEW
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Nodes.Vfx;
#endif
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Orbs;
using STS2RitsuLib;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static Delegate ContactCallback<T>(Func<Creature, Task> move) =>
        (Func<Creature, Task<T>>)(async target =>
        {
            await move(target);
            return (T)Activator.CreateInstance(typeof(T), true, false, 0, true, true)!;
        });

    private static Node2D AddStabActor(NCombatRoom room, Control scene, Creature entity, Vector2 pos)
    {
        var actor = new AimContractCreature { Position = pos };
        var visuals = new AimContractVisuals();
        var rig = new Node2D { Name = "AirborneAnchor" };
        var pose = new Node2D { Name = "AimPose" };
        var body = new Sprite2D { Name = "Visuals", Scale = new(.4f, .4f) };
        var center = new Marker2D { Position = new(0, -80), Name = "CenterPos" };
        visuals.AddChild(center); visuals.AddChild(rig); rig.AddChild(pose); pose.AddChild(body);
        body.Owner = visuals; body.UniqueNameInOwner = true;
        AccessTools.Field(typeof(NCreatureVisuals), "_body").SetValue(visuals, body);
        AccessTools.Property(typeof(NCreatureVisuals), "VfxSpawnPosition").SetValue(visuals, center);
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, entity);
        AccessTools.Property(typeof(NCreature), "Visuals").SetValue(actor, visuals);
        actor.AddChild(visuals); scene.AddChild(actor);
        ((List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(room)!).Add(actor);
        return rig;
    }

    private async Task VerifyDarkStrikeContact()
    {
        Type presentation = typeof(ShurikenOrb).Assembly.GetType(
            "NinjaSlayer.Code.ExternalAnimations.DarkNinjaSpecialAttackPresentation", true)!;
        var failures = new List<string>();
#if !NINJASLAYER_CHANNEL_STABLE
        var observe = new Harmony("NinjaSlayer.OrbContracts.StabReceiver");
        try
        {
            observe.Patch(AccessTools.Method(presentation, "CaptureImpactVfx"),
                prefix: new HarmonyMethod(GetType(), nameof(RecordStabReceiver)));
            observe.Patch(AccessTools.Method(presentation, "PlayDarkStrike"),
                prefix: new HarmonyMethod(GetType(), nameof(RecordStabPrediction)));
            foreach (var (petHp, block, loss) in new[] { (50, 0, 0), (50, 3, 0), (2, 0, 12), (2, 20, 0) })
            {
                using var fight = new DarkStrikeFixture();
                Stealable(fight, ModelDb.Card<NinjaCaltrops>());
                var pet = await PlayerCmd.AddPet<Wuppy>(fight.Combat.Player);
                pet.SetMaxHpInternal(petHp); pet.SetCurrentHpInternal(petHp);
                await PowerCmd.Apply<WuppysLoyaltyPower>(Choice, pet, 1, null, null);
                if (block > 0) await CreatureCmd.GainBlock(fight.Target, block, ValueProp.Unpowered, null);
                _stabReceiver = null;
                int hp = fight.Target.CurrentHp;
                fight.Monster.Creature.SetCurrentHpInternal(400);
                await DarkStrike(fight.Monster, fight.Target);
                int stolen = fight.Monster.Creature.Powers.OfType<SwipePower>().Count();
                string actual = _stabReceiver == pet ? "Wuppy" : _stabReceiver == fight.Target ? "player" : "none";
                GD.Print($"STAB petHP={petHp} block={block} contact={actual} petLoss={petHp-pet.CurrentHp} playerLoss={hp-fight.Target.CurrentHp} stolen={stolen}");
                Require(fight.Monster.Creature.CurrentHp == 414 && hp - fight.Target.CurrentHp == loss && petHp - pet.CurrentHp == Math.Min(14, petHp)
                    && stolen == (loss > 0 ? 1 : 0), "Native damage or theft changed.");
                Creature expected = loss > 0 ? fight.Target : pet;
                if (_stabReceiver != expected || _stabPrediction != (expected, true)) failures.Add($"Wrong stab receiver ({petHp}/{block}): {actual}.");
            }
            using (var fight = new DarkStrikeFixture())
            {
                var card = Stealable(fight, ModelDb.Card<NinjaCaltrops>());
                var pet = await PlayerCmd.AddPet<Wuppy>(fight.Combat.Player);
                pet.SetMaxHpInternal(50); pet.SetCurrentHpInternal(50);
                fight.Monster.Creature.SetCurrentHpInternal(400);
                await DarkStrike(fight.Monster, pet);
                Require(fight.Monster.Creature.CurrentHp == 414 && pet.CurrentHp == 36
                    && fight.Target.CurrentHp == 500 && !fight.Monster.Creature.HasPower<SwipePower>()
                    && fight.Combat.Player.Deck.Cards.Contains(card.DeckVersion!), "Direct Wuppy hit must heal 14 and leave the owner's card alone.");
                GD.Print("PASS Wuppy interception and direct stab heal the attacker; direct pet damage cannot steal from its owner.");
            }
        }
        finally { _stabReceiver = null; _stabPrediction = default; observe.UnpatchAll(observe.Id); }
#endif
        RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(NCreature).Assembly, RitsuLibFramework.CreateLogger("Dark Strike contact"));
        RegisterMountedUids("res://scenes/vfx/vfx_dramatic_stab.tscn", []);
        using var f = new DarkStrikeFixture(multiplayer: true);
        var room = new FreeInputContractRoom();
        var scene = new Control();
        _hurtRoom = room;
        room.AddChild(scene);
        AccessTools.Property(typeof(NCombatRoom), "SceneContainer").SetValue(room, scene);
        var effects = new Control();
        room.AddChild(effects);
        AccessTools.Property(typeof(NCombatRoom), "CombatVfxContainer").SetValue(room, effects);
        var flashes = new List<Vector2>();
        effects.ChildEnteredTree += node => { if (node is Node2D vfx) flashes.Add(vfx.GlobalPosition); };
        AddChild(room);
        try
        {
            AddStabActor(room, scene, f.Monster.Creature, new(800, 400));
#if NINJASLAYER_CHANNEL_PREVIEW
            var darkVisuals = room.GetCreatureNode(f.Monster.Creature)!.Visuals;
            var home = darkVisuals.Body;
            const string arrowPath = "res://scenes/vfx/impaled_arrow_vfx.tscn";
            RegisterMountedUids(arrowPath, []);
            (NImpaledArrowVfx Front, Node2D Rear, ArrowSlot Slot) AddArrow()
            {
                var slot = new ArrowSlot(f.Combat.Player) { Status = ArrowStatus.Away };
                var vfx = GD.Load<PackedScene>(arrowPath).Instantiate<NImpaledArrowVfx>();
                AccessTools.Field(typeof(NImpaledArrowVfx), "_creature").SetValue(vfx, f.Monster.Creature);
                AccessTools.Field(typeof(NImpaledArrowVfx), "_arrowSlot").SetValue(vfx, slot);
                darkVisuals.AddChild(vfx);
                return (vfx, (Node2D)AccessTools.Field(typeof(NImpaledArrowVfx), "_backArrowNode").GetValue(vfx)!, slot);
            }
            var lodged = AddArrow(); var pulled = AddArrow(); var broken = AddArrow();
            var frontHome = lodged.Front.Transform; var rearHome = lodged.Rear.Transform;
            var frontLocal = home.ToLocal(lodged.Front.GlobalPosition); var rearLocal = home.ToLocal(lodged.Rear.GlobalPosition);
            void CheckArrow(Node2D body)
            {
                Require(lodged.Front.GlobalPosition.DistanceTo(body.ToGlobal(frontLocal)) < .05f
                    && lodged.Rear.GlobalPosition.DistanceTo(body.ToGlobal(rearLocal)) < .05f
                    && lodged.Front.ZIndex > lodged.Rear.ZIndex && lodged.Front.GetParent() == darkVisuals
                    && lodged.Rear.GetParent() == darkVisuals, "Native lodged arrow lost contact, depth or ownership.");
            }
#endif
            var rig = AddStabActor(room, scene, f.Target, new(250, 400));
            var center = rig.GetParent().GetNode<Marker2D>("CenterPos");
            var second = f.Other!.Creature;
            var secondRig = AddStabActor(room, scene, second, new(460, 400));
            var arrow = new Marker2D { Name = "ArrowPosition", Position = new(32, -56) };
            secondRig.GetParent().AddChild(arrow);
            rig.Position = new(14, -24);
            center.Position += rig.Position; // AimPose publishes the posed body center before the strike.
            var shift = new Vector2(60, -35);
            var hitPositions = new List<Vector2>();
            int impacts = 0;
            bool atRelease = false;
            async Task Step() { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            async Task Move(Creature target)
            {
                impacts++;
                var swordsman = scene.GetNode<Node2D>("DarkNinjaDarkStrike");
                Vector2 Entry() => swordsman.ToGlobal(new(151.4f - 733f / 2, 325f - 649f / 2));
                if (target == second)
                {
                    Require(rig.ZAsRelative && Entry().DistanceTo(arrow.GlobalPosition) < .05f, "Second stab missed native ArrowPosition or kept first victim's layers.");
                    hitPositions.Add(arrow.GlobalPosition);
                    Vector2 last = swordsman.GlobalPosition;
                    center.Position += shift;
                    var actor = room.GetCreatureNode(second)!;
                    ((List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(room)!).Remove(actor);
                    actor.Free(); // Native death can remove a target before the damage task returns.
                    await Step();
                    Require(swordsman.GlobalPosition.DistanceTo(last) < .05f, "Removed victim or previous target moved the swordsman.");
                    return;
                }
                Require(Entry().DistanceTo(center.GlobalPosition) < .05f, "Strike counted the initial pose twice.");
                Vector2 before = Entry();
                rig.Scale = new(-1, 1);
                rig.Position += shift;
                center.Position += shift;
                await Step();
                Vector2 moved = Entry() - before;
#if NINJASLAYER_CHANNEL_PREVIEW
                CheckArrow(swordsman.GetNode<Node2D>("Character"));
                pulled.Slot.Status = ArrowStatus.Retrieved;
                broken.Slot.Status = ArrowStatus.Destroyed;
#endif
                GD.Print($"STAB hold target={shift} sword={moved}");
                if (moved.DistanceTo(shift) > .05f) failures.Add($"Sword drifted by {shift - moved} during impact.");
                scene.Scale = new(1.25f, 1.25f);
                await Step();
                Require(Entry().DistanceTo(center.GlobalPosition) < .05f, "Camera zoom separated the blade from the victim.");
                hitPositions.Add(center.GlobalPosition);
            }
            async Task Release(Node2D detached)
            {
                atRelease = true;
                Vector2 before = detached.GlobalPosition;
                center.Position -= shift;
                await Step();
                Require(detached.GlobalPosition.DistanceTo(before) < .05f && rig.ZAsRelative, "Return still bound to victim or wrong draw order.");
#if NINJASLAYER_CHANNEL_PREVIEW
                var full = detached.GetNode<Node2D>("FullBody");
                full.Scale = new(-.6f, 1); detached.Position += new Vector2(90, -40);
                await Step();
                CheckArrow(full);
                Require(!GodotObject.IsInstanceValid(broken.Front) && !GodotObject.IsInstanceValid(broken.Rear),
                    "Destroyed native arrow left a detached half.");
                if (GodotObject.IsInstanceValid(pulled.Front))
                    Require(pulled.Rear.GetParent() == pulled.Front, "Native retrieval lost the back of the arrow.");
#endif
            }
            var hitType = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.DarkStrikeImpactOutcome", true)!;
            object? hit = AccessTools.Method(GetType(), nameof(ContactCallback)).MakeGenericMethod(hitType).Invoke(null, [(Func<Creature, Task>)Move]);
            await (Task)AccessTools.Method(presentation, "PlayDarkStrike").Invoke(null,
                [f.Monster.Creature, new[] { f.Target, second }, (Func<Creature, bool>)(_ => true), hit, (Func<Node2D, Task>)Release, 1, null])!;
            Require(impacts == 2 && atRelease, "Detached sword presentation did not finish both stabs.");
#if NINJASLAYER_CHANNEL_PREVIEW
            Require(lodged.Front.Transform.IsEqualApprox(frontHome) && lodged.Rear.Transform.IsEqualApprox(rearHome)
                && lodged.Front.ZAsRelative && lodged.Rear.ZAsRelative && lodged.Slot.Status == ArrowStatus.Away,
                "Surviving arrow did not return to the standing body.");
            await ToSignal(GetTree().CreateTimer(.6), SceneTreeTimer.SignalName.Timeout);
            Require(!GodotObject.IsInstanceValid(pulled.Front) && !GodotObject.IsInstanceValid(pulled.Rear),
                "Native retrieval did not finish.");
            GD.Print("PASS native Fool arrow front/rear halves follow the stab and mirrored return, restore, retrieve and destroy.");
#endif
            Require(flashes.Count == 2 && flashes.Zip(hitPositions).All(p => p.First.DistanceTo(p.Second) < .05f),
                "Stab flash missed the moving or despawned victim.");
        }
        finally { _hurtRoom = null; room.Free(); }
        Require(failures.Count == 0, string.Join(" ", failures));
        GD.Print("PASS Dark Strike posed center, movement, zoom, native ArrowPosition, hit flash, removed victim and detached return.");
    }

#if !NINJASLAYER_CHANNEL_STABLE
    private static Creature? _stabReceiver;
    private static (Creature?, bool) _stabPrediction;
    private static void RecordStabReceiver(Creature target) => _stabReceiver = target;
    private static void RecordStabPrediction(IReadOnlyList<Creature> targets, Delegate __6)
    {
        // Accept the old bool-only selector as well, so the regression can run against either candidate.
        _stabPrediction = __6.DynamicInvoke(targets[0]) switch
        {
            ValueTuple<Creature, bool> guess => guess,
            bool pierces => (targets[0], pierces),
            _ => throw new InvalidOperationException("Unknown Dark Strike prediction.")
        };
    }
#endif
}
