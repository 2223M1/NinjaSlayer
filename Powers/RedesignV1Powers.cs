using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

#pragma warning disable CA1707, CA1725, CA1826

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

public sealed class ChadoHealPower : RedesignV1CounterPower
{
    public override async Task AfterCardPlayed(PlayerChoiceContext c, CardPlay p) { if (p.Card.Owner.Creature == Owner && p.Card is ChadoBreathRedesignV1) { Flash(); await CreatureCmd.Heal(Owner, Amount); } }
}

public sealed class ChadoBlockPower : RedesignV1CounterPower
{
    public sealed class EnergyGainPatch : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_chado_block_energy_gain";
        public static string Description => "Resolve Chado block before successful energy gain completes.";
        public static bool IsCritical => true;

        public static ModPatchTarget[] GetTargets() =>
        [
            new(typeof(PlayerCmd), nameof(PlayerCmd.GainEnergy), [typeof(decimal), typeof(Player)])
        ];

        public static void Prefix(
            Player player,
            out (Player Player, int Energy, ChadoBlockPower? Power, int Block) __state)
        {
            ChadoBlockPower? power = player.Creature.GetPower<ChadoBlockPower>();
            __state = (player, player.PlayerCombatState!.Energy, power, power?.Amount ?? 0);
        }

        public static void Postfix(
            ref Task __result,
            (Player Player, int Energy, ChadoBlockPower? Power, int Block) __state) =>
            __result = Complete(__result, __state);

        private static async Task Complete(
            Task task,
            (Player Player, int Energy, ChadoBlockPower? Power, int Block) state)
        {
            await task;
            if (state.Power == null
                || state.Block <= 0
                || state.Player.PlayerCombatState!.Energy <= state.Energy)
            {
                return;
            }

            state.Power.Flash();
            await CreatureCmd.GainBlock(state.Power.Owner, state.Block, ValueProp.Move, null);
        }
    }
}

public sealed class ChadoEnergyPower : RedesignV1CounterPower
{
    public override async Task AfterCardPlayed(PlayerChoiceContext c, CardPlay p) { if (p.Card.Owner.Creature == Owner && p.Card is ChadoBreathRedesignV1) { Flash(); await PlayerCmd.GainEnergy(Amount, Owner.Player!); } }
}

public sealed class ScryDrawPower : RedesignV1CounterPower, IRedesignScryListener
{
    public async Task AfterScry(PlayerChoiceContext c, int viewed, int discarded) { Flash(); await CardPileCmd.Draw(c, Amount, Owner.Player!); }
}

public sealed class ScryBlockPower : RedesignV1CounterPower, IRedesignScryListener
{
    public async Task AfterScry(PlayerChoiceContext c, int viewed, int discarded) { Flash(); await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null); }
}

public sealed class NextAttackStrengthPower : RedesignV1CounterPower
{
    public override async Task BeforeCardPlayed(CardPlay p)
    {
        if (p.Card.Owner.Creature != Owner || p.Card.Type != CardType.Attack) return;
        await PowerCmd.Apply<RetainedForcePower>(new ThrowingPlayerChoiceContext(), Owner, Amount, Owner, null);
        await PowerCmd.Remove(this);
    }
}

public sealed class PerCardStrengthPower : RedesignV1CounterPower
{
    public override async Task BeforeCardPlayed(CardPlay p)
    {
        if (p.Card.Owner.Creature == Owner) await PowerCmd.Apply<RetainedForcePower>(new ThrowingPlayerChoiceContext(), Owner, Amount, Owner, null);
    }

    public override Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants) =>
        side == Owner.Side && participants.Contains(Owner)
            ? PowerCmd.Remove(this)
            : Task.CompletedTask;
}

public sealed class CarriedStrengthPower : RedesignV1CounterPower
{
    public override Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal delta,
        Creature? applier,
        CardModel? source) =>
        power == this && delta > 0
            ? PowerCmd.Apply<StrengthPower>(choiceContext, Owner, delta, applier, source)
            : Task.CompletedTask;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext c, Player player)
    {
        if (player != Owner.Player) return;
        await PowerCmd.Apply<StrengthPower>(c, Owner, -Amount, Owner, null);
        await PowerCmd.Remove(this);
    }
}

