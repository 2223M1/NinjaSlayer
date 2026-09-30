namespace NinjaSlayer.Content;

public static class NinjaSlayerCardRules
{
    public const int StartingHp = 72;
    public const int StartingStrikeCount = 4;
    public const int StartingDefendCount = 4;
    public const int StartingSignatureCardCount = 1;
    public const int StartingPrejudgeCount = 1;
    public const int CommonRewardCount = 20;
    public const int UncommonRewardCount = 35;
    public const int RareRewardCount = 25;
    public const int ShurikenBaseDamage = 6;
    public const int BlackFlameDamage = 4;

    public static IReadOnlyList<string> CommonRewardCardIds { get; } =
    [
        "Stillness",
        "CatapultThrow",
        "SomersaultKick",
        "RegulateBreath",
        "Jujutsu",
        "GrapplingHook",
        "Chop",
        "ChopStrike",
        "NinjaTaunt",
        "BladePrep",
        "SpiralJump",
        "ReadyShuriken",
        "BS1260Kick",
        "FlameGuard",
        "ReadTheEnemy",
        "KunaiThrow",
        "CrossGuard",
        "LeftUppercut",
        "PalmThrust",
        "RightUppercut"
    ];

    public static IReadOnlyList<string> UncommonRewardCardIds { get; } =
    [
        "HissAndHuff",
        "GatherMomentum",
        "LetGo",
        "MotionAndStillness",
        "HalfMoonCompassKick",
        "Sip",
        "TomoeThrow",
        "Discern",
        "Endurance",
        "GatherKi",
        "Training",
        "BackBridge",
        "ShurikenCreation",
        "BladeSweep",
        "StarlessNight",
        "HiddenEdge",
        "Moonsault",
        "BattleReady",
        "BladeBarrier",
        "Kindle",
        "NarakusMight",
        "BlackFlameInferno",
        "DevourFlame",
        "Rekindle",
        "Adapt",
        "StrikeStrike",
        "Composure",
        "Assess",
        "NavyHammer",
        "Resilience",
        "TornadoFist",
        "DragonRoundhouseKick",
        "PressTheAttack",
        "NinjaCaltrops",
        "BarehandedCatch"
    ];

    public static IReadOnlyList<string> RareRewardCardIds { get; } =
    [
        "DeepBreath",
        "FurinKazan",
        "Poise",
        "Meditation",
        "StormFist",
        "Recover",
        "Redouble",
        "AlabamaDrop",
        "HellChop",
        "AntiAirBangBangFist",
        "KarateBulwark",
        "Onslaught",
        "HellTornado",
        "BladeCycle",
        "ShurikenStorm",
        "WatchfulBlades",
        "NarakuForm",
        "Zanshin",
        "Forethought",
        "Relentless",
        "DragonFlyingKick",
        "ClearMind",
        "Macaco",
        "KillingIntent",
        "GreatUkemi"
    ];

    public static IReadOnlyList<string> ExcludedSpecialCardIds { get; } =
    [
        "StrikeNinjaSlayer",
        "DefendNinjaSlayer",
        "StraightPunch",
        "ReadAhead",
        "Chado",
        "StraightKi",
        "BlackFlame",
        "CollapseFist",
        "StrongShuriken",
        "BusyLine"
    ];


    public static int ResolveChadoBreathIncrease(int amount, bool hasChadoInHand) =>
        Math.Max(0, amount - (hasChadoInHand ? 0 : 1));

    internal static ShurikenStockResolution ResolveShurikenDiscard(
        int stock,
        bool isOwnerDiscard,
        int targetCount)
    {
        int availableStock = Math.Max(0, stock);
        return !isOwnerDiscard || availableStock == 0 || targetCount <= 0
            ? new ShurikenStockResolution(0, availableStock)
            : new ShurikenStockResolution(1, availableStock - 1);
    }

    internal static ShurikenStockResolution ResolveBladeCycleShuffle(
        int stock,
        bool hasBladeCycle,
        bool isOwnerShuffle,
        int targetCount,
        int stockLoss = 3)
    {
        int availableStock = Math.Max(0, stock);
        return !hasBladeCycle || !isOwnerShuffle || availableStock == 0 || targetCount <= 0
            ? new ShurikenStockResolution(0, availableStock)
            : new ShurikenStockResolution(availableStock, Math.Max(0, availableStock - stockLoss));
    }

    internal static bool IsBlackFlameTurnEndTarget(
        bool isAlive,
        bool isOwner,
        bool isSameSide) =>
        isAlive && (isOwner || !isSameSide);

    internal static int ResolveTurtleShellPlating(int karate) => Math.Max(0, karate);

    public static int ResolveHardItOutWounds(int accumulatedDamage, int threshold) =>
        threshold <= 0 ? 0 : Math.Max(0, accumulatedDamage) / threshold;
}

internal readonly record struct ShurikenStockResolution(int Shots, int RemainingStock);
