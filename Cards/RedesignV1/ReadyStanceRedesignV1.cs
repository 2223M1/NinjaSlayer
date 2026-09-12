using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ReadyStanceRedesignV1 : RedesignV1CommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(3)];

    public ReadyStanceRedesignV1()
        : base(nameof(ReadyStanceRedesignV1), "KataDrill", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);
        await CardCmd.Discard(choiceContext, await CardSelectCmd.FromHandForDiscard(
            choiceContext, Owner,
            new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1),
            null, this));
    }

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(2);
}
