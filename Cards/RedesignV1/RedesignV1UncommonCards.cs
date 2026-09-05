using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(NinjaSlayerCardPool), Inherit = true)]
public abstract class RedesignV1UncommonCard(
    string id,
    string art,
    int cost,
    CardType type,
    TargetType target) : NinjaSlayerRedesignCardTemplate(
        new NinjaSlayerCardSpec(id, cost, type, CardRarity.Uncommon, target, true),
        art);

public sealed class FlyingBladesComeRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Discard", 2), new DynamicVar("Stock", 3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromOrb<ShurikenOrb>()];

    public FlyingBladesComeRedesignV1()
        : base(nameof(FlyingBladesComeRedesignV1), nameof(ShurikenCleave), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await NinjaSlayerActions.ChooseAndDiscard(
            choiceContext,
            Owner,
            DynamicVars["Discard"].IntValue,
            this);
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
    }

    protected override void OnUpgrade() => DynamicVars["Stock"].UpgradeValueBy(1);
}

public sealed class ShurikenGenerationRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<ShurikenGuardRedesignPower>()];

    public ShurikenGenerationRedesignV1()
        : base(nameof(ShurikenGenerationRedesignV1), nameof(ShurikenGuard), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await NinjaSlayerActions.ChooseAndDiscardOne(choiceContext, Owner, this);
        await PowerCmd.Apply<ShurikenGuardRedesignPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class CombatAdjustmentRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(7, ValueProp.Move), new DynamicVar("Breath", 1)];

    public CombatAdjustmentRedesignV1()
        : base(nameof(CombatAdjustmentRedesignV1), nameof(TeaHitsPeople), 1, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await PowerCmd.Apply<CombatAdjustmentPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Breath"].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() { }
}

public sealed class SweepKickRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(5, ValueProp.Move)];

    public SweepKickRedesignV1()
        : base(nameof(SweepKickRedesignV1), nameof(Evade), 0, CardType.Attack, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int consumed = await NinjaSlayerActions.ChooseAndExhaustAnyRedesignChado(
            choiceContext,
            Owner,
            this);
        int hits = 1 + consumed * 2;
        await this.ExecuteSequenceWithFinisher(
            choiceContext,
            cardPlay,
            hits,
            () => NinjaSlayerXAttackSequence.Run(
                Owner.Creature,
                hits,
                Owner.Character.AttackAnimDelay,
                Owner.Character.AttackAnimDelay,
                async _ =>
                {
                    AttackCommand command = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                        .FromCard(this)
#else
                        .FromCard(this, cardPlay)
#endif
                        .WithDefectStrikeHitFx()
                        .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
                        .TargetingAllOpponents(CombatState!);
                    await command.Execute(choiceContext);
                    return CombatState!.HittableEnemies.Count == 0;
                }));
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
    }
}

public sealed class TeaStormRedesignV1 : RedesignV1UncommonCard
{
    protected override bool HasEnergyCostX => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("BreathPerX", 2)];

    public TeaStormRedesignV1()
        : base(nameof(TeaStormRedesignV1), nameof(SteepTea), 0, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int x = ResolveEnergyXValue();
        return ChadoBreathCmd.Apply(Owner, x * DynamicVars["BreathPerX"].IntValue, this);
    }

    protected override void OnUpgrade()
    {
        RemoveKeyword(CardKeyword.Ethereal);
        AddKeyword(CardKeyword.Retain);
    }
}

public sealed class MetabolicAccelerationRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override bool IsPlayable => NinjaSlayerActions.HasRedesignChadoInHand(Owner);
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Heal", 5)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<ChadoEnergyRedesignV1>()];

    public MetabolicAccelerationRedesignV1()
        : base(nameof(MetabolicAccelerationRedesignV1), nameof(DrinkTea), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!await NinjaSlayerActions.ChooseAndExhaustRedesignChado(choiceContext, Owner, this))
        {
            return;
        }

        await CreatureCmd.Heal(Owner.Creature, DynamicVars["Heal"].BaseValue);
    }

    protected override void OnUpgrade() => DynamicVars["Heal"].UpgradeValueBy(3);
}

