using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
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
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

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
    protected override bool HasEnergyCostX => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("StockPerX", 2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<ShurikenStockPower>()];

    public FlyingBladesComeRedesignV1()
        : base(nameof(FlyingBladesComeRedesignV1), nameof(ShurikenCleave), 0, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<ShurikenStockPower>(
            choiceContext,
            Owner.Creature,
            ResolveEnergyXValue() * DynamicVars["StockPerX"].IntValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars["StockPerX"].UpgradeValueBy(1);
}

public sealed class ShurikenGenerationRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Stock", 3), new CardsVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<ShurikenStockPower>()];

    public ShurikenGenerationRedesignV1()
        : base(nameof(ShurikenGenerationRedesignV1), nameof(ShurikenGuard), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<ShurikenStockPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Stock"].BaseValue,
            Owner.Creature,
            this);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars["Stock"].UpgradeValueBy(1);
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
        [new DamageVar(11, ValueProp.Move), new PowerVar<WeakPower>(1), new DynamicVar("Breath", 2)];

    public SweepKickRedesignV1()
        : base(nameof(SweepKickRedesignV1), nameof(Evade), 2, CardType.Attack, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("SlowAttack", Owner.Character.AttackAnimDelay)
            .TargetingAllOpponents(CombatState!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);

        foreach (Creature enemy in CombatState!.HittableEnemies.ToList())
        {
            await PowerCmd.Apply<WeakPower>(
                choiceContext,
                enemy,
                DynamicVars.Weak.BaseValue,
                Owner.Creature,
                this);
        }

        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars.Weak.UpgradeValueBy(1);
    }
}

public sealed class TeaStormRedesignV1 : RedesignV1UncommonCard
{
    protected override bool HasEnergyCostX => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("BreathPerX", 2)];

    public TeaStormRedesignV1()
        : base(nameof(TeaStormRedesignV1), nameof(SteepTea), 0, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int x = ResolveEnergyXValue() + (IsUpgraded ? 1 : 0);
        return ChadoBreathCmd.Apply(Owner, x * DynamicVars["BreathPerX"].IntValue, this);
    }

    protected override void OnUpgrade() { }
}

public sealed class MetabolicAccelerationRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Heal", 5)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<ChadoEnergyRedesignV1>()];

    public MetabolicAccelerationRedesignV1()
        : base(nameof(MetabolicAccelerationRedesignV1), nameof(DrinkTea), 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<MetabolicAccelerationPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Heal"].BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars["Heal"].UpgradeValueBy(3);
}

public sealed class GuwaaRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Breath", 2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<Wound>()];

    public GuwaaRedesignV1()
        : base(nameof(GuwaaRedesignV1), nameof(IBlock), 0, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await NinjaSlayerActions.AddGeneratedCard<Wound>(Owner, PileType.Discard);
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
    }

    protected override void OnUpgrade() => DynamicVars["Breath"].UpgradeValueBy(1);
}

public sealed class AlabamaDropRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(20, ValueProp.Move), new PowerVar<VulnerablePower>(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<Dazed>()];

    public AlabamaDropRedesignV1()
        : base(nameof(AlabamaDropRedesignV1), nameof(AlabamaDrop), 2, CardType.Attack, TargetType.AnyEnemy) { }

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
            await CreatureCmd.Damage(
                choiceContext,
                cardPlay.Target!,
                DynamicVars.Damage.BaseValue,
                ValueProp.Move,
                this
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , cardPlay
#endif
            );
            if (cardPlay.Target!.IsAlive)
            {
                await PowerCmd.Apply<VulnerablePower>(
                    choiceContext,
                    cardPlay.Target,
                    DynamicVars.Vulnerable.BaseValue,
                    Owner.Creature,
                    this);
            }
        }

        await AlabamaDropAnimation.Play(Owner.Creature, cardPlay.Target!, ResolveImpact);
        await ResolveImpact();
        await NinjaSlayerActions.AddGeneratedCard<Dazed>(Owner, PileType.Draw);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4);
        DynamicVars.Vulnerable.UpgradeValueBy(1);
    }
}

