using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class NarakuHealthBar
    {
        private static readonly FieldInfo? Creature = AccessTools.Field(typeof(NHealthBar), "_creature");
        private static readonly FieldInfo? ExpectedMaxForegroundWidth =
            AccessTools.Field(typeof(NHealthBar), "_expectedMaxFgWidth");
        private static readonly FieldInfo? OriginalBlockPosition =
            AccessTools.Field(typeof(NHealthBar), "_originalBlockPosition");

        public static bool TryGetCreature(NHealthBar healthBar, out Creature? creature)
        {
            creature = Creature?.GetValue(healthBar) as Creature;
            return creature != null;
        }

        public static float GetMaxForegroundWidth(NHealthBar healthBar)
        {
            float expectedWidth = ExpectedMaxForegroundWidth?.GetValue(healthBar) is float value
                ? value
                : 0f;
            return expectedWidth > 0f
                ? expectedWidth
                : healthBar.GetNodeOrNull<Control>("%HpForegroundContainer")?.Size.X ?? 0f;
        }

        public static void AnchorBlock(NHealthBar healthBar, float blockLeft)
        {
            Control? block = healthBar.GetNodeOrNull<Control>("%BlockContainer");
            if (block == null)
            {
                return;
            }

            Vector2 globalPosition = block.GlobalPosition;
            globalPosition.X = blockLeft;
            block.GlobalPosition = globalPosition;
            OriginalBlockPosition?.SetValue(healthBar, block.Position);
        }
    }
}