public sealed class GuwaaRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    protected override bool IsPlayable =>
        PileType.Hand.GetPile(Owner).Cards.OfType<ChadoEnergyRedesignV1>().Any();
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(4, ValueProp.Move), new DynamicVar("Breath", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<ChadoEnergyRedesignV1>()];

    public GuwaaRedesignV1()
        : base(nameof(GuwaaRedesignV1), nameof(IBlock), 0, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}

public sealed class AlabamaDropRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(0),
        new ExtraDamageVar(6),
        new CalculatedDamageVar(ValueProp.Move | ValueProp.Unpowered)
            .WithMultiplier(static (card, _) => card.Owner.Creature.GetPowerAmount<KaratePower>()),
        new DynamicVar("Dazed", 3)
    ];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), HoverTipFactory.FromCard<Dazed>()];

    public AlabamaDropRedesignV1()
        : base(nameof(AlabamaDropRedesignV1), nameof(AlabamaDrop), 3, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        bool resolved = false;
        async Task ResolveImpact()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            int karate = Owner.Creature.GetPowerAmount<KaratePower>();
            await CreatureCmd.Damage(
                choiceContext,
                cardPlay.Target!,
                karate * DynamicVars.ExtraDamage.BaseValue,
                ValueProp.Move | ValueProp.Unpowered,
                this
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , cardPlay
#endif
            );
        }

        await AlabamaDropAnimation.Play(Owner.Creature, cardPlay.Target!, ResolveImpact);
        await ResolveImpact();
        await PowerCmd.Remove<KaratePower>(Owner.Creature);
        for (int index = 0; index < DynamicVars["Dazed"].IntValue; index++)
        {
            await NinjaSlayerActions.AddGeneratedCard<Dazed>(Owner, PileType.Draw);
        }
    }

    protected override void OnUpgrade() => DynamicVars.ExtraDamage.UpgradeValueBy(2);
}

public sealed class AdversityCarapaceRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<VigorPower>(6)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<ChadoEnergyRedesignV1>(), HoverTipFactory.FromPower<VigorPower>()];

    public AdversityCarapaceRedesignV1()
        : base(nameof(AdversityCarapaceRedesignV1), nameof(IronShirt), 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<VitalityTeaPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(VigorPower)].BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars[nameof(VigorPower)].UpgradeValueBy(2);
}

public sealed class RedBlackFlameAttackRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CardsVar(3), new DynamicVar("BlackFlames", 2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public RedBlackFlameAttackRedesignV1()
        : base(nameof(RedBlackFlameAttackRedesignV1), nameof(ImpureFlame), 0, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.IntValue, Owner);
        for (int index = 0; index < DynamicVars["BlackFlames"].IntValue; index++)
        {
            await NinjaSlayerActions.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Draw);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}

public sealed class RoundhouseKickRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(7, ValueProp.Move), new RepeatVar(2)];

    public RoundhouseKickRedesignV1()
        : base(nameof(RoundhouseKickRedesignV1), nameof(SweepKick), 2, CardType.Attack, TargetType.AllEnemies) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiSomersaultKickEvent);
        int hits = DynamicVars.Repeat.IntValue;
        return this.ExecuteSequenceWithFinisher(
            choiceContext,
            cardPlay,
            hits,
            () => NinjaSlayerXAttackSequence.Run(
                Owner.Creature,
                hits,
                Owner.Character.AttackAnimDelay,
                Owner.Character.AttackAnimDelay,
                async _ =>
                {
                    AttackCommand command = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                        .FromCard(this)
#else
                        .FromCard(this, cardPlay)
#endif
                        .WithDefectStrikeHitFx()
                        .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
                        .TargetingAllOpponents(CombatState!);
                    await command.Execute(choiceContext);
                    return CombatState!.HittableEnemies.Count == 0;
                }));
    }

    public override async Task AfterAutoPostPlayPhaseEntered(
        PlayerChoiceContext choiceContext,
        Player player)
    {
        if (player != Owner)
        {
            return;
        }

        IReadOnlyList<CardModel> drawPile = PileType.Draw.GetPile(Owner).Cards;
        if (drawPile.Count > 0 && drawPile[0] == this)
        {
            await CardPileCmd.AutoPlayFromDrawPile(
                choiceContext,
                Owner,
                1,
                CardPilePosition.Top,
                forceExhaust: false);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2);
}

