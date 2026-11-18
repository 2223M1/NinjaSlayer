using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Powers;
#if NINJASLAYER_CHANNEL_STABLE
using AimPower = NinjaSlayer.Powers.BladeSweepPower;
#endif

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyZanshin()
    {
        Type[] buffs =
        [
            typeof(StrengthPower), typeof(DexterityPower), typeof(VigorPower), typeof(KaratePower),
            typeof(FocusPower), typeof(ArtifactPower), typeof(BufferPower), typeof(IntangiblePower),
            typeof(ThornsPower), typeof(PlatingPower), typeof(RegenPower), typeof(EvasionPower),
            typeof(NarakuLifePower), typeof(StretchPower), typeof(SoarPower),
            typeof(AimPower),
        ];
        foreach (bool upgraded in new[] { false, true })
        foreach (Type type in buffs)
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            var hand = PileType.Hand.GetPile(combat.Player);
            for (int i = 0; i < 6; i++) AddCard<DefendIronclad>(combat, PileType.Draw);
            var card = AddCard<Zanshin>(combat, upgraded: upgraded);
            Require(card.EnergyCost.GetWithModifiers(CostModifiers.Local) == 0, "Both versions of Zanshin must cost zero.");
            Require(card.Keywords.Contains(CardKeyword.Innate) == upgraded, "Only upgraded Zanshin should be Innate.");
            await CardCmd.AutoPlay(Choice, card, null);
            Require(hand.Cards.Count == 0, "Zanshin itself is not a counted combat buff.");
            PowerModel canonical = ModelDb.GetById<PowerModel>(ModelDb.GetId(type));
            await PowerCmd.Apply(Choice, canonical.ToMutable(), owner, 2, owner, null);
            Require(hand.Cards.Count == 1, $"Zanshin must draw one on first gaining {type.Name}.");
            await PowerCmd.Apply(Choice, canonical.ToMutable(), owner, 1, owner, null);
            Require(hand.Cards.Count == 1, $"Adding stacks of {type.Name} must not draw again.");
            await PowerCmd.Remove(owner.GetPower(canonical.Id)!);
            await PowerCmd.Apply(Choice, canonical.ToMutable(), owner, 1, owner, null);
            Require(hand.Cards.Count == 2, $"Reacquiring {type.Name} must draw again after it is gone.");
        }

        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<SoarPower>(Choice, owner, 1, owner, null);
            await PowerCmd.Apply<SoarPower>(Choice, owner, 1, owner, null);
            int hits = 2;
            await PowerCmd.Apply<AimPower>(Choice, owner, 3, owner, null);
            await PowerCmd.Apply<AimPower>(Choice, owner, 2, owner, null);
            hits++;
            var fist = AddCard<AntiAirBangBangFist>(combat, upgraded: upgraded);
            Require(((CalculatedVar)fist.DynamicVars["CalculatedHits"]).Calculate(combat.Enemy) == hits,
                "Soar and native Aim each add one Anti-Air hit, regardless of stacks.");
            await CardCmd.AutoPlay(Choice, fist, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - hits * (upgraded ? 11 : 8),
                "Anti-Air must execute the extra hits shown for Soar and native Aim.");
        }

        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            var hand = PileType.Hand.GetPile(combat.Player);
            for (int i = 0; i < 9; i++) AddCard<DefendIronclad>(combat, PileType.Draw);
            await PowerCmd.Apply<KaratePower>(Choice, owner, 1, owner, null);
            await CardCmd.AutoPlay(Choice, AddCard<Zanshin>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<Zanshin>(combat, upgraded: true), null);
            Require(hand.Cards.Count == 0, "Playing Zanshin must not reward pre-existing buffs or its own stacks.");
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            Require(owner.GetPowerAmount<KaratePower>() == 0 && hand.Cards.Count == 0,
                "Spending the last Karate stack must not draw cards.");
            await PowerCmd.Apply<KaratePower>(Choice, owner, 1, owner, null);
            Require(hand.Cards.Count == 2, "Base and upgraded Zanshin together must draw two when spent Karate is regained.");
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            await PowerCmd.Apply<KaratePower>(Choice, owner, 1, owner, null);
            Require(hand.Cards.Count == 4, "Karate can be fully consumed and regained repeatedly in the same combat.");
            for (int i = 0; i < 3; i++) await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            combat.State.RoundNumber++;
            combat.Player.PlayerCombatState!.IncrementTurnNumber();
            Require(owner.GetPower<ZanshinPower>()!.ModifyHandDraw(combat.Player, 5) == 5,
                "Zanshin must no longer change next-turn draw after three attacks.");
        }

        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            var hand = PileType.Hand.GetPile(combat.Player);
            for (int i = 0; i < 8; i++) AddCard<DefendIronclad>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<Zanshin>(combat), null);
            await PowerCmd.Apply<StrengthPower>(Choice, owner, -2, combat.Enemy, null);
            await PowerCmd.Apply<StrengthPower>(Choice, owner, 1, owner, null);
            Require(hand.Cards.Count == 0, "Improving negative Strength without becoming positive is not a new buff.");
            await PowerCmd.Apply<StrengthPower>(Choice, owner, 2, owner, null);
            Require(hand.Cards.Count == 1, "Crossing from negative to positive Strength grants the previously absent buff.");
            await PowerCmd.Apply<WeakPower>(Choice, owner, 1, combat.Enemy, null);
            await PowerCmd.Apply<ComposurePower>(Choice, owner, 2, owner, null);
            await PowerCmd.Apply<DexterityPower>(Choice, combat.Enemy, 1, combat.Enemy, null);
            var other = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
            combat.State.AddPlayer(other);
            other.ResetCombatState();
            await PowerCmd.Apply<DexterityPower>(Choice, other.Creature, 1, other.Creature, null);
            Require(hand.Cards.Count == 1, "Debuffs, continuous abilities and other creatures' buffs must not trigger Zanshin.");
            await PowerCmd.Apply<NoDrawPower>(Choice, owner, 1, owner, null);
            await PowerCmd.Apply<DexterityPower>(Choice, owner, 1, owner, null);
            Require(hand.Cards.Count == 1, "Zanshin draws must obey native No Draw.");
            await PowerCmd.Remove<NoDrawPower>(owner);
            await PowerCmd.Apply<DexterityPower>(Choice, owner, 1, owner, null);
            Require(hand.Cards.Count == 1, "A blocked draw must not defer the buff gain until a later stack.");
            await PowerCmd.Remove<DexterityPower>(owner);
            await PowerCmd.Apply<DexterityPower>(Choice, owner, 1, owner, null);
            Require(hand.Cards.Count == 2, "A freshly regained buff can draw normally after No Draw expires.");
        }
        GD.Print($"PASS Zanshin: {buffs.Length} buffs, base/upgrade, existing stacks, real Karate consumption/reacquisition, stacking, negative Strength, owner isolation and No Draw; Soar/native Aim Anti-Air hits.");
    }
}
