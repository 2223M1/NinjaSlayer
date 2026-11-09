using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyRekindle()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            combat.AddEnemy();
            var selected = AddCard<DefendIronclad>(combat);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [selected]));
            await CardCmd.AutoPlay(Choice, AddCard<Rekindle>(combat, upgraded: upgraded), null);
            int reward = upgraded ? 3 : 2;
            Require(owner.GetPowerAmount<RekindlePower>() == reward
                && PileType.Hand.GetPile(combat.Player).Cards.OfType<BlackFlame>().Count() == 1
                && !owner.HasPower<NarakuLifePower>(),
                "Rekindle must transform the selected card and grant a two/three-point trigger effect without immediate life.");
            await CardCmd.AutoPlay(Choice, AddCard<DefendIronclad>(combat), null);
            Require(!owner.HasPower<NarakuLifePower>(), "Ordinary Skills must not trigger Rekindle.");
            await CardCmd.AutoPlay(Choice, AddCard<BS1260Kick>(combat), combat.Enemy);
            Require(owner.GetPowerAmount<NarakuLifePower>() == reward,
                "Only the pre-existing flame may reward life: not each hit, each enemy, or the newly generated flame.");
            await CardCmd.AutoPlay(Choice, AddCard<TwinStrike>(combat), combat.Enemy);
            Require(owner.GetPowerAmount<NarakuLifePower>() == 3 * reward,
                "The next multi-hit attack must trigger both held flames once each, regardless of enemy count.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            int reward = upgraded ? 3 : 2;
            await PowerCmd.Apply<RekindlePower>(Choice, owner, reward, owner, null);
            await PowerCmd.Apply<NarakuFormPower>(Choice, owner, 2, owner, null);
            combat.AddEnemy();
            await CardCmd.AutoPlay(Choice, AddCard<TwinStrike>(combat), combat.Enemy);
            Require(owner.GetPowerAmount<NarakuLifePower>() == 2 * reward,
                "Each Naraku Form flame must reward life once, even without a Black Flame card in hand.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            int reward = upgraded ? 3 : 2;
            owner.SetCurrentHpInternal(10);
            await PowerCmd.Apply<RekindlePower>(Choice, owner, reward, owner, null);
            for (int i = 0; i < 2; i++) await ResolveBurnTurnEnd(AddCard<BlackFlame>(combat));
            Require(owner.CurrentHp == 10 - 2 * (4 - reward)
                && owner.GetPowerAmount<NarakuLifePower>() == 0
                && combat.Enemy.CurrentHp == 992
                && PileType.Exhaust.GetPile(combat.Player).Cards.OfType<BlackFlame>().Count() == 2,
                "Each turn-end flame must grant life before its self-damage, without a second reward for hitting the owner.");
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [owner]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [owner]);
#endif
            AddCard<BlackFlame>(combat);
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(!owner.HasPower<RekindlePower>() && !owner.HasPower<NarakuLifePower>(),
                "Black Flame must stop granting Rekindle life after the owner's turn ends.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            var other = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
            combat.State.AddPlayer(other);
            other.ResetCombatState();
            await PowerCmd.Apply<RekindlePower>(Choice, owner, 2, owner, null);
            await PowerCmd.Apply<RekindlePower>(Choice, other.Creature, 7, other.Creature, null);
            var flame = combat.State.CreateCard<BlackFlame>(other);
            PileType.Hand.GetPile(other).AddInternal(flame, -1, silent: true);
            var attack = combat.State.CreateCard<StrikeIronclad>(other);
            PileType.Hand.GetPile(other).AddInternal(attack, -1, silent: true);
            await CardCmd.AutoPlay(Choice, attack, combat.Enemy);
            Require(!owner.HasPower<NarakuLifePower>() && other.Creature.GetPowerAmount<NarakuLifePower>() == 7,
                "A teammate's Black Flame must reward only its owner using that owner's Rekindle amount.");
        }
        GD.Print("PASS Rekindle: selected transformation, 2/3 life per flame, generated-card exclusion, AOE/multihit counting, Naraku Form, turn-end protection/expiry and owner isolation.");
    }
}
