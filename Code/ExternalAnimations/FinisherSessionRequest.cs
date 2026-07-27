using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal enum FinisherScenarioKind
{
    NinjaSlayerAttack,
    YamotoKokiIaiSlash,
    EnemyExecutesNinjaSlayer
}

internal enum FinisherCompletionCondition
{
    AllCandidatesLethal,
    AnyCandidateLethal
}

internal sealed record FinisherSessionRequest(
    FinisherScenarioKind Scenario,
    FinisherCompletionCondition CompletionCondition,
    Creature Actor,
    NCreature ActorNode,
    NCreature FocusNode,
    IReadOnlyList<Creature> Victims,
    CombatCinematicCameraLease Camera,
    IFinisherActionAdapter ActionAdapter,
    CardPlay? CardPlay,
    bool RequiresAfterCardPlayed,
    int ResolvedHits,
    IReadOnlySet<ulong>? VfxBaselineChildIds = null)
{
    public bool UsesNinjaSlayerSignatureImpact =>
        Scenario == FinisherScenarioKind.NinjaSlayerAttack;
}
