using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyOrbSlotSemantics()
    {
        MegaCrit.Sts2.Core.Context.LocalContext.NetId = 1;
        CharacterModel[] characters = [ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(),
            ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>(), ModelDb.Character<Defect>()];
        foreach (CharacterModel character in characters)
        {
            using var combat = new OrbCombat(character: character);
            int slots = Math.Max(1, character.BaseOrbSlotCount);
            await AddStock(combat.Player, 2);
            Require(combat.Capacity == slots && combat.Queue.Orbs.Single() is ShurikenOrb,
                $"{character.Id}: stock must use a normal slot.");
            await AddStock(combat.Player, 3);
            Require(combat.Stock == 5 && combat.Queue.Orbs.Count == 1, "Stock must merge, not channel duplicate orbs.");
            int hp = combat.Enemy.CurrentHp;
            await OrbCmd.EvokeNext(Choice, combat.Player);
            Require(hp - combat.Enemy.CurrentHp == 30 && combat.Stock == 0 && combat.Capacity == slots,
                $"{character.Id}: native evoke must fire five shots and preserve normal capacity.");
            await OrbCmd.AddSlots(combat.Player, 2);
            await OrbCmd.Channel<LightningOrb>(Choice, combat.Player);
            await AddStock(combat.Player, 2);
            await OrbCmd.Channel<FrostOrb>(Choice, combat.Player);
            Require(combat.Queue.Orbs.Select(o => o.GetType()).SequenceEqual([typeof(LightningOrb), typeof(ShurikenOrb), typeof(FrostOrb)]),
                $"{character.Id}: Shuriken must follow normal insertion order, not remain at the back.");
            OrbCmd.RemoveSlots(combat.Player, combat.Capacity - 2);
            Require(combat.Queue.Orbs.Count == 2 && combat.Stock == 2, "Shrinking must remove the native last occupied slot.");
            hp = combat.Enemy.CurrentHp;
            OrbCmd.RemoveSlots(combat.Player, 1);
            Require(combat.Stock == 0 && combat.Enemy.CurrentHp == hp, "Slot loss must remove Shuriken without evoking it.");
            OrbCmd.RemoveSlots(combat.Player, 1);
            await AddStock(combat.Player, 1);
            Require(combat.Capacity == (character is Defect ? 0 : 1)
                && combat.Stock == (character is Defect ? 0 : 1), "Zero-slot channeling must use the host's non-Defect rule.");
        }
        GD.Print("PASS all five vanilla characters: native stock placement, merge, depletion, slot loss and zero-slot channeling");

        using (var combat = new OrbCombat(ninjaSlayer: true))
        {
            await AddStock(combat.Player, 3);
            await OrbCmd.Channel<LightningOrb>(Choice, combat.Player);
            Require(combat.Capacity == 1 && combat.Stock == 3 && combat.Enemy.CurrentHp == 1000
                && combat.Queue.Orbs[0] is LightningOrb, "Ordinary channeling must allocate its own slot without evoking dedicated stock.");
            await OrbCmd.Channel<FrostOrb>(Choice, combat.Player);
            Require(combat.Stock == 3 && combat.Enemy.CurrentHp == 992 && combat.Queue.Orbs[0] is FrostOrb,
                "Full normal slots must replace the normal orb and leave stock untouched.");
            await OrbCmd.AddSlots(combat.Player, 20);
            Require(combat.Capacity == 10, "Dedicated stock must not consume the ten-slot normal limit.");
            for (int i = 0; i < 9; i++) await OrbCmd.Channel<LightningOrb>(Choice, combat.Player);
            Require(combat.Queue.Orbs.Count == 11 && combat.Queue.Orbs.Last() is ShurikenOrb, "Ten normal orbs and dedicated stock must coexist.");
            await OrbCmd.EvokeLast(Choice, combat.Player);
            Require(combat.Stock == 3 && combat.Queue.Orbs.Count == 10, "Back evocation must also prioritize normal orbs.");
            OrbCmd.RemoveSlots(combat.Player, 20);
            Require(combat.Capacity == 0 && combat.Queue.Orbs.Single() is ShurikenOrb && combat.Stock == 3,
                "Removing all normal slots must preserve dedicated stock.");
            var darkness = ModelDb.Potion<EssenceOfDarkness>().ToMutable();
            darkness.Owner = combat.Player;
            await (Task)HarmonyLib.AccessTools.Method(typeof(EssenceOfDarkness), "OnUse").Invoke(darkness, [Choice, combat.Player.Creature])!;
            Require(combat.Capacity == 0 && combat.Queue.Orbs.Count == 1, "Slot-count-based effects must see zero normal slots.");
            await OrbCmd.AddSlots(combat.Player, 2);
            await (Task)HarmonyLib.AccessTools.Method(typeof(EssenceOfDarkness), "OnUse").Invoke(darkness, [Choice, combat.Player.Creature])!;
            Require(combat.Queue.Orbs.OfType<DarkOrb>().Count() == 2 && combat.Stock == 3,
                "Slot-count-based effects must generate exactly the normal capacity.");
        }
        GD.Print("PASS dedicated slot capacity, overflow, ten normal slots, back evoke and native capacity-based potion");

        foreach (bool ninja in new[] { false, true })
        foreach (int repeat in new[] { 2, 4 })
        {
            using var combat = new OrbCombat(ninjaSlayer: ninja);
            await AddStock(combat.Player, 3);
            int hp = combat.Enemy.CurrentHp, events = _evoked;
            CardModel card = repeat == 2 ? AddCard<Dualcast>(combat) : AddCard<Quadcast>(combat);
            await CardCmd.AutoPlay(Choice, card, null);
            Require(hp - combat.Enemy.CurrentHp == 3 * repeat * 6 && combat.Stock == 0 && _evoked - events == repeat,
                "Native multi-evoke cards must fire all stock on every invocation, then remove once.");
        }
        foreach (int energy in new[] { 0, 1, 3 })
        foreach (bool upgrade in new[] { false, true })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            await AddStock(combat.Player, 3);
            await OrbCmd.AddSlots(combat.Player, 2); // Empty normal slots must not block the fallback.
            var card = AddCard<MultiCast>(combat);
            if (upgrade) card.UpgradeInternal();
            await PlayerCmd.SetEnergy(energy, combat.Player);
            int events = _evoked, hp = combat.Enemy.CurrentHp, count = energy + (upgrade ? 1 : 0);
            await CardCmd.AutoPlay(Choice, card, null);
            Require(hp - combat.Enemy.CurrentHp == count * 18 && _evoked - events == count
                && combat.Stock == (count == 0 ? 3 : 0) && combat.Capacity == 2,
                $"Native Multi-Cast X={energy}, upgrade={upgrade}: wrong shots, stock, events or normal capacity.");
        }
        using (var combat = new OrbCombat(ninjaSlayer: true))
        {
            await AddStock(combat.Player, 3);
            await OrbCmd.Channel<LightningOrb>(Choice, combat.Player);
            int hp = combat.Enemy.CurrentHp;
            await CardCmd.AutoPlay(Choice, AddCard<Dualcast>(combat), null);
            Require(hp - combat.Enemy.CurrentHp == 16 && combat.Stock == 3 && combat.Queue.Orbs.Count == 1,
                "Dualcast must repeat only the normal front orb, not spill over into dedicated stock.");
        }
        GD.Print("PASS real Dualcast, Quadcast, Multi-Cast X=0/1/3 and upgrades, normal-orb priority and empty-slot fallback");

        foreach (bool ninja in new[] { false, true })
        {
            using var combat = new OrbCombat(ninjaSlayer: ninja);
            await OrbCmd.AddSlots(combat.Player, 3);
            await OrbCmd.Channel<LightningOrb>(Choice, combat.Player);
            await AddStock(combat.Player, 3);
            await OrbCmd.Channel<FrostOrb>(Choice, combat.Player);
            await PowerCmd.Apply<FocusPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            int events = _evoked, hp = combat.Enemy.CurrentHp;
            await CardCmd.AutoPlay(Choice, AddCard<Shatter>(combat), null);
            Require(hp - combat.Enemy.CurrentHp == 7 + 20 + 48 && combat.Player.Creature.Block == 14
                && combat.Stock == 0 && combat.Queue.Orbs.Count == 0 && combat.Capacity == 3
                && _evoked - events == 6 && combat.Tokens == 0,
                "Native Shatter must evoke every orb twice, including every stock with Focus and Starless, and preserve slots.");
        }
        GD.Print("PASS real Shatter mixed-orb order, full-stock double evoke, Focus, tokens, native hooks and cleanup");
    }
}
