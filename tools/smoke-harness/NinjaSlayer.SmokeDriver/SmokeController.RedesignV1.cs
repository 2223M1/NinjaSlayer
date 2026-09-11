using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private void ValidateRedesignContent()
    {
        CardModel[] poolCards = ModelDb.CardPool<NinjaSlayerCardPool>().AllCards.ToArray();
        CardModel[] cards = ModelDb.CardPool<NinjaSlayerCardPool>()
            .GetUnlockedCards(UnlockState.all, CardMultiplayerConstraint.SingleplayerOnly).ToArray();

        Type[] expectedBasicTypes =
        [
            typeof(StrikeNinjaSlayerRedesignV1),
            typeof(DefendNinjaSlayerRedesignV1),
            typeof(KarateStraightRedesignV1),
            typeof(Prejudge)
        ];
        HashSet<Type> expectedBasic = [.. expectedBasicTypes];
        HashSet<Type> actualBasic = [.. cards.Where(card => card.Rarity == CardRarity.Basic).Select(card => card.GetType())];
        Require(
            actualBasic.SetEquals(expectedBasic),
            $"Redesign Basic card mismatch. Missing=[{string.Join(", ", expectedBasic.Except(actualBasic).Select(type => type.Name))}], " +
            $"Unexpected=[{string.Join(", ", actualBasic.Except(expectedBasic).Select(type => type.Name))}].");

        (CardRarity Rarity, IReadOnlyList<string> ExpectedIds)[] rewardPools =
        [
            (CardRarity.Common, RedesignV1Rules.CommonRewardCardIds),
            (CardRarity.Uncommon, RedesignV1Rules.UncommonRewardCardIds),
            (CardRarity.Rare, RedesignV1Rules.RareRewardCardIds)
        ];
        foreach ((CardRarity rarity, IReadOnlyList<string> expectedIds) in rewardPools)
        {
            HashSet<string> expected = [.. expectedIds];
            HashSet<string> actual =
            [
                .. cards
                    .Where(card => card.Rarity == rarity)
                    .Select(card => card.GetType().Name)
            ];
            Require(
                actual.SetEquals(expected),
                $"Redesign {rarity} card mismatch. Missing=[{string.Join(", ", expected.Except(actual))}], " +
                $"Unexpected=[{string.Join(", ", actual.Except(expected))}].");
        }

        Type[] expectedAncientTypes =
        [
            typeof(CollapseFistRedesignV1),
            typeof(OneBodyOneSoul),
            typeof(ZazenDrink)
        ];
        HashSet<Type> expectedAncients = [.. expectedAncientTypes];
        HashSet<Type> actualAncients = [.. poolCards.Where(card => card.Rarity == CardRarity.Ancient).Select(card => card.GetType())];
        Require(
            actualAncients.SetEquals(expectedAncients),
            $"Redesign Ancient card mismatch. Missing=[{string.Join(", ", expectedAncients.Except(actualAncients).Select(type => type.Name))}], " +
            $"Unexpected=[{string.Join(", ", actualAncients.Except(expectedAncients).Select(type => type.Name))}].");

        Require(cards.Count(card => card.Rarity == CardRarity.Basic) == 4, "Ninja Slayer must have 4 Basic card models.");
        Require(
            cards.Count(card => card.Rarity == CardRarity.Common) == RedesignV1Rules.CommonRewardCount,
            $"Redesign card pool must contain {RedesignV1Rules.CommonRewardCount} Common cards.");
        Require(
            cards.Count(card => card.Rarity == CardRarity.Uncommon) == RedesignV1Rules.UncommonRewardCount,
            $"Redesign card pool must contain {RedesignV1Rules.UncommonRewardCount} Uncommon cards.");
        Require(
            cards.Count(card => card.Rarity == CardRarity.Rare) == RedesignV1Rules.RareRewardCount,
            $"Redesign card pool must contain {RedesignV1Rules.RareRewardCount} Rare cards.");
        Require(cards.Select(card => card.Id).Distinct().Count() == cards.Length, "Redesign card pool contains duplicate IDs.");

        CardModel[] visibleCards = poolCards.Concat(new CardModel[]
        {
            ModelDb.Card<ChadoEnergyRedesignV1>(), ModelDb.Card<StraightKiRedesignV1>(),
            ModelDb.Card<BlackFlameRedesignV1>(), ModelDb.Card<StrongShurikenTokenRedesignV1>(),
            ModelDb.Card<BusyLine>()
        }).Distinct().ToArray();
        Require(visibleCards.Length == 92, $"Current card catalog contains {visibleCards.Length} models instead of 92.");
        foreach (CardModel canonical in visibleCards)
        {
            Require(canonical.IsCanonical, $"Redesign card pool returned mutable card {canonical.Id}.");
            CardModel mutable = canonical.ToMutable();
            Require(mutable.IsMutable, $"Redesign card {canonical.Id} could not become mutable.");
            Require(mutable.GetType() == canonical.GetType(), $"Redesign card {canonical.Id} changed type when cloned.");
            Require(mutable.Id == canonical.Id, $"Redesign card {canonical.Id} changed ID when cloned.");
            Require(canonical.TitleLocString.Exists(), $"Redesign card {canonical.Id} has no title in the active locale.");
            Require(canonical.Description.Exists(), $"Redesign card {canonical.Id} has no description in the active locale.");
            Require(!string.IsNullOrWhiteSpace(canonical.TitleLocString.GetRawText()), $"Redesign card {canonical.Id} has an empty title.");
            string description = mutable.GetDescriptionForPile(PileType.None);
            Require(canonical is BusyLine || !string.IsNullOrWhiteSpace(description), $"Card {canonical.Id} could not format its description.");
            mutable.UpgradeInternal();
            Require(canonical is BusyLine || !string.IsNullOrWhiteSpace(mutable.GetDescriptionForPile(PileType.None)), $"Upgraded card {canonical.Id} could not format its description.");
        }

        CharacterModel visible = ModelDb.Character<NinjaSlayerCharacter>();
        Require(ModelDb.AllCharacters.Count(character => character is INinjaSlayerCharacter) == 1,
            "Exactly one Ninja Slayer character must be registered.");
        CardModel[] starter = visible.StartingDeck.ToArray();
        Require(starter.Length == 10
            && starter.Count(card => card is StrikeNinjaSlayerRedesignV1) == 4
            && starter.Count(card => card is DefendNinjaSlayerRedesignV1) == 4
            && starter.Count(card => card is KarateStraightRedesignV1) == 1
            && starter.Count(card => card is Prejudge) == 1,
            "Ninja Slayer starting deck must be 4 Strikes, 4 Defends, 1 Karate Straight and 1 Prejudge.");

        _checkpoints.Write("redesign.content-validated", data: new System.Text.Json.Nodes.JsonObject { ["cardCount"] = cards.Length });
    }

    private static void ValidateRedesignRunIdentity(Player player)
    {
        ModelId visibleId = ModelDb.Character<NinjaSlayerCharacter>().Id;
        Require(player.Character is NinjaSlayerCharacter, "The run did not use the registered Ninja Slayer character.");

        SerializableRun save = RunManager.Instance.ToSave(null);
        SerializablePlayer serializedPlayer = save.Players.Single(candidate => candidate.NetId == player.NetId);
        Require(serializedPlayer.CharacterId == visibleId, "The run did not serialize the canonical Ninja Slayer character ID.");
    }

    private static void ValidateRedesignCombatProgress(ICombatState combatState)
    {
        ModelId visibleId = ModelDb.Character<NinjaSlayerCharacter>().Id;
        EncounterModel encounter = combatState.Encounter
            ?? throw new InvalidOperationException("The completed smoke combat had no encounter.");
        ProgressState progress = SaveManager.Instance.Progress;
        if (!progress.EncounterStats.TryGetValue(encounter.Id, out EncounterStats? encounterStats))
        {
            throw new InvalidOperationException("Completed encounter stats were not recorded.");
        }
        Require(encounterStats.FightStats.Any(stats => stats.Character == visibleId), "Completed encounter was not recorded for visible Ninja Slayer.");

        foreach (ModelId enemyId in encounter.SpawnedEnemies.Select(enemy => enemy.Id).Distinct())
        {
            if (!progress.EnemyStats.TryGetValue(enemyId, out EnemyStats? enemyStats))
            {
                throw new InvalidOperationException($"Enemy stats were not recorded for {enemyId}.");
            }
            Require(enemyStats.FightStats.Any(stats => stats.Character == visibleId), $"Enemy {enemyId} was not recorded for visible Ninja Slayer.");
        }
    }

    private async Task VerifyRedesignCardsAndPowers(ICombatState combatState, Player player, Creature target)
    {
        var choiceContext = new BlockingPlayerChoiceContext();
        await PlayerCmd.SetEnergy(20m, player);

        ChadoEnergyRedesignV1 retained = combatState.CreateCard<ChadoEnergyRedesignV1>(player);
        TeaTeaRedesignV1 meditation = combatState.CreateCard<TeaTeaRedesignV1>(player);
        await CardPileCmd.Add(retained, PileType.Hand);
        await CardPileCmd.Add(meditation, PileType.Hand);
        await CardCmd.AutoPlay(choiceContext, meditation, null);
        Require(retained.Keywords.Contains(CardKeyword.Retain), "Meditation did not grant native Retain to tea.");
        retained.EndOfTurnCleanup();
        Require(retained.ShouldRetainThisTurn, "Meditation Retain did not survive turn cleanup.");
        await PowerCmd.Remove<TeaTeaPower>(player.Creature);

        foreach (ChadoEnergyRedesignV1 heldTea in PileType.Hand.GetPile(player).Cards.OfType<ChadoEnergyRedesignV1>().ToArray())
            await CardCmd.Exhaust(choiceContext, heldTea);
        KarateTeaRedesignV1 karateTea = combatState.CreateCard<KarateTeaRedesignV1>(player);
        await CardPileCmd.Add(karateTea, PileType.Hand);
        await CardCmd.AutoPlay(choiceContext, karateTea, player.Creature);
        int karateBeforeGeneration = player.Creature.GetPowerAmount<KaratePower>();
        await ChadoBreathCmd.Apply(choiceContext, player, 2);
        ChadoEnergyRedesignV1 tea = PileType.Hand.GetPile(player).Cards.OfType<ChadoEnergyRedesignV1>().Single();
        Require(tea.DynamicVars.Energy.BaseValue == 2
            && player.Creature.GetPowerAmount<KaratePower>() == karateBeforeGeneration + 3,
            "New Chado did not trigger Karate Tea exactly once.");
        await ChadoBreathCmd.Apply(choiceContext, player, 1);
        Require(tea.DynamicVars.Energy.BaseValue == 3
            && player.Creature.GetPowerAmount<KaratePower>() == karateBeforeGeneration + 3,
            "Increasing held Chado incorrectly triggered generation again.");
        await PlayerCmd.SetEnergy(0m, player);
        int karateBeforeEnergyGain = player.Creature.GetPower<KaratePower>()?.Amount ?? 0;
        await PlayerCmd.GainEnergy(1m, player);
        Require(player.PlayerCombatState!.Energy == 1, "The energy scenario did not gain energy.");
        Require(
            (player.Creature.GetPower<KaratePower>()?.Amount ?? 0) == karateBeforeEnergyGain,
            "Energy gain incorrectly triggered Karate Tea.");

        NoEnergyGainPower noEnergy = await PowerCmd.Apply<NoEnergyGainPower>(
            choiceContext, player.Creature, 1, player.Creature, null)
            ?? throw new InvalidOperationException("No Energy Gain could not be applied.");
        int energyBeforeBlockedGain = player.PlayerCombatState!.Energy;
        int karateBeforeBlockedGain = player.Creature.GetPower<KaratePower>()?.Amount ?? 0;
        await PlayerCmd.GainEnergy(1m, player);
        Require(player.PlayerCombatState.Energy == energyBeforeBlockedGain, "No Energy Gain failed to block energy.");
        Require(
            (player.Creature.GetPower<KaratePower>()?.Amount ?? 0) == karateBeforeBlockedGain,
            "Blocked energy gain incorrectly granted Karate.");
        await PowerCmd.Remove(noEnergy);
        await PowerCmd.Remove(player.Creature.GetPower<KarateTeaPower>()!);
        await PowerCmd.Remove(player.Creature.GetPower<KaratePower>()!);

        int hpBeforeGreatUke = player.Creature.CurrentHp;
        GreatUkeRedesignV1 greatUke = combatState.CreateCard<GreatUkeRedesignV1>(player);
        await CardPileCmd.Add(greatUke, PileType.Hand);
        await CardCmd.AutoPlay(choiceContext, greatUke, null);
        Require(tea.Pile?.Type == PileType.Exhaust && player.Creature.GetPowerAmount<BufferPower>() == 1,
            "Great Uke must play and exhaust Chado and grant one Buffer.");
        await CreatureCmd.Damage(
            choiceContext,
            [player.Creature],
            10,
            ValueProp.Unblockable | ValueProp.Unpowered,
            target,
            null
#if !NINJASLAYER_LEGACY_DAMAGE_API
            , null
#endif
        );
        Require(player.Creature.CurrentHp == hpBeforeGreatUke, "Great Uke Buffer did not prevent the first hit.");
        await CreatureCmd.Damage(
            choiceContext,
            [player.Creature],
            11,
            ValueProp.Unblockable | ValueProp.Unpowered,
            target,
            null
#if !NINJASLAYER_LEGACY_DAMAGE_API
            , null
#endif
        );
        Require(player.Creature.CurrentHp == hpBeforeGreatUke - 11, "Great Uke incorrectly prevented a second hit.");
        await CreatureCmd.Heal(player.Creature, 11);
        _checkpoints.Write("redesign.runtime-contracts-validated");
    }
}
