using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class Endurance : RedesignV1UncommonCard
{
    protected override bool ShouldGlowGoldInternal =>
        CombatState != null && EndurancePower.HasNotAttackedThisTurn(Owner);

    public Endurance() : base(nameof(Endurance), nameof(Endurance), 1, CardType.Skill, TargetType.Self) { }
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(1), new DynamicVar("LaterKarate", 3)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<KaratePower>(choiceContext, Owner.Creature, DynamicVars.Karate().BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<EndurancePower>(choiceContext, Owner.Creature, DynamicVars["LaterKarate"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Karate().UpgradeValueBy(1);
        DynamicVars["LaterKarate"].UpgradeValueBy(1);
    }
}
