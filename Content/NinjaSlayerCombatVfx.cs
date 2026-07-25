using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Vfx;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCombatVfx
{
    private const string BurnDamageSfx = "event:/sfx/characters/attack_fire";

    public static AttackCommand WithDefectStrikeHitFx(this AttackCommand command) =>
        command.WithHitFx(VfxCmd.bluntPath, null, TmpSfx.bluntAttack);

    public static AttackCommand WithHeavyBluntHitFx(this AttackCommand command) =>
        command.WithHitFx(VfxCmd.heavyBluntPath, null, TmpSfx.heavyAttack);

    public static async Task<AttackCommand> ExecuteWithoutScreenShake(this AttackCommand command, PlayerChoiceContext? choiceContext)
    {
        using var _ = ScreenShakeSuppressionContext.Suppress();
        return await command.Execute(choiceContext);
    }

    public static void PlayDefectStrikeHitFx(Creature target)
    {
        VfxCmd.PlayOnCreatureCenter(target, VfxCmd.bluntPath);
        NDebugAudioManager.Instance?.Play(TmpSfx.bluntAttack);
    }

    public static void PlayYamotoKokiIaiPetals(Creature attacker)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null)
        {
            return;
        }

        NYamotoKokiIaiPetalsVfx? petals = NYamotoKokiIaiPetalsVfx.Create(attacker);
        if (petals != null)
        {
            room.CombatVfxContainer.AddChildSafely(petals);
        }

    }

    public static void PlayYamotoKokiIaiImpact(IEnumerable<Creature> targets)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null)
        {
            return;
        }

        foreach (Creature target in targets)
        {
            NYamotoKokiIaiImpactVfx? impact = NYamotoKokiIaiImpactVfx.Create(target);
            if (impact != null)
            {
                room.CombatVfxContainer.AddChildSafely(impact);
            }
        }
    }

    public static void PreloadYamotoKokiIaiFeedback()
    {
        NinjaSlayerVfxUtil.PreloadModVfxScene(NYamotoKokiIaiPetalsVfx.ScenePath);
        NinjaSlayerVfxUtil.PreloadModVfxScene(NYamotoKokiIaiImpactVfx.ScenePath);
    }

    public static void PlayBurnStatusFeedback(IEnumerable<Creature> targets)
    {
        if (NCombatRoom.Instance is null)
        {
            return;
        }

        foreach (Creature target in targets)
        {
            try
            {
                NNinjaSlayerGroundFireVfx? vfx = NNinjaSlayerGroundFireVfx.Create(target);
                if (vfx is not null)
                {
                    NCombatRoom.Instance.CombatVfxContainer.AddChildSafely(vfx);
                }
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"Failed to play burn feedback for {target.GetType().Name}: {ex}");
            }
        }

        try
        {
            SfxCmd.Play(BurnDamageSfx);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Failed to play burn feedback audio: {ex}");
        }
    }
}
