using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ShurikenStockPower : NinjaSlayerPowerTemplate
{
    private bool _isResolving;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(ExhaustForShurikenPower));

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (_isResolving
            || cardPlay.Card.Owner.Creature != Owner
            || cardPlay.Card.Type != CardType.Attack
            || Amount <= 0)
        {
            return;
        }

        IReadOnlyList<Creature> candidates = CombatState.HittableEnemies;
        if (candidates.Count == 0)
        {
            return;
        }

        Creature target = cardPlay.Target is { } original && candidates.Contains(original)
            ? original
            : Owner.Player!.RunState.Rng.CombatTargets.NextItem(candidates)!;
        int bonus = Owner.GetPower<ShurikenDamagePower>()?.Amount ?? 0;

        _isResolving = true;
        try
        {
            Flash();
            await PowerCmd.Decrement(this);
            await GameCompatibility.Damage.Deal(
                choiceContext,
                [target],
                RedesignV1Rules.ShurikenDamage(bonus),
                ValueProp.Unpowered | ValueProp.Move,
                Owner,
                cardPlay.Card,
                cardPlay);
        }
        finally
        {
            _isResolving = false;
        }
    }
}

public sealed class ShurikenDamagePower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(ExhaustForShurikenPower));
}
