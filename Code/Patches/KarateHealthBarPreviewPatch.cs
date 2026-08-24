using MegaCrit.Sts2.addons.mega_text;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class KarateCardPreviewTargetPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_karate_preview_target";

    public static string Description => "Track active drag targets for karate health bar forecast.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCardPlay), "OnCreatureHover", [typeof(NCreature)])];

    public static void Postfix(NCardPlay __instance, NCreature creature)
    {
        CardModel? card = __instance.Holder?.CardNode?.Model;
        if (card == null)
        {
            KaratePreviewScopeRegistry.Release(__instance);
            return;
        }

        Creature target = creature.Entity;
        if (!CanPreviewKarate(card, target))
        {
            KaratePreviewScopeRegistry.Release(__instance);
            return;
        }

        if (card.Owner.Creature.GetPowerAmount<KaratePower>() <= 0)
        {
            KaratePreviewScopeRegistry.Release(__instance);
            return;
        }

        KaratePreviewScopeRegistry.Replace(__instance, card, target);
    }

    private static bool CanPreviewKarate(CardModel card, Creature target) =>
        card.Type == CardType.Attack
        && KarateTriggerRules.CanTriggerFromCardSource(card)
        && card.Owner.Creature.CombatState != null
        && card.Owner.Creature.CombatState == target.CombatState;
}

public sealed class KarateCardPreviewAllEnemiesPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_karate_preview_all_enemies";

    public static string Description => "Track all targets while dragging an all-enemy attack.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCardPlay), "ShowMultiCreatureTargetingVisuals")];

    public static void Postfix(NCardPlay __instance)
    {
        CardModel? card = __instance.Holder?.CardNode?.Model;
        if (card?.Type != CardType.Attack
            || card.TargetType != TargetType.AllEnemies
            || card.CombatState == null
            || card.Owner.Creature.GetPowerAmount<KaratePower>() <= 0)
        {
            KaratePreviewScopeRegistry.Release(__instance);
            return;
        }

        KaratePreviewScopeRegistry.Replace(__instance, card, card.CombatState.HittableEnemies);
    }
}

public sealed class KarateCardPreviewClearPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_karate_preview_clear";

    public static string Description => "Clear karate forecasts when card dragging ends.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCardPlay), "OnCreatureUnhover", [typeof(NCreature)]),
        new(typeof(NCardPlay), "Cleanup", [typeof(bool)])
    ];

    public static void Prefix(NCardPlay __instance)
    {
        KaratePreviewScopeRegistry.Release(__instance);
    }
}

internal static class KaratePreviewScopeRegistry
{
    private static readonly ConditionalWeakTable<NCardPlay, ScopeHolder> Scopes = new();

    public static void Replace(NCardPlay cardPlay, CardModel card, Creature target)
        => Replace(cardPlay, card, [target]);

    public static void Replace(NCardPlay cardPlay, CardModel card, IReadOnlyList<Creature> targets)
    {
        Release(cardPlay);
        Scopes.Add(cardPlay, new ScopeHolder(KarateCombatPreviewContext.Enter(card, targets)));
    }

    public static void Release(NCardPlay cardPlay)
    {
        if (Scopes.TryGetValue(cardPlay, out ScopeHolder? holder))
        {
            Scopes.Remove(cardPlay);
            holder.Dispose();
        }
    }

    private sealed class ScopeHolder(IDisposable scope) : IDisposable
    {
        public void Dispose() => scope.Dispose();
    }
}

public sealed class KarateHealthBarTextPreviewPatch : IPatchMethod
{
    private static readonly FieldInfo HealthBarCreature =
        AccessTools.Field(typeof(NHealthBar), "_creature")
        ?? throw new MissingFieldException(typeof(NHealthBar).FullName, "_creature");
    private static readonly FieldInfo HpLabel =
        AccessTools.Field(typeof(NHealthBar), "_hpLabel")
        ?? throw new MissingFieldException(typeof(NHealthBar).FullName, "_hpLabel");

    public static string PatchId => "ninjaslayer_karate_hp_label_preview";

    public static string Description => "Subtract forecasted karate damage from HP label while targeting with an attack.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NHealthBar), "RefreshText")];

    public static void Postfix(NHealthBar __instance)
    {
        Creature? creature = HealthBarCreature.GetValue(__instance) switch
        {
            null => null,
            Creature value => value,
            _ => throw new InvalidOperationException(
                "NHealthBar._creature has an unexpected runtime type.")
        };
        MegaLabel? hpLabel = HpLabel.GetValue(__instance) switch
        {
            null => null,
            MegaLabel value => value,
            _ => throw new InvalidOperationException(
                "NHealthBar._hpLabel has an unexpected runtime type.")
        };
        if (creature is null
            || hpLabel is null
            || !creature.HpDisplay.ShowsNumbers())
        {
            return;
        }

        CardModel? previewCard = KarateCombatPreviewContext.TryGetCard(creature);
        Creature? attacker = previewCard?.Owner.Creature;
        if (attacker == null || attacker.Side == creature.Side)
        {
            return;
        }

        int karateDamage = KarateForecastCalculator.ResolveHpPreviewDamage(
            attacker.GetPowerAmount<KaratePower>(),
            previewCard,
            creature);
        if (karateDamage <= 0)
        {
            return;
        }

        int displayHp = Math.Max(0, creature.CurrentHp - karateDamage);
        hpLabel.SetTextAutoSize($"{displayHp}/{creature.MaxHp}");
    }
}
