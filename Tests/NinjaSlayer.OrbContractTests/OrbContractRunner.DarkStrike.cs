using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Encounters;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static Task DarkStrike(DarkNinjaMonster monster, params Creature[] targets) =>
        (Task)AccessTools.Method(typeof(DarkNinjaMonster), "DarkStrikeMove").Invoke(monster, [targets])!;

    private static CardModel Stealable(DarkStrikeFixture fixture, CardModel canonical,
        PileType pile = PileType.Draw, bool upgrade = false, Player? owner = null)
    {
        owner ??= fixture.Combat.Player;
        CardModel deck = fixture.Run.CreateCard(canonical, owner);
        if (upgrade) CardCmd.Upgrade(deck);
        owner.Deck.AddInternal(deck, -1, silent: true);
        CardModel copy = fixture.Combat.State.CloneCard(deck);
        copy.DeckVersion = deck;
        pile.GetPile(owner).AddInternal(copy, -1, silent: true);
        return copy;
    }

    private sealed class DarkStrikeFixture : IDisposable
    {
        internal readonly OrbCombat Combat = new(ninjaSlayer: true);
        internal readonly RunState Run;
        internal readonly CombatRoom Room;
        internal readonly DarkNinjaMonster Monster;
        internal readonly Player? Other;
        internal Creature Target => Combat.Player.Creature;
        internal DarkStrikeFixture(bool multiplayer = false)
        {
            if (multiplayer)
            {
                Other = Player.CreateForNewRun<NinjaSlayer.Content.NinjaSlayerCharacter>(UnlockState.all, 2);
                Combat.State.AddPlayer(Other);
                Other.ResetCombatState();
                Other.Creature.SetMaxHpInternal(500);
                Other.Creature.SetCurrentHpInternal(500);
            }
            Run = RunState.CreateForTest(Other == null ? [Combat.Player] : [Combat.Player, Other], seed: "dark-strike-theft");
            AccessTools.Field(Combat.State.GetType(), "<RunState>k__BackingField").SetValue(Combat.State, Run);
            Run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, ModelDb.Encounter<DarkNinjaEncounter>().Id);
            Room = new CombatRoom(Combat.State);
            Run.PushRoom(Room);
            Monster = (DarkNinjaMonster)ModelDb.Monster<DarkNinjaMonster>().ToMutable();
            Monster.RunRng = Run.Rng;
            var creature = new Creature(Monster, CombatSide.Enemy, null);
            AccessTools.Method(Combat.State.GetType(), "AttachCreature").Invoke(Combat.State, [creature]);
            Combat.State.AddCreature(creature);
            creature.SetMaxHpInternal(500);
            creature.SetCurrentHpInternal(500);
            Target.SetMaxHpInternal(500);
            Target.SetCurrentHpInternal(500);
        }
        public void Dispose()
        {
            Other?.PlayerCombatState?.AfterCombatEnd();
            Combat.Dispose();
        }
    }

    private static async Task VerifyDarkStrikeTheft()
    {
        foreach (string defense in new[] { "none", "block", "partial-block", "buffer", "evasion", "naraku", "overflow", "zero" })
        {
            using var f = new DarkStrikeFixture();
            CardModel card = Stealable(f, ModelDb.Card<PlaceholderBlueDefense01>(), upgrade: true);
            if (defense is "block" or "partial-block")
                await CreatureCmd.GainBlock(f.Target, defense == "block" ? 100 : 3, ValueProp.Unpowered, null);
            if (defense == "buffer") await PowerCmd.Apply<BufferPower>(Choice, f.Target, 1, f.Target, null);
            if (defense == "evasion") await PowerCmd.Apply<EvasionPower>(Choice, f.Target, 1, f.Target, null);
            if (defense is "naraku" or "overflow")
                await PowerCmd.Apply<NarakuLifePower>(Choice, f.Target, defense == "naraku" ? 100 : 3, f.Target, null);
            if (defense == "zero") await PowerCmd.Apply<StrengthPower>(Choice, f.Monster.Creature, -100, f.Monster.Creature, null);
            await DarkStrike(f.Monster, f.Target);
            bool expected = defense is "none" or "partial-block" or "naraku" or "overflow";
            SwipePower[] swipes = f.Monster.Creature.Powers.OfType<SwipePower>().ToArray();
            Require(swipes.Length == (expected ? 1 : 0), $"Dark Strike theft mismatch for {defense}.");
            Require(!f.Target.HasPower<WeakPower>(), "Dark Strike still applied Weak.");
            Require(f.Combat.Player.Deck.Cards.Contains(card.DeckVersion!) != expected,
                "Theft and permanent deck removal diverged.");
            if (expected)
                Require(ReferenceEquals(swipes.Single().StolenCard, card) && card.IsUpgraded,
                    "Native Swipe did not retain the exact upgraded card.");
        }
        GD.Print("PASS Dark Strike real HP/Naraku receipts, full/partial block, Buffer, Evasion and zero damage.");

        string[] stolenSequences = new string[2];
        for (int repeat = 0; repeat < 2; repeat++)
        {
            using var f = new DarkStrikeFixture();
            CardModel hand = Stealable(f, ModelDb.Card<PlaceholderBlueDefense01>(), PileType.Hand);
            CardModel generated = AddCard<PlaceholderBlueDefense01>(f.Combat, PileType.Draw);
            CardModel basic = Stealable(f, ModelDb.Card<StrikeIronclad>());
            CardModel common = Stealable(f, ModelDb.Card<PalmThrustRedesignV1>(), PileType.Discard);
            CardModel blue = Stealable(f, ModelDb.Card<PlaceholderBlueDefense01>());
            CardModel otherBlue = Stealable(f, ModelDb.Card<FlyingBladeDanceRedesignV1>());
            var seen = new List<CardModel>();
            for (int index = 0; index < 5; index++)
            {
                await DarkStrike(f.Monster, f.Target);
                seen = f.Monster.Creature.Powers.OfType<SwipePower>().Select(p => p.StolenCard!).ToList();
                Require(seen.Count == Math.Min(index + 1, 4), "Repeated theft duplicated or missed a card.");
            }
            Require(seen.Take(2).ToHashSet().SetEquals(new[] { blue, otherBlue })
                && seen[2] == common && seen[3] == basic, "Thieving Hopper priority tiers changed.");
            Require(hand.Pile?.Type == PileType.Hand && generated.Pile?.Type == PileType.Draw,
                "Swipe stole from the hand or stole a generated card.");
            stolenSequences[repeat] = string.Join(",", seen.Select(c => c.Id));
            await CreatureCmd.Kill(f.Monster.Creature, force: true);
            var rewards = f.Room.ExtraRewards[f.Combat.Player].OfType<SpecialCardReward>().ToArray();
            Require(rewards.Length == 4, "Death must offer one native reward per stolen card.");
            Require(!seen.Any(c => f.Combat.Player.Deck.Cards.Contains(c.DeckVersion!)), "Death auto-collected stolen cards.");
            await rewards[0].SelectUnsynchronized();
            Require(f.Combat.Player.Deck.Cards.Contains(seen[0].DeckVersion!), "Selected card did not return to the deck.");
            rewards[1].OnSkipped();
            Require(!f.Combat.Player.Deck.Cards.Contains(seen[1].DeckVersion!), "Skipped card returned to the deck.");
            var reloadedReward = Reward.FromSerializable(rewards[2].ToSerializable(), f.Combat.Player);
            await reloadedReward.SelectUnsynchronized();
            Require(f.Combat.Player.Deck.Cards.Any(c => c.Id == seen[2].Id), "Reloaded native reward lost its card.");
        }
        Require(stolenSequences[0] == stolenSequences[1], "Fixed-seed theft sequence changed after restart.");
        GD.Print("PASS Dark Strike repeated theft, candidate tiers, seeded restart and individual native reward take/skip/reload.");

        using (var f = new DarkStrikeFixture(multiplayer: true))
        {
            CardModel imbued = Stealable(f, ModelDb.Card<PlaceholderBlueDefense01>(), upgrade: true);
            CardCmd.Enchant<Imbued>(imbued.DeckVersion!, 1);
            CardCmd.Enchant<Imbued>(imbued, 1);
            CardModel basic = Stealable(f, ModelDb.Card<StrikeIronclad>());
            CardModel theirs = Stealable(f, ModelDb.Card<PalmThrustRedesignV1>(), owner: f.Other!);
            await DarkStrike(f.Monster, f.Target, f.Other!.Creature);
            var powers = f.Monster.Creature.Powers.OfType<SwipePower>().ToArray();
            Require(powers.Length == 2 && powers[0].StolenCard == basic && powers[1].StolenCard == theirs
                && powers[0].Target == f.Target && powers[1].Target == f.Other.Creature,
                "Sequential multiplayer impacts must steal once from each damaged owner; Imbued comes last.");
            await DarkStrike(f.Monster, f.Target);
            await CreatureCmd.Kill(f.Monster.Creature, force: true);
            Require(f.Room.ExtraRewards[f.Combat.Player].Count == 2 && f.Room.ExtraRewards[f.Other].Count == 1,
                "Stolen card rewards crossed player ownership.");
            var reward = f.Room.ExtraRewards[f.Combat.Player].Last().ToSerializable();
            CardModel restored = CardModel.FromSerializable(reward.SpecialCard!);
            Require(restored.IsUpgraded && restored.Enchantment is Imbued,
                "Native reward serialization lost the card upgrade or enchantment.");
        }
        GD.Print("PASS Dark Strike Imbued priority, upgraded/enchantment reward data and two-player ownership.");

        using (var f = new DarkStrikeFixture(multiplayer: true))
        {
            CardModel card = Stealable(f, ModelDb.Card<PlaceholderBlueDefense01>());
            f.Target.SetCurrentHpInternal(1);
            await DarkStrike(f.Monster, f.Target);
            Require(f.Target.IsDead && f.Monster.Creature.Powers.OfType<SwipePower>().Single().StolenCard == card,
                "A lethal hit must still steal once from its damaged owner.");
            await CreatureCmd.Kill(f.Monster.Creature, force: true);
            var reward = f.Room.ExtraRewards[f.Combat.Player].OfType<SpecialCardReward>().Single();
            await f.Combat.Player.ReviveBeforeCombatEnd();
            await reward.SelectUnsynchronized();
            Require(f.Combat.Player.Deck.Cards.Contains(card.DeckVersion!),
                "A downed player's stolen card did not return to that player.");
        }
        GD.Print("PASS lethal Dark Strike theft and native returned-card ownership for a downed player.");

        // Vanilla steals before Thorns. Dark Strike needs a damage receipt first;
        // both must leave the stolen card as optional loot after lethal retaliation.
        foreach (bool vanilla in new[] { true, false })
        {
            using var f = new DarkStrikeFixture();
            CardModel card = Stealable(f, ModelDb.Card<PlaceholderBlueDefense01>());
            Creature attacker = f.Monster.Creature;
            MonsterModel monster = f.Monster;
            if (vanilla)
            {
                monster = ModelDb.Monster<ThievingHopper>().ToMutable();
                monster.RunRng = f.Run.Rng;
                attacker = new Creature(monster, CombatSide.Enemy, null);
                AccessTools.Method(f.Combat.State.GetType(), "AttachCreature").Invoke(f.Combat.State, [attacker]);
                f.Combat.State.AddCreature(attacker);
                attacker.SetMaxHpInternal(100);
            }
            attacker.SetCurrentHpInternal(1);
            await PowerCmd.Apply<ThornsPower>(Choice, f.Target, 999, f.Target, null);
            if (vanilla)
                await (Task)AccessTools.Method(typeof(ThievingHopper), "ThieveryMove").Invoke(monster, [new[] { f.Target }])!;
            else await DarkStrike(f.Monster, f.Target);
            Require(attacker.IsDead && f.Target.CurrentHp < 500, "Lethal Thorns did not finish the current attack.");
            Require(f.Room.ExtraRewards[f.Combat.Player].OfType<SpecialCardReward>().Count() == 1
                && !f.Combat.Player.Deck.Cards.Contains(card.DeckVersion!),
                $"{(vanilla ? "Thieving Hopper" : "Dark Strike")} lost or auto-collected the retaliation reward.");
        }
        GD.Print("PASS actual Thieving Hopper and Dark Strike lethal-Thorns native loot parity.");

        using (var f = new DarkStrikeFixture())
        {
            var cards = new List<CardModel>();
            for (int index = 0; index < 4; index++)
                cards.Add(Stealable(f, ModelDb.Card<PlaceholderBlueDefense01>(), upgrade: index % 2 == 0));
            for (int index = 0; index < 3; index++) await DarkStrike(f.Monster, f.Target);
            Require(f.Monster.Creature.Powers.OfType<SwipePower>().Count() == 3, "Earlier theft receipts were not independent.");
            await PowerCmd.Apply<ThornsPower>(Choice, f.Target, 999, f.Target, null);
            f.Monster.Creature.SetCurrentHpInternal(1);
            await DarkStrike(f.Monster, f.Target);
            var rewards = f.Room.ExtraRewards[f.Combat.Player].OfType<SpecialCardReward>().ToArray();
            Require(rewards.Length == 4, "Lethal retaliation must return the three earlier cards AND the final stolen card.");
            foreach (SpecialCardReward reward in rewards) await reward.SelectUnsynchronized();
            Require(cards.All(card => f.Combat.Player.Deck.Cards.Count(deck => ReferenceEquals(deck, card.DeckVersion)) == 1),
                "Collecting all four native rewards did not restore each exact permanent card once.");
        }
        GD.Print("PASS three earlier thefts plus final lethal-Thorns theft: all FOUR cards reclaimed exactly once.");
    }
}
