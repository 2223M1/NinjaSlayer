using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyFriendlyHive()
    {
        foreach (bool otherOwner in new[] { false, true })
        foreach (MonsterModel model in new MonsterModel[] { ModelDb.Monster<YamotoKokiMonster>(),
                     ModelDb.Monster<ForestSawatariMonster>(), ModelDb.Monster<YukanoMonster>(),
                     ModelDb.Monster<OrigamiMissileMonster>() })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            Player owner = combat.Player;
            if (otherOwner)
            {
                owner = Player.CreateForNewRun<Necrobinder>(UnlockState.all, 2);
                owner.InitializeSeed("friendly-hive-other");
                combat.State.AddPlayer(owner);
                owner.ResetCombatState();
            }
            var pet = AddHivePet(combat, model, owner);
            await PowerCmd.Apply<PersonalHivePower>(Choice, combat.Enemy, 2, combat.Enemy, null);
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner.Creature, 13, owner.Creature, null);
            int ownerHp = owner.Creature.CurrentHp;
            int localHp = combat.Player.Creature.CurrentHp;
            int cardCount = owner.PlayerCombatState!.AllCards.Count();
            decimal energy = owner.PlayerCombatState.Energy;
            int powers = owner.Creature.Powers.Count;
            await CreatureCmd.Damage(Choice, combat.Enemy, 10, ValueProp.Move, pet);
            Require(combat.Enemy.CurrentHp == 990 && owner.Creature.CurrentHp == ownerHp
                && combat.Player.Creature.CurrentHp == localHp && owner.PlayerCombatState.Energy == energy
                && owner.Creature.GetPowerAmount<NarakuLifePower>() == 13 && owner.Creature.Powers.Count == powers
                && owner.PlayerCombatState.AllCards.Count() == cardCount,
                "A native companion hit must deal damage without giving either player Hive penalties.");
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            Require(combat.Player.PlayerCombatState!.AllCards.OfType<Dazed>().Count() == 2,
                "The player must still be able to play and receive the original Hive penalty after a companion hit.");
            if (otherOwner) owner.PlayerCombatState.AfterCombatEnd();
        }
        using (var combat = new OrbCombat())
        {
            var osty = AddHivePet(combat, ModelDb.Monster<Osty>(), combat.Player);
            await PowerCmd.Apply<PersonalHivePower>(Choice, combat.Enemy, 2, combat.Enemy, null);
            await CreatureCmd.Damage(Choice, combat.Enemy, 10, ValueProp.Move, osty);
            Require(combat.Player.PlayerCombatState!.AllCards.OfType<Dazed>().Count() == 2,
                "Native Osty must retain its owner's Hive penalty.");
        }
        foreach (string retaliation in new[] { "thorns", "reflect", "barrier" })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            var pet = AddHivePet(combat, ModelDb.Monster<YamotoKokiMonster>(), combat.Player);
            if (retaliation == "thorns") await PowerCmd.Apply<ThornsPower>(Choice, combat.Enemy, 4, combat.Enemy, null);
            else if (retaliation == "barrier") await PowerCmd.Apply<FlameBarrierPower>(Choice, combat.Enemy, 4, combat.Enemy, null);
            else
            {
                await PowerCmd.Apply<ReflectPower>(Choice, combat.Enemy, 1, combat.Enemy, null);
                await CreatureCmd.GainBlock(combat.Enemy, 4, ValueProp.Unpowered, null);
            }
            int hp = combat.Player.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice, combat.Enemy, 10, ValueProp.Move, pet);
            Require(pet.CurrentHp == 96 && combat.Player.Creature.CurrentHp == hp,
                "Retaliation must damage the actual companion, not its owner, without blanket immunity.");
        }
        GD.Print("PASS friendly Hive: four mod allies, two owner models, no owner HP/Naraku/energy/power/card penalty, subsequent play, native Osty and attacker retaliation.");
    }

    private static Creature AddHivePet(OrbCombat combat, MonsterModel model, Player owner)
    {
        var pet = new Creature(model.ToMutable(), CombatSide.Player, null)
        {
            CombatState = combat.State,
            PetOwner = owner
        };
        AccessTools.Method(typeof(CombatState), "AttachCreature").Invoke(combat.State, [pet]);
        combat.State.AddCreature(pet);
        pet.SetMaxHpInternal(100);
        pet.SetCurrentHpInternal(100);
        return pet;
    }

    private static async Task VerifyFriendlyHiveBaseline()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        var companion = new Creature(ModelDb.Monster<YamotoKokiMonster>().ToMutable(), CombatSide.Player, null)
        {
            CombatState = combat.State,
            PetOwner = combat.Player
        };
        AccessTools.Method(typeof(CombatState), "AttachCreature").Invoke(combat.State, [companion]);
        combat.State.AddCreature(companion);
        companion.SetMaxHpInternal(100);
        companion.SetCurrentHpInternal(100);
        await PowerCmd.Apply<PersonalHivePower>(Choice, combat.Enemy, 1, combat.Enemy, null);
        bool reproduced = false;
        try
        {
            await CreatureCmd.Damage(Choice, combat.Enemy, 10, ValueProp.Move, companion);
        }
        catch (NullReferenceException error)
        {
            reproduced = error.StackTrace?.Contains("AddGeneratedCardsToCombat", StringComparison.Ordinal) == true;
        }
        Require(reproduced, "The published 1.0.12 companion hit must reproduce the native status-generation null reference.");
        GD.Print("PASS 1.0.12 baseline: Yamoto damage into Personal Hive throws in AddGeneratedCardsToCombat.");
    }
}
