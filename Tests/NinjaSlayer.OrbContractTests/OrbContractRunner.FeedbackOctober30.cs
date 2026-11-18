using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyTemporaryHpSemantics()
    {
        foreach (bool overflow in new[] { false, true })
        foreach (bool buffer in new[] { false, true })
        {
            using var combat = new OrbCombat();
            Creature owner = combat.Player.Creature;
            owner.SetCurrentHpInternal(40);
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 5, owner, null);
            if (buffer) await PowerCmd.Apply<BufferPower>(Choice, owner, 1, owner, null);
            else combat.Player.AddRelicInternal(ModelDb.Relic<TungstenRod>().ToMutable());
            var result = (await CreatureCmd.Damage(Choice, owner, overflow ? 9 : 4,
                ValueProp.Unblockable | ValueProp.Unpowered, owner)).Single();
            int loss = overflow && !buffer ? 3 : 0;
            Require(owner.CurrentHp == 40 - loss && result.UnblockedDamage == loss
                && owner.GetPowerAmount<NarakuLifePower>() == (overflow ? 0 : 1)
                && owner.HasPower<BufferPower>() == (buffer && !overflow),
                "Temporary HP must absorb self-damage before Tungsten Rod/Buffer, protecting Buffer until overflow.");
        }
        using (var combat = new OrbCombat())
        {
            Creature owner = combat.Player.Creature;
            owner.SetCurrentHpInternal(40);
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 120, owner, null);
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 20, owner, null);
            int maxHp = owner.MaxHp;
            await CreatureCmd.Heal(owner, 3);
            await CreatureCmd.Damage(Choice, owner, 0, ValueProp.Unpowered, combat.Enemy);
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [owner]);
            Require(owner.CurrentHp == 43 && owner.MaxHp == maxHp && owner.GetPowerAmount<NarakuLifePower>() == 140,
                "Temporary HP must stack above max HP, survive turn end, and remain separate from healing and real HP.");
            var direct = owner.LoseHpInternal(1, ValueProp.Unblockable | ValueProp.Unpowered);
            Require(direct.UnblockedDamage == 1 && owner.GetPowerAmount<NarakuLifePower>() == 140,
                "Direct HP assignment used by forced death must bypass temporary HP.");
            combat.Player.AfterCombatEnd();
            Require(!owner.HasPower<NarakuLifePower>(), "Native combat cleanup must clear temporary HP.");
        }
        GD.Print("PASS StSLib temporary HP: Block/Intangible before absorption, Buffer/Tungsten after, self-damage, overflow, no false HP-loss hooks, turn retention and combat cleanup.");
    }

    private async Task VerifyArchitectExitBounds()
    {
        VerifyArchitectExecutionPriority();
        using var combat = new OrbCombat(ninjaSlayer: true);
        var harmony = new Harmony("NinjaSlayer.ArchitectExitContract");
        harmony.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), nameof(NCombatRoom.Instance)),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtRoom)));
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.GetCreatureNode)),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveAimActor)));
        STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(ShurikenOrb).Assembly,
            STS2RitsuLib.RitsuLibFramework.CreateLogger("Architect exit contract"));
        try
        {
            foreach (var (size, zoom) in new[] { (new Vector2I(1280, 720), 1f), (new Vector2I(1920, 1080), .8f), (new Vector2I(2560, 1080), 1.25f) })
            {
                var viewport = new SubViewport { Size = size, TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                AddChild(viewport);
                _hurtRoom = new FreeInputContractRoom { Size = size };
                var scene = new Control { Size = size, Scale = new Vector2(zoom, zoom), Position = new Vector2(75, 0) };
                _hurtRoom.AddChild(scene);
                AccessTools.Property(typeof(NCombatRoom), nameof(NCombatRoom.SceneContainer)).SetValue(_hurtRoom, scene);
                var actor = new AimContractCreature { Position = new Vector2(850, 600) };
                var visuals = STS2RitsuLib.Scaffolding.Godot.RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(
                    "res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn")!;
                AccessTools.Property(typeof(NCreature), nameof(NCreature.Entity)).SetValue(actor, combat.Player.Creature);
                AccessTools.Property(typeof(NCreature), nameof(NCreature.Visuals)).SetValue(actor, visuals);
                actor.AddChild(visuals);
                scene.AddChild(actor);
                AimActors.Add(combat.Player.Creature, actor);
                var cinematic = new ArchitectExecutionCinematic();
                _hurtRoom.AddChild(cinematic);
                foreach (var (field, value) in new (string, object)[]
                {
                    ("_room", _hurtRoom), ("_owner", combat.Player.Creature), ("_ownerNode", actor), ("_architectDeathCommitted", true)
                }) AccessTools.Field(typeof(ArchitectExecutionCinematic), field).SetValue(cinematic, value);
                viewport.AddChild(_hurtRoom);
                try
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Vector2 slot = actor.Position;
                    await (Task)AccessTools.Method(typeof(ArchitectExecutionCinematic), "ExitScene").Invoke(cinematic, [CancellationToken.None])!;
                    var body = visuals.GetNode<Sprite2D>("%Visuals");
                    Rect2 bounds = body.GetGlobalTransformWithCanvas() * body.GetRect();
                    Require(bounds.Position.X > size.X && actor.Position == slot,
                        $"Architect exit left trailing scarf/texture inside {size} at zoom {zoom}: left={bounds.Position.X}.");
                    GD.Print($"PASS Architect exit: viewport={size}, zoom={zoom}, full texture left={bounds.Position.X}, UI slot unchanged.");
                }
                finally
                {
                    AimActors.Remove(combat.Player.Creature);
                    viewport.Free();
                    _hurtRoom = null;
                }
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static void VerifyArchitectExecutionPriority()
    {
        var select = AccessTools.Method(typeof(ArchitectExecutionCinematic), "SelectMeleeExecution");
        using var combat = new OrbCombat(ninjaSlayer: true);
        foreach (CardModel initial in combat.Player.Deck.Cards.ToArray())
            combat.Player.Deck.RemoveInternal(initial);
        var normal = AddCard<StrikeNinjaSlayer>(combat, PileType.Deck);
        var heavy = AddCard<StraightPunch>(combat, PileType.Deck);
        var lightKick = AddCard<DragonFlyingKick>(combat, PileType.Deck);
        var heavyKick = AddCard<SomersaultKick>(combat, PileType.Deck);
        var special = AddCard<CollapseFist>(combat, PileType.Deck);
        var alabama = AddCard<AlabamaDrop>(combat, PileType.Deck);
        var nonMelee = AddCard<DefendNinjaSlayer>(combat, PileType.Deck);
        foreach (CardModel expected in new CardModel[] { alabama, special, heavyKick, lightKick, heavy, normal })
        {
            var before = combat.Player.Deck.Cards.ToArray();
            for (int i = 0; i < 3; i++)
                Require(ReferenceEquals(select.Invoke(null, [combat.Player]), expected),
                    $"Architect execution must consistently prefer {expected.Id} over lower-priority owned cards.");
            Require(combat.Player.Deck.Cards.SequenceEqual(before), "Execution selection must not alter the deck.");
            combat.Player.Deck.RemoveInternal(expected);
        }
        var fallback = (CardModel)select.Invoke(null, [combat.Player])!;
        Require(fallback is StrikeNinjaSlayer && fallback.Owner == combat.Player
            && combat.Player.Deck.Cards.SequenceEqual([nonMelee]),
            "A deck without eligible melee must use a visual Strike without adding it to the deck.");
        var tile = AddCard<TileBreakingPunch>(combat, PileType.Deck);
        AddCard<CollapseFist>(combat, PileType.Deck);
        AddCard<AlabamaDrop>(combat, PileType.Hand);
        Require(ReferenceEquals(select.Invoke(null, [combat.Player]), tile),
            "Tile-Breaking Punch must use Collapse Fist's special-heavy tier, preserve deck order on ties, and ignore temporary hand cards.");
        GD.Print("PASS Architect execution priority: Alabama, special heavy, heavy kick, light kick, heavy, normal; stable ties, owned deck only, visual fallback.");
    }
}
