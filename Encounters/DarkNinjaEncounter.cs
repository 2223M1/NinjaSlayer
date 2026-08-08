using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Encounters;

[RegisterActEncounter(typeof(Glory))]
public sealed class DarkNinjaEncounter : ModEncounterTemplate
{
    public override RoomType RoomType => RoomType.Monster;

    public override string CustomBgm => NinjaSlayerAudio.DarkNinjaBattleMusicEvent;

    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
        [ModelDb.Monster<DarkNinjaMonster>()];

    public override bool IsValidForAct(ActModel act) => false;

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
        [(ModelDb.Monster<DarkNinjaMonster>().ToMutable(), null)];
}
