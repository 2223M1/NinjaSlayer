using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool))]
public sealed class HackerMotokoRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    // ponytail: reuse the existing terminal relic art until Nancy gets dedicated icons.
    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic.png",
        IconOutlinePath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic_outline.png",
        BigIconPath: "res://NinjaSlayer/images/relics/PortableIrcTerminalRelic_large.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("DebuffBonus", 2)
    ];

    private CardModel? _triggeringCard;
    private readonly List<PowerModel> _boostedPowers = [];

    public override Task BeforeCombatStart()
    {
        ClearPlayState();
        return Task.CompletedTask;
    }

    public override Task BeforePowerAmountChanged(PowerModel power, decimal amount, Creature target, Creature? applier, CardModel? cardSource)
    {
        if (_triggeringCard != null)
        {
            return Task.CompletedTask;
        }

        if (cardSource == null || applier != Owner.Creature || target.Side == Owner.Creature.Side)
        {
            return Task.CompletedTask;
        }

        if (!power.IsVisible || power.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            return Task.CompletedTask;
        }

        _triggeringCard = cardSource;
        _boostedPowers.Add(power);
        return Task.CompletedTask;
    }

    public override decimal ModifyPowerAmountGivenAdditive(PowerModel power, Creature giver, decimal amount, Creature? target, CardModel? cardSource)
    {
        if (_triggeringCard == null || cardSource != _triggeringCard || giver != Owner.Creature)
        {
            return 0m;
        }

        if (target == null || target.Side == Owner.Creature.Side)
        {
            return 0m;
        }

        if (HasBoostedTemporaryPowerSource(power))
        {
            return 0m;
        }

        if (power.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            return 0m;
        }

        return DynamicVars["DebuffBonus"].BaseValue;
    }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner)
        {
            return Task.CompletedTask;
        }

        if (_triggeringCard == cardPlay.Card)
        {
            Flash();
        }

        ClearPlayState();
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        ClearPlayState();
        return Task.CompletedTask;
    }

    private void ClearPlayState()
    {
        _triggeringCard = null;
        _boostedPowers.Clear();
    }

    private bool HasBoostedTemporaryPowerSource(PowerModel power) =>
        _boostedPowers.OfType<ITemporaryPower>().Any(p => p.InternallyAppliedPower.GetType() == power.GetType());
}
