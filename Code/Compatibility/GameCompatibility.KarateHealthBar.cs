using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class KarateHealthBar
    {
        private static readonly FieldInfo? Creature = AccessTools.Field(typeof(NHealthBar), "_creature");
        private static readonly FieldInfo? HpLabel = AccessTools.Field(typeof(NHealthBar), "_hpLabel");

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            CapabilityProbe.Optional(
                "NHealthBar.creature",
                Creature != null,
                Creature != null ? "available" : "NHealthBar._creature is unavailable"),
            CapabilityProbe.Optional(
                "NHealthBar.hp-label",
                HpLabel != null,
                HpLabel != null ? "available" : "NHealthBar._hpLabel is unavailable")
        ];

        public static bool TryGetState(NHealthBar healthBar, out Creature? creature, out MegaLabel? hpLabel)
        {
            creature = Creature?.GetValue(healthBar) as Creature;
            hpLabel = HpLabel?.GetValue(healthBar) as MegaLabel;
            return creature != null && hpLabel != null;
        }
    }
}
