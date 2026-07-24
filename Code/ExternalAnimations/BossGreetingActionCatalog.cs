using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed record BossGreetingActionSpec(
    string? AnimationTrigger,
    string? SfxPath,
    float MinimumDuration,
    string? VfxPath = null,
    float VfxDelay = 0f);

internal static class BossGreetingActionCatalog
{
    public static BossGreetingActionSpec Get(Creature boss)
    {
        if (boss.CombatState?.Encounter is KaiserCrabBoss)
        {
            return new BossGreetingActionSpec(null, null, 1.75f);
        }

        return boss.Monster switch
        {
            CeremonialBeast => new BossGreetingActionSpec(
                "Cast",
                "event:/sfx/enemy/enemy_attacks/ceremonial_beast/ceremonial_beast_shrill",
                1.05f,
                "vfx/vfx_scream",
                0.3f),
            KinPriest => new BossGreetingActionSpec(
                "Rally",
                "event:/sfx/enemy/enemy_attacks/the_kin_priest/the_kin_priest_rally",
                1f),
            Vantom => new BossGreetingActionSpec(
                "BUFF",
                "event:/sfx/enemy/enemy_attacks/vantom/vantom_buff",
                0.6f),
            LagavulinMatriarch => new BossGreetingActionSpec("Sleep", null, 1f),
            WaterfallGiant => new BossGreetingActionSpec(
                "Heal",
                "event:/sfx/enemy/enemy_attacks/waterfall_giant/waterfall_giant_eruption",
                0.8f),
            SoulFysh => new BossGreetingActionSpec(
                "Beckon",
                "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_beckon",
                0.6f,
                "vfx/vfx_spooky_scream",
                0.3f),
            TheInsatiable => new BossGreetingActionSpec(
                "LiquifySand",
                "event:/sfx/enemy/enemy_attacks/the_insatiable/the_insatiable_liquify_ground",
                1.25f,
                "vfx/vfx_scream",
                0.5f),
            KnowledgeDemon => new BossGreetingActionSpec("MindRotTrigger", null, 1f),
            Queen => new BossGreetingActionSpec(
                "Cast",
                "event:/sfx/enemy/enemy_attacks/queen/queen_cast",
                0.5f),
            TestSubject => new BossGreetingActionSpec(
                "BiteTrigger",
                "event:/sfx/enemy/enemy_attacks/test_subject/test_subject_bite",
                0.25f),
            Aeonglass => new BossGreetingActionSpec("Cast", null, 0.4f),
            HunterKiller => new BossGreetingActionSpec(
                "Hit",
                "event:/sfx/enemy/enemy_attacks/hunter_killer/hunter_killer_hurt",
                0.4f),
            _ => new BossGreetingActionSpec(null, null, 0.8f)
        };
    }
}
