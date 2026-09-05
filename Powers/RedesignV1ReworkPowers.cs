using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Combat.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class BladeSweepPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(ExhaustForShurikenPower));
}

public sealed class RecycledBladesPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(ExhaustForShurikenPower));

    public override Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        if (card.Owner != Owner.Player || ShurikenOrb.Find(Owner.Player!) is not null)
        {
            return Task.CompletedTask;
        }

        return AddStockAfterDiscard(choiceContext);
    }

    internal async Task AddStockAfterDiscard(PlayerChoiceContext choiceContext)
    {
        Flash();
        await ShurikenOrb.AddStock(choiceContext, Owner.Player!, Amount);
    }
}

public sealed class MomentumRedesignPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DamageFocusPower));

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner.Creature == Owner && cardPlay.Card.Type == CardType.Skill)
        {
            Flash();
            await PowerCmd.Apply<MomentumTemporaryFocusPower>(
                choiceContext,
                Owner,
                Amount,
                Owner,
                cardPlay.Card);
        }
    }
}

public sealed class ScryDrawPower : RedesignV1CounterPower, IRedesignScryListener
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DiscardDefensePower));

    public async Task AfterScry(PlayerChoiceContext choiceContext, int viewed, int discarded)
    {
        Flash();
        await CardPileCmd.Draw(choiceContext, Amount, Owner.Player!);
    }
}

public sealed class KarateReversalPower : RedesignV1CounterPower
{
    private Creature? _counterTarget;
    private int _counterDamage;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));

    public override decimal ModifyHpLostAfterOstyLate(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        int karate = Owner.GetPowerAmount<KaratePower>();
        if (target != Owner
            || dealer == null
            || dealer.Side == Owner.Side
            || !props.IsPoweredAttack()
            || amount <= 0
            || karate <= 0)
        {
            return amount;
        }

        _counterTarget = dealer;
        _counterDamage = Math.Min((int)amount, karate);
        return amount - _counterDamage;
    }

    public override async Task AfterModifyingHpLostAfterOsty()
    {
        Creature? target = _counterTarget;
        int damage = _counterDamage;
        _counterTarget = null;
        _counterDamage = 0;
        if (target == null || damage <= 0)
        {
            return;
        }

        Flash();
        if (target.IsAlive)
        {
            await CreatureCmd.Damage(
                new ThrowingPlayerChoiceContext(),
                [target],
                damage,
                ValueProp.Unpowered | ValueProp.Move,
                Owner);
        }

        if (Owner.GetPower<KaratePower>() is { Amount: > 0 } karate)
        {
            await PowerCmd.Decrement(karate);
        }
    }

    public override Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Decrement(this)
            : Task.CompletedTask;
}

public sealed class ChopChainPower : RedesignV1CounterPower
{
    private int _changes;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Changes", 7)];

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));

    public void UseThreshold(int threshold) =>
        DynamicVars["Changes"].BaseValue = Math.Min(
            DynamicVars["Changes"].IntValue,
            Math.Max(1, threshold));

    public override async Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (power is not KaratePower || amount == 0)
        {
            return;
        }

        _changes++;
        int threshold = DynamicVars["Changes"].IntValue;
        if (_changes < threshold)
        {
            return;
        }

        _changes -= threshold;
        Flash();
        for (int index = 0; index < Amount; index++)
        {
            CommonChopRedesignV1 chop = CombatState.CreateCard<CommonChopRedesignV1>(Owner.Player!);
            CardCmd.ApplyKeyword(chop, CardKeyword.Exhaust);
            await CardPileCmd.AddGeneratedCardToCombat(chop, PileType.Hand, Owner.Player!);
        }
    }
}

public sealed class KarateFormPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));

    public void Trigger() => Flash();
}

public sealed class LingeringMeleePower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(HellTornadoPower));

    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != Owner.Side || !participants.Contains(Owner))
        {
            return;
        }

        Flash();
        for (int index = 0; index < Amount; index++)
        {
            IReadOnlyList<CardModel> cards = PileType.Draw.GetPile(Owner.Player!).Cards;
            if (cards.Count == 0)
            {
                break;
            }

            await CardCmd.AutoPlay(choiceContext, cards[0], null);
        }
    }
}

[RegisterPower]
public sealed class CounteroffensiveTemporaryStrengthPower
    : ModTemporaryAppliedPowerTemplate<CounteroffensiveGuardRedesignV1, StrengthPower>
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("RemainingBlockStrengthPower");
}

