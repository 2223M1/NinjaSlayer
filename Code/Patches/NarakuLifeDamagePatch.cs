using HarmonyLib;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.Utils.HarmonyIl;

namespace NinjaSlayer.Code.Patches;

public sealed class NarakuLifeDamagePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_naraku_life_absorption";
    public static string Description => "Absorb damage after Block and before HP-loss protections, like StSLib temporary HP.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets()
    {
        MethodInfo damage = AccessTools.DeclaredMethod(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
            [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
                typeof(Creature), typeof(CardModel)
#if !NINJASLAYER_LEGACY_DAMAGE_API
                , typeof(CardPlay)
#endif
            ]) ?? throw new MissingMethodException(typeof(CreatureCmd).FullName, nameof(CreatureCmd.Damage));
        Type stateMachine = damage.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new MissingMethodException(typeof(CreatureCmd).FullName, "Damage state machine");
        return [new(stateMachine, nameof(IAsyncStateMachine.MoveNext), Type.EmptyTypes)];
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo original = AccessTools.Method(typeof(Hook), nameof(Hook.ModifyHpLost));
        MethodInfo replacement = AccessTools.Method(typeof(NarakuLifeDamagePatch), nameof(AbsorbBeforeHpLoss));
        var rewriter = HarmonyIlRewriter.From(instructions);
        var report = rewriter.ReplaceEach("Naraku temporary HP at the damage boundary",
            (code, index) => code[index].Calls(original), (_, _) => [HarmonyIl.Call(replacement)]);
        return rewriter.InstructionsChecked(report, 3);
    }

    private static decimal AbsorbBeforeHpLoss(IRunState runState, ICombatState? combatState, Creature target,
        decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, HpLossHookPhase phases,
        out IEnumerable<AbstractModel> modifiers)
    {
        // Only real damage commands spend temporary HP. Read-only hook queries,
        // direct HP assignment and forced kills never enter this replacement.
        if (phases.HasFlag(HpLossHookPhase.BeforeOsty) && target.GetPower<NarakuLifePower>() is { } life)
            amount = life.AbsorbDamage(amount);
        return Hook.ModifyHpLost(runState, combatState, target, amount, props, dealer, cardSource, phases, out modifiers);
    }
}
