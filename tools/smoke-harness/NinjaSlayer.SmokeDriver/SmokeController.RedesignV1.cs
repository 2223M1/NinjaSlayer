using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Combat.SecondaryResources;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private void ValidateRedesignContent()
    {
        Type[] expectedTypes =
        [
            typeof(AlabamaDropRedesignV1),
            typeof(AssassinationFistRedesignV1),
            typeof(BackBridgeRedesignV1),
            typeof(BangBangFistRedesignV1),
            typeof(BeatPeopleChadoRedesignV1),
            typeof(BladeDanceRedesignV1),
            typeof(BladesComeRedesignV1),
            typeof(BloodTearsRedesignV1),
            typeof(BorrowedDexterityRedesignV1),
            typeof(BrewTeaRedesignV1),
            typeof(BurningStrikeRedesignV1),
            typeof(ChadoBreathRedesignV1),
            typeof(ChopRedesignV1),
            typeof(ClankDrinkTeaRedesignV1),
            typeof(ColdBrewRedesignV1),
            typeof(CollapseFist),
            typeof(ContraptionRedesignV1),
            typeof(DefendNinjaSlayerRedesignV1),
            typeof(DrinkTeaRedesignV1),
            typeof(DrowsyBlackTeaRedesignV1),
            typeof(EvadeRedesignV1),
            typeof(EvolutionRedesignV1),
            typeof(FlowingGuardRedesignV1),
            typeof(FootworkRedesignV1),
            typeof(ForgoStrengthRedesignV1),
            typeof(GreatUkeRedesignV1),
            typeof(HalfMoonCompassKickRedesignV1),
            typeof(HandChopRedesignV1),
            typeof(HellTornadoRedesignV1),
            typeof(IBlockRedesignV1),
            typeof(ImpureFlameRedesignV1),
            typeof(InjectionRedesignV1),
            typeof(IronShirtRedesignV1),
            typeof(IyaIronSlashWaveRedesignV1),
            typeof(KarateFinishRedesignV1),
            typeof(KarateRollingStoneRedesignV1),
            typeof(KarateWallRedesignV1),
            typeof(KillingIntentRedesignV1),
            typeof(LockOnRedesignV1),
            typeof(LuckyStrikeRedesignV1),
            typeof(MasochisticBlissRedesignV1),
            typeof(MomentumRedesignV1),
            typeof(MurderFistRedesignV1),
            typeof(NarakuRecoveryRedesignV1),
            typeof(NinjaGreetingRedesignV1),
            typeof(NinjaWhipRedesignV1),
            typeof(ObserverGuardRedesignV1),
            typeof(OmnidirectionalThrowRedesignV1),
            typeof(OneBodyOneSoul),
            typeof(OpeningGuardRedesignV1),
            typeof(PalmThrustRedesignV1),
            typeof(PourTeaRedesignV1),
            typeof(PursuitStrikeRedesignV1),
            typeof(ReadyBladeRedesignV1),
            typeof(RecycleRedesignV1),
            typeof(RedBlackFlameRedesignV1),
            typeof(RedoubleRedesignV1),
            typeof(ReflexGuardRedesignV1),
            typeof(RepeatSweepRedesignV1),
            typeof(RetainedForceRedesignV1),
            typeof(RetainGuardRedesignV1),
            typeof(RiffleRedesignV1),
            typeof(RubHandsRedesignV1),
            typeof(SenchaStormRedesignV1),
            typeof(ShieldFromNothingRedesignV1),
            typeof(ShurikenStockRedesignV1),
            typeof(ShurikenVolleyRedesignV1),
            typeof(StrikeNinjaSlayerRedesignV1),
            typeof(SweepKickRedesignV1),
            typeof(ThrowKunaiRedesignV1),
            typeof(TornadoFistRedesignV1),
            typeof(ZazenDrink)
        ];
        CardModel[] poolCards = ModelDb.CardPool<NinjaSlayerRedesignCardPool>().AllCards.ToArray();
        HashSet<Type> expected = [.. expectedTypes];
        HashSet<Type> actual = [.. poolCards.Select(card => card.GetType())];
        Require(
            actual.SetEquals(expected),
            $"Redesign card pool mismatch. Missing=[{string.Join(", ", expected.Except(actual).Select(type => type.Name))}], " +
            $"Unexpected=[{string.Join(", ", actual.Except(expected).Select(type => type.Name))}].");
        Require(poolCards.Length == 72, $"Redesign card pool contains {poolCards.Length} cards instead of 69 design cards and 3 Ancient cards.");

        CardModel[] cards = poolCards.OfType<NinjaSlayerRedesignCardTemplate>().ToArray();
        Require(cards.Length == 69, $"Redesign card pool contains {cards.Length} cards instead of 69.");
        Require(
            ModelDb.CardPool<NinjaSlayerCardPool>().AllCards.All(card => card is not NinjaSlayerRedesignCardTemplate),
            "Legacy card pool contains a Redesign card.");
        Require(cards.Count(card => card.Rarity == CardRarity.Basic) == 4, "Redesign card pool must contain 4 Basic cards.");
        Require(cards.Count(card => card.Rarity == CardRarity.Common) == 15, "Redesign card pool must contain 15 Common cards.");
        Require(cards.Count(card => card.Rarity == CardRarity.Uncommon) == 30, "Redesign card pool must contain 30 Uncommon cards.");
        Require(cards.Count(card => card.Rarity == CardRarity.Rare) == 20, "Redesign card pool must contain 20 Rare cards.");
        Require(cards.Select(card => card.Id).Distinct().Count() == cards.Length, "Redesign card pool contains duplicate IDs.");

        foreach (CardModel canonical in poolCards)
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

    private static void ValidateTeaCounterLayout()
    {
        NCombatUi ui = NCombatRoom.Instance?.Ui
            ?? throw new InvalidOperationException("Combat UI was unavailable for the tea counter check.");
        Control starCounter = ui.GetNode<Control>("%StarCounter");
        NSecondaryResourceCounter teaCounter = ui.GetChildren()
            .OfType<NSecondaryResourceCounter>()
            .Single();
        NSecondaryResourceIcon icon = teaCounter.GetChildren()
            .OfType<NSecondaryResourceIcon>()
            .Single();
        MegaLabel label = teaCounter.GetChildren()
            .OfType<MegaLabel>()
            .Single();

        Require(
            teaCounter.GlobalPosition.IsEqualApprox(starCounter.GlobalPosition),
            $"Tea counter did not occupy the Regent star-counter position. " +
            $"tea={teaCounter.GlobalPosition}, star={starCounter.GlobalPosition}.");
        Require(teaCounter.Size.IsEqualApprox(starCounter.Size), "Tea counter did not use the Regent star-counter size.");
        Require(teaCounter.Scale.IsEqualApprox(starCounter.Scale), "Tea counter did not use the Regent star-counter scale.");
        Require(icon.Position.IsEqualApprox(label.Position), "Tea amount was not centered on its icon.");
        Require(icon.Size.IsEqualApprox(label.Size), "Tea amount and icon used different layout bounds.");
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

        ShurikenVolleyRedesignV1 stockGrantingAttack = combatState.CreateCard<ShurikenVolleyRedesignV1>(player);
        ShurikenStockPower grantedStock = await PowerCmd.Apply<ShurikenStockPower>(
            choiceContext,
            player.Creature,
            2,
            player.Creature,
            stockGrantingAttack)
            ?? throw new InvalidOperationException("Shuriken Stock could not be applied.");
        await grantedStock.AfterCardPlayed(
            choiceContext,
            new CardPlay
            {
                Card = stockGrantingAttack,
                Player = player,
                Target = target,
                ResultPile = PileType.Discard,
                Resources = new ResourceInfo
                {
                    EnergySpent = 0,
                    EnergyValue = 0,
                    StarsSpent = 0,
                    StarValue = 0
                },
                IsAutoPlay = true,
                PlayIndex = 0,
                PlayCount = 1
            });
        Require(grantedStock.Amount == 2, "The Attack that granted Shuriken Stock consumed one of its new charges.");
        await PowerCmd.Remove(grantedStock);

        HandChopRedesignV1 handChop = combatState.CreateCard<HandChopRedesignV1>(player);
        await CardPileCmd.Add(handChop, PileType.Hand);
        await CardCmd.AutoPlay(choiceContext, handChop, target);
        Require(handChop.Pile?.Type == PileType.Hand, "Hand Chop did not return to hand.");

        ChopRedesignV1 chop = combatState.CreateCard<ChopRedesignV1>(player);
        int strengthBeforeChop = player.Creature.GetPower<StrengthPower>()?.Amount ?? 0;
        int karateBeforeChop = target.GetPower<KaratePower>()?.Amount ?? 0;
        await CardPileCmd.Add(chop, PileType.Hand);
        await CardCmd.AutoPlay(choiceContext, chop, target);
        Require(player.Creature.GetPower<StrengthPower>()?.Amount == strengthBeforeChop + 3, "Chop did not grant temporary Strength.");
        Require((target.GetPower<KaratePower>()?.Amount ?? 0) == karateBeforeChop, "Chop retained the legacy Karate effect.");
        ChopTemporaryStrengthPower chopStrength = player.Creature.GetPower<ChopTemporaryStrengthPower>()
            ?? throw new InvalidOperationException("Chop did not use its native temporary Strength power.");
        Require(chop.Pile?.Type == PileType.Draw, "Chop did not move to the draw pile.");
        Require(ReferenceEquals(PileType.Draw.GetPile(player).Cards.First(), chop), "Chop was not placed on top of the draw pile.");
        await chopStrength.AfterSideTurnEnd(choiceContext, player.Creature.Side, [player.Creature]);
        Require(player.Creature.GetPower<ChopTemporaryStrengthPower>() is null, "Chop temporary Strength survived the player's turn.");
        Require((player.Creature.GetPower<StrengthPower>()?.Amount ?? 0) == strengthBeforeChop, "Chop removed the wrong Strength amount.");

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

        ChadoEnergyPower chadoEnergy = await PowerCmd.Apply<ChadoEnergyPower>(
            choiceContext, player.Creature, 2, player.Creature, null)
            ?? throw new InvalidOperationException("Chado Energy could not be applied.");
        await chadoEnergy.AfterSideTurnEnd(choiceContext, player.Creature.Side, [player.Creature]);
        Require(player.Creature.GetPower<ChadoEnergyPower>() is null, "Chado Energy survived the turn in which it was applied.");

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

        SameNameCostPower sameName = await PowerCmd.Apply<SameNameCostPower>(
            choiceContext, player.Creature, 2, player.Creature, null)
            ?? throw new InvalidOperationException("Same-name cost power could not be applied.");
        DefendNinjaSlayerRedesignV1 firstNamed = combatState.CreateCard<DefendNinjaSlayerRedesignV1>(player);
        DefendNinjaSlayerRedesignV1 secondNamed = combatState.CreateCard<DefendNinjaSlayerRedesignV1>(player);
        DefendNinjaSlayerRedesignV1 thirdNamed = combatState.CreateCard<DefendNinjaSlayerRedesignV1>(player);
        DefendNinjaSlayerRedesignV1 fourthNamed = combatState.CreateCard<DefendNinjaSlayerRedesignV1>(player);
        StrikeNinjaSlayerRedesignV1 differentName = combatState.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
        foreach (CardModel card in new CardModel[] { firstNamed, secondNamed, thirdNamed, fourthNamed, differentName })
        {
            await CardPileCmd.Add(card, PileType.Hand);
        }
        await CardCmd.AutoPlay(choiceContext, firstNamed, player.Creature);
        Require(sameName.TryModifyEnergyCostInCombatLate(secondNamed, 1, out decimal secondCost) && secondCost == 0, "The first matching card was not free.");
        Require(!sameName.TryModifyEnergyCostInCombatLate(differentName, 1, out _), "A differently named card became free.");
        await CardCmd.AutoPlay(choiceContext, secondNamed, player.Creature);
        Require(sameName.TryModifyEnergyCostInCombatLate(thirdNamed, 1, out decimal thirdCost) && thirdCost == 0, "The upgraded second matching card was not free.");
        await CardCmd.AutoPlay(choiceContext, thirdNamed, player.Creature);
        Require(!sameName.TryModifyEnergyCostInCombatLate(fourthNamed, 1, out _), "Same-name cost power exceeded its per-turn limit.");
        await PowerCmd.Remove(sameName);

        await CreatureCmd.GainBlock(player.Creature, 7, ValueProp.Unpowered, null);
        RemainingBlockStrengthPower blockStrength = await PowerCmd.Apply<RemainingBlockStrengthPower>(
            choiceContext, player.Creature, 1, player.Creature, null)
            ?? throw new InvalidOperationException("Remaining-block Strength could not be applied.");
        int strengthBeforeBlockConversion = player.Creature.GetPower<StrengthPower>()?.Amount ?? 0;
        await blockStrength.BeforeSideTurnStart(choiceContext, player.Creature.Side, [player.Creature], combatState);
        Require(
            (player.Creature.GetPower<StrengthPower>()?.Amount ?? 0) == strengthBeforeBlockConversion + 7,
            "Remaining Block did not become temporary Strength.");
        RetainedForcePower temporaryStrength = player.Creature.GetPower<RetainedForcePower>()
            ?? throw new InvalidOperationException("Temporary Strength tracker was not applied.");
        await PowerCmd.Remove(temporaryStrength);
        Require(
            (player.Creature.GetPower<StrengthPower>()?.Amount ?? 0) == strengthBeforeBlockConversion,
            "Temporary Strength was not removed with its tracker.");
        await RemoveSmokeBlock(player.Creature);

        int hpBeforeNullify = player.Creature.CurrentHp;
        NullifyHitsPower nullify = await PowerCmd.Apply<NullifyHitsPower>(
            choiceContext, player.Creature, 2, player.Creature, null)
            ?? throw new InvalidOperationException("Nullify Hits could not be applied.");
        await GameCompatibility.Damage.Deal(
            choiceContext, [player.Creature], 12, ValueProp.Unblockable | ValueProp.Unpowered, target, null, null);
        Require(player.Creature.CurrentHp == hpBeforeNullify - 12, "A 12-damage hit incorrectly triggered Great Uke.");
        Require(nullify.Amount == 2, "A 12-damage hit consumed Great Uke.");
        await GameCompatibility.Damage.Deal(
            choiceContext, [player.Creature], 13, ValueProp.Unblockable | ValueProp.Unpowered, target, null, null);
        Require(player.Creature.CurrentHp == hpBeforeNullify - 12, "A hit above 12 damage was not nullified.");
        Require(nullify.Amount == 1, "A hit above 12 damage did not consume one Great Uke charge.");
        await PowerCmd.Remove(nullify);
        await CreatureCmd.Heal(player.Creature, 12);
        _checkpoints.Write("redesign.runtime-contracts-validated");
    }
}
