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
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.RedesignV1;

[RegisterCard(typeof(NinjaSlayerCardPool), Inherit = true)]
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
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<SoarPower>(), HoverTipFactory.FromOrb<ShurikenOrb>()];

    public HellTornadoRedesignV1()
        : base(nameof(HellTornadoRedesignV1), nameof(HellTornado), 3, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiLongjuanquanEvent);
        await PowerCmd.Apply<SoarPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
        if (ShurikenOrb.Find(Owner) is { } stock)
        {
            await ShurikenOrb.AddStock(choiceContext, Owner, stock.StackCount);
        }

        await PowerCmd.Apply<HellTornadoRedesignPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}

public sealed class GiantShurikenRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<StrongShurikenTokenRedesignV1>(), HoverTipFactory.FromPower<StarlessNightRedesignPower>()];

    public GiantShurikenRedesignV1()
        : base(nameof(GiantShurikenRedesignV1), nameof(StarlessNight), 2, CardType.Power, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        StarlessNightRedesignPower? power = await PowerCmd.Apply<StarlessNightRedesignPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
        if (power != null && IsUpgraded)
        {
            power.GenerateUpgradedToken = true;
        }
    }

    protected override void OnUpgrade() { }
}

public sealed class KarateRallyRedesignV1 : ArchivedRedesignV1Card
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new EnergyVar(1), new DynamicVar("NextEnergy", 1), new DynamicVar("Breath", 1)];

    public KarateRallyRedesignV1()
        : base(nameof(KarateRallyRedesignV1), nameof(KarateRollingStone), 0, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

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
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Innate, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<ArtifactPower>(1), new DynamicVar("Breath", 2)];

    public FurinKazanChadoRedesignV1()
        : base(nameof(FurinKazanChadoRedesignV1), nameof(TeaSamadhi), 2, CardType.Skill, TargetType.Self) { }

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
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<EvasionPower>(), HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public HardItOutRedesignV1()
        : base(nameof(HardItOutRedesignV1), nameof(KarateWall), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<EvasionPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
        await NinjaSlayerActions.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Hand);
        await NinjaSlayerActions.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Hand);
    }

    protected override void OnUpgrade() { }
}

public sealed class NarakuFormRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public NarakuFormRedesignV1()
        : base(nameof(NarakuFormRedesignV1), "NarakuWithin", 3, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<NarakuFormRedesignPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);

    protected override void OnUpgrade() { }
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

public sealed class TornadoFistRedesignV1 : RedesignV1UncommonCard
{
    protected override bool HasEnergyCostX => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(4, ValueProp.Move), new PowerVar<VulnerablePower>(1), new DynamicVar("Threshold", 4)];

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
                    AttackCommand command;
                    using (CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.PreserveDamage))
                    {
                        command = await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                            .FromCard(this)
#else
                            .FromCard(this, cardPlay)
#endif
                            .WithDefectStrikeHitFx()
                            .WithAttackerAnim(TornadoFistSpinAnimation.TriggerName, TornadoFistSpinAnimation.TurnSeconds)
                            .TargetingAllOpponents(CombatState!)
                            .Execute(choiceContext);
                    }

                    if (hits >= DynamicVars["Threshold"].IntValue)
                    {
                        foreach (Creature target in command.Results
                                     .SelectMany(results => results)
                                     .Select(result => result.Receiver)
                                     .Where(target => target.IsAlive && target.Side != Owner.Creature.Side)
                                     .Distinct())
                        {
                            await PowerCmd.Apply<VulnerablePower>(
                                choiceContext,
                                target,
                                DynamicVars.Vulnerable.BaseValue,
                                Owner.Creature,
                                this);
                        }
                    }

                    return CombatState!.HittableEnemies.Count == 0;
                }));
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2);
}

public sealed partial class ClankDrinkTeaRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(30, ValueProp.Move)];
    public int KarateMultiplier => IsUpgraded ? 5 : 4;

    public ClankDrinkTeaRedesignV1()
        : base(nameof(ClankDrinkTeaRedesignV1), nameof(ClankDrinkTea), 4, CardType.Attack, TargetType.AnyEnemy) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

#if NINJASLAYER_LEGACY_DAMAGE_API
    public override decimal ModifyDamageAdditive(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource) =>
        ModifyDamageAdditiveCore(target, amount, props, dealer, cardSource);
#else
    public override decimal ModifyDamageAdditive(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay) =>
        ModifyDamageAdditiveCore(target, amount, props, dealer, cardSource);
#endif

    private static decimal ModifyDamageAdditiveCore(
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
        [new DamageVar(7, ValueProp.Move), new KarateVar(2), new CardsVar(3)];

    public ChopRedesignV1()
        : base(nameof(ChopRedesignV1), nameof(Chop), 1, CardType.Attack, TargetType.AnyEnemy) { }

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
    }

    public override async Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner
            || cardPlay.Card.Type != CardType.Skill
            || Pile?.Type == PileType.Hand)
        {
            return;
        }

        int skills = CombatManager.Instance.History.CardPlaysFinished.Count(entry =>
            entry.HappenedThisTurn(CombatState!)
            && entry.CardPlay.Card.Type == CardType.Skill
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            && entry.CardPlay.Card.Owner == Owner);
#else
            && entry.CardPlay.Player == Owner);
#endif
        if (skills > 0 && skills % DynamicVars.Cards.IntValue == 0)
        {
            await CardPileCmd.Add(this, PileType.Hand);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars.Karate().UpgradeValueBy(1);
    }
}

public sealed class DragonFlyingKickRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(18, ValueProp.Move), new CardsVar(10), new DynamicVar("Breath", 2)];

    public DragonFlyingKickRedesignV1()
        : base(nameof(DragonFlyingKickRedesignV1), nameof(NinjaSlayerFootwork), 3, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiDragonFlyingKickEvent);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithDefectStrikeHitFx()
            .WithNoAttackerAnim()
            .AfterAttackerAnim(() => JumpAnimation.Play(Owner.Creature))
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
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

[RegisterCard(typeof(TokenCardPool))]
public sealed class StraightKiRedesignV1 : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(StraightKiRedesignV1),
        2,
        CardType.Attack,
        CardRarity.Token,
        TargetType.AnyEnemy,
        false,
        nameof(StraightKi));

    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(37, ValueProp.Move)];

    public StraightKiRedesignV1()
        : base(Spec) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithHeavyBluntHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(13);
}

public sealed class ChadoFurinKazanRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];

    public ChadoFurinKazanRedesignV1()
        : base(nameof(ChadoFurinKazanRedesignV1), nameof(SenchaStorm), 1, CardType.Skill, TargetType.Self) { }

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
    protected override bool IsPlayable => NinjaSlayerActions.HasRedesignChadoInHand(Owner);
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Hits", 2), new DynamicVar("Threshold", 10)];

    public GreatUkeRedesignV1()
        : base(nameof(GreatUkeRedesignV1), nameof(OmnidirectionalThrow), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!await NinjaSlayerActions.ChooseAndExhaustRedesignChado(choiceContext, Owner, this))
        {
            return;
        }

        await PowerCmd.Apply<GreatUkeRedesignPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Hits"].BaseValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() { }
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
