using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Events;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private sealed class SawatariFixture : IDisposable
    {
        internal readonly OrbCombat Combat = new(ninjaSlayer: true);
        internal readonly RunState Run;
        internal readonly SawatariMonster Monster;
        internal readonly Player? Other;
        private readonly object? _previousRun = AccessTools.Property(typeof(RunManager), "State").GetValue(RunManager.Instance);
        private readonly object? _previousAscension = RunManager.Instance.AscensionManager;
        internal Creature Target => Combat.Player.Creature;
        internal SawatariFixture(bool third = true, int ascension = 0, bool multiplayer = false, string seed = "sawatari-weapons")
        {
            if (multiplayer)
            {
                Other = Player.CreateForNewRun<NinjaSlayerCharacter>(UnlockState.all, 2);
                Combat.State.AddPlayer(Other);
                Other.ResetCombatState();
                Other.Creature.SetMaxHpInternal(500);
                Other.Creature.SetCurrentHpInternal(500);
            }
            Run = RunState.CreateForTest(Other == null ? [Combat.Player] : [Combat.Player, Other],
                ascensionLevel: ascension, seed: seed);
            AccessTools.Property(typeof(RunManager), "State").SetValue(RunManager.Instance, Run);
            AccessTools.Property(typeof(RunManager), "AscensionManager").SetValue(RunManager.Instance, new AscensionManager(ascension));
            AccessTools.Field(Combat.State.GetType(), "<RunState>k__BackingField").SetValue(Combat.State, Run);
            Run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, new ModelId("ENCOUNTER", "TEST"));
            Run.PushRoom(new CombatRoom(Combat.State));
            Monster = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
            Monster.ActThree = third;
            Monster.RunRng = Run.Rng;
            var creature = new Creature(Monster, CombatSide.Enemy, null);
            AccessTools.Method(Combat.State.GetType(), "AttachCreature").Invoke(Combat.State, [creature]);
            Combat.State.AddCreature(creature);
            creature.SetMaxHpInternal(Monster.MaxInitialHp);
            creature.SetCurrentHpInternal(Monster.MaxInitialHp);
            Target.SetMaxHpInternal(500);
            Target.SetCurrentHpInternal(500);
            Monster.SetUpForCombat();
            Monster.RollMove(Combat.State.PlayerCreatures);
        }
        internal async Task Move()
        {
            await Monster.PerformMove();
            Monster.RollMove(Combat.State.PlayerCreatures);
        }
        public void Dispose()
        {
            Other?.PlayerCombatState?.AfterCombatEnd();
            Combat.Dispose();
            AccessTools.Property(typeof(RunManager), "State").SetValue(RunManager.Instance, _previousRun);
            AccessTools.Property(typeof(RunManager), "AscensionManager").SetValue(RunManager.Instance, _previousAscension);
        }
    }

    private static async Task VerifyIaiLifeBoundary()
    {
        foreach (string state in new[] { "alive", "zero", "removed", "lethal", "target-dead" })
        {
            using var f = new DarkStrikeFixture();
            var dark = f.Monster.Creature;
            await PowerCmd.Apply<StrengthPower>(Choice, dark, 4, dark, null);
            var iai = await PowerCmd.Apply<IaiPower>(Choice, dark, 1, dark, null);
            int flashes = 0;
            iai!.Flashed += _ => flashes++;
            int hp = f.Target.CurrentHp;
            if (state == "zero") dark.SetCurrentHpInternal(0);
            if (state == "removed") f.Combat.State.RemoveCreature(dark);
            if (state == "target-dead") f.Target.SetCurrentHpInternal(0);
            if (state == "lethal")
                await CreatureCmd.Damage(Choice, dark, 999, ValueProp.Move, f.Target, null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                    , null
#endif
                );
            else
                await iai.AfterDamageReceived(Choice, dark, new DamageResult(dark, ValueProp.Move),
                    ValueProp.Move, f.Target, null);
            Require(flashes == (state == "alive" ? 1 : 0), $"{state}: unexpected Iai flash.");
            if (state != "target-dead") Require(f.Target.CurrentHp == hp - (state == "alive" ? 4 : 0),
                $"{state}: unexpected Iai damage.");
        }
        GD.Print("PASS Iai alive/zero/removed/lethal/dead-target boundaries and flash counts.");
    }

    private static async Task VerifySawatariWeapons()
    {
        MegaCrit.Sts2.Core.Context.LocalContext.NetId = 1;
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitPrefsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        foreach (int act in new[] { 0, 1, 2 })
        {
            using var f = new SawatariFixture();
            f.Run.CurrentActIndex = act;
            f.Run.Act.GenerateRooms(f.Run.Rng.UpFront, UnlockState.all);
            // Leave the weak-encounter prefix: only the strong normal pool is eligible.
            while (f.Run.Act.PullNextEncounter(RoomType.Monster).IsWeak)
                f.Run.Act.MarkRoomVisited(RoomType.Monster);
            foreach (bool visited in new[] { false, true })
            {
                if (visited) f.Run.AddVisitedEvent(ModelDb.Event<SawatariEvent>());
                var odds = f.Run.Odds.UnknownMapPoint;
                SawatariUnknownRoomRollPatch.Prefix(odds, f.Run, out var roll);
                try
                {
                    AccessTools.Method(typeof(SawatariUnknownRoomRollPatch), "CaptureAllowedRooms")
                        .Invoke(null, [f.Run, new HashSet<RoomType> { RoomType.Monster, RoomType.Event }]);
                    Require((roll.Chance > 0f) == (act != 1 && !visited),
                        $"Sawatari routing eligibility differs at act {act + 1}, visited {visited}.");
                }
                finally { SawatariUnknownRoomRollPatch.Finalizer(null, odds, roll); }
            }
        }
        GD.Print("PASS strong-unknown route in acts one/three and exclusion after a previous visit.");
        foreach (var spec in new[] {
            (Asc: 0, Hp1: 76, Hp3: 280, Arrow: 14, Dual: 8, Throw: 12, Bamboo: 3),
            (Asc: 7, Hp1: 76, Hp3: 280, Arrow: 14, Dual: 8, Throw: 12, Bamboo: 3),
            (Asc: 8, Hp1: 80, Hp3: 300, Arrow: 14, Dual: 8, Throw: 12, Bamboo: 3),
            (Asc: 9, Hp1: 80, Hp3: 300, Arrow: 16, Dual: 10, Throw: 14, Bamboo: 4),
            (Asc: 10, Hp1: 80, Hp3: 300, Arrow: 16, Dual: 10, Throw: 14, Bamboo: 4) })
        {
            using (var f = new SawatariFixture(third: false, ascension: spec.Asc))
            {
                Require(f.Monster.Creature.CurrentHp == spec.Hp1, $"Act-one A{spec.Asc} HP differs.");
                string[] moves = [SawatariMonster.EnhanceMoveId, SawatariMonster.AttackMoveId,
                    SawatariMonster.SecondAttackMoveId];
                int strength = 0;
                for (int turn = 0; turn < 6; turn++)
                {
                    int index = turn % 3;
                    int damage = index == 0 ? spec.Arrow + strength : (2 + strength) * 4;
                    Require(f.Monster.NextMove.Id == moves[index], "Act-one cycle lost its native move position.");
                    var intent = f.Monster.NextMove.Intents.OfType<MegaCrit.Sts2.Core.MonsterMoves.Intents.AttackIntent>().Single();
                    Require(intent.GetTotalDamage([f.Target], f.Monster.Creature) == damage, "Act-one intent differs from damage.");
                    int hp = f.Target.CurrentHp;
                    await f.Move();
                    if (index != 0) strength++;
                    Require(f.Target.CurrentHp == hp - damage
                        && f.Monster.Creature.GetPowerAmount<StrengthPower>() == strength
                        && f.Monster.Creature.GetPowerAmount<PlatingPower>() == 4 * (turn / 3 + 1)
                        && f.Monster.NextMove.Id == moves[(index + 1) % 3],
                        $"Act-one A{spec.Asc} turn {turn + 1}: damage, buffs or follow-up differ.");
                }
            }
            using (var f = new SawatariFixture(ascension: spec.Asc))
            {
                Require(f.Monster.Creature.CurrentHp == spec.Hp3, "Act-three HP is incorrect.");
                await f.Move();
                Require(f.Target.CurrentHp == 500 - 2 * spec.Dual && !f.Monster.Creature.HasPower<StrengthPower>()
                    && f.Monster.NextMove.Id == SawatariMonster.ThrowMoveId, "Opening dual attack or forced throw is incorrect.");
                await f.Move();
                Require(f.Target.CurrentHp == 500 - 2 * spec.Dual - spec.Throw
                    && f.Monster.MacheteCount == 1 && f.Monster.Creature.GetPowerAmount<VigorPower>() == 6
                    && !f.Monster.Creature.HasPower<StrengthPower>(), "First throw damage or Vigor differs.");
                int hp = f.Target.CurrentHp;
                await f.Move();
                Require(f.Target.CurrentHp == hp - spec.Throw - 6 && f.Monster.MacheteCount == 0
                    && f.Monster.Creature.GetPowerAmount<VigorPower>() == 6
                    && PileType.Hand.GetPile(f.Combat.Player).Cards.OfType<SawatariMachete>().Count() == 2,
                    "Second throw must consume old Vigor and grant six new Vigor.");
                hp = f.Target.CurrentHp;
                var intent = f.Monster.NextMove.Intents.OfType<MegaCrit.Sts2.Core.MonsterMoves.Intents.AttackIntent>().Single();
                Require(intent.GetTotalDamage([f.Target], f.Monster.Creature) == (spec.Bamboo + 6) * 4,
                    "Unarmed intent must include Vigor on every hit.");
                await f.Move();
                Require(f.Target.CurrentHp == hp - (spec.Bamboo + 6) * 4
                    && !f.Monster.Creature.HasPower<VigorPower>()
                    && f.Monster.Creature.GetPowerAmount<StrengthPower>() == 4,
                    "Bamboo must consume Vigor after all four hits, then grant four Strength.");
                hp = f.Target.CurrentHp;
                await f.Move();
                Require(f.Target.CurrentHp == hp - (spec.Bamboo + 4) * 4
                    && f.Monster.Creature.GetPowerAmount<StrengthPower>() == 8,
                    "Later bamboo damage or Strength accumulation differs.");
            }
        }
        GD.Print("PASS Sawatari A0/A7/A8/A9/A10: two arrow/bamboo cycles, separate damage and native Vigor consumption.");

        foreach (string defense in new[] { "block", "buffer", "evasion", "naraku" })
        {
            using var f = new SawatariFixture();
            await f.Move();
            if (defense == "block") await CreatureCmd.GainBlock(f.Target, 100, ValueProp.Unpowered, null);
            if (defense == "buffer") await PowerCmd.Apply<BufferPower>(Choice, f.Target, 1, f.Target, null);
            if (defense == "evasion") await PowerCmd.Apply<EvasionPower>(Choice, f.Target, 1, f.Target, null);
            if (defense == "naraku") await PowerCmd.Apply<NarakuLifePower>(Choice, f.Target, 100, f.Target, null);
            int before = f.Target.CurrentHp;
            await f.Move();
            Require(f.Target.CurrentHp == before && f.Monster.MacheteCount == 1
                && PileType.Hand.GetPile(f.Combat.Player).Cards.OfType<SawatariMachete>().Count() == 1
                && f.Monster.Creature.GetPowerAmount<VigorPower>() == 6,
                $"{defense} incorrectly prevented the thrown knife transfer.");
        }

        foreach (bool third in new[] { false, true })
        {
            using var f = new SawatariFixture(third: third, ascension: 10);
            // The event calls these shared presentation/attack entry points for support.
            int hp = f.Target.CurrentHp;
            await (Task)AccessTools.Method(typeof(SawatariMonster), third ? "PlayDualAttack" : "PlayAttack")
                .Invoke(f.Monster, [f.Target])!;
            Require(f.Target.CurrentHp == hp - (third ? 20 : 8)
                && !f.Monster.Creature.HasPower<StrengthPower>()
                && !f.Monster.Creature.HasPower<VigorPower>()
                && !f.Monster.Creature.HasPower<PlatingPower>(), "Support entry point granted enemy-only buffs.");
        }
        foreach (string move in new[] { "ArrowMove", "AttackMove", "ThrowMove" })
        {
            using var f = new SawatariFixture(third: move != "ArrowMove");
            f.Monster.Creature.SetCurrentHpInternal(1);
            await PowerCmd.Apply<ThornsPower>(Choice, f.Target, 100, f.Target, null);
            await (Task)AccessTools.Method(typeof(SawatariMonster), move).Invoke(f.Monster, [new Creature[] { f.Target }])!;
            Require(f.Monster.Creature.IsDead && !f.Monster.Creature.HasPower<StrengthPower>()
                && !f.Monster.Creature.HasPower<VigorPower>() && !f.Monster.Creature.HasPower<PlatingPower>(),
                $"{move} granted a buff after lethal thorns.");
        }
        using (var f = new SawatariFixture(third: false, multiplayer: true))
        {
            await f.Move();
            Require(f.Monster.Creature.GetPowerAmount<PlatingPower>() == 12,
                "Two-player Plating must use native scaling once.");
        }
        GD.Print("PASS support without enemy buffs, lethal thorns and native two-player Plating scaling.");

        using (var f = new SawatariFixture())
        {
            await f.Move(); await f.Move();
            SawatariMachete card = PileType.Hand.GetPile(f.Combat.Player).Cards.OfType<SawatariMachete>().Single();
            CardModel copy = f.Combat.State.CloneCard(card);
            await CardPileCmd.Add(copy, PileType.Hand);
            Require(card.EnergyCost.GetWithModifiers(CostModifiers.Local) == 2 && !card.IsUpgradable
                && card.Type == CardType.Attack && card.Rarity == CardRarity.Token
                && !card.CanBeGeneratedInCombat && !card.CanBeGeneratedByModifiers
                && card.Keywords.SetEquals([CardKeyword.Retain, CardKeyword.Exhaust]),
                "Machete card properties differ from the approved design.");
            await PowerCmd.Apply<StrengthPower>(Choice, f.Target, 4, f.Target, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, f.Monster.Creature, 2, f.Target, null);
            int hp = f.Monster.Creature.CurrentHp;
            await CardCmd.AutoPlay(Choice, card, f.Monster.Creature);
            Require(f.Monster.Creature.CurrentHp == hp - 24 && card.Pile?.Type == PileType.Exhaust
                && f.Monster.NextMove.Id == SawatariMonster.DualMoveId && f.Monster.MacheteCount == 2,
                "Returned Machete must use normal Strength/Vulnerable damage and immediately change intent.");
            await f.Move();
            Require(f.Monster.NextMove.Id == SawatariMonster.ThrowMoveId
                && f.Monster.Creature.GetPowerAmount<StrengthPower>() == 0 && !f.Monster.Creature.HasPower<VigorPower>(), "Later dual attack gained Strength or skipped forced throw.");
            await f.Move();
            await CardCmd.AutoPlay(Choice, copy, f.Monster.Creature);
            Require(f.Monster.MacheteCount == 2, "A copied Machete must be able to replace a missing knife.");
            f.Monster.ReturnMachete();
            Require(f.Monster.MacheteCount == 2, "Machete count exceeded two.");
            SavedProperties saved = SavedProperties.From(f.Monster)!;
            var restored = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
            saved.Fill(restored);
            Require(restored.ActThree && restored.HeldMachetes == f.Monster.HeldMachetes
                && restored.MustThrow == f.Monster.MustThrow && restored.NextThrowHand == f.Monster.NextThrowHand,
                "Native saved properties lost weapon state.");
            CardModel restoredCard = CardModel.FromSerializable(card.ToSerializable());
            Require(restoredCard is SawatariMachete && !restoredCard.IsUpgradable,
                "Native card serialization lost the event Token attack model.");
        }
        GD.Print("PASS defenses, native Machete damage/exhaust, immediate intent, copied return and model serialization.");

        foreach (string action in new[] { "discard", "exhaust", "other-enemy", "full-hand" })
        {
            using var f = new SawatariFixture();
            await f.Move();
            if (action == "full-hand") for (int i = 0; i < 10; i++) f.Combat.Card();
            await f.Move();
            SawatariMachete knife = f.Combat.Player.Piles.SelectMany(p => p.Cards).OfType<SawatariMachete>().Single();
            if (action == "discard") await CardCmd.Discard(Choice, knife);
            if (action == "exhaust") await CardCmd.Exhaust(Choice, knife);
            if (action == "other-enemy") await CardCmd.AutoPlay(Choice, knife, f.Combat.Enemy);
            if (action == "full-hand") Require(knife.Pile?.Type == PileType.Discard, "Full-hand transfer must use native overflow.");
            Require(f.Monster.MacheteCount == 1, $"{action} unexpectedly returned a knife.");
        }
        string? firstSequence = null;
        for (int repeat = 0; repeat < 2; repeat++)
        {
            using var f = new SawatariFixture(multiplayer: true);
            await f.Move(); await f.Move(); await f.Move();
            int first = f.Combat.Player.Piles.Sum(p => p.Cards.OfType<SawatariMachete>().Count());
            int second = f.Other!.Piles.Sum(p => p.Cards.OfType<SawatariMachete>().Count());
            string sequence = $"{first}/{second}/{f.Target.CurrentHp}/{f.Other.Creature.CurrentHp}";
            Require(first + second == 2, "Multiplayer duplicated a thrown knife for each player.");
            if (firstSequence != null) Require(sequence == firstSequence, "Fixed-seed weapon targeting diverged.");
            firstSequence = sequence;
        }
        GD.Print("PASS native discard/exhaust/overflow, other enemy targeting and seeded two-player transfer.");
        var enemyThrows = new HashSet<int>();
        var playerCatches = new HashSet<int>();
        var playerThrows = new HashSet<int>();
        var enemyCatches = new HashSet<int>();
        for (int seed = 0; seed < 16; seed++)
        {
            string? firstHands = null;
            for (int repeat = 0; repeat < 2; repeat++)
            {
                using var f = new SawatariFixture(seed: $"sawatari-hands-{seed}");
                await f.Move(); await f.Move();
                var first = PileType.Hand.GetPile(f.Combat.Player).Cards.OfType<SawatariMachete>().Single();
                int caught = first.HeldHand;
                Require(caught is 0 or 1, "First knife has no receiving hand.");
                playerCatches.Add(caught);
                enemyThrows.Add(f.Monster.NextThrowHand);
                Require(((SawatariMachete)CardModel.FromSerializable(first.ToSerializable())).HeldHand == caught,
                    "Native card serialization lost the receiving hand.");
                await f.Move();
                var second = PileType.Hand.GetPile(f.Combat.Player).Cards.OfType<SawatariMachete>().Single(c => c != first);
                Require(first.HeldHand == caught && second.HeldHand == 1 - caught,
                    "Second catch moved the first knife or reused its occupied hand.");
                await CardCmd.AutoPlay(Choice, first, f.Monster.Creature);
                Require(second.HeldHand is 0 or 1 && first.HeldHand == -1,
                    "Random throwing must release one hand and keep the remaining knife assigned.");
                playerThrows.Add(1 - second.HeldHand);
                enemyCatches.Add(f.Monster.HeldMachetes);
                string hands = $"{caught}/{second.HeldHand}/{f.Monster.HeldMachetes}";
                if (firstHands != null) Require(firstHands == hands, "Fixed-seed hand choices diverged.");
                firstHands = hands;
                await CardCmd.AutoPlay(Choice, second, f.Monster.Creature);
                Require(f.Monster.HeldMachetes == 3, "Second return did not fill the remaining enemy hand.");
            }
        }
        Require(enemyThrows.SetEquals([0, 1]) && playerCatches.SetEquals([0, 1])
            && playerThrows.SetEquals([0, 1]) && enemyCatches.SetEquals([1, 2]),
            "A throw or catch still always selects the same hand.");
        GD.Print("PASS both actors randomly throw/catch either hand, preserve occupied hands, serialize and repeat fixed seeds.");
        using (var f = new SawatariFixture())
        {
            await f.Move(); await f.Move(); await f.Move();
            SawatariMachete card = PileType.Hand.GetPile(f.Combat.Player).Cards.OfType<SawatariMachete>().First();
            for (int count = 0; count <= 2; count++)
            {
                var restored = (SawatariMonster)ModelDb.Monster<SawatariMonster>().ToMutable();
                SavedProperties.From(f.Monster)!.Fill(restored);
                Require(restored.MacheteCount == count && restored.NextThrowHand == f.Monster.NextThrowHand,
                    "Saving zero/one/two weapons changed which hand owns each knife.");
                if (count < 2) await CardCmd.AutoPlay(Choice, card, f.Monster.Creature);
            }
            Require(card.Pile?.Type == PileType.Exhaust && f.Monster.MacheteCount == 2,
                "Repeated plays of the same status must each return one knife, up to two.");
        }
        GD.Print("PASS zero/one/two weapon serialization and repeated plays of one Machete.");
    }
}
