using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public abstract class RedesignV1RareCard(
    string id,
    string art,
    int cost,
    CardType type,
    TargetType target) : NinjaSlayerRedesignCardTemplate(
        new NinjaSlayerCardSpec(id, cost, type, CardRarity.Rare, target, true),
        art);

public sealed class HellTornadoRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("TriggersPerStock", 2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<ShurikenStockPower>()];

    public HellTornadoRedesignV1()
        : base(nameof(HellTornadoRedesignV1), nameof(HellTornado), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ShurikenStockPower? stock = Owner.Creature.GetPower<ShurikenStockPower>();
        int stockAmount = stock?.Amount ?? 0;
        if (stock == null || stockAmount <= 0)
        {
            return;
        }

        await PowerCmd.ModifyAmount(choiceContext, stock, -stockAmount, Owner.Creature, this);
        int shots = stockAmount * DynamicVars["TriggersPerStock"].IntValue;
        for (int index = 0; index < shots; index++)
        {
            Creature? target = Owner.RunState.Rng.CombatTargets.NextItem(CombatState!.HittableEnemies);
            if (target == null)
            {
                break;
            }

            await ShurikenCombat.TriggerStockShot(choiceContext, Owner.Creature, target, this);
        }
    }

    protected override void OnUpgrade() => DynamicVars["TriggersPerStock"].UpgradeValueBy(1);
}

public sealed class GiantShurikenRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Stock", 4), new DynamicVar("BonusDamage", 3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<ShurikenStockPower>(), HoverTipFactory.FromPower<ShurikenDamagePower>()];

    public GiantShurikenRedesignV1()
        : base(nameof(GiantShurikenRedesignV1), nameof(GiantShurikenCard), 4, CardType.Power, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<ShurikenStockPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Stock"].BaseValue,
            Owner.Creature,
            this);
        await PowerCmd.Apply<ShurikenDamagePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["BonusDamage"].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() { }
}

public sealed class KarateRallyRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new EnergyVar(1), new DynamicVar("NextEnergy", 1), new DynamicVar("Breath", 1)];

    public KarateRallyRedesignV1()
        : base(nameof(KarateRallyRedesignV1), nameof(KarateRollingStone), 0, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, Owner);
        await PowerCmd.Apply<EnergyNextTurnPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["NextEnergy"].BaseValue,
            Owner.Creature,
            this);
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["NextEnergy"].UpgradeValueBy(1);
        DynamicVars["Breath"].UpgradeValueBy(2);
    }
}

public sealed class FurinKazanChadoRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<ArtifactPower>(1), new DynamicVar("Breath", 2)];

    public FurinKazanChadoRedesignV1()
        : base(nameof(FurinKazanChadoRedesignV1), nameof(TeaSamadhi), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<ArtifactPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(ArtifactPower)].BaseValue,
            Owner.Creature,
            this);
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
    }

    protected override void OnUpgrade() => DynamicVars["Breath"].UpgradeValueBy(1);
}

public sealed class HardItOutRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Threshold", 7)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<Wound>()];

    public HardItOutRedesignV1()
        : base(nameof(HardItOutRedesignV1), nameof(KarateWall), 2, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<HardItOutPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Threshold"].BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars["Threshold"].UpgradeValueBy(3);
}

public sealed class NarakuFormRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public NarakuFormRedesignV1()
        : base(nameof(NarakuFormRedesignV1), "NarakuWithin", 3, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<NarakuFormRedesignPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(1);
}

public sealed class OnlyKarateRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public OnlyKarateRedesignV1()
        : base(nameof(OnlyKarateRedesignV1), nameof(OneBodyOneSoul), 1, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int current = Owner.Creature.GetPowerAmount<KaratePower>();
        return current > 0
            ? PowerCmd.Apply<KaratePower>(choiceContext, Owner.Creature, current, Owner.Creature, this)
            : Task.CompletedTask;
    }

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}

public sealed class TornadoFistRedesignV1 : RedesignV1RareCard
{
    protected override bool HasEnergyCostX => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(7, ValueProp.Move)];

    public TornadoFistRedesignV1()
        : base(nameof(TornadoFistRedesignV1), "DragonTornado", 0, CardType.Attack, TargetType.AllEnemies) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiLongjuanquanEvent);
        int hits = ResolveEnergyXValue();
        return this.ExecuteSequenceWithFinisher(
            choiceContext,
            cardPlay,
            hits,
            () => NinjaSlayerXAttackSequence.Run(
                Owner.Creature,
                hits,
                TornadoFistSpinAnimation.TurnSeconds,
                CombatActionTimingRuntime.AttackSeconds + CombatActionTimingRuntime.DamageRecoverySeconds,
                async _ =>
                {
                    using (CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.PreserveDamage))
                    {
                        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
                            .FromCard(this, cardPlay)
                            .WithDefectStrikeHitFx()
                            .WithAttackerAnim(TornadoFistSpinAnimation.TriggerName, TornadoFistSpinAnimation.TurnSeconds)
                            .TargetingAllOpponents(CombatState!)
                            .Execute(choiceContext);
                    }

                    return CombatState!.HittableEnemies.Count == 0;
                }));
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}

