using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
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
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(NinjaSlayerCardPool), Inherit = true)]
public abstract class RedesignV1CommonCard(
    string id,
    string art,
    int cost,
    CardType type,
    TargetType target) : NinjaSlayerRedesignCardTemplate(
        new NinjaSlayerCardSpec(id, cost, type, CardRarity.Common, target, true), art);

public sealed class CountermeasureRedesignV1 : ArchivedRedesignV1Card
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(10, ValueProp.Move), new DynamicVar("BlockPerKarate", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<KaratePower>()];

    public CountermeasureRedesignV1()
        : base(
            nameof(CountermeasureRedesignV1),
            nameof(OpeningGuard),
            2,
            CardType.Skill,
            CardRarity.Basic,
            TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        CreatureCmd.GainBlock(
            Owner.Creature,
            DynamicVars.Block.BaseValue
                + Owner.Creature.GetPowerAmount<KaratePower>() * DynamicVars["BlockPerKarate"].IntValue,
            DynamicVars.Block.Props,
            cardPlay);

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2);
        DynamicVars["BlockPerKarate"].UpgradeValueBy(1);
    }
}

public sealed class SpiralRoundhouseJumpRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(6, ValueProp.Move), new DynamicVar("Stock", 1)];

    public SpiralRoundhouseJumpRedesignV1()
        : base(nameof(SpiralRoundhouseJumpRedesignV1), nameof(ShurikenSpread), 1, CardType.Attack, TargetType.AllEnemies) { }

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
            .TargetingAllOpponents(CombatState!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars["Stock"].UpgradeValueBy(1);
    }
}

public sealed class HiddenEdgeRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Stock", 2), new PowerVar<FocusPower>(3)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<ShurikenOrb>(), HoverTipFactory.FromPower<FocusPower>()];

    public HiddenEdgeRedesignV1()
        : base(nameof(HiddenEdgeRedesignV1), nameof(ShurikenStock), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
        await PowerCmd.Apply<HiddenEdgeTemporaryFocusPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(FocusPower)].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => DynamicVars[nameof(FocusPower)].UpgradeValueBy(1);
}

public sealed class BladeReserveRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Stock", 2), new CardsVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromOrb<ShurikenOrb>()];

    public BladeReserveRedesignV1()
        : base(nameof(BladeReserveRedesignV1), nameof(ShurikenCard), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ShurikenOrb.AddStock(choiceContext, Owner, DynamicVars["Stock"].IntValue);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.IntValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars["Stock"].UpgradeValueBy(1);
}

public sealed class PourTeaRedesignV1 : RedesignV1CommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(6, ValueProp.Move), new DynamicVar("Breath", 1)];

    public PourTeaRedesignV1()
        : base(nameof(PourTeaRedesignV1), nameof(PourTea), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<PourTeaNextTurnPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Breath"].IntValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
    }
}

public sealed class ChadoStillnessRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Breath", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(CardKeyword.Retain)];

    public ChadoStillnessRedesignV1()
        : base(nameof(ChadoStillnessRedesignV1), nameof(Meditation), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
        await PowerCmd.Apply<ChadoRetainPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => DynamicVars["Breath"].UpgradeValueBy(1);
}

public sealed class OverexertRedesignV1 : ArchivedRedesignV1Card
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(3)];

    public OverexertRedesignV1()
        : base(
            nameof(OverexertRedesignV1),
            nameof(PursuitStrike),
            0,
            CardType.Skill,
            CardRarity.Common,
            TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.IntValue, Owner);
        await NinjaSlayerActions.AddGeneratedCard<Wound>(Owner, PileType.Draw);
        await NinjaSlayerActions.AddGeneratedCard<Wound>(Owner, PileType.Draw);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}

public sealed class GuidingFlameRedesignV1 : RedesignV1CommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(16, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public GuidingFlameRedesignV1()
        : base(nameof(GuidingFlameRedesignV1), nameof(BurningStrike), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await NinjaSlayerActions.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Draw);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}

