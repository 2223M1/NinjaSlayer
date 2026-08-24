using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ShurikenStockPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named(nameof(ExhaustForShurikenPower));

    public override async Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)
    {
        IReadOnlyList<Creature> candidates = CombatState.HittableEnemies;
        ShurikenShuffleResolution resolution = RedesignV1Rules.ResolveShurikenShuffle(
            Amount,
            Owner.HasPower<BladeCyclePower>(),
            shuffler == Owner.Player,
            candidates.Count);
        if (resolution.Shots <= 0)
        {
            return;
        }

        Flash();
        if (resolution.RemainingStock == 0)
        {
            await PowerCmd.Remove(this);
        }

        for (int index = 0; index < resolution.Shots; index++)
        {
            Creature? target = Owner.Player!.RunState.Rng.CombatTargets.NextItem(CombatState.HittableEnemies);
            if (target == null)
            {
                break;
            }

            await ShurikenCombat.TriggerStockShot(choiceContext, Owner, target, null);
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
