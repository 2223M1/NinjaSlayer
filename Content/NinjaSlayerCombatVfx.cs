using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Nodes;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCombatVfx
{
    private const string BurnDamageSfx = "event:/sfx/characters/attack_fire";

    public static AttackCommand WithDefectStrikeHitFx(this AttackCommand command) =>
        command.WithHitFx(VfxCmd.bluntPath, null, TmpSfx.bluntAttack);

    public static AttackCommand WithHeavyBluntHitFx(this AttackCommand command) =>
        command.WithHitFx(VfxCmd.heavyBluntPath, null, TmpSfx.heavyAttack);

    public static void PlayDefectStrikeHitFx(Creature target)
    {
        VfxCmd.PlayOnCreatureCenter(target, VfxCmd.bluntPath);
        NDebugAudioManager.Instance?.Play(TmpSfx.bluntAttack);
    }

    public static void PlayBurnStatusFeedback(IEnumerable<Creature> targets)
    {
        if (NCombatRoom.Instance is null)
        {
            return;
        }

        foreach (Creature target in targets)
        {
            NNinjaSlayerGroundFireVfx? vfx = NNinjaSlayerGroundFireVfx.Create(target);
            if (vfx is not null)
            {
                NCombatRoom.Instance.CombatVfxContainer.AddChildSafely(vfx);
            }
        }

        SfxCmd.Play(BurnDamageSfx);
    }
}