public sealed class TechniqueSearchRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CardsVar(3), new DynamicVar("Scry", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public TechniqueSearchRedesignV1()
        : base(nameof(TechniqueSearchRedesignV1), nameof(ReadyBlade), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars["Scry"].IntValue);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}

public sealed class DoubleForceRedesignV1 : ArchivedRedesignV1Card
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(4)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public DoubleForceRedesignV1()
        : base(nameof(DoubleForceRedesignV1), nameof(ColdBrew), 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
        foreach (CardModel card in PileType.Hand.GetPile(Owner).Cards)
        {
            card.GiveSingleTurnRetain();
        }
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(2);
}

public sealed class FlyingBladeDanceRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(3, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public FlyingBladeDanceRedesignV1()
        : base(nameof(FlyingBladeDanceRedesignV1), nameof(BladeDance), 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<ScryBlockPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Block.BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(1);
}

public sealed class NinjaSixthSenseRedesignV1 : ArchivedRedesignV1Card
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(4)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public NinjaSixthSenseRedesignV1()
        : base(nameof(NinjaSixthSenseRedesignV1), "SmokeRead", 0, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int generatedStatuses = CombatManager.Instance.History.Entries
            .OfType<CardGeneratedEntry>()
            .Count(entry => entry.Creator == Owner && entry.Card.Type == CardType.Status);
        return ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue + generatedStatuses);
    }

    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
}

public sealed class DecidedOutcomeRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public DecidedOutcomeRedesignV1()
        : base(nameof(DecidedOutcomeRedesignV1), nameof(KarateFinish), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ScryResult result = await ScryCmd.Execute(
            choiceContext,
            Owner,
            DynamicVars.Cards.IntValue,
            exhaustDiscarded: true);
        await ChadoBreathCmd.Apply(Owner, result.ExhaustedCards, this);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(2);
}

public sealed class AetherEnergyRedesignV1 : ArchivedRedesignV1Card
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Innate];

    public AetherEnergyRedesignV1()
        : base(nameof(AetherEnergyRedesignV1), nameof(Contraption), 1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<AetherEnergyPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);

    protected override void OnUpgrade() { }
}

public sealed class AbyssStrengthRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(6)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public AbyssStrengthRedesignV1()
        : base(nameof(AbyssStrengthRedesignV1), nameof(NarakuRecovery), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
        await NinjaSlayerActions.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Draw);
    }

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(2);
}

public sealed class DisadvantageTacticsRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(3, ValueProp.Move), new RepeatVar(2), new KarateVar(3)];

    public DisadvantageTacticsRedesignV1()
        : base(nameof(DisadvantageTacticsRedesignV1), nameof(MurderFist), 2, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int hits = DynamicVars.Repeat.IntValue;
        await this.ExecuteSequenceWithFinisher(
            choiceContext,
            cardPlay,
            hits,
            () => NinjaSlayerXAttackSequence.Run(
                Owner.Creature,
                hits,
                Owner.Character.AttackAnimDelay,
                Owner.Character.AttackAnimDelay,
                async _ =>
                {
                    AttackCommand command = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                        .FromCard(this)
#else
                        .FromCard(this, cardPlay)
#endif
                        .WithDefectStrikeHitFx()
                        .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
                        .Targeting(cardPlay.Target!);
                    await command.Execute(choiceContext);
                    return !cardPlay.Target!.IsAlive;
                }));
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            CombatState!.HittableEnemies.Count * DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(1);
}

