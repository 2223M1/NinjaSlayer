using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Content.Patches;

namespace NinjaSlayer.Powers;

public interface IRedesignScryListener
{
    Task AfterScry(PlayerChoiceContext choiceContext, int viewed, int discarded);
}

public abstract class RedesignV1CounterPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
}

public sealed class ChadoRetainPower : RedesignV1CounterPower
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

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner))
        {
            await PowerCmd.Decrement(this);
        }
    }
}

[RegisterPower]
public sealed class HookRopeStrengthDownPower : TemporaryStrengthPower, IModPowerAssetOverrides
{
    public PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(RiffleStrengthDownPower));
    public string? CustomIconPath => AssetProfile.IconPath;
    public string? CustomBigIconPath => AssetProfile.BigIconPath;

    public override AbstractModel OriginModel => ModelDb.Card<HookRopeRedesignV1>();

    protected override bool IsPositive => false;
}

public sealed class CombatAdjustmentPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(TeaDrinkingSwordPower));

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner.Creature == Owner && cardPlay.Card.Type == CardType.Skill)
        {
            Flash();
            await ChadoBreathCmd.Apply(Owner.Player!, Amount, cardPlay.Card);
        }
    }

    public override Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Remove(this)
            : Task.CompletedTask;
}

public sealed class MetabolicAccelerationPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DrinkTeaPower));

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner.Creature != Owner || cardPlay.Card is not ChadoEnergyRedesignV1)
        {
            return;
        }

        Flash();
        await CreatureCmd.Heal(Owner, Amount);
        foreach (CardModel status in PileType.Hand.GetPile(Owner.Player!).Cards
                     .Where(card => card.Type == CardType.Status)
                     .ToList())
        {
            await CardCmd.Exhaust(choiceContext, status);
        }

        await PowerCmd.Remove(this);
    }
}

public sealed class ScryBlockPower : RedesignV1CounterPower, IRedesignScryListener
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DiscardDefensePower));

    public async Task AfterScry(PlayerChoiceContext choiceContext, int viewed, int discarded)
    {
        Flash();
        await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null);
    }
}

public sealed class ScryStatusExhaustPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(DiscardDefensePower));
}

public sealed class AetherEnergyPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));

    public sealed class EnergyGainPatch : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_redesign_aether_energy_gain";
        public static string Description => "Grant Karate after successful extra energy gain.";
        public static bool IsCritical => true;

        public static ModPatchTarget[] GetTargets() =>
        [
            new(typeof(PlayerCmd), nameof(PlayerCmd.GainEnergy), [typeof(decimal), typeof(Player)])
        ];

#pragma warning disable CA1707 // Harmony reserves double-underscore parameter names.
        public static void Prefix(
            Player player,
            out (Player Player, int Energy, AetherEnergyPower? Power) __state) =>
            __state = (
                player,
                player.PlayerCombatState?.Energy ?? 0,
                player.Creature.GetPower<AetherEnergyPower>());

        public static void Postfix(
            ref Task __result,
            (Player Player, int Energy, AetherEnergyPower? Power) __state) =>
            __result = Complete(__result, __state);
#pragma warning restore CA1707

        private static async Task Complete(
            Task task,
            (Player Player, int Energy, AetherEnergyPower? Power) state)
        {
            await task;
            if (state.Power == null
                || state.Power.Amount <= 0
                || state.Player.PlayerCombatState == null
                || state.Player.PlayerCombatState.Energy <= state.Energy)
            {
                return;
            }

            state.Power.Flash();
            await PowerCmd.Apply<KaratePower>(
                new ThrowingPlayerChoiceContext(),
                state.Player.Creature,
                state.Power.Amount,
                state.Player.Creature,
                null);
        }
    }
}

public sealed class KarateTrainingPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KaratePower));

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player == Owner.Player)
        {
            Flash();
            await PowerCmd.Apply<KaratePower>(choiceContext, Owner, Amount, Owner, null);
        }
    }
}

public sealed class ShuffleBlockPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(IBlockPower));

    public override async Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)
    {
        if (shuffler == Owner.Player)
        {
            Flash();
            await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null);
        }
    }
}

public sealed class WasshoiDuplicationPower : RedesignV1CounterPower
{
    private CardModel? _targetCard;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("EveryThirdAttackPower");

    public void Arm(CardModel card)
    {
        AssertMutable();
        _targetCard = card;
    }

    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount) =>
        card == _targetCard ? playCount + Amount : playCount;

    public override Task AfterModifyingCardPlayCount(CardModel card) =>
        card == _targetCard ? PowerCmd.Remove(this) : Task.CompletedTask;
}

