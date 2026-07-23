namespace NinjaSlayer.Code.Compatibility;

internal static class NinjaSlayerCapabilityIds
{
    public const string Gameplay = "gameplay";
    public const string CardResolution = "card-resolution";
    public const string ReporterPass = "reporter-pass";
    public const string NancyCandidateFilter = "nancy-candidate-filter";
    public const string NancyLoadedRunRepair = "nancy-loaded-run-repair";
    public const string KaratePreview = "karate-preview";
    public const string Typography = "typography";
    public const string ChadoPresentation = "chado-presentation";
    public const string CinematicInfrastructure = "cinematic-infrastructure";
    public const string PreparedSafety = "prepared-safety";
    public const string PreparedGameplay = "prepared-gameplay";
    public const string PreparedUi = "prepared-ui";
    public const string FinisherCore = "finisher-core";
    public const string FinisherPresentation = "finisher-presentation";
    public const string FinisherTornadoCadence = "finisher-tornado-cadence";
    public const string TransitionCore = "transition-core";
    public const string TransitionLoadSmoothing = "transition-load-smoothing";
    public const string Feedback = "feedback";
    public const string TelemetryIdentity = "telemetry-identity";

    public static IReadOnlyList<string> All { get; } =
    [
        Gameplay,
        CardResolution,
        ReporterPass,
        NancyCandidateFilter,
        NancyLoadedRunRepair,
        KaratePreview,
        Typography,
        ChadoPresentation,
        CinematicInfrastructure,
        PreparedSafety,
        PreparedGameplay,
        PreparedUi,
        FinisherCore,
        FinisherPresentation,
        FinisherTornadoCadence,
        TransitionCore,
        TransitionLoadSmoothing,
        Feedback,
        TelemetryIdentity
    ];
}
