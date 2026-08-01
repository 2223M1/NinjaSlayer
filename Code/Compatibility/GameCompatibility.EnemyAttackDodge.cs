using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class EnemyAttackDodge
    {
        private static readonly FieldInfo? AttackerAnimName =
            AccessTools.Field(typeof(AttackCommand), "_attackerAnimName");
        private static readonly FieldInfo? VisualAttacker =
            AccessTools.Field(typeof(AttackCommand), "_visualAttacker");
        private static readonly FieldInfo? WaitBeforeHit =
            AccessTools.Field(typeof(AttackCommand), "_waitBeforeHit");

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            OptionalMember("AttackCommand.attacker-animation", AttackerAnimName, "AttackCommand._attackerAnimName"),
            OptionalMember("AttackCommand.visual-attacker", VisualAttacker, "AttackCommand._visualAttacker"),
            OptionalMember("AttackCommand.wait-before-hit", WaitBeforeHit, "AttackCommand._waitBeforeHit")
        ];

        public static bool TryReadPresentation(
            AttackCommand command,
            Creature fallbackAttacker,
            out EnemyAttackPresentation presentation)
        {
            presentation = default;
            if (AttackerAnimName is null || VisualAttacker is null || WaitBeforeHit is null
                || AttackerAnimName.GetValue(command) is not string triggerName
                || WaitBeforeHit.GetValue(command) is not float[] { Length: >= 2 } hitWaits)
            {
                return false;
            }

            presentation = new EnemyAttackPresentation(
                triggerName,
                VisualAttacker.GetValue(command) as Creature ?? fallbackAttacker,
                Math.Max(0f, hitWaits[0]),
                Math.Max(0f, hitWaits[1]));
            return true;
        }

        private static CapabilityProbe OptionalMember(
            string name,
            MemberInfo? member,
            string memberDescription) =>
            CapabilityProbe.Optional(
                name,
                member != null,
                member != null ? "available" : $"{memberDescription} is unavailable");
    }

    internal readonly record struct EnemyAttackPresentation(
        string TriggerName,
        Creature VisualAttacker,
        float FastHitWait,
        float StandardHitWait);
}
