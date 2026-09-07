using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ObserveBattlefield : RedesignV1UncommonCard
{
    public ObserveBattlefield() : base(nameof(ObserveBattlefield), "ReadyBlade", 1, CardType.Skill, TargetType.AllEnemies) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<VulnerablePower>(1), new PowerVar<WeakPower>(1)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var targets = CombatState!.HittableEnemies.ToArray();
        await PowerCmd.Apply<VulnerablePower>(choiceContext, targets, DynamicVars[nameof(VulnerablePower)].BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<WeakPower>(choiceContext, targets, DynamicVars[nameof(WeakPower)].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade()
    {
        DynamicVars[nameof(VulnerablePower)].UpgradeValueBy(1);
        DynamicVars[nameof(WeakPower)].UpgradeValueBy(1);
    }
}
