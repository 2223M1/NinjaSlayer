using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class HackerMotokoRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    // ponytail: reuse the existing terminal relic art until Nancy gets dedicated icons.
    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For<PortableIrcTerminalRelic>();

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("DebuffBonus", 2)
    ];

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner == Owner
            && cardPlay.Target?.HasPower<ArtifactPower>() != true)
        {
            CardPlayResolutionScope.GetOrCreatePlayState(cardPlay, this, () => new CardPlayState(cardPlay));
        }

        return Task.CompletedTask;
    }

    public override Task BeforePowerAmountChanged(PowerModel power, decimal amount, Creature target, Creature? applier, CardModel? cardSource)
    {
        if (cardSource == null || applier != Owner.Creature || target.Side == Owner.Creature.Side)
        {
            return Task.CompletedTask;
        }

        CardPlayState? state = FindActiveState(cardSource);
        if (state == null || target.HasPower<ArtifactPower>())
        {
            return Task.CompletedTask;
        }

        if (!power.IsVisible || power.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            return Task.CompletedTask;
        }

        if (state.TemporaryInternalPowerTypes.Contains(power.GetType()) || !state.BoostedPowers.Add(power))
        {
            return Task.CompletedTask;
        }

        state.PendingBonuses.Add(power);
        if (power is ITemporaryPower temporaryPower)
        {
            state.TemporaryInternalPowerTypes.Add(temporaryPower.InternallyAppliedPower.GetType());
        }

        state.Triggered = true;
        return Task.CompletedTask;
    }

    public override decimal ModifyPowerAmountGivenAdditive(PowerModel power, Creature giver, decimal amount, Creature? target, CardModel? cardSource)
    {
        if (cardSource == null || giver != Owner.Creature)
        {
            return 0m;
        }

        CardPlayState? state = FindActiveState(cardSource);
        if (state == null || target == null || target.Side == Owner.Creature.Side || target.HasPower<ArtifactPower>())
        {
            return 0m;
        }

        if (!power.IsVisible || power.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            return 0m;
        }

        if (!state.PendingBonuses.Remove(power))
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

        CardPlayState? state = FindActiveState(cardPlay.Card);
        if (state?.Triggered == true)
        {
            Flash();
        }
        return Task.CompletedTask;
    }

    private CardPlayState? FindActiveState(CardModel cardSource) =>
        CardPlayResolutionScope.TryGetLatestPlayState(cardSource, this, out CardPlayState? state)
            ? state
            : null;

    private sealed class CardPlayState(CardPlay cardPlay)
    {
        public CardPlay CardPlay { get; } = cardPlay;
        public HashSet<PowerModel> BoostedPowers { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<PowerModel> PendingBonuses { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<Type> TemporaryInternalPowerTypes { get; } = [];
        public bool Triggered { get; set; }
    }
}
