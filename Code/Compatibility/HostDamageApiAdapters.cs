using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Powers;

#pragma warning disable CA1707 // Harmony reserves double-underscore parameter names.

namespace NinjaSlayer.Code.Patches
{
    public sealed partial class NinjaSlayerFinisherPrimaryDamagePatch
    {
#if NINJASLAYER_LEGACY_DAMAGE_API
        public static bool Prefix(
            PlayerChoiceContext choiceContext,
            ref IEnumerable<Creature>? targets,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource,
            ref Task<IEnumerable<DamageResult>> __result) =>
            PrefixCore(
                choiceContext,
                ref targets,
                amount,
                props,
                dealer,
                cardSource,
                NinjaSlayerFinisherCinematic.ResolveActiveCardPlay(dealer, cardSource),
                ref __result);
#else
        public static bool Prefix(
            PlayerChoiceContext choiceContext,
            ref IEnumerable<Creature>? targets,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource,
            CardPlay? cardPlay,
            ref Task<IEnumerable<DamageResult>> __result) =>
            PrefixCore(
                choiceContext,
                ref targets,
                amount,
                props,
                dealer,
                cardSource,
                cardPlay,
                ref __result);
#endif
    }
}

#pragma warning restore CA1707

namespace NinjaSlayer.Powers
{
    public sealed partial class EveryThirdAttackPower
    {
#if NINJASLAYER_LEGACY_DAMAGE_API
        public override decimal ModifyDamageMultiplicative(
            Creature? target,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource) =>
            ModifyDamageMultiplicativeCore(target, amount, props, dealer, cardSource);
#else
        public override decimal ModifyDamageMultiplicative(
            Creature? target,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource,
            CardPlay? cardPlay) =>
            ModifyDamageMultiplicativeCore(target, amount, props, dealer, cardSource);
#endif
    }

    public sealed partial class NullifyHitsPower
    {
#if NINJASLAYER_LEGACY_DAMAGE_API
        public override decimal ModifyDamageCap(
            Creature? target,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource) =>
            ModifyDamageCapCore(target, props, dealer, cardSource);
#else
        public override decimal ModifyDamageCap(
            Creature? target,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource,
            CardPlay? cardPlay) =>
            ModifyDamageCapCore(target, props, dealer, cardSource);
#endif
    }

    public sealed partial class DamageFocusPower
    {
#if NINJASLAYER_LEGACY_DAMAGE_API
        public override decimal ModifyDamageMultiplicative(
            Creature? target,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource) =>
            ModifyDamageMultiplicativeCore(target, amount, props, dealer, cardSource);
#else
        public override decimal ModifyDamageMultiplicative(
            Creature? target,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource,
            CardPlay? cardPlay) =>
            ModifyDamageMultiplicativeCore(target, amount, props, dealer, cardSource);
#endif
    }

    public sealed partial class OpeningPower
    {
#if NINJASLAYER_LEGACY_DAMAGE_API
        public override decimal ModifyDamageMultiplicative(
            Creature? target,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource) =>
            ModifyDamageMultiplicativeCore(target, amount, props, dealer, cardSource);
#else
        public override decimal ModifyDamageMultiplicative(
            Creature? target,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource,
            CardPlay? cardPlay) =>
            ModifyDamageMultiplicativeCore(target, amount, props, dealer, cardSource);
#endif
    }
}

namespace NinjaSlayer.Cards.RedesignV1
{
    public sealed partial class ClankDrinkTeaRedesignV1
    {
#if NINJASLAYER_LEGACY_DAMAGE_API
        public override decimal ModifyDamageAdditive(
            Creature? target,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource) =>
            ModifyDamageAdditiveCore(target, amount, props, dealer, cardSource);
#else
        public override decimal ModifyDamageAdditive(
            Creature? target,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource,
            CardPlay? cardPlay) =>
            ModifyDamageAdditiveCore(target, amount, props, dealer, cardSource);
#endif
    }
}
