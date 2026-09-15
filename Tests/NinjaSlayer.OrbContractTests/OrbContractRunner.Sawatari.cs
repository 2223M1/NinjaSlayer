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
        internal SawatariFixture(bool third = true, int ascension = 0, bool multiplayer = false)
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
                ascensionLevel: ascension, seed: "sawatari-weapons");
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
        foreach (int ascension in new[] { 0, 10 })
        {
            using (var f = new SawatariFixture(third: false, ascension: ascension))
            {
                Require(f.Monster.Creature.CurrentHp == (ascension == 0 ? 60 : 66), "Act-one HP changed.");
                await f.Move();
                Require(f.Target.CurrentHp == 500 - (ascension == 0 ? 14 : 16)
                    && f.Monster.Creature.GetPowerAmount<StrengthPower>() == 2,
                    "Act-one arrow must hit before gaining two Strength.");
                await f.Move();
                Require(f.Target.CurrentHp == 500 - (ascension == 0 ? 14 : 16) - 16,
                    "Act-one bamboo must retain its 2x4 base plus opening Strength.");
            }
            using (var f = new SawatariFixture(ascension: ascension))
            {
                int hit = ascension == 0 ? 12 : 14;
                Require(f.Monster.Creature.CurrentHp == (ascension == 0 ? 252 : 278), "Act-three HP is incorrect.");
                await f.Move();
                Require(f.Target.CurrentHp == 500 - 2 * hit && !f.Monster.Creature.HasPower<StrengthPower>()
                    && f.Monster.NextMove.Id == SawatariMonster.ThrowMoveId, "Opening dual attack or forced throw is incorrect.");
                await f.Move();
                Require(f.Monster.MacheteCount == 1 && f.Monster.Creature.GetPowerAmount<StrengthPower>() == 3,
                    "First throw must transfer one knife and grant three Strength.");
                int remainingHand = f.Monster.HeldMachetes;
                await f.Move();
                Require(f.Monster.MacheteCount == 0 && remainingHand != 0
                    && f.Monster.Creature.GetPowerAmount<StrengthPower>() == 6
                    && PileType.Hand.GetPile(f.Combat.Player).Cards.OfType<SawatariMachete>().Count() == 2,
                    "Second throw did not transfer the remaining knife.");
                int before = f.Target.CurrentHp;
                await f.Move();
                Require(f.Target.CurrentHp == before - 32 && f.Monster.NextMove.Id == SawatariMonster.AttackMoveId,
                    "Unarmed bamboo must use 2x4 plus the six accumulated Strength.");
            }
        }
        GD.Print("PASS Sawatari both ascensions: arrow/Strength, dual/throw/unarmed sequence and hit totals.");

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
                && PileType.Hand.GetPile(f.Combat.Player).Cards.OfType<SawatariMachete>().Count() == 1,
                $"{defense} incorrectly prevented the thrown knife transfer.");
        }

        using (var f = new SawatariFixture())
        {
            await f.Move(); await f.Move();
            SawatariMachete card = PileType.Hand.GetPile(f.Combat.Player).Cards.OfType<SawatariMachete>().Single();
            CardModel copy = f.Combat.State.CloneCard(card);
            PileType.Hand.GetPile(f.Combat.Player).AddInternal(copy, -1, silent: true);
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
                && f.Monster.Creature.GetPowerAmount<StrengthPower>() == 3, "Later dual attack gained Strength or skipped forced throw.");
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
