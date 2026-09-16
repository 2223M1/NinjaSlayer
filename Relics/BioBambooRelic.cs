using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace NinjaSlayer.Relics;

public sealed class BioBambooRelic : NinjaSlayerRelicTemplate
{
    private int _attacksPlayed;

    public override RelicRarity Rarity => RelicRarity.Event;
    public override bool ShowCounter => true;
    public override int DisplayAmount => AttacksPlayed;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CardsVar(2), new PowerVar<PlatingPower>("Plating", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<PlatingPower>()];

    [SavedProperty]
    public int AttacksPlayed
    {
        get => _attacksPlayed;
        set
        {
            AssertMutable();
            _attacksPlayed = value;
            Status = value == DynamicVars.Cards.IntValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
            InvokeDisplayAmountChanged();
        }
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner || cardPlay.Card.Type != CardType.Attack) return;
        AttacksPlayed = (AttacksPlayed + 1) % DynamicVars.Cards.IntValue;
        if (AttacksPlayed != 0 || !CombatManager.Instance.IsInProgress) return;
        Flash();
        await PowerCmd.Apply<PlatingPower>(choiceContext, Owner.Creature,
            DynamicVars["Plating"].BaseValue, Owner.Creature, null);
    }
}
