using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class KarateCardPreviewTargetPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_karate_preview_target";

    public static string Description => "Track card preview targets for karate health bar forecast.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCard), nameof(NCard.SetPreviewTarget), [typeof(Creature)])];

    public static void Postfix(NCard __instance, Creature? creature)
    {
        CardModel? card = __instance.Model;
        if (card == null)
        {
            return;
        }

        Creature? previousTarget = KarateCombatPreviewContext.CurrentCard == card
            ? KarateCombatPreviewContext.CurrentTarget
            : null;

        if (creature == null || !CanPreviewKarate(card, creature))
        {
            KarateCombatPreviewContext.Clear(card);
            return;
        }

        bool currentHasKarate = creature.GetPowerAmount<KaratePower>() > 0;
        bool previousHasKarate = previousTarget?.GetPowerAmount<KaratePower>() > 0;
        if (!currentHasKarate && !previousHasKarate)
        {
            return;
        }

        if (currentHasKarate)
        {
            KarateCombatPreviewContext.Set(card, creature);
        }
        else
        {
            KarateCombatPreviewContext.Clear(card);
        }
    }

    private static bool CanPreviewKarate(CardModel card, Creature target) =>
        card.Type == CardType.Attack
        && KarateTriggerRules.CanTriggerFromCardSource(card)
        && card.Owner.Creature.CombatState != null
        && card.Owner.Creature.CombatState == target.CombatState;
}

public sealed class KarateHealthBarTextPreviewPatch : IPatchMethod
{
    private static readonly FieldInfo? CreatureField = AccessTools.Field(typeof(NHealthBar), "_creature");
    private static readonly FieldInfo? HpLabelField = AccessTools.Field(typeof(NHealthBar), "_hpLabel");

    public static string PatchId => "ninjaslayer_karate_hp_label_preview";

    public static string Description => "Subtract forecasted karate damage from HP label while targeting with an attack.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NHealthBar), "RefreshText")];

    public static void Postfix(NHealthBar __instance)
    {
        Creature? creature = CreatureField?.GetValue(__instance) as Creature;
        MegaLabel? hpLabel = HpLabelField?.GetValue(__instance) as MegaLabel;
        if (creature == null || hpLabel == null || !creature.HpDisplay.ShowsNumbers())
        {
            return;
        }

        KaratePower? karate = creature.GetPower<KaratePower>();
        CardModel? previewCard = KarateCombatPreviewContext.TryGetCard(creature);
        if (karate == null || previewCard == null)
        {
            return;
        }

        int karateDamage = KarateForecastCalculator.ResolveHpPreviewDamage(karate, previewCard, creature);
        if (karateDamage <= 0)
        {
            return;
        }

        int displayHp = Math.Max(0, creature.CurrentHp - karateDamage);
        hpLabel.SetTextAutoSize($"{displayHp}/{creature.MaxHp}");
    }
}
