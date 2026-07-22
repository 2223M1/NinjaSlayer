using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public abstract class NarakuThemedCardTemplate : NinjaSlayerCardTemplate
{
    protected NarakuThemedCardTemplate(NinjaSlayerCardSpec cardSpec)
        : base(cardSpec)
    {
    }

    public override Material? CustomFrameMaterial => NinjaSlayerCardFrames.NarakuFrameMaterial;

    protected Task EnsureNarakuForm(PlayerChoiceContext choiceContext) =>
        NinjaSlayerActions.EnsureNarakuForm(choiceContext, Owner);

    protected Task EnterNarakuWithLife(PlayerChoiceContext choiceContext, decimal life) =>
        NinjaSlayerActions.EnterNaraku(choiceContext, Owner, life);
}
