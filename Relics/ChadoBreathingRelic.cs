using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Content;
using NinjaSlayer.Cards;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterCharacterStarterRelic(typeof(NinjaSlayerCharacter), 1)]
[RegisterTouchOfOrobasRefinement(typeof(DeepChadoBreathingRelic))]
public class ChadoBreathingRelic : NinjaSlayerRelicTemplate
{
    private int _healedThisCombat;

    protected virtual int HealAmount => 2;
    protected virtual int MaxHealPerCombat => 12;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new HealVar(HealAmount)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromCard<ChadoCard>()
    ];

    public override async Task BeforeCombatStart()
    {
        _healedThisCombat = 0;
        Flash();
        await CardPileCmd.AddToCombatAndPreview<ChadoCard>(
            Owner.Creature,
            PileType.Hand,
            1,
            Owner);
    }

    public override Task AfterCombatEnd(CombatRoom _)
    {
        _healedThisCombat = 0;
        return Task.CompletedTask;
    }

    public override async Task AfterCombatVictory(CombatRoom _)
    {
        if (!Owner.Creature.IsDead)
        {
            await TryBreathe();
        }
    }

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (!participants.Contains(Owner.Creature) || Owner.Creature.IsDead)
        {
            return;
        }

        await TryBreathe();
    }

    private async Task TryBreathe()
    {
        if (NinjaSlayerActions.ChadoInHandCount(Owner) <= 0)
        {
            return;
        }

        int remaining = MaxHealPerCombat - _healedThisCombat;
        if (remaining <= 0)
        {
            return;
        }

        int heal = Math.Min((int)DynamicVars.Heal.BaseValue, remaining);
        Flash();
        await CreatureCmd.Heal(Owner.Creature, heal);
        _healedThisCombat += heal;

        if (Owner.Creature.HasPower<NarakuPower>())
        {
            await NinjaSlayerActions.ExitNaraku(Owner.Creature);
        }
    }
}