[RegisterPower]
public sealed class HiddenEdgeTemporaryFocusPower
    : ModTemporaryAppliedPowerTemplate<HiddenEdgeRedesignV1, FocusPower>
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DamageFocusPower));
}

[RegisterPower]
public sealed class MomentumTemporaryFocusPower
    : ModTemporaryAppliedPowerTemplate<MomentumRedesignV1, FocusPower>
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DamageFocusPower));
}

public sealed class PourTeaNextTurnPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(PourTeaPower));

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != Owner.Side || !participants.Contains(Owner))
        {
            return;
        }

        Flash();
        await ChadoBreathCmd.Apply(Owner.Player!, Amount, this);
        await PowerCmd.Remove(this);
    }
}

public sealed class ChopStrikeNextTurnPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != Owner.Side || !participants.Contains(Owner))
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<KaratePower>(choiceContext, Owner, Amount, Owner, null);
        await PowerCmd.Remove(this);
    }
}

public sealed class FocusedMindNextTurnPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DamageFocusPower));

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != Owner.Side || !participants.Contains(Owner))
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<FocusPower>(choiceContext, Owner, Amount, Owner, null);
        await PowerCmd.Remove(this);
    }
}

public sealed class EmptyShurikenPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(DamageFocusPower));

    public override async Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (power is not KaratePower || power.Owner != Owner || amount <= 0)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<FocusPower>(choiceContext, Owner, Amount, Owner, cardSource);
    }
}

public sealed class TeaTeaPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named("EndTurnRetainPower");

    public override Task BeforeFlush(PlayerChoiceContext choiceContext, Player player)
    {
        if (player == Owner.Player)
        {
            foreach (ChadoEnergyRedesignV1 chado in PileType.Hand.GetPile(player).Cards
                         .OfType<ChadoEnergyRedesignV1>())
            {
                chado.GiveSingleTurnRetain();
            }
        }

        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player)
        {
            return;
        }

        Flash();
        await ChadoBreathCmd.Apply(player, Amount, this);
    }
}

public sealed class BurnBurnBurnPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(NarakuPower));
}

public sealed class ReturnReturnReturnPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(NarakuLifePower));

    public override async Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature? dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel? cardSource)
    {
        if (dealer != Owner
            || cardSource is not BlackFlameRedesignV1
            || target.Side == Owner.Side
            || result.TotalDamage <= 0)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<NarakuLifePower>(
            choiceContext,
            Owner,
            Amount,
            Owner,
            cardSource);
    }

    public override Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Remove(this)
            : Task.CompletedTask;
}

public sealed class ShurikenGuardRedesignPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(IBlockPower));

    internal async Task AfterShurikenDamage(
        PlayerChoiceContext choiceContext,
        IReadOnlyCollection<DamageResult> results)
    {
        int block = results.Sum(result => result.TotalDamage + result.OverkillDamage) * Amount;
        if (block <= 0)
        {
            return;
        }

        Flash();
        await CreatureCmd.GainBlock(Owner, block, ValueProp.Move, null);
    }

    public override Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Remove(this)
            : Task.CompletedTask;
}

public sealed class VitalityTeaPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DrinkTeaPower));

    public override async Task AfterCardExhausted(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool causedByEthereal)
    {
        if (card.Owner.Creature != Owner || card is not ChadoEnergyRedesignV1)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<VigorPower>(choiceContext, Owner, Amount, Owner, card);
    }
}

public sealed class StarlessNightRedesignPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(StarlessNightPower));

    public bool GenerateUpgradedToken { get; set; }

    internal async Task GenerateStrongShuriken(PlayerChoiceContext choiceContext)
    {
        StrongShurikenTokenRedesignV1 card =
            CombatState.CreateCard<StrongShurikenTokenRedesignV1>(Owner.Player!);
        if (GenerateUpgradedToken)
        {
            CardCmd.Upgrade(card);
        }

        Flash();
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner.Player!);
    }
}

public sealed class KarateTeaPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(TeaDrinkingSwordPower));

    public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (creator?.Creature != Owner || card is not ChadoEnergyRedesignV1)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<KaratePower>(
            new ThrowingPlayerChoiceContext(),
            Owner,
            Amount,
            Owner,
            card);
    }
}
