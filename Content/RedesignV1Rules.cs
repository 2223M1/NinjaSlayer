namespace NinjaSlayer.Content;

public static class RedesignV1Rules
{
    public const int StartingHp = 72;
    public const int CommonRewardCount = 19;
    public const int UncommonRewardCount = 32;
    public const int RareRewardCount = 17;
    public const int ShurikenBaseDamage = 4;

    public static IReadOnlyList<string> CommonRewardCardIds { get; } =
    [
        "CountermeasureRedesignV1",
        "SpiralRoundhouseJumpRedesignV1",
        "HiddenEdgeRedesignV1",
        "PourTeaRedesignV1",
        "OverexertRedesignV1",
        "GuidingFlameRedesignV1",
        "ThrowKunaiRedesignV1",
        "ObserverGuardRedesignV1",
        "ReflexGuardRedesignV1",
        "ReadyStanceRedesignV1",
        "StormFistRedesignV1",
        "AbandonThoughtRedesignV1",
        "BodyguardRedesignV1",
        "LeftHeavyPunchRedesignV1",
        "RightHeavyPunchRedesignV1",
        "TrumpCardRedesignV1",
        "HookRopeRedesignV1",
        "RoundhouseKickRedesignV1",
        "PalmThrustRedesignV1"
    ];

    public static IReadOnlyList<string> UncommonRewardCardIds { get; } =
    [
        "FlyingBladesComeRedesignV1",
        "ShurikenGenerationRedesignV1",
        "CombatAdjustmentRedesignV1",
        "SweepKickRedesignV1",
        "TeaStormRedesignV1",
        "MetabolicAccelerationRedesignV1",
        "GuwaaRedesignV1",
        "AlabamaDropRedesignV1",
        "AdversityCarapaceRedesignV1",
        "RedBlackFlameAttackRedesignV1",
        "TechniqueSearchRedesignV1",
        "DoubleForceRedesignV1",
        "FlyingBladeDanceRedesignV1",
        "NinjaSixthSenseRedesignV1",
        "DecidedOutcomeRedesignV1",
        "AetherEnergyRedesignV1",
        "AbyssStrengthRedesignV1",
        "DisadvantageTacticsRedesignV1",
        "KarateTrainingRedesignV1",
        "JujutsuStanceRedesignV1",
        "IyaRedesignV1",
        "BackBridgeRedesignV1",
        "ChadoSecretRedesignV1",
        "MaskRedesignV1",
        "ExecutionMoveRedesignV1",
        "WasshoiRedesignV1",
        "ObserveBattleRedesignV1",
        "EnduranceRedesignV1",
        "EmptyMindRedesignV1",
        "GauntletRedesignV1",
        "BloodTearsRedesignV1",
        "BladeCycleRedesignV1"
    ];

    public static IReadOnlyList<string> RareRewardCardIds { get; } =
    [
        "HellTornadoRedesignV1",
        "GiantShurikenRedesignV1",
        "KarateRallyRedesignV1",
        "FurinKazanChadoRedesignV1",
        "HardItOutRedesignV1",
        "NarakuFormRedesignV1",
        "OnlyKarateRedesignV1",
        "TornadoFistRedesignV1",
        "ClankDrinkTeaRedesignV1",
        "ChopRedesignV1",
        "DragonFlyingKickRedesignV1",
        "KillingIntentRedesignV1",
        "StraightKiRedesignV1",
        "ChadoFurinKazanRedesignV1",
        "GreatUkeRedesignV1",
        "NinjaGreetingRedesignV1",
        "ComposeHaikuRedesignV1"
    ];

    public static IReadOnlyList<string> ExcludedSpecialCardIds { get; } =
    [
        "KarateStraightRedesignV1",
        "TurtleShellRedesignV1",
        "ChadoEnergyRedesignV1",
        "PunchRedesignV1",
        "IyaEchoRedesignV1",
        "BlackFlameRedesignV1",
        "CollapseFistRedesignV1"
    ];

    public static int ShurikenDamage(int bonus) => ShurikenBaseDamage + Math.Max(0, bonus);

    public static int ResolveChadoBreathIncrease(int amount, bool hasChadoEnergy) =>
        Math.Max(0, amount - (hasChadoEnergy ? 0 : 1));

    internal static ShurikenShuffleResolution ResolveShurikenShuffle(
        int stock,
        bool preserveStock,
        bool isOwnerShuffle,
        int targetCount)
    {
        int availableStock = Math.Max(0, stock);
        return !isOwnerShuffle || availableStock == 0 || targetCount <= 0
            ? new ShurikenShuffleResolution(0, availableStock)
            : new ShurikenShuffleResolution(
                availableStock,
                preserveStock ? availableStock : 0);
    }

    internal static int ResolveTurtleShellPlating(int karate, int bonusPlating) =>
        Math.Max(0, karate) + Math.Max(0, bonusPlating);

    public static int ResolveHardItOutWounds(int accumulatedDamage, int threshold) =>
        threshold <= 0 ? 0 : Math.Max(0, accumulatedDamage) / threshold;
}

internal readonly record struct ShurikenShuffleResolution(int Shots, int RemainingStock);
