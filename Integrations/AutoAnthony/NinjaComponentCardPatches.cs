using global::AutoAnthony;
using ChaosCardGenerator;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.AutoAnthony;

internal static class NinjaComponentCardPatches
{
    internal static void Register(ModPatcher patcher)
    {
        patcher.RegisterPatch<ResultLocation>();
        patcher.RegisterPatch<ReturnOnAttacks>();
        patcher.RegisterPatch<PlayFromTop>();
        patcher.RegisterPatch<RequireChado>();
    }

    private static bool Has(ChaosCardModel card, string variant) => card.Generated.Operations.Any(op =>
        op.RuntimeSpec is { Opcode: NinjaComponentSources.Opcode } spec && spec.Variant == variant);

    private sealed class ResultLocation : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_return_first";
        public static string Description => "Keep the native post-play destination and Exhaust priority for return components.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(ChaosCardModel), "GetResultLocationForCardPlay")];
        public static void Postfix(ChaosCardModel __instance, ref CardLocation __result)
        {
            if (__result.pileType == PileType.Discard && Has(__instance, "return_first")
                && CombatManager.Instance.History.CardPlaysStarted.Count(e => e.CardPlay.Card == __instance
                    && e.CardPlay.IsFirstInSeries && e.HappenedThisTurn(__instance.CombatState!)) < 3)
                __result.pileType = PileType.Hand;
        }
    }

    private sealed class ReturnOnAttacks : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_return_attacks";
        public static string Description => "Use native completed card plays for the Hell Chop component.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(ChaosCardModel), nameof(ChaosCardModel.AfterCardPlayedLate))];
        public static void Postfix(ChaosCardModel __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
        {
            if (Has(__instance, "return_attacks")) __result = Complete(__result, __instance, choiceContext, cardPlay);
        }
        private static async Task Complete(Task original, ChaosCardModel card, PlayerChoiceContext choice, CardPlay played)
        {
            await original;
            if (played.Card == card || played.Card.Owner != card.Owner || played.Card.Type != CardType.Attack
                || card.Pile?.Type == PileType.Hand) return;
            int count = CombatManager.Instance.History.CardPlaysFinished.Count(e => e.HappenedThisTurn(card.CombatState!)
                && e.CardPlay.Card != card && e.CardPlay.Card.Type == CardType.Attack && e.CardPlay.Player == card.Owner);
            if (count > 0 && count % 3 == 0) await CardPileCmd.Add(card, PileType.Hand);
        }
    }

    private sealed class PlayFromTop : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_play_on_top";
        public static string Description => "Play a top-of-draw-pile component in the native automatic post-play phase.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(ChaosCardModel), nameof(ChaosCardModel.AfterAutoPostPlayPhaseEntered))];
        public static void Postfix(ChaosCardModel __instance, PlayerChoiceContext choiceContext, Player player, ref Task __result)
        {
            if (player == __instance.Owner && Has(__instance, "play_on_top")) __result = Complete(__result, __instance, choiceContext);
        }
        private static async Task Complete(Task original, ChaosCardModel card, PlayerChoiceContext choice)
        {
            await original;
            if (PileType.Draw.GetPile(card.Owner).Cards.FirstOrDefault() == card)
                await CardPileCmd.AutoPlayFromDrawPile(choice, card.Owner, 1, CardPilePosition.Top, forceExhaust: false);
        }
    }

    private sealed class RequireChado : IPatchMethod
    {
        public static string PatchId => "ninjaslayer_anthony_require_chado";
        public static string Description => "Keep Chado-consuming components unplayable without the required card.";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() => [new(typeof(ChaosCardModel), "get_IsPlayable")];
        public static void Postfix(ChaosCardModel __instance, ref bool __result)
        {
            if (__result && (Has(__instance, "tea_heal") || Has(__instance, "tea_karate") || Has(__instance, "tea_block_weak")))
                __result = PileType.Hand.GetPile(__instance.Owner).Cards.OfType<ChadoEnergyRedesignV1>().Any();
        }
    }
}