public sealed class SatsubatsuRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(27, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public SatsubatsuRedesignV1()
        : base(nameof(SatsubatsuRedesignV1), nameof(RedBlackFlame), 3, CardType.Attack, TargetType.AnyEnemy) { }

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
        await NinjaSlayerActions.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Hand);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(6);
}

public sealed class ThrowKunaiRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(9, ValueProp.Move), new CardsVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public ThrowKunaiRedesignV1()
        : base(nameof(ThrowKunaiRedesignV1), nameof(ThrowKunai), 1, CardType.Attack, TargetType.AnyEnemy) { }

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
        await NinjaSlayerActions.ChooseAndDiscardOne(choiceContext, Owner, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}

public sealed class ObserverGuardRedesignV1 : ArchivedRedesignV1Card
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(5, ValueProp.Move), new CardsVar(3), new DynamicVar("Draw", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public ObserverGuardRedesignV1()
        : base(
            nameof(ObserverGuardRedesignV1),
            nameof(LuckyStrike),
            1,
            CardType.Skill,
            CardRarity.Common,
            TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
        await CardPileCmd.Draw(choiceContext, DynamicVars["Draw"].BaseValue, Owner);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
        DynamicVars.Cards.UpgradeValueBy(1);
    }
}

public sealed class ReflexGuardRedesignV1 : ArchivedRedesignV1Card
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Sly, CardKeyword.Retain];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(11, ValueProp.Move)];

    public ReflexGuardRedesignV1()
        : base(
            nameof(ReflexGuardRedesignV1),
            nameof(DiscardDefense),
            2,
            CardType.Skill,
            CardRarity.Common,
            TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4);
}

public sealed class ReadyStanceRedesignV1 : RedesignV1CommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(3)];

    public ReadyStanceRedesignV1()
        : base(nameof(ReadyStanceRedesignV1), nameof(KataDrill), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
        await NinjaSlayerActions.ChooseAndDiscardOne(choiceContext, Owner, this);
    }

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(2);
}

public sealed class StormFistRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<FinisherRedesignV1>()];

    public StormFistRedesignV1()
        : base(nameof(StormFistRedesignV1), nameof(TornadoFist), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        FinisherRedesignV1 punch = CombatState!.CreateCard<FinisherRedesignV1>(Owner);
        if (IsUpgraded)
        {
            CardCmd.Upgrade(punch);
        }

        await CardPileCmd.AddGeneratedCardToCombat(punch, PileType.Hand, Owner);
    }

    protected override void OnUpgrade() { }
}

public sealed class AbandonThoughtRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Breath", 2)];

    public AbandonThoughtRedesignV1()
        : base(nameof(AbandonThoughtRedesignV1), nameof(BrewTea), 0, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        IReadOnlyList<CardModel> cards = PileType.Draw.GetPile(Owner).Cards;
        if (cards.Count > 0)
        {
            await CardCmd.Exhaust(choiceContext, cards[0]);
        }

        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
    }

    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
}

public sealed class BodyguardRedesignV1 : ArchivedRedesignV1Card
{
    public override bool GainsBlock => true;
    protected override bool IsPlayable =>
        PileType.Hand.GetPile(Owner).Cards.All(card => card == this || card.Type != CardType.Skill);
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(9, ValueProp.Move)];

    public BodyguardRedesignV1()
        : base(nameof(BodyguardRedesignV1), nameof(RestGuard), 0, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4);
}

public sealed class LeftHeavyPunchRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(7, ValueProp.Move), new KarateVar(2)];

    public LeftHeavyPunchRedesignV1()
        : base(nameof(LeftHeavyPunchRedesignV1), nameof(MasochisticBliss), 1, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await PowerCmd.Apply<ChopStrikeNextTurnPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
        DynamicVars.Karate().UpgradeValueBy(1);
    }
}

public sealed class RightHeavyPunchRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(8, ValueProp.Move), new PowerVar<VulnerablePower>(1)];

    public RightHeavyPunchRedesignV1()
        : base(nameof(RightHeavyPunchRedesignV1), nameof(BangBangFist), 1, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        if (NinjaSlayerCombatMetrics.PreviousFinishedCardWasAttack(Owner)
            && cardPlay.Target is { IsAlive: true } target)
        {
            await PowerCmd.Apply<VulnerablePower>(
                choiceContext,
                target,
                DynamicVars.Vulnerable.BaseValue,
                Owner.Creature,
                this);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars.Vulnerable.UpgradeValueBy(1);
    }
}

