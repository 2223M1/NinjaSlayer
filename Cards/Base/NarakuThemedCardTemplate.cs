using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public abstract class NarakuThemedCardTemplate : NinjaSlayerCardTemplate
{
    protected NarakuThemedCardTemplate(
        int energyCost,
        CardType type,
        CardRarity rarity,
        TargetType targetType,
        bool shouldShowInCardLibrary)
        : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary)
    {
    }

    public override Material? CustomFrameMaterial => NinjaSlayerCardFrames.NarakuFrameMaterial;

    protected Task EnsureNarakuForm(PlayerChoiceContext choiceContext) =>
        NinjaSlayerActions.EnsureNarakuForm(choiceContext, Owner);

    protected Task EnterNarakuWithLife(PlayerChoiceContext choiceContext, decimal life) =>
        NinjaSlayerActions.EnterNaraku(choiceContext, Owner, life);
}