public sealed class BloodTearsRedesignPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(BloodTearsPower));

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player == Owner.Player)
        {
            await CreatureCmd.Damage(
                choiceContext,
                [Owner],
                Amount,
                ValueProp.Unblockable | ValueProp.Unpowered,
                Owner,
                null
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , null
#endif
            );
        }
    }
}

public sealed class BladeCyclePower : RedesignV1CounterPower
{
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(ExhaustForShurikenPower));
}

public sealed class HardItOutPower : RedesignV1CounterPower
{
    private int _damageRemainder;
    private int _pendingWounds;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(GreatUkePower));

    public override decimal ModifyHpLostAfterOstyLate(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner || dealer == null || dealer.Side == Owner.Side || amount <= 0)
        {
            return amount;
        }

        int accumulated = _damageRemainder + (int)amount;
        _pendingWounds += RedesignV1Rules.ResolveHardItOutWounds(accumulated, Amount);
        _damageRemainder = Amount <= 0 ? 0 : accumulated % Amount;
        return 0;
    }

    public override async Task AfterModifyingHpLostAfterOsty()
    {
        Flash();
        while (_pendingWounds-- > 0)
        {
            await NinjaSlayerActions.AddGeneratedCard<Wound>(Owner.Player!, PileType.Hand);
        }

        _pendingWounds = 0;
    }

    public override Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Remove(this)
            : Task.CompletedTask;
}

public sealed class NarakuFormRedesignPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(NarakuPower));

    public override bool TryModifyEnergyCostInCombatLate(
        CardModel card,
        decimal originalCost,
        out decimal modifiedCost)
    {
        if (card.Owner.Creature != Owner || card.Type != CardType.Attack)
        {
            modifiedCost = originalCost;
            return false;
        }

        modifiedCost = 0;
        return true;
    }

#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
    public override (PileType, CardPilePosition) ModifyCardPlayResultPileTypeAndPosition(
        CardModel card,
        bool isAutoPlay,
        ResourceInfo resources,
        PileType pileType,
        CardPilePosition position) =>
        card.Owner.Creature == Owner && card.Type == CardType.Attack
            ? (PileType.Exhaust, position)
            : (pileType, position);
#else
    public override CardLocation ModifyCardPlayResultLocation(
        CardModel card,
        bool isAutoPlay,
        ResourceInfo resources,
        CardLocation cardLocation)
    {
        if (card.Owner.Creature == Owner && card.Type == CardType.Attack)
        {
            cardLocation.pileType = PileType.Exhaust;
        }

        return cardLocation;
    }

    public override Task AfterModifyingCardPlayResultLocation(
        CardModel card,
        CardLocation cardLocation)
    {
        if (card.Owner.Creature == Owner && card.Type == CardType.Attack)
        {
            Flash();
        }

        return Task.CompletedTask;
    }
#endif

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner.Creature != Owner || cardPlay.Card.Type != CardType.Attack)
        {
            return;
        }

        Flash();
        await NinjaSlayerActions.AddGeneratedCard<BlackFlameRedesignV1>(
            Owner.Player!,
            PileType.Draw,
            CardPilePosition.Top);
    }
}

public sealed class KillingIntentRedesignPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(KillingIntentPower));
    public bool GenerateUpgradedCard { get; set; }

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner
            || dealer == null
            || dealer.Side == Owner.Side
            || !props.IsPoweredAttack()
            || !result.WasFullyBlocked
            || result.TotalDamage <= 0)
        {
            return;
        }

        StraightKiRedesignV1 card = CombatState.CreateCard<StraightKiRedesignV1>(Owner.Player!);
        if (GenerateUpgradedCard)
        {
            CardCmd.Upgrade(card);
        }

        Flash();
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner.Player!);
        await PowerCmd.Remove(this);
    }

    public override Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Remove(this)
            : Task.CompletedTask;
}

public sealed class GreatUkeRedesignPower : RedesignV1CounterPower
{
    private bool _consumed;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(GreatUkePower));

    public override decimal ModifyHpLostAfterOstyLate(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner || amount <= 10 || Amount <= 0)
        {
            return amount;
        }

        _consumed = true;
        return 0;
    }

    public override async Task AfterModifyingHpLostAfterOsty()
    {
        if (!_consumed)
        {
            return;
        }

        _consumed = false;
        Flash();
        await PowerCmd.Decrement(this);
    }
}