public sealed class ShuffleBlockPower : RedesignV1CounterPower
{
    public override async Task AfterShuffle(PlayerChoiceContext c, Player player) { if (player == Owner.Player) { Flash(); await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null); } }
}

public sealed partial class EveryThirdAttackPower : RedesignV1CounterPower
{
    private int _attacks;
    private decimal ModifyDamageMultiplicativeCore(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? source)
        => dealer == Owner && source?.Type == CardType.Attack && (_attacks + 1) % 3 == 0 ? Amount : 1m;
    public override Task AfterCardPlayed(PlayerChoiceContext c, CardPlay p) { if (p.Card.Owner.Creature == Owner && p.Card.Type == CardType.Attack) _attacks++; return Task.CompletedTask; }
}

public sealed class IntentOpeningPower : RedesignV1CounterPower
{
    private readonly HashSet<Creature> _seen = [];
    public override async Task BeforeCardPlayed(CardPlay p)
    {
        Creature? target = p.Target;
        if (p.Card.Owner.Creature != Owner || p.Card.Type != CardType.Attack || target?.Monster?.IntendsToAttack != true || !_seen.Add(target)) return;
        foreach (CardModel card in PileType.Hand.GetPile(Owner.Player!).Cards.Where(x => x != p.Card).Take(Amount)) card.EnergyCost.SetThisTurnOrUntilPlayed(0);
        Flash();
        await Task.CompletedTask;
    }
}

public sealed class ChadoBaseEnergyPower : RedesignV1CounterPower
{
    public override async Task AfterCardPlayed(PlayerChoiceContext c, CardPlay p) { if (p.Card.Owner.Creature == Owner && p.Card is ChadoBreathRedesignV1) await PlayerCmd.GainEnergy(Amount, Owner.Player!); }
}

public sealed class EndTurnRetainPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext c, CombatSide side, IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner))
        {
            CardModel? card = (await CardSelectCmd.FromHand(c, Owner.Player!, new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), null, this)).FirstOrDefault();
            card?.AddKeyword(CardKeyword.Retain);
        }
    }
}

public sealed class ThreeTurnBlockPower : RedesignV1CounterPower
{
    private int _turns = 2;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player || _turns <= 0) return;
        await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null);
        if (--_turns == 0) await PowerCmd.Remove(this);
    }
}

public sealed class OneTurnBarricadePower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override bool ShouldClearBlock(Creature creature) => creature != Owner;
    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (participants.Contains(Owner)) await PowerCmd.Remove(this);
    }
}

public sealed class RetaliatoryIntentPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || result.TotalDamage <= 0 || result.WasFullyBlocked) return;
        foreach (Creature enemy in Owner.CombatState!.HittableEnemies.ToList())
        {
            await PowerCmd.Apply<WeakPower>(choiceContext, enemy, 3, Owner, cardSource);
            await PowerCmd.Apply<VulnerablePower>(choiceContext, enemy, 3, Owner, cardSource);
        }
        await PowerCmd.Remove(this);
    }
}

public sealed class ShurikenRegenerationPower : RedesignV1CounterPower
{
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext c, PowerModel power, decimal delta, Creature? applier, CardModel? source)
    {
        if (power is ShurikenStockPower && power.Owner == Owner && delta < 0) await PowerCmd.Apply<ShurikenStockPower>(c, Owner, Amount, Owner, source);
    }
}

public sealed partial class NullifyHitsPower : RedesignV1CounterPower
{
    private bool _consumed;
    private decimal ModifyDamageCapCore(Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || Amount <= 0) return decimal.MaxValue;
        _consumed = true;
        return 0;
    }
    public override async Task AfterModifyingDamageAmount(CardModel? cardSource)
    {
        if (!_consumed) return;
        _consumed = false;
        Flash();
        await PowerCmd.Decrement(this);
    }
}

public sealed class BloodTearsRedesignPower : RedesignV1CounterPower
{
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext c, Player player)
    {
        if (player == Owner.Player)
        {
            await GameCompatibility.Damage.Deal(
                c,
                [Owner],
                Amount,
                ValueProp.Unblockable | ValueProp.Unpowered,
                Owner,
                null,
                null);
        }
    }
}
