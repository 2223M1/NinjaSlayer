using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ZanshinPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(ZanshinPower));

    public override decimal ModifyHandDraw(Player player, decimal count) =>
        player == Owner.Player && CombatManager.Instance.History.CardPlaysFinished.Count(entry =>
            entry.HappenedLastPlayerTurn(player) && entry.CardPlay.Card.Owner == player
            && entry.CardPlay.Card.Type == CardType.Attack) >= 3
            ? count + Amount : count;

    public override Task AfterModifyingHandDraw()
    {
        Flash();
        return Task.CompletedTask;
    }
}
