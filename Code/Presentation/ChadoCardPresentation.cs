using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using NinjaSlayer.Cards;

namespace NinjaSlayer.Code.Presentation;

internal static class ChadoCardPresentation
{
    public static void Refresh(CardModel? model)
    {
        if (model is ChadoCard && model.Pile?.Type == PileType.Hand)
        {
            Refresh(NCard.FindOnTable(model));
        }
    }

    public static void Refresh(NCard? node)
    {
        if (node is null
            || !GodotObject.IsInstanceValid(node)
            || !node.IsNodeReady()
            || node.Model is not ChadoCard
            || node.Model.Pile?.Type != PileType.Hand)
        {
            return;
        }

        node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
    }
}
