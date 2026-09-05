using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ComposeHaikuRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [NinjaSlayerKeywords.Judgment];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Threshold", 30)];

    public ComposeHaikuRedesignV1()
        : base(nameof(ComposeHaikuRedesignV1), "Recycle", 1, CardType.Skill, TargetType.AnyEnemy) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        cardPlay.Target!.CurrentHp < DynamicVars["Threshold"].IntValue
            ? CreatureCmd.Kill(cardPlay.Target, true)
            : Task.CompletedTask;

    protected override void OnUpgrade() => DynamicVars["Threshold"].UpgradeValueBy(10);
}
