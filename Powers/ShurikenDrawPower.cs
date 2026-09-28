using MegaCrit.Sts2.Core.Entities.Players;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ShurikenDrawPower : RedesignV1CounterPower
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named(nameof(ShurikenDrawPower));
    public override decimal ModifyHandDraw(Player player, decimal count) =>
        player == Owner.Player && ShurikenOrb.FiredLastTurn(player) ? count + Amount : count;

    public override Task AfterModifyingHandDraw()
    {
        Flash();
        return Task.CompletedTask;
    }
}
