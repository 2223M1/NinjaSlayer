using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class ClankDrinkTeaPower : NinjaSlayerPowerTemplate
{
    private class Data
    {
        public int ChadoConsumedThisTurn;
    }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CardsVar(2)
    ];

    protected override object InitInternalData() => new Data();

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (cardSource is ClankDrinkTea card)
        {
            DynamicVars.Cards.BaseValue = card.DynamicVars.Cards.BaseValue;
        }

        return Task.CompletedTask;
    }

    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (participants.Contains(Owner))
        {
            GetInternalData<Data>().ChadoConsumedThisTurn = 0;
        }

        return Task.CompletedTask;
    }

    public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (card.Owner != Owner.Player || card is not ChadoCard)
        {
            return;
        }

        Data data = GetInternalData<Data>();
        if (data.ChadoConsumedThisTurn >= Amount)
        {
            return;
        }

        data.ChadoConsumedThisTurn++;
        Flash();
        await CardCmd.Exhaust(choiceContext, card);
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.IntValue, Owner.Player);
    }
}
