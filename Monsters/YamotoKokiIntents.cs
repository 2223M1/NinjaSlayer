using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Intents;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace NinjaSlayer.Monsters;

internal sealed class YamotoKokiSummonIntent : AbstractIntent
{
    internal const string IconPath =
        "res://NinjaSlayer/images/intents/yamoto_koki_summon.png";
    private static Texture2D? _icon;

    public override IntentType IntentType => IntentType.Summon;
    protected override string IntentPrefix => "NINJA_SLAYER_YAMOTO_KOKI_SUMMON";
    protected override string SpritePath => IconPath;
    public override IEnumerable<string> AssetPaths => [IconPath];

    public override Texture2D GetTexture(IEnumerable<Creature> targets, Creature owner) =>
        _icon ??= GD.Load<Texture2D>(IconPath)
            ?? throw new InvalidOperationException("Could not load Yamoto Koki summon intent icon.");

    public override string GetAnimation(IEnumerable<Creature> targets, Creature owner) =>
        IntentAnimData.summon;

    protected override LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner)
    {
        LocString description = base.GetIntentDescription(targets, owner);
        description.Add("Count", YamotoKokiMonster.SummonMissileCount);
        return description;
    }
}

internal sealed class YamotoKokiIaiSlashIntent(Func<decimal> damageCalc)
    : SingleAttackIntent(damageCalc)
{
    internal const string IconPath =
        "res://NinjaSlayer/images/intents/yamoto_koki_iai_slash.png";
    private static Texture2D? _icon;

    protected override string IntentPrefix => "NINJA_SLAYER_YAMOTO_KOKI_IAI_SLASH";
    protected override string SpritePath => IconPath;
    public override IEnumerable<string> AssetPaths => [IconPath];

    public override Texture2D GetTexture(IEnumerable<Creature> targets, Creature owner) =>
        _icon ??= GD.Load<Texture2D>(IconPath)
            ?? throw new InvalidOperationException("Could not load Yamoto Koki Iai intent icon.");

    public override string GetAnimation(IEnumerable<Creature> targets, Creature owner) =>
        IntentAnimData.attack1;
}
