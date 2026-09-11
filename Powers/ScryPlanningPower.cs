using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ScryPlanningPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("ScryDrawPower");

    internal void ApplyToUnselectedCards(IEnumerable<CardModel> cards)
    {
        Flash();
        foreach (CardModel card in cards) CardCmd.ApplyKeyword(card, CardKeyword.Sly);
    }
}
