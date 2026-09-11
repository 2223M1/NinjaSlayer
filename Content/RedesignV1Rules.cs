namespace NinjaSlayer.Content;

public static class RedesignV1Rules
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
        "ChadoStillnessRedesignV1",
        "WhiskTeaFlashRedesignV1",
        "OneDrinkOneStrikeRedesignV1",
        "PourTeaRedesignV1",
        "ReadyStanceRedesignV1",
        "HookRopeRedesignV1",
        "CommonChopRedesignV1",
        "LeftHeavyPunchRedesignV1",
        "IronBodyRedesignV1",
        "BladeReserveRedesignV1",
        "SpiralRoundhouseJumpRedesignV1",
        "PreparedShurikenRedesignV1",
        "SatsubatsuRedesignV1",
        "GuidingFlameRedesignV1",
        "LuckyStrikeRedesignV1",
        "ThrowKunaiRedesignV1",
        "ChopDefenseRedesignV1",
        "RightHeavyPunchRedesignV1",
        "PalmThrustRedesignV1",
        "RightHeavyPunchAfterSkillRedesignV1"
    ];

    public static IReadOnlyList<string> UncommonRewardCardIds { get; } =
    [
        "MetabolicAccelerationRedesignV1",
        "AdversityCarapaceRedesignV1",
        "AbandonThoughtRedesignV1",
        "CombatAdjustmentRedesignV1",
        "SweepKickRedesignV1",
        "SipTea",
        "ObserveBattlefield",
        "DecidedOutcomeRedesignV1",
        "KarateTrainingRedesignV1",
        "Endurance",
        "GatherKi",
        "Slaughter",
        "BackBridgeRedesignV1",
        "ShurikenCreation",
        "BladeSweepRedesignV1",
        "HiddenEdgeRedesignV1",
        "OyeahThrowSword",
        "GiantShurikenRedesignV1",
        "ShurikenGenerationRedesignV1",
        "RedBlackFlameAttackRedesignV1",
        "AbyssStrengthRedesignV1",
        "BurnBurnBurnRedesignV1",
        "ReturnReturnReturnRedesignV1",
        "BlackFlameRecovery",
        "TechniqueSearchRedesignV1",
        "BattlefieldInsightRedesignV1",
        "ChopStrikeRedesignV1",
        "FlyingBladeDanceRedesignV1",
        "TonyRetention",
        "WasshoiRedesignV1",
        "StatusDraw",
        "TornadoFistRedesignV1",
        "RoundhouseKickRedesignV1",
        "PlaceholderBlueDefense01",
        "CounteroffensiveGuardRedesignV1"
    ];

    public static IReadOnlyList<string> RareRewardCardIds { get; } =
    [
        "TeaStormRedesignV1",
        "ChadoFurinKazanRedesignV1",
        "KarateTeaRedesignV1",
        "TeaTeaRedesignV1",
        "StormFistRedesignV1",
        "PlaceholderGoldDefense01",
        "OnlyKarateRedesignV1",
        "AlabamaDropRedesignV1",
        "ChopRedesignV1",
        "AntiAirBangBangFist",
        "TurtleShellRedesignV1",
        "RecycledBladesRedesignV1",
        "HellTornadoRedesignV1",
        "Wasssssshoi",
        "ShurikenStorm",
        "BladeCycleRedesignV1",
        "NarakuFormRedesignV1",
        "KarateScry",
        "ComposeHaikuRedesignV1",
        "LingeringMeleeRedesignV1",
        "DragonFlyingKickRedesignV1",
        "FurinKazanChadoRedesignV1",
        "HardItOutRedesignV1",
        "KillingIntentRedesignV1",
        "GreatUkeRedesignV1"
    ];

    public static IReadOnlyList<string> ExcludedSpecialCardIds { get; } =
    [
        "StrikeNinjaSlayerRedesignV1",
        "DefendNinjaSlayerRedesignV1",
        "KarateStraightRedesignV1",
        "Prejudge",
        "ChadoEnergyRedesignV1",
        "StraightKiRedesignV1",
        "BlackFlameRedesignV1",
        "CollapseFistRedesignV1",
        "StrongShurikenTokenRedesignV1",
        "BusyLine"
    ];

    internal static bool ShouldOwnTransientShurikenSlot(int baseOrbSlotCount, int capacity) =>
        baseOrbSlotCount == 0 && capacity == 0;

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
