using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class YamotoKokiDodgePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_dodge";
    public static string Description => "Make Yamoto Koki evade attacks aimed at her owner.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(CreatureCmd),
            nameof(CreatureCmd.Damage),
            [
                typeof(PlayerChoiceContext),
                typeof(IEnumerable<Creature>),
                typeof(decimal),
                typeof(ValueProp),
                typeof(Creature),
                typeof(CardModel),
                typeof(CardPlay)
            ])
    ];

    public static void Prefix(
        ref IEnumerable<Creature>? targets,
        ValueProp props,
        Creature? dealer)
    {
        if (dealer is not { IsMonster: true } || !props.IsCardOrMonsterMove())
        {
            return;
        }

        List<Creature> targetList = targets?.ToList() ?? [];
        targets = targetList;
        foreach (var player in targetList
                     .Where(target => target.Side != dealer.Side)
                     .Select(target => target.Player ?? target.PetOwner)
                     .OfType<Player>()
                     .Distinct())
        {
            Creature? yamotoKoki = player.PlayerCombatState?.Pets
                .FirstOrDefault(pet => pet.Monster is YamotoKokiMonster && pet.IsAlive);
            if (yamotoKoki != null)
            {
                _ = TaskHelper.RunSafely(CreatureCmd.TriggerAnim(yamotoKoki, "Dodge", 0f));
            }
        }
    }
}
