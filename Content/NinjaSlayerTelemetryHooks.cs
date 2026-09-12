using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Telemetry;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Content;

[RegisterSingleton]
public sealed class NinjaSlayerTelemetryHooks : NinjaSlayerCombatSingletonTemplate
{
    public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
    {
        NinjaSlayerCombatTelemetry.AfterDeath(creature);
        return Task.CompletedTask;
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        NinjaSlayerCombatTelemetry.PileChanged(card);
        return Task.CompletedTask;
    }

    public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        NinjaSlayerCombatTelemetry.HpChanged(creature, delta);
        return Task.CompletedTask;
    }

    public override Task AfterCreatureAddedToCombat(Creature creature)
    {
        NinjaSlayerCombatTelemetry.ObserveCreature(creature);
        return Task.CompletedTask;
    }

    public override Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)
    {
        NinjaSlayerCombatTelemetry.Mechanic("shuffle", shuffler.Creature, 1);
        return Task.CompletedTask;
    }
}