public sealed class AdversityCarapaceRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CardsVar(2), new DynamicVar("StatusDraw", 1)];

    public AdversityCarapaceRedesignV1()
        : base(nameof(AdversityCarapaceRedesignV1), nameof(IronShirt), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int remaining = DynamicVars.Cards.IntValue;
        while (remaining-- > 0)
        {
            CardModel? drawn = (await CardPileCmd.Draw(choiceContext, 1, Owner)).FirstOrDefault();
            if (drawn == null)
            {
                break;
            }

            if (drawn.Type == CardType.Status)
            {
                remaining += DynamicVars["StatusDraw"].IntValue;
            }
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
        DynamicVars["StatusDraw"].UpgradeValueBy(1);
    }
}

public sealed class RedBlackFlameAttackRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(12, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public RedBlackFlameAttackRedesignV1()
        : base(nameof(RedBlackFlameAttackRedesignV1), nameof(BurningCard), 1, CardType.Attack, TargetType.AnyEnemy) { }

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
        await NinjaSlayerActions.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Draw);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(5);
}

public sealed class TechniqueSearchRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(5), new DynamicVar("Draw", 2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public TechniqueSearchRedesignV1()
        : base(nameof(TechniqueSearchRedesignV1), nameof(ReadyBlade), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
        await CardPileCmd.Draw(choiceContext, DynamicVars["Draw"].BaseValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(3);
}

public sealed class DoubleForceRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(4)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public DoubleForceRedesignV1()
        : base(nameof(DoubleForceRedesignV1), nameof(ColdBrew), 1, CardType.Skill, TargetType.Self) { }

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

public sealed class NinjaSixthSenseRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(4)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public NinjaSixthSenseRedesignV1()
        : base(nameof(NinjaSixthSenseRedesignV1), "SmokeRead", 0, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int generatedStatuses = CombatManager.Instance.History.Entries
            .OfType<CardGeneratedEntry>()
            .Count(entry => entry.Creator == Owner && entry.Card.Type == CardType.Status);
        return ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue + generatedStatuses);
    }

    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
}

public sealed class DecidedOutcomeRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromKeyword(NinjaSlayerKeywords.Scry)];

    public DecidedOutcomeRedesignV1()
        : base(nameof(DecidedOutcomeRedesignV1), nameof(KarateFinish), 1, CardType.Power, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<ScryStatusExhaustPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
        await ScryCmd.Execute(choiceContext, Owner, DynamicVars.Cards.IntValue);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(2);
}

public sealed class AetherEnergyRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Innate];

    public AetherEnergyRedesignV1()
        : base(nameof(AetherEnergyRedesignV1), nameof(Contraption), 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<AetherEnergyPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);

    protected override void OnUpgrade() { }
}

public sealed class AbyssStrengthRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(7)];
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
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(3)];

    public DisadvantageTacticsRedesignV1()
        : base(nameof(DisadvantageTacticsRedesignV1), nameof(MurderFist), 1, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            CombatState!.HittableEnemies.Count * DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(1);
}

public sealed class KarateTrainingRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(3)];

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

public sealed class JujutsuStanceRedesignV1 : RedesignV1UncommonCard, IReturnToHandAfterPlay
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];
    protected override bool IsPlayable => Owner.Creature.GetPowerAmount<KaratePower>() >= DynamicVars["KarateCost"].IntValue;
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("KarateCost", 3), new PowerVar<DexterityPower>(1)];

    public JujutsuStanceRedesignV1()
        : base(nameof(JujutsuStanceRedesignV1), nameof(ForgoStrength), 0, CardType.Skill, TargetType.Self) { }

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

public sealed class IyaRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(9, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<IyaEchoRedesignV1>()];

    public IyaRedesignV1()
        : base(nameof(IyaRedesignV1), nameof(IyaIronSlashWave), 1, CardType.Attack, TargetType.AnyEnemy) { }

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

        IyaEchoRedesignV1 echo = CombatState!.CreateCard<IyaEchoRedesignV1>(Owner);
        if (IsUpgraded)
        {
            CardCmd.Upgrade(echo);
        }

        await CardPileCmd.AddGeneratedCardToCombat(echo, PileType.Hand, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4);
}

