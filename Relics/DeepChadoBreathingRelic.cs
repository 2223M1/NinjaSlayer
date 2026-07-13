using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Content;

namespace NinjaSlayer.Relics;

public sealed class DeepChadoBreathingRelic : ChadoBreathingRelic
{
    private const int HealAmount = 4;
    private const int MaxHealPerCombat = 24;

    private readonly ChadoBreathingEffect _breathing = new();

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new HealVar(HealAmount)
    ];

    public override Task BeforeCombatStart()
    {
        _breathing.Reset();
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom _)
    {
        _breathing.Reset();
        return Task.CompletedTask;
    }

    public override async Task AfterCombatVictory(CombatRoom _)
    {
        await TryBreathe();
    }

    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner.Creature))
        {
            await TryBreathe();
        }
    }

    private Task<bool> TryBreathe() => _breathing.TryApply(
        Owner,
        (int)DynamicVars.Heal.BaseValue,
        MaxHealPerCombat,
        Flash);
}
