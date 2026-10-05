using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class FriendlyCompanionPersonalHivePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_friendly_companion_personal_hive";
    public static string Description => "Do not give a companion's owner Personal Hive status penalties.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(PersonalHivePower), nameof(PersonalHivePower.AfterDamageReceived),
            [typeof(PlayerChoiceContext), typeof(Creature), typeof(DamageResult), typeof(ValueProp),
                typeof(Creature), typeof(CardModel)])
    ];

    public static bool Prefix(PersonalHivePower __instance, Creature target, ValueProp props,
        Creature? dealer, ref Task __result)
    {
        if (target != __instance.Owner || !props.IsPoweredAttack()
            || dealer?.Side != CombatSide.Player
            || dealer.Monster is not (YamotoKokiMonster or ForestSawatariMonster or YukanoMonster or OrigamiMissileMonster))
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
