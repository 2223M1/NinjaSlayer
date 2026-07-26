using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed record FinisherForecastDescriptor(
    Func<Creature, decimal> Damage,
    ValueProp Props,
    int HitCount,
    FinisherTargeting Targeting,
    Creature? SingleTarget = null,
    IReadOnlyList<Creature>? FixedTargets = null);

internal sealed record FinisherActionForecastDescriptor(
    Func<Creature, decimal> Damage,
    ValueProp Props,
    int HitCount,
    FinisherTargeting Targeting,
    CardModel? CardSource = null,
    CardPlay? CardPlay = null,
    bool TriggersKarate = true,
    Creature? SingleTarget = null,
    IReadOnlyList<Creature>? FixedTargets = null);
