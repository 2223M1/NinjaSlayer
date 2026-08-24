using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class Finisher
    {
        private static readonly FieldInfo? DamagePerHit = AccessTools.Field(typeof(AttackCommand), "_damagePerHit");
        private static readonly FieldInfo? CalculatedDamage = AccessTools.Field(typeof(AttackCommand), "_calculatedDamageVar");
        private static readonly FieldInfo? HitCount = AccessTools.Field(typeof(AttackCommand), "_hitCount");
        private static readonly FieldInfo? SingleTarget = AccessTools.Field(typeof(AttackCommand), "_singleTarget");
        private static readonly FieldInfo? AttackerAnimName = AccessTools.Field(typeof(AttackCommand), "_attackerAnimName");
        private static readonly FieldInfo? ShouldPlayAnimation = AccessTools.Field(typeof(AttackCommand), "_shouldPlayAnimation");
        public static bool TryReadAttackCommand(AttackCommand command, out AttackCommandState state)
        {
            if (DamagePerHit == null
                || CalculatedDamage == null
                || HitCount == null
                || SingleTarget == null
                || AttackerAnimName == null
                || ShouldPlayAnimation == null)
            {
                throw new MissingFieldException(
                    typeof(AttackCommand).FullName,
                    "Required finisher command fields");
            }

            state = new AttackCommandState(
                (decimal)(DamagePerHit.GetValue(command) ?? 0m),
                CalculatedDamage.GetValue(command) as CalculatedDamageVar,
                (int)(HitCount.GetValue(command) ?? 1),
                SingleTarget.GetValue(command) as Creature,
                AttackerAnimName.GetValue(command) as string,
                (bool)(ShouldPlayAnimation.GetValue(command) ?? false));
            return true;
        }
    }

    internal readonly record struct AttackCommandState(
        decimal DamagePerHit,
        CalculatedDamageVar? CalculatedDamage,
        int HitCount,
        Creature? SingleTarget,
        string? AttackerAnimName,
        bool ShouldPlayAnimation);
}