public sealed partial class ClankDrinkTeaRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(30, ValueProp.Move)];
    public int KarateMultiplier => IsUpgraded ? 4 : 3;

    public ClankDrinkTeaRedesignV1()
        : base(nameof(ClankDrinkTeaRedesignV1), nameof(ClankDrinkTea), 3, CardType.Attack, TargetType.AnyEnemy) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

    private decimal ModifyDamageAdditiveCore(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? source) => 0;

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(10);
}

public sealed class ChopRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(4, ValueProp.Move), new KarateVar(1), new CardsVar(3)];

    public ChopRedesignV1()
        : base(nameof(ChopRedesignV1), nameof(Chop), 0, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
    }

    public override async Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner
            || cardPlay.Card.Type != CardType.Attack
            || Pile?.Type == PileType.Hand)
        {
            return;
        }

        int attacks = CombatManager.Instance.History.CardPlaysFinished.Count(entry =>
            entry.HappenedThisTurn(CombatState!)
            && entry.CardPlay.Card.Type == CardType.Attack
            && GameCompatibility.CardPlays.GetPlayer(entry.CardPlay) == Owner);
        if (attacks > 0 && attacks % DynamicVars.Cards.IntValue == 0)
        {
            await CardPileCmd.Add(this, PileType.Hand);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(1);
        DynamicVars.Karate().UpgradeValueBy(1);
    }
}

public sealed class DragonFlyingKickRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(18, ValueProp.Move), new CardsVar(10)];

    public DragonFlyingKickRedesignV1()
        : base(nameof(DragonFlyingKickRedesignV1), nameof(NinjaSlayerFootwork), 2, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiDragonFlyingKickEvent);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithNoAttackerAnim()
            .AfterAttackerAnim(() => JumpAnimation.Play(Owner.Creature))
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() { }
}

public sealed class KillingIntentRedesignV1 : RedesignV1RareCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(9, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<StraightKiRedesignV1>()];

    public KillingIntentRedesignV1()
        : base(nameof(KillingIntentRedesignV1), nameof(KillingIntent), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        KillingIntentRedesignPower? power = await PowerCmd.Apply<KillingIntentRedesignPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
        if (power != null && IsUpgraded)
        {
            power.GenerateUpgradedCard = true;
        }
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4);
}

public sealed class StraightKiRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(37, ValueProp.Move)];

    public StraightKiRedesignV1()
        : base(nameof(StraightKiRedesignV1), nameof(StraightKi), 2, CardType.Attack, TargetType.AnyEnemy) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(13);
}

public sealed class ChadoFurinKazanRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public ChadoFurinKazanRedesignV1()
        : base(nameof(ChadoFurinKazanRedesignV1), nameof(SenchaStorm), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        CardModel? selected = (await CardSelectCmd.FromHand(
            choiceContext,
            Owner,
            new CardSelectorPrefs(SelectionScreenPrompt, 1),
            card => card != this,
            this)).FirstOrDefault();
        if (selected == null)
        {
            return;
        }

        await CardPileCmd.AddGeneratedCardToCombat(
            selected.CreateClone(),
            PileType.Draw,
            Owner,
            CardPilePosition.Top);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class GreatUkeRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Hits", 2), new DynamicVar("Threshold", 10)];

    public GreatUkeRedesignV1()
        : base(nameof(GreatUkeRedesignV1), nameof(OmnidirectionalThrow), 2, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<GreatUkeRedesignPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Hits"].BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class NinjaGreetingRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Innate, CardKeyword.Ethereal, CardKeyword.Exhaust];

    public NinjaGreetingRedesignV1()
        : base(nameof(NinjaGreetingRedesignV1), nameof(StunStrike), 3, CardType.Skill, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.Stun(cardPlay.Target!);
        foreach (CardModel card in PileType.Hand.GetPile(Owner).Cards)
        {
            card.GiveSingleTurnRetain();
        }
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class ComposeHaikuRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [NinjaSlayerKeywords.Judgment];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Threshold", 30)];

    public ComposeHaikuRedesignV1()
        : base(nameof(ComposeHaikuRedesignV1), nameof(Recycle), 1, CardType.Skill, TargetType.AnyEnemy) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        cardPlay.Target!.CurrentHp < DynamicVars["Threshold"].IntValue
            ? CreatureCmd.Kill(cardPlay.Target, true)
            : Task.CompletedTask;

    protected override void OnUpgrade() => DynamicVars["Threshold"].UpgradeValueBy(10);
}
