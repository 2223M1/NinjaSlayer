using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Relics;

public sealed class BeppinFragmentRelic : NinjaSlayerRelicTemplate
{
    private bool _usedThisCombat;

    public override RelicRarity Rarity => RelicRarity.Event;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<KaratePower>("Karate", 7)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>()];

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target,
        DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (!CombatManager.Instance.IsInProgress || target != Owner.Creature || _usedThisCombat
            || (result.UnblockedDamage <= 0 && NarakuLifeDamagePatch.AbsorbedBy(result) == 0)) return;
        _usedThisCombat = true;
        Flash();
        await PowerCmd.Apply<KaratePower>(choiceContext, Owner.Creature,
            DynamicVars["Karate"].BaseValue, Owner.Creature, null);
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        _usedThisCombat = false;
        return Task.CompletedTask;
    }
}
