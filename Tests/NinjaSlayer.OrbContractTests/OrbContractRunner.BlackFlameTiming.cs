using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly List<string> BurnSounds = [];
    private static CardModel? _turnEndSoundCard;
    private static readonly List<string> FlameFinishes = [];

    private static void ObserveFlameForecast(Creature owner, object descriptor)
    {
        Type forecast = typeof(BlackFlame).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherForecast", true)!;
        FlameFinishes.Add(AccessTools.Method(forecast, "EvaluateAction").Invoke(null,
            [owner, owner.CombatState!.HittableEnemies, descriptor, null])!.ToString()!);
    }

    private static async Task VerifyFlameFinisher()
    {
        var observer = new Harmony("NinjaSlayer.Contracts.FlameFinisher");
        var eligibility = typeof(BlackFlame).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherEligibilityService", true)!;
        observer.Patch(AccessTools.Method(eligibility, "CreateActionSession"),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ObserveFlameForecast)));
        try
        {
            foreach (int count in new[] { 1, 2 })
            foreach (int form in new[] { 0, 1, 2 })
            foreach (int spareHp in new[] { 0, 1 })
            {
                using var combat = new OrbCombat(ninjaSlayer: true);
                combat.CreateRun("flame-karate-finisher");
                Creature[] enemies = count == 1 ? [combat.Enemy] : [combat.Enemy, combat.AddEnemy()];
                var owner = combat.Player.Creature;
                foreach (Creature enemy in enemies) enemy.SetCurrentHpInternal(8 + spareHp);
                // Strike + Karate leave the first enemy at the same HP as the untouched second enemy.
                combat.Enemy.SetCurrentHpInternal(17 + spareHp);
                await PowerCmd.Apply<KaratePower>(Choice, owner, 3, owner, null);
                if (form > 0) await PowerCmd.Apply<NarakuFormPower>(Choice, owner, form, owner, null);
                for (int i = form; i < 2; i++) AddCard<BlackFlame>(combat);
                FlameFinishes.Clear();
                await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
                Require(enemies.All(e => e.CurrentHp == spareHp) && owner.GetPowerAmount<KaratePower>() == 2,
                    "Flame/karate finishing changed damage or spent Karate twice.");
                Require(FlameFinishes.SequenceEqual(new[] { "NotGuaranteed", spareHp == 0 ? "Guaranteed" : "NotGuaranteed" }),
                    $"Wrong clear forecast: enemies={count}, form={form}, spare HP={spareHp}: [{string.Join(", ", FlameFinishes)}].");
            }
        }
        finally { observer.UnpatchAll(observer.Id); FlameFinishes.Clear(); }
        GD.Print("PASS Black Flame finisher: real attack + Karate + two flames, hand/form, single/group clear and one-HP survivors.");
    }

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
            var enterPlay = (Task)AccessTools.Method(typeof(CombatManager), "AddTurnEndCardToPlayPileWithDelay")
                .Invoke(CombatManager.Instance, [card, 0f])!;
            await (Task)AccessTools.Method(typeof(CombatManager), "ResolveTurnEndCardEffects")
                .Invoke(CombatManager.Instance, [card, Choice, enterPlay])!;
        }
        finally { _turnEndSoundCard = null; }
    }

    private static async Task VerifyBlackFlameTiming()
    {
        await VerifyFlameFinisher();
        foreach (bool upgraded in new[] { false, true })
        foreach (int held in new[] { 0, 1, 2 })
        {
            using var combat = new OrbCombat();
            for (int i = 0; i < held; i++) AddCard<BlackFlame>(combat);
            await PowerCmd.Apply<BlackFlameInfernoPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
            BurnSounds.Clear();
            await CardCmd.AutoPlay(Choice, AddCard<BS1260Kick>(combat, upgraded: upgraded), combat.Enemy);
            int burnDamage = held * 7;
            Require(combat.Enemy.CurrentHp == 1000 - (upgraded ? 26 : 20) - burnDamage
                && BurnSounds.Count == held,
                "BS1260 must burn only pre-existing held flames, with one sound and one amplification per flame.");
            CombatManager.Instance.History.Clear();
            BurnSounds.Clear();
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            var hits = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(e => e.CardSource is BlackFlame).ToArray();
            Require(hits.Length == held + 1 && hits.All(hit => hit.Result.TotalDamage == 7) && BurnSounds.Count == held + 1,
                "The next attack must include the newly generated flame and play one native burn sound per flame.");
        }
        using (var combat = new OrbCombat())
        {
            AddCard<BS1260Kick>(combat, PileType.Draw);
            BurnSounds.Clear();
            await CardCmd.AutoPlay(Choice, AddCard<NavyHammer>(combat, upgraded: true), null);
            var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(e => e.CardSource is BlackFlame).Select(e => e.Result.TotalDamage).ToArray();
            Require(burns.SequenceEqual(new[] { 4 }) && BurnSounds.Count == 1,
                "Hammer's two BS1260 plays must capture a new hand each time: no burn, then one flame.");
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
            if (immune) await PowerCmd.Apply<OneMindOneBodyPower>(Choice, owner, 1, owner, null);
            if (naraku > 0) await PowerCmd.Apply<NarakuLifePower>(Choice, owner, naraku, owner, null);
            await CreatureCmd.GainBlock(owner, 99, ValueProp.Unpowered, null);
            await PowerCmd.Apply<BlackFlameInfernoPower>(Choice, owner, 3, owner, null);
            int hp = owner.CurrentHp;
            BurnSounds.Clear();
            for (int i = 0; i < 2; i++) await ResolveBurnTurnEnd(AddCard<BlackFlame>(combat));
            Require(BurnSounds.Count == 2 && combat.Enemy.CurrentHp == 986
                && owner.CurrentHp == hp - (8 - naraku)
                && owner.Block == 99 && owner.GetPowerAmount<NarakuLifePower>() == 0
                && PileType.Exhaust.GetPile(combat.Player).Cards.Count == 2,
                "Native turn-end must burn/exhaust each flame once, sound once each, bypass Block and preserve Naraku protection without One Body immunity.");
        }
        using (var combat = new OrbCombat())
        {
            var first = AddCard<BlackFlame>(combat);
            var removed = AddCard<BlackFlame>(combat);
            combat.Player.Creature.GetPower<EvokeObserver>()!.AfterFlameDamage = () =>
                CardPileCmd.Add(removed, PileType.Discard);
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(entry => entry.CardSource is BlackFlame).ToArray();
            Require(burns.Length == 1 && burns[0].CardSource == first && removed.Pile?.Type == PileType.Discard,
                "A snapshotted flame moved out of hand during the first burn must not trigger later.");
        }
        using (var combat = new OrbCombat())
        {
            AddCard<BlackFlame>(combat);
            AddCard<BlackFlame>(combat);
            combat.Player.Creature.GetPower<EvokeObserver>()!.AfterFlameDamage = async () =>
                await PowerCmd.Apply<BlackFlameInfernoPower>(Choice, combat.Player.Creature, 6, combat.Player.Creature, null);
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(entry => entry.CardSource is BlackFlame).ToArray();
            Require(burns.Select(entry => (decimal)entry.Result.TotalDamage).SequenceEqual(new decimal[] { 4, 10 }),
                "Each flame reads its own amplification after earlier damage callbacks finish.");
        }
        await VerifyBlackFlameTurnEnd();
        GD.Print("PASS Black Flame timing: generated-card exclusion, subsequent/repeated attacks, native Burn audio and turn-end lifecycle/protection.");
    }
}