public sealed class KarateTrainingRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(2)];

    public KarateTrainingRedesignV1()
        : base(nameof(KarateTrainingRedesignV1), nameof(Evolution), 2, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<KarateTrainingPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(1);
}

public sealed class JujutsuStanceRedesignV1 : ArchivedRedesignV1Card, IReturnToHandAfterPlay
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];
    protected override bool IsPlayable => Owner.Creature.GetPowerAmount<KaratePower>() >= DynamicVars["KarateCost"].IntValue;
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("KarateCost", 3), new PowerVar<DexterityPower>(1)];

    public JujutsuStanceRedesignV1()
        : base(nameof(JujutsuStanceRedesignV1), nameof(ForgoStrength), 0, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        KaratePower? karate = Owner.Creature.GetPower<KaratePower>();
        if (karate == null || karate.Amount < DynamicVars["KarateCost"].IntValue)
        {
            return;
        }

        await PowerCmd.ModifyAmount(
            choiceContext,
            karate,
            -DynamicVars["KarateCost"].BaseValue,
            Owner.Creature,
            this);
        await PowerCmd.Apply<DexterityPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Dexterity.BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["KarateCost"].UpgradeValueBy(-1);
        RemoveKeyword(CardKeyword.Ethereal);
    }
}

public sealed class ChopStrikeRedesignV1 : RedesignV1UncommonCard, IReturnToHandAfterPlay
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(8, ValueProp.Move), new CardsVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public ChopStrikeRedesignV1()
        : base(nameof(ChopStrikeRedesignV1), nameof(IyaIronSlashWave), 1, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}

public sealed class BackBridgeRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(8, ValueProp.Move), new DynamicVar("BlockPerKarate", 2)];

    public BackBridgeRedesignV1()
        : base(nameof(BackBridgeRedesignV1), nameof(BackBridge), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(
            Owner.Creature,
            DynamicVars.Block.BaseValue
                + Owner.Creature.GetPowerAmount<KaratePower>()
                * DynamicVars["BlockPerKarate"].IntValue,
            ValueProp.Move,
            cardPlay);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2);
        DynamicVars["BlockPerKarate"].UpgradeValueBy(1);
    }
}

public sealed class ChadoSecretRedesignV1 : ArchivedRedesignV1Card
{
    private static readonly Type[] CandidateTypes =
    [
        typeof(PourTeaRedesignV1),
        typeof(CombatAdjustmentRedesignV1),
        typeof(SweepKickRedesignV1),
        typeof(TeaStormRedesignV1),
        typeof(MetabolicAccelerationRedesignV1),
        typeof(GuwaaRedesignV1),
        typeof(FurinKazanChadoRedesignV1),
        typeof(DecidedOutcomeRedesignV1),
        typeof(ChadoFurinKazanRedesignV1),
        typeof(ClankDrinkTeaRedesignV1)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public ChadoSecretRedesignV1()
        : base(
            nameof(ChadoSecretRedesignV1),
            nameof(ChadoCard),
            1,
            CardType.Skill,
            CardRarity.Uncommon,
            TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        List<CardModel> choices = CandidateTypes
            .Select(type => ModelDb.AllCards.First(card => card.GetType() == type))
            .ToList()
            .StableShuffle(Owner.RunState.Rng.CombatCardGeneration)
            .Take(3)
            .Select(card => CombatState!.CreateCard(card, Owner))
            .ToList();
        CardModel? selected = await CardSelectCmd.FromChooseACardScreen(
            choiceContext,
            choices,
            Owner,
            canSkip: false);
        if (selected != null)
        {
            selected.SetToFreeThisTurn();
            await CardPileCmd.AddGeneratedCardToCombat(selected, PileType.Hand, Owner);
        }
    }

    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
}

public sealed class MaskRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(9, ValueProp.Move)];

    public MaskRedesignV1()
        : base(nameof(MaskRedesignV1), nameof(ShieldFromNothing), 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<ShuffleBlockPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Block.BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4);
}

