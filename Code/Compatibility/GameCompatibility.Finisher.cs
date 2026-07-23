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
        private static readonly MethodInfo? StartDeathAnim = AccessTools.Method(
            typeof(NCreature),
            nameof(NCreature.StartDeathAnim),
            [typeof(bool)]);

        public static IReadOnlyList<CapabilityProbe> GetProbes()
        {
            bool lethalTargetAvailable = CanProtectLethalDamage(out string lethalReason);
            return
            [
                RequiredMember("AttackCommand.damage-per-hit", DamagePerHit, "AttackCommand._damagePerHit"),
                RequiredMember("AttackCommand.calculated-damage", CalculatedDamage, "AttackCommand._calculatedDamageVar"),
                RequiredMember("AttackCommand.hit-count", HitCount, "AttackCommand._hitCount"),
                RequiredMember("AttackCommand.single-target", SingleTarget, "AttackCommand._singleTarget"),
                CapabilityProbe.Required(
                    "Creature.lethal-damage-contract",
                    lethalTargetAvailable,
                    lethalTargetAvailable ? "validated" : lethalReason)
            ];
        }

        public static IReadOnlyList<CapabilityProbe> GetPresentationProbes() =>
        [
            RequiredMember(
                "NCreature.start-death-animation",
                StartDeathAnim,
                "NCreature.StartDeathAnim(bool)")
        ];

        public static bool CanProtectLethalDamage(out string reason)
        {
            if (!FinisherLethalTargetContract.TryValidate(
                    out MethodInfo? lethalDamage,
                    out _,
                    out reason)
                || lethalDamage == null)
            {
                return false;
            }

            HarmonyLib.Patches? patchInfo = Harmony.GetPatchInfo(lethalDamage);
            if (patchInfo == null)
            {
                reason = string.Empty;
                return true;
            }

            HarmonyLib.Patch? unsafeTranspiler = patchInfo.Transpilers.FirstOrDefault(patch => !IsNinjaSlayerPatch(patch));
            if (unsafeTranspiler != null)
            {
                reason = $"foreign transpiler {DescribePatch(unsafeTranspiler)} targets Creature.LoseHpInternal.";
                return false;
            }

            HarmonyLib.Patch? skippingPrefix = patchInfo.Prefixes.FirstOrDefault(patch =>
                !IsNinjaSlayerPatch(patch) && patch.PatchMethod.ReturnType == typeof(bool));
            if (skippingPrefix != null)
            {
                reason = $"foreign bool Prefix {DescribePatch(skippingPrefix)} can skip Creature.LoseHpInternal.";
                return false;
            }

            HarmonyLib.Patch? resultReplacement = patchInfo.Prefixes
                .Concat(patchInfo.Postfixes)
                .Concat(patchInfo.Finalizers)
                .FirstOrDefault(patch =>
                    !IsNinjaSlayerPatch(patch)
                    && patch.PatchMethod.GetParameters().Any(parameter =>
                        parameter.Name == "__result"
                        && parameter.ParameterType.IsByRef
                        && parameter.ParameterType.GetElementType() == typeof(DamageResult)));
            if (resultReplacement != null)
            {
                reason = $"foreign result-replacement Patch {DescribePatch(resultReplacement)} targets Creature.LoseHpInternal.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool TryReadAttackCommand(AttackCommand command, out AttackCommandState state)
        {
            if (DamagePerHit == null || CalculatedDamage == null || HitCount == null || SingleTarget == null)
            {
                state = default;
                return false;
            }

            state = new AttackCommandState(
                (decimal)(DamagePerHit.GetValue(command) ?? 0m),
                CalculatedDamage.GetValue(command) as CalculatedDamageVar,
                (int)(HitCount.GetValue(command) ?? 1),
                SingleTarget.GetValue(command) as Creature);
            return true;
        }

        private static bool IsNinjaSlayerPatch(HarmonyLib.Patch patch) =>
            patch.PatchMethod.DeclaringType?.Assembly == typeof(GameCompatibility).Assembly;

        private static string DescribePatch(HarmonyLib.Patch patch) =>
            $"owner={patch.owner}, method={patch.PatchMethod.DeclaringType?.FullName}.{patch.PatchMethod.Name}, "
            + $"priority={patch.priority}, before=[{string.Join(',', patch.before)}], after=[{string.Join(',', patch.after)}]";
    }

    internal readonly record struct AttackCommandState(
        decimal DamagePerHit,
        CalculatedDamageVar? CalculatedDamage,
        int HitCount,
        Creature? SingleTarget);
}
