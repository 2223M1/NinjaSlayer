using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class Damage
    {
        public static Type[] CommandParameterTypes =>
        [
            typeof(PlayerChoiceContext),
            typeof(IEnumerable<Creature>),
            typeof(decimal),
            typeof(ValueProp),
            typeof(Creature),
            typeof(CardModel)
#if !NINJASLAYER_LEGACY_DAMAGE_API
            , typeof(CardPlay)
#endif
        ];

        public static Task<IEnumerable<DamageResult>> Deal(
            PlayerChoiceContext choiceContext,
            IEnumerable<Creature>? targets,
            decimal amount,
            ValueProp props,
            Creature? dealer,
            CardModel? cardSource,
            CardPlay? cardPlay)
        {
#if NINJASLAYER_LEGACY_DAMAGE_API
            return targets == null
                ? Task.FromResult<IEnumerable<DamageResult>>([])
                : CreatureCmd.Damage(
                    choiceContext,
                    targets,
                    amount,
                    props,
                    dealer,
                    cardSource);
#else
            return CreatureCmd.Damage(
                choiceContext,
                targets,
                amount,
                props,
                dealer,
                cardSource,
                cardPlay);
#endif
        }

        public static async Task<IEnumerable<DamageResult>> DealFromCard(
            PlayerChoiceContext choiceContext,
            Creature target,
            decimal amount,
            ValueProp props,
            CardModel cardSource,
            CardPlay cardPlay)
        {
#if NINJASLAYER_LEGACY_DAMAGE_API
            IEnumerable<DamageResult> results = await CreatureCmd.Damage(
                choiceContext,
                target,
                amount,
                props,
                cardSource);
#else
            IEnumerable<DamageResult> results = await CreatureCmd.Damage(
                choiceContext,
                target,
                amount,
                props,
                cardSource,
                cardPlay);
#endif
            return results;
        }

        public static decimal Modify(
            IRunState runState,
            ICombatState? combatState,
            Creature? target,
            Creature? dealer,
            decimal amount,
            ValueProp props,
            CardModel? cardSource,
            CardPlay? cardPlay,
            ModifyDamageHookType hookType,
            CardPreviewMode previewMode,
            out IEnumerable<AbstractModel> modifiers)
        {
#if NINJASLAYER_LEGACY_DAMAGE_API
            return Hook.ModifyDamage(
                runState,
                combatState,
                target,
                dealer,
                amount,
                props,
                cardSource,
                hookType,
                previewMode,
                out modifiers);
#else
            return Hook.ModifyDamage(
                runState,
                combatState,
                target,
                dealer,
                amount,
                props,
                cardSource,
                cardPlay,
                hookType,
                previewMode,
                out modifiers);
#endif
        }
    }
}
