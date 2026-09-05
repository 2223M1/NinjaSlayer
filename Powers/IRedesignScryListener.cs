using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace NinjaSlayer.Powers;

public interface IRedesignScryListener
{
    Task AfterScry(PlayerChoiceContext choiceContext, int viewed, int discarded);
}