public sealed class BackBridgeRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(7, ValueProp.Move), new DynamicVar("MaxKarate", 3), new DynamicVar("BlockPerKarate", 4)];

    public BackBridgeRedesignV1()
        : base(nameof(BackBridgeRedesignV1), nameof(BackBridge), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        KaratePower? karate = Owner.Creature.GetPower<KaratePower>();
        int consumed = Math.Min(karate?.Amount ?? 0, DynamicVars["MaxKarate"].IntValue);
        if (karate == null || consumed <= 0)
        {
            return;
        }

        await PowerCmd.ModifyAmount(choiceContext, karate, -consumed, Owner.Creature, this);
        await CreatureCmd.GainBlock(
            Owner.Creature,
            consumed * DynamicVars["BlockPerKarate"].IntValue,
            ValueProp.Move,
            cardPlay);
    }

    protected override void OnUpgrade() => DynamicVars["BlockPerKarate"].UpgradeValueBy(1);
}

public sealed class ChadoSecretRedesignV1 : RedesignV1UncommonCard
{
    private static readonly Type[] CandidateTypes =
    [
        typeof(PourTeaRedesignV1),
        typeof(CombatAdjustmentRedesignV1),
        typeof(SweepKickRedesignV1),
        typeof(TeaStormRedesignV1),
        typeof(MetabolicAccelerationRedesignV1),
        typeof(GuwaaRedesignV1),
        typeof(KarateRallyRedesignV1),
        typeof(FurinKazanChadoRedesignV1),
        typeof(EmptyMindRedesignV1)
    ];

    public ChadoSecretRedesignV1()
        : base(nameof(ChadoSecretRedesignV1), nameof(ChadoCard), 2, CardType.Skill, TargetType.Self) { }

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

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

public sealed class MaskRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(8, ValueProp.Move)];

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

public sealed class ExecutionMoveRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];

    public ExecutionMoveRedesignV1()
        : base(nameof(ExecutionMoveRedesignV1), nameof(AssassinationFist), 1, CardType.Skill, TargetType.Self) { }

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
        CardModel? top = PileType.Draw.GetPile(Owner).Cards.FirstOrDefault();
        if (top == null)
        {
            return;
        }

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

public sealed class ObserveBattleRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<VulnerablePower>(1), new PowerVar<WeakPower>(1)];

    public ObserveBattleRedesignV1()
        : base(nameof(ObserveBattleRedesignV1), nameof(LockOn), 1, CardType.Skill, TargetType.AllEnemies) { }

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

    protected override void OnUpgrade()
    {
        DynamicVars.Vulnerable.UpgradeValueBy(1);
        DynamicVars.Weak.UpgradeValueBy(1);
    }
}

public sealed class EnduranceRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(20, ValueProp.Move), new EnergyVar(2)];

    public EnduranceRedesignV1()
        : base(nameof(EnduranceRedesignV1), nameof(GreatUke), 3, CardType.Skill, TargetType.Self) { }

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

public sealed class EmptyMindRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Breath", 3)];

    public EmptyMindRedesignV1()
        : base(nameof(EmptyMindRedesignV1), nameof(ZazenDrink), 0, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CardPileCmd.Shuffle(choiceContext, Owner);
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue, this);
    }

    protected override void OnUpgrade() => DynamicVars["Breath"].UpgradeValueBy(1);
}

public sealed class GauntletRedesignV1 : RedesignV1UncommonCard
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(14, ValueProp.Move), new CardsVar(1)];

    public GauntletRedesignV1()
        : base(nameof(GauntletRedesignV1), "IronShirtRedesignV1", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(5);
}

public sealed class BloodTearsRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new EnergyVar(1), new CardsVar(1), new HpLossVar(2)];

    public BloodTearsRedesignV1()
        : base(nameof(BloodTearsRedesignV1), nameof(BloodTears), 0, CardType.Power, TargetType.Self) { }

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
        : base(nameof(BladeCycleRedesignV1), "ShurikenBarrage", 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<BladeCyclePower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
