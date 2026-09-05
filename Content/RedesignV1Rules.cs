namespace NinjaSlayer.Content;

public static class RedesignV1Rules
{
    public const int StartingHp = 72;
    public const int StartingStrikeCount = 4;
    public const int StartingDefendCount = 5;
    public const int StartingSignatureCardCount = 1;
    public const int CommonRewardCount = 20;
    public const int UncommonRewardCount = 31;
    public const int RareRewardCount = 23;
    public const int ShurikenBaseDamage = 4;
    public const int BlackFlameDamage = 4;

    public static IReadOnlyList<string> CommonRewardCardIds { get; } =
    [
        "SpiralRoundhouseJumpRedesignV1",
        "BladeReserveRedesignV1",
        "PourTeaRedesignV1",
        "ChadoStillnessRedesignV1",
        "GuidingFlameRedesignV1",
        "SatsubatsuRedesignV1",
        "ThrowKunaiRedesignV1",
        "LuckyStrikeRedesignV1",
        "ReadyStanceRedesignV1",
        "CommonChopRedesignV1",
        "IronBodyRedesignV1",
        "LeftHeavyPunchRedesignV1",
        "RightHeavyPunchRedesignV1",
        "HookRopeRedesignV1",
        "PalmThrustRedesignV1",
        "WhiskTeaFlashRedesignV1",
        "OneDrinkOneStrikeRedesignV1",
        "PreparedShurikenRedesignV1",
        "ChopDefenseRedesignV1",
        "RightHeavyPunchAfterSkillRedesignV1"
    ];

    public static IReadOnlyList<string> UncommonRewardCardIds { get; } =
    [
        "FlyingBladesComeRedesignV1",
        "ShurikenGenerationRedesignV1",
        "BladeSweepRedesignV1",
        "RecycledBladesRedesignV1",
        "CombatAdjustmentRedesignV1",
        "SweepKickRedesignV1",
        "TeaStormRedesignV1",
        "MetabolicAccelerationRedesignV1",
        "GuwaaRedesignV1",
        "AdversityCarapaceRedesignV1",
        "RedBlackFlameAttackRedesignV1",
        "RoundhouseKickRedesignV1",
        "TechniqueSearchRedesignV1",
        "FlyingBladeDanceRedesignV1",
        "BattlefieldInsightRedesignV1",
        "AbyssStrengthRedesignV1",
        "DisadvantageTacticsRedesignV1",
        "KarateTrainingRedesignV1",
        "ChopStrikeRedesignV1",
        "BackBridgeRedesignV1",
        "KarateReversalRedesignV1",
        "TornadoFistRedesignV1",
        "MaskRedesignV1",
        "WasshoiRedesignV1",
        "CounteroffensiveGuardRedesignV1",
        "BladeCycleRedesignV1",
        "HiddenEdgeRedesignV1",
        "AbandonThoughtRedesignV1",
        "FocusedMindRedesignV1",
        "BurnBurnBurnRedesignV1",
        "ReturnReturnReturnRedesignV1"
    ];

    public static IReadOnlyList<string> RareRewardCardIds { get; } =
    [
        "TurtleShellRedesignV1",
        "FurinKazanChadoRedesignV1",
        "HardItOutRedesignV1",
        "NarakuFormRedesignV1",
        "OnlyKarateRedesignV1",
        "ClankDrinkTeaRedesignV1",
        "ChopRedesignV1",
        "DragonFlyingKickRedesignV1",
        "KillingIntentRedesignV1",
        "ChadoFurinKazanRedesignV1",
        "GreatUkeRedesignV1",
        "NinjaGreetingRedesignV1",
        "ComposeHaikuRedesignV1",
        "DecidedOutcomeRedesignV1",
        "MomentumRedesignV1",
        "LingeringMeleeRedesignV1",
        "AlabamaDropRedesignV1",
        "KarateTeaRedesignV1",
        "GiantShurikenRedesignV1",
        "StormFistRedesignV1",
        "EmptyShurikenRedesignV1",
        "HellTornadoRedesignV1",
        "TeaTeaRedesignV1"
    ];

    public static IReadOnlyList<string> ExcludedSpecialCardIds { get; } =
    [
        "StrikeNinjaSlayerRedesignV1",
        "DefendNinjaSlayerRedesignV1",
        "KarateStraightRedesignV1",
        "ChadoEnergyRedesignV1",
        "StraightKiRedesignV1",
        "BlackFlameRedesignV1",
        "CollapseFistRedesignV1",
        "StrongShurikenTokenRedesignV1",
        "FinisherRedesignV1",
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
        int targetCount)
    {
        int availableStock = Math.Max(0, stock);
        return !hasBladeCycle || !isOwnerShuffle || availableStock == 0 || targetCount <= 0
            ? new ShurikenStockResolution(0, availableStock)
            : new ShurikenStockResolution(availableStock, availableStock - 1);
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
