using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Enchantments;
using NinjaSlayer.Events;
using NinjaSlayer.Relics;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static void VerifyEventAppearanceRates()
    {
        foreach (int act in new[] { 0, 2 })
        {
            using var fixture = new SawatariFixture();
            RunState run = fixture.Run;
            run.CurrentActIndex = act;
            run.Act.GenerateRooms(run.Rng.UpFront, UnlockState.all);
            SawatariActRoomGenerationPatch.Postfix(run.Act);
            var rooms = (RoomSet)AccessTools.Field(typeof(ActModel), "_rooms").GetValue(run.Act)!;
            var full = new HashSet<RoomType> { RoomType.Monster, RoomType.Elite, RoomType.Treasure, RoomType.Shop, RoomType.Event };
            float Chance(IReadOnlySet<RoomType> allowed)
            {
                var odds = run.Odds.UnknownMapPoint;
                SawatariUnknownRoomRollPatch.Prefix(odds, run, out var roll);
                try
                {
                    AccessTools.Method(typeof(SawatariUnknownRoomRollPatch), "CaptureAllowedRooms")
                        .Invoke(null, [run, allowed]);
                    return roll.Chance;
                }
                finally { SawatariUnknownRoomRollPatch.Finalizer(null, odds, roll); }
            }

            Require(!rooms.events.Any(e => e is TheMovingJungleEvent), "Sawatari entered the ordinary event queue.");
            if (run.Act.PullNextEncounter(RoomType.Monster).IsWeak)
                Require(Chance(full) == 0f, "Sawatari replaced a weak opening encounter.");
            while (run.Act.PullNextEncounter(RoomType.Monster).IsWeak)
                run.Act.MarkRoomVisited(RoomType.Monster);
            EventModel[] available = rooms.events.Where(e => e.IsAllowed(run)).ToArray();
            EventModel shared = available.First(e => ModelDb.AllSharedEvents.Contains(e));
            int eventCount = available.Length + 1;
            Require(Math.Abs(Chance(full) * 0.1f - 0.85f / eventCount) < 0.00001f,
                "Sawatari's ordinary-event share must include eligible shared events in the generated pool.");
            Require(Math.Abs(Chance(new HashSet<RoomType> { RoomType.Monster, RoomType.Event }) * 0.1f
                    - 0.9f / eventCount) < 0.00001f,
                "Blacklisted treasure/shop odds must remain in the host's event probability.");

            rooms.events.Remove(shared);
            Require(Math.Abs(Chance(full) * 0.1f - 0.85f / (eventCount - 1)) < 0.00001f,
                "Events absent from the generated/save-loaded pool must not dilute Sawatari's chance.");
            run.AddVisitedEvent(available.First(e => e != shared));
            Require(Math.Abs(Chance(full) * 0.1f - 0.85f / (eventCount - 2)) < 0.00001f,
                "Visited events must not dilute Sawatari's chance.");
            Require(Chance(new HashSet<RoomType> { RoomType.Event }) == 0f
                    && Chance(new HashSet<RoomType> { RoomType.Monster }) == 0f,
                "Sawatari must respect restrictions on event and monster room types.");
            run.AddVisitedEvent(ModelDb.Event<TheMovingJungleEvent>());
            Require(Chance(full) == 0f, "Sawatari repeated after a previous visit.");
        }
        GD.Print("PASS Sawatari rate uses the generated full event pool, shared/unlocked/visited eligibility and host room restrictions.");
    }

    private static async Task VerifySushiEnchantment()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        RunState run = RunState.CreateForTest([combat.Player]);
        var player = combat.Player;
        var creature = player.Creature;
        creature.SetCurrentHpInternal(creature.MaxHp - 10);
        var cards = new List<CardModel>();
        for (int i = 0; i < 5; i++)
        {
            CardModel card = run.CreateCard<DefendIronclad>(player);
            player.Deck.AddInternal(card, -1, silent: true);
            cards.Add(card);
        }
        CardCmd.Enchant<Swift>(cards[4], 1);
        using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
        {
            Require(!options.Contains(cards[4]), "Sushi offered an already-enchanted card.");
            return cards.Take(3);
        }));
        RelicModel sushi = ModelDb.Relic<MaguroSushiRelic>().ToMutable();
        player.AddRelicInternal(sushi);
        int hp = creature.CurrentHp;
        int maximum = creature.MaxHp;
        await sushi.AfterObtained();
        Require(cards.Take(3).All(c => c.Enchantment is NourishingEnchantment { Amount: 1 })
                && cards[3].Enchantment is null && cards[4].Enchantment is Swift
                && creature.CurrentHp == hp && creature.MaxHp == maximum,
            "Sushi must enchant exactly three chosen cards without healing on pickup.");

        foreach (CardModel deckCard in cards.Take(3))
        {
            CardModel card = combat.State.CloneCard(deckCard);
            await CardPileCmd.Add(card, PileType.Hand);
            await CardCmd.AutoPlay(Choice, card, null);
            maximum++;
            hp++;
            Require(creature.MaxHp == maximum && creature.CurrentHp == hp
                    && card.Enchantment!.Status == EnchantmentStatus.Disabled,
                "Each enchanted card's first play must grant one max HP with native healing.");
            await CardPileCmd.Add(card, PileType.Hand);
            await CardCmd.AutoPlay(Choice, card, null);
            Require(creature.MaxHp == maximum && creature.CurrentHp == hp,
                "Replaying the same card granted max HP twice.");
            Require(deckCard.Enchantment!.Status == EnchantmentStatus.Normal,
                "Combat use consumed the permanent deck enchantment.");
        }
        CardModel restored = CardModel.FromSerializable(cards[0].ToSerializable());
        Require(restored.Enchantment is NourishingEnchantment { Amount: 1, Status: EnchantmentStatus.Normal },
            "The permanent enchantment did not survive native deck serialization.");
        CardModel nextCombatCopy = combat.State.CloneCard(cards[0]);
        await CardPileCmd.Add(nextCombatCopy, PileType.Hand);
        await CardCmd.AutoPlay(Choice, nextCombatCopy, null);
        Require(creature.MaxHp == maximum + 1, "A fresh combat copy did not regain its first-play effect.");
        GD.Print("PASS Sushi native selection, three independent first-play max HP gains, no repeat, deck persistence and fresh combat copy.");
    }
}
