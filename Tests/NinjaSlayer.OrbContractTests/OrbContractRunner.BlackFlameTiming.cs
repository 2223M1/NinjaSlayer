using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly List<string> BurnSounds = [];
    private static CardModel? _turnEndSoundCard;

    private static void RecordBlackFlameSound(string sfx)
    {
        if (sfx != "event:/sfx/characters/attack_fire") return;
        BurnSounds.Add(sfx);
        if (_turnEndSoundCard != null)
            Require(_turnEndSoundCard.Pile?.Type == PileType.Play,
                "Burn audio must occur during native turn-end card resolution, before discard/exhaust.");
    }

    private static async Task ResolveBurnTurnEnd(CardModel card)
    {
        _turnEndSoundCard = card;
        try
        {
#if NINJASLAYER_CHANNEL_STABLE
            await card.OnTurnEndInHandWrapper(Choice);
#else
            var enterPlay = (Task)AccessTools.Method(typeof(CombatManager), "AddTurnEndCardToPlayPileWithDelay")
                .Invoke(CombatManager.Instance, [card, 0f])!;
            await (Task)AccessTools.Method(typeof(CombatManager), "ResolveTurnEndCardEffects")
                .Invoke(CombatManager.Instance, [card, Choice, enterPlay])!;
#endif
        }
        finally { _turnEndSoundCard = null; }
    }

    private static async Task VerifyBlackFlameTiming()
    {
        foreach (bool upgraded in new[] { false, true })
        foreach (int held in new[] { 0, 1, 2 })
        {
            using var combat = new OrbCombat();
            for (int i = 0; i < held; i++) AddCard<BlackFlameRedesignV1>(combat);
            await PowerCmd.Apply<BurnBurnBurnPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
            BurnSounds.Clear();
            await CardCmd.AutoPlay(Choice, AddCard<SatsubatsuRedesignV1>(combat, upgraded: upgraded), combat.Enemy);
            int burnDamage = held * 7;
            Require(combat.Enemy.CurrentHp == 1000 - (upgraded ? 33 : 27) - burnDamage
                && BurnSounds.Count == held,
                "BS1260 must burn only pre-existing held flames, with one sound and one amplification per flame.");
            CombatManager.Instance.History.Clear();
            BurnSounds.Clear();
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            var hits = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(e => e.CardSource is BlackFlameRedesignV1).ToArray();
            Require(hits.Length == held + 1 && hits.All(hit => hit.Result.TotalDamage == 7) && BurnSounds.Count == held + 1,
                "The next attack must include the newly generated flame and play one native burn sound per flame.");
        }
        using (var combat = new OrbCombat())
        {
            AddCard<SatsubatsuRedesignV1>(combat, PileType.Draw);
            BurnSounds.Clear();
            await CardCmd.AutoPlay(Choice, AddCard<WasshoiRedesignV1>(combat, upgraded: true), null);
            var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(e => e.CardSource is BlackFlameRedesignV1).Select(e => e.Result.TotalDamage).ToArray();
            Require(burns.SequenceEqual(new[] { 4, 4, 4 }) && BurnSounds.Count == 3,
                "Repeated BS1260 plays must capture a new hand each time: no burn, then one flame, then two separate flames.");
        }
        using (var combat = new OrbCombat())
        {
            BurnSounds.Clear();
            var burn = AddCard<Burn>(combat);
            await ResolveBurnTurnEnd(burn);
            Require(BurnSounds.SequenceEqual(new[] { "event:/sfx/characters/attack_fire" })
                && burn.Pile?.Type == PileType.Discard, "Vanilla Burn must establish the native sound and lifecycle oracle.");
        }
        foreach (bool immune in new[] { false, true })
        foreach (int naraku in new[] { 0, 6 })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            if (immune) await PowerCmd.Apply<OneBodyOneSoulPower>(Choice, owner, 1, owner, null);
            if (naraku > 0) await PowerCmd.Apply<NarakuLifePower>(Choice, owner, naraku, owner, null);
            await CreatureCmd.GainBlock(owner, 99, ValueProp.Unpowered, null);
            await PowerCmd.Apply<BurnBurnBurnPower>(Choice, owner, 3, owner, null);
            int hp = owner.CurrentHp;
            BurnSounds.Clear();
            for (int i = 0; i < 2; i++) await ResolveBurnTurnEnd(AddCard<BlackFlameRedesignV1>(combat));
            Require(BurnSounds.Count == 2 && combat.Enemy.CurrentHp == 986
                && owner.CurrentHp == hp - (8 - naraku)
                && owner.Block == 99 && owner.GetPowerAmount<NarakuLifePower>() == 0
                && PileType.Exhaust.GetPile(combat.Player).Cards.Count == 2,
                "Native turn-end must burn/exhaust each flame once, sound once each, bypass Block and preserve Naraku protection without One Body immunity.");
        }
        using (var combat = new OrbCombat())
        {
            var first = AddCard<BlackFlameRedesignV1>(combat);
            var removed = AddCard<BlackFlameRedesignV1>(combat);
            combat.Player.Creature.GetPower<EvokeObserver>()!.AfterFlameDamage = () =>
                CardPileCmd.Add(removed, PileType.Discard);
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(entry => entry.CardSource is BlackFlameRedesignV1).ToArray();
            Require(burns.Length == 1 && burns[0].CardSource == first && removed.Pile?.Type == PileType.Discard,
                "A snapshotted flame moved out of hand during the first burn must not trigger later.");
        }
        using (var combat = new OrbCombat())
        {
            AddCard<BlackFlameRedesignV1>(combat);
            AddCard<BlackFlameRedesignV1>(combat);
            combat.Player.Creature.GetPower<EvokeObserver>()!.AfterFlameDamage = async () =>
                await PowerCmd.Apply<BurnBurnBurnPower>(Choice, combat.Player.Creature, 6, combat.Player.Creature, null);
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(entry => entry.CardSource is BlackFlameRedesignV1).ToArray();
            Require(burns.Select(entry => (decimal)entry.Result.TotalDamage).SequenceEqual(new decimal[] { 4, 10 }),
                "Each flame reads its own amplification after earlier damage callbacks finish.");
        }
        await VerifyBlackFlameTurnEnd();
        GD.Print("PASS Black Flame timing: generated-card exclusion, subsequent/repeated attacks, native Burn audio and turn-end lifecycle/protection.");
    }
}
