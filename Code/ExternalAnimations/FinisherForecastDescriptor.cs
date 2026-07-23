using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed record FinisherForecastDescriptor(
    Func<Creature, decimal> Damage,
    ValueProp Props,
    int HitCount,
    FinisherTargeting Targeting,
    Creature? SingleTarget = null,
    IReadOnlyList<Creature>? FixedTargets = null);