public sealed class ExecutionMoveRedesignV1 : ArchivedRedesignV1Card
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];

    public ExecutionMoveRedesignV1()
        : base(nameof(ExecutionMoveRedesignV1), nameof(AssassinationFist), 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        IEnumerable<CardModel> selected = await CardSelectCmd.FromCombatPile(
            choiceContext,
            PileType.Draw.GetPile(Owner),
            Owner,
            new CardSelectorPrefs(SelectionScreenPrompt, DynamicVars.Cards.IntValue));
        foreach (CardModel card in selected)
        {
            CardCmd.ApplyKeyword(card, CardKeyword.Retain);
            await CardPileCmd.Add(card, PileType.Hand);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}

public sealed class WasshoiRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new RepeatVar(2)];

    public WasshoiRedesignV1()
        : base(nameof(WasshoiRedesignV1), nameof(NinjaGreeting), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        IReadOnlyList<CardModel> cards = PileType.Draw.GetPile(Owner).Cards;
        if (cards.Count == 0)
        {
            return;
        }
        CardModel top = cards[0];

        if (top.Type == CardType.Attack)
        {
            var power = (WasshoiDuplicationPower)ModelDb.Power<WasshoiDuplicationPower>().ToMutable();
            power.Arm(top);
            await PowerCmd.Apply(
                choiceContext,
                power,
                Owner.Creature,
                DynamicVars.Repeat.IntValue - 1,
                Owner.Creature,
                this);
        }

        await CardCmd.AutoPlay(choiceContext, top, null);
    }

    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1);
}

public sealed class ObserveBattleRedesignV1 : ArchivedRedesignV1Card
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<VulnerablePower>(2), new PowerVar<WeakPower>(2)];

    public ObserveBattleRedesignV1()
        : base(nameof(ObserveBattleRedesignV1), nameof(LockOn), 1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        foreach (Creature enemy in CombatState!.HittableEnemies.ToList())
        {
            await PowerCmd.Apply<VulnerablePower>(
                choiceContext,
                enemy,
                DynamicVars.Vulnerable.BaseValue,
                Owner.Creature,
                this);
            await PowerCmd.Apply<WeakPower>(
                choiceContext,
                enemy,
                DynamicVars.Weak.BaseValue,
                Owner.Creature,
                this);
        }
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class EnduranceRedesignV1 : ArchivedRedesignV1Card
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(20, ValueProp.Move), new EnergyVar(2)];

    public EnduranceRedesignV1()
        : base(nameof(EnduranceRedesignV1), nameof(GreatUke), 3, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<EnergyNextTurnPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Energy.BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(5);
}

public sealed class EmptyMindRedesignV1 : ArchivedRedesignV1Card
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Breath", 3)];

    public EmptyMindRedesignV1()
        : base(nameof(EmptyMindRedesignV1), nameof(ZazenDrink), 0, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CardPileCmd.Shuffle(choiceContext, Owner);
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
    }

    protected override void OnUpgrade() => DynamicVars["Breath"].UpgradeValueBy(1);
}

public sealed class GauntletRedesignV1 : ArchivedRedesignV1Card
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(14, ValueProp.Move), new CardsVar(1)];

    public GauntletRedesignV1()
        : base(nameof(GauntletRedesignV1), "IronShirtRedesignV1", 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(5);
}

public sealed class BloodTearsRedesignV1 : ArchivedRedesignV1Card
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new EnergyVar(1), new CardsVar(1), new HpLossVar(2)];

    public BloodTearsRedesignV1()
        : base(nameof(BloodTearsRedesignV1), nameof(BloodTears), 0, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, Owner);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
        await PowerCmd.Apply<BloodTearsRedesignPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.HpLoss.BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Energy.UpgradeValueBy(1);
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}

public sealed class BladeCycleRedesignV1 : RedesignV1UncommonCard
{
    public BladeCycleRedesignV1()
        : base(nameof(BladeCycleRedesignV1), "ShurikenBarrage", 2, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<BladeCyclePower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
