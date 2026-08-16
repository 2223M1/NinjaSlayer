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
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private void ValidateRedesignContent()
    {
        CardModel[] cards = ModelDb.CardPool<NinjaSlayerRedesignCardPool>()
            .AllCards
            .Where(card => card is NinjaSlayerRedesignCardTemplate)
            .ToArray();
        Require(cards.Length == 69, $"Redesign card pool contains {cards.Length} cards instead of 69.");
        Require(cards.Count(card => card.Rarity == CardRarity.Basic) == 4, "Redesign card pool must contain 4 Basic cards.");
        Require(cards.Count(card => card.Rarity == CardRarity.Common) == 15, "Redesign card pool must contain 15 Common cards.");
        Require(cards.Count(card => card.Rarity == CardRarity.Uncommon) == 30, "Redesign card pool must contain 30 Uncommon cards.");
        Require(cards.Count(card => card.Rarity == CardRarity.Rare) == 20, "Redesign card pool must contain 20 Rare cards.");
        Require(cards.Select(card => card.Id).Distinct().Count() == cards.Length, "Redesign card pool contains duplicate IDs.");

        foreach (CardModel canonical in cards)
        {
            Require(canonical.IsCanonical, $"Redesign card pool returned mutable card {canonical.Id}.");
            CardModel mutable = canonical.ToMutable();
            Require(mutable.IsMutable, $"Redesign card {canonical.Id} could not become mutable.");
            Require(mutable.GetType() == canonical.GetType(), $"Redesign card {canonical.Id} changed type when cloned.");
            Require(mutable.Id == canonical.Id, $"Redesign card {canonical.Id} changed ID when cloned.");
            Require(canonical.TitleLocString.Exists(), $"Redesign card {canonical.Id} has no title in the active locale.");
            Require(canonical.Description.Exists(), $"Redesign card {canonical.Id} has no description in the active locale.");
            Require(!string.IsNullOrWhiteSpace(canonical.TitleLocString.GetRawText()), $"Redesign card {canonical.Id} has an empty title.");
            Require(!string.IsNullOrWhiteSpace(canonical.Description.GetRawText()), $"Redesign card {canonical.Id} has an empty description.");
            Require(!string.IsNullOrWhiteSpace(mutable.GetDescriptionForPile(PileType.None)), $"Redesign card {canonical.Id} could not format its description.");
        }

        Require(
            typeof(NinjaSlayerRedesignCardTemplate).Assembly.GetType("NinjaSlayer.Cards.RedesignV1.RedesignV1ProxyCard`1") is null,
            "The removed Redesign proxy-card type is still present.");

        CharacterModel visible = ModelDb.Character<NinjaSlayerCharacter>();
        CharacterModel redesign = ModelDb.Character<NinjaSlayerRedesignCharacter>();
        Require(visible.Title.GetRawText() == redesign.Title.GetRawText(), "Visible and Redesign character titles differ.");
        Require(visible.TitleObject.GetRawText() == redesign.TitleObject.GetRawText(), "Visible and Redesign character object titles differ.");
        CardModel[] lobbyCards =
        [
            ModelDb.Card<StrikeNinjaSlayer>(),
            ModelDb.Card<DefendNinjaSlayer>(),
            ModelDb.Card<Meditation>(),
            ModelDb.Card<KarateStraight>(),
            ModelDb.Card<ChadoCard>()
        ];
        foreach (CardModel card in lobbyCards)
        {
            Require(card.TitleLocString.Exists(), $"Lobby card {card.Id} has no title in the active locale.");
            Require(card.Description.Exists(), $"Lobby card {card.Id} has no description in the active locale.");
        }

        _checkpoints.Write("redesign.content-validated", data: new System.Text.Json.Nodes.JsonObject { ["cardCount"] = cards.Length });
    }

    private static void ValidateRedesignRunIdentity(Player player)
    {
        ModelId visibleId = ModelDb.Character<NinjaSlayerCharacter>().Id;
        ModelId redesignId = ModelDb.Character<NinjaSlayerRedesignCharacter>().Id;
        RunState runState = RunManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("The active Redesign run state was unavailable.");
        Require(
            NinjaSlayerRunData.GetRulesVersion(runState) == NinjaSlayerRulesVersion.RedesignV1,
            "The active run did not snapshot the Redesign V1 rules.");
        Require(player.Character is NinjaSlayerRedesignCharacter, "The Redesign run did not use the hidden Redesign character model.");

        SerializableRun save = RunManager.Instance.ToSave(null);
        SerializablePlayer serializedPlayer = save.Players.Single(candidate => candidate.NetId == player.NetId);
        Require(serializedPlayer.CharacterId == redesignId, "The active Redesign run did not serialize the Redesign character ID.");

        ProgressState progress = SaveManager.Instance.Progress;
        CharacterStats visibleStats = progress.GetOrCreateCharacterStats(visibleId);
        CharacterStats redesignStats = progress.GetOrCreateCharacterStats(redesignId);
        Require(ReferenceEquals(visibleStats, redesignStats), "Redesign progress lookup did not resolve to visible Ninja Slayer stats.");
        Require(!progress.CharacterStats.ContainsKey(redesignId), "Progress contains a separate hidden Redesign character entry.");

        var ancientStats = new AncientStats
        {
            Id = ModelId.none,
            CharStats =
            [
                new AncientCharacterStats { Character = visibleId, Wins = 2, Losses = 1 }
            ]
        };
        Require(ancientStats.GetVisitsAs(redesignId) == 3, "Ancient visit lookup did not use visible Ninja Slayer identity.");
        foreach (AncientEventModel ancient in ModelDb.AllAncients)
        {
            Require(
                ancient.DialogueSet.GetValidDialogues(visibleId, 1, 2, true)
                    .SequenceEqual(ancient.DialogueSet.GetValidDialogues(redesignId, 1, 2, true)),
                $"Ancient dialogue identity differs for {ancient.Id}.");
        }
    }

    private static void ValidateRedesignCombatProgress(ICombatState combatState)
    {
        ModelId visibleId = ModelDb.Character<NinjaSlayerCharacter>().Id;
        ModelId redesignId = ModelDb.Character<NinjaSlayerRedesignCharacter>().Id;
        EncounterModel encounter = combatState.Encounter
            ?? throw new InvalidOperationException("The completed smoke combat had no encounter.");
        ProgressState progress = SaveManager.Instance.Progress;
        if (!progress.EncounterStats.TryGetValue(encounter.Id, out EncounterStats? encounterStats))
        {
            throw new InvalidOperationException("Completed encounter stats were not recorded.");
        }
        Require(encounterStats.FightStats.Any(stats => stats.Character == visibleId), "Completed encounter was not recorded for visible Ninja Slayer.");
        Require(encounterStats.FightStats.All(stats => stats.Character != redesignId), "Completed encounter retained hidden Redesign stats.");

        foreach (ModelId enemyId in encounter.SpawnedEnemies.Select(enemy => enemy.Id).Distinct())
        {
            if (!progress.EnemyStats.TryGetValue(enemyId, out EnemyStats? enemyStats))
            {
                throw new InvalidOperationException($"Enemy stats were not recorded for {enemyId}.");
            }
            Require(enemyStats.FightStats.Any(stats => stats.Character == visibleId), $"Enemy {enemyId} was not recorded for visible Ninja Slayer.");
            Require(enemyStats.FightStats.All(stats => stats.Character != redesignId), $"Enemy {enemyId} retained hidden Redesign stats.");
        }
    }

    private async Task VerifyRedesignCardsAndPowers(ICombatState combatState, Player player, Creature target)
    {
        var choiceContext = new BlockingPlayerChoiceContext();
        await PlayerCmd.SetEnergy(20m, player);

        HandChopRedesignV1 handChop = combatState.CreateCard<HandChopRedesignV1>(player);
        await CardPileCmd.Add(handChop, PileType.Hand);
        await CardCmd.AutoPlay(choiceContext, handChop, target);
        Require(handChop.Pile?.Type == PileType.Hand, "Hand Chop did not return to hand.");

        ChopRedesignV1 chop = combatState.CreateCard<ChopRedesignV1>(player);
        await CardPileCmd.Add(chop, PileType.Hand);
        await CardCmd.AutoPlay(choiceContext, chop, target);
        Require(chop.Pile?.Type == PileType.Draw, "Chop did not move to the draw pile.");
        Require(ReferenceEquals(PileType.Draw.GetPile(player).Cards.First(), chop), "Chop was not placed on top of the draw pile.");

        foreach (CardModel card in PileType.Hand.GetPile(player).Cards.ToArray())
        {
            await CardPileCmd.Add(card, PileType.Draw);
        }
        DefendNinjaSlayerRedesignV1 repeated = combatState.CreateCard<DefendNinjaSlayerRedesignV1>(player);
        SenchaStormRedesignV1 repeat = combatState.CreateCard<SenchaStormRedesignV1>(player);
        await CardPileCmd.Add(repeated, PileType.Hand);
        await CardPileCmd.Add(repeat, PileType.Hand);
        await CardCmd.AutoPlay(choiceContext, repeat, null);
        await CardCmd.AutoPlay(choiceContext, repeated, player.Creature);
        Require(repeat.Pile?.Type == PileType.Exhaust, "Sencha Storm did not exhaust.");
        Require(repeated.Pile?.Type == PileType.Hand, "Sencha Storm did not make the selected card Repeat.");

        DefendNinjaSlayerRedesignV1 retained = combatState.CreateCard<DefendNinjaSlayerRedesignV1>(player);
        RedBlackFlameRedesignV1 redBlackFlame = combatState.CreateCard<RedBlackFlameRedesignV1>(player);
        await CardPileCmd.Add(retained, PileType.Hand);
        await CardPileCmd.Add(redBlackFlame, PileType.Hand);
        await CardCmd.AutoPlay(choiceContext, redBlackFlame, target);
        Require(redBlackFlame.Pile?.Type == PileType.Exhaust, "Red Black Flame did not exhaust.");
        Require(retained.ShouldRetainThisTurn, "Red Black Flame did not retain the hand for this turn.");
        Require(!retained.Keywords.Contains(CardKeyword.Retain), "Red Black Flame permanently added Retain.");
        retained.EndOfTurnCleanup();
        Require(!retained.ShouldRetainThisTurn, "Red Black Flame retain survived end-of-turn cleanup.");

        if (player.Creature.Block > 0)
        {
            await RemoveSmokeBlock(player.Creature);
        }
        ChadoBlockPower chadoBlock = await PowerCmd.Apply<ChadoBlockPower>(
            choiceContext, player.Creature, 5, player.Creature, null)
            ?? throw new InvalidOperationException("Chado Block could not be applied.");
        await PlayerCmd.SetEnergy(0m, player);
        await PlayerCmd.GainEnergy(1m, player);
        Require(player.Creature.Block == 5, "Chado Block was not resolved before the first energy task completed.");
        await PlayerCmd.GainEnergy(1m, player);
        Require(player.Creature.Block == 10, "Chado Block did not trigger for consecutive energy gains.");
        NoEnergyGainPower noEnergy = await PowerCmd.Apply<NoEnergyGainPower>(
            choiceContext, player.Creature, 1, player.Creature, null)
            ?? throw new InvalidOperationException("No Energy Gain could not be applied.");
        int energyBeforeBlockedGain = player.PlayerCombatState!.Energy;
        await PlayerCmd.GainEnergy(1m, player);
        Require(player.PlayerCombatState.Energy == energyBeforeBlockedGain, "No Energy Gain failed to block energy.");
        Require(player.Creature.Block == 10, "Chado Block triggered when energy gain was blocked.");
        await PowerCmd.Remove(noEnergy);
        await PowerCmd.Remove(chadoBlock);

        int strengthBefore = player.Creature.GetPower<StrengthPower>()?.Amount ?? 0;
        CarriedStrengthPower carried = await PowerCmd.Apply<CarriedStrengthPower>(
            choiceContext, player.Creature, 2, player.Creature, null)
            ?? throw new InvalidOperationException("Carried Strength could not be applied.");
        await PowerCmd.Apply<CarriedStrengthPower>(choiceContext, player.Creature, 3, player.Creature, null);
        Require(carried.Amount == 5, "Carried Strength did not accumulate its current amount.");
        Require(player.Creature.GetPower<StrengthPower>()?.Amount == strengthBefore + 5, "Carried Strength did not grant each positive delta.");
        await carried.AfterPlayerTurnStart(choiceContext, player);
        Require(player.Creature.GetPower<CarriedStrengthPower>() is null, "Carried Strength survived the next player turn.");
        Require((player.Creature.GetPower<StrengthPower>()?.Amount ?? 0) == strengthBefore, "Carried Strength removed the wrong Strength amount.");

        PerCardStrengthPower perCard = await PowerCmd.Apply<PerCardStrengthPower>(
            choiceContext, player.Creature, 1, player.Creature, null)
            ?? throw new InvalidOperationException("Per-card Strength could not be applied.");
        await perCard.AfterSideTurnEnd(choiceContext, player.Creature.Side, [player.Creature]);
        Require(player.Creature.GetPower<PerCardStrengthPower>() is null, "Per-card Strength survived its owner's turn.");

        if (player.Creature.Block > 0)
        {
            await RemoveSmokeBlock(player.Creature);
        }
        ThreeTurnBlockPower first = await PowerCmd.Apply<ThreeTurnBlockPower>(
            choiceContext, player.Creature, 6, player.Creature, null)
            ?? throw new InvalidOperationException("First three-turn Block instance could not be applied.");
        await first.AfterPlayerTurnStart(choiceContext, player);
        ThreeTurnBlockPower second = await PowerCmd.Apply<ThreeTurnBlockPower>(
            choiceContext, player.Creature, 6, player.Creature, null)
            ?? throw new InvalidOperationException("Second three-turn Block instance could not be applied.");
        await first.AfterPlayerTurnStart(choiceContext, player);
        await second.AfterPlayerTurnStart(choiceContext, player);
        Require(!player.Creature.Powers.Contains(first) && player.Creature.Powers.Contains(second), "Staggered three-turn Block instances shared a lifetime.");
        await second.AfterPlayerTurnStart(choiceContext, player);
        Require(!player.Creature.Powers.Contains(second), "Second three-turn Block instance did not complete independently.");
        Require(player.Creature.Block == 24, "Three-turn Block produced the wrong staggered total.");
        await RemoveSmokeBlock(player.Creature);
        _checkpoints.Write("redesign.runtime-contracts-validated");
    }
}