public sealed class TrumpCardRedesignV1 : ArchivedRedesignV1Card
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(6, ValueProp.Move)];

    public TrumpCardRedesignV1()
        : base(
            nameof(TrumpCardRedesignV1),
            "Topdeck",
            1,
            CardType.Skill,
            CardRarity.Common,
            TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        CardModel? selected = (await CardSelectCmd.FromHand(
            choiceContext,
            Owner,
            new CardSelectorPrefs(SelectionScreenPrompt, 1),
            card => card != this,
            this)).FirstOrDefault();
        if (selected != null)
        {
            CardCmd.ApplyKeyword(selected, CardKeyword.Retain);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}

public sealed class HookRopeRedesignV1 : RedesignV1CommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<WeakPower>(1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), HoverTipFactory.FromPower<StrengthPower>()];

    public HookRopeRedesignV1()
        : base(nameof(HookRopeRedesignV1), nameof(NinjaWhip), 1, CardType.Skill, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int strengthLoss = Owner.Creature.GetPowerAmount<KaratePower>();
        if (strengthLoss > 0)
        {
            await PowerCmd.Apply<HookRopeStrengthDownPower>(
                choiceContext,
                cardPlay.Target!,
                strengthLoss,
                Owner.Creature,
                this);
        }
        await PowerCmd.Apply<WeakPower>(
            choiceContext,
            cardPlay.Target!,
            DynamicVars.Weak.BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Weak.UpgradeValueBy(1);
    }
}

public sealed class PalmThrustRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(5, ValueProp.Move), new RepeatVar(2)];

    public PalmThrustRedesignV1()
        : base(nameof(PalmThrustRedesignV1), nameof(PalmThrust), 1, CardType.Attack, TargetType.RandomEnemy) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
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
                    IReadOnlyList<Creature> enemies = CombatState!.HittableEnemies;
                    Creature? target = Owner.RunState.Rng.CombatTargets.NextItem(enemies);
                    if (target == null)
                    {
                        return true;
                    }

                    AttackCommand command = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                        .FromCard(this)
#else
                        .FromCard(this, cardPlay)
#endif
                        .WithDefectStrikeHitFx()
                        .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
                        .Targeting(target);
                    await command.Execute(choiceContext);
                    return CombatState.HittableEnemies.Count == 0;
                }));
    }

    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1);
}

public sealed class LuckyStrikeRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1), new DynamicVar("Draw", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public LuckyStrikeRedesignV1()
        : base(nameof(LuckyStrikeRedesignV1), "LuckyStrikeRedesignV1", 0, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
        await CardPileCmd.Draw(choiceContext, DynamicVars["Draw"].IntValue, Owner);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
        DynamicVars["Draw"].UpgradeValueBy(1);
    }
}

public sealed class CommonChopRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(5, ValueProp.Move), new KarateVar(1)];

    public CommonChopRedesignV1()
        : base(nameof(CommonChopRedesignV1), nameof(PerfectChop), 0, CardType.Attack, TargetType.AnyEnemy) { }

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
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
        if (!Keywords.Contains(CardKeyword.Exhaust) && !ExhaustOnNextPlay)
        {
            await CardPileCmd.Add(this, PileType.Draw, CardPilePosition.Top);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(1);
        DynamicVars.Karate().UpgradeValueBy(1);
    }
}

public sealed class IronBodyRedesignV1 : RedesignV1CommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(15, ValueProp.Move), new KarateVar(4)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<KaratePower>()];

    public IronBodyRedesignV1()
        : base(nameof(IronBodyRedesignV1), nameof(RestGuard), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        Creature? target = Owner.RunState.Rng.CombatTargets.NextItem(CombatState!.HittableEnemies);
        if (target != null)
        {
            await PowerCmd.Apply<KaratePower>(
                choiceContext,
                target,
                DynamicVars.Karate().BaseValue,
                Owner.Creature,
                this);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}
