using System.Text.Json;
using System.Text.Json.Serialization;

namespace NinjaSlayer.SmokeDriver;

internal sealed record TheaterScript
{
    public string Seed { get; init; } = "NINJASLAYER_THEATER_01";
    public string Purpose { get; init; } = "promo";
    public int Act { get; init; } = 3;
    public double Duration { get; init; } = 38.5;
    public string[] Relics { get; init; } = [];
    public int Karate { get; init; } = 2;
    public string GreetingMode { get; init; } = "brief";
    public string? GreetingEncounter { get; init; }
    public string GreetingSpeed { get; init; } = "Fast";
    public bool GreetingPause { get; init; }
    public TheaterCue[] Cues { get; init; } = [];

    internal static TheaterScript Load(string path)
    {
        var script = JsonSerializer.Deserialize<TheaterScript>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Theater script is empty.");
        if (script.Act != 3 || script.Cues.Length == 0 || script.Duration <= 0)
            throw new InvalidDataException("Theater requires Act 3, positive duration and at least one cue.");
        if (script.Purpose is not ("promo" or "tomoe" or "hook" or "aim" or "architect" or "theft" or "blood" or "greeting" or "overhead")) throw new InvalidDataException("Unknown theater purpose.");
        if (script.GreetingMode is not ("brief" or "switch" or "full" or "switch-response")) throw new InvalidDataException("Unknown greeting mode.");
        if (script.Relics.Contains("BigMushroom") || script.Relics.Distinct().Count() != script.Relics.Length)
            throw new InvalidDataException("Theater relics must be unique and exclude BigMushroom.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (TheaterCue cue in script.Cues)
        {
            if (string.IsNullOrWhiteSpace(cue.Id) || !names.Add(cue.Id))
                throw new InvalidDataException($"Duplicate or empty theater cue: {cue.Id}");
            foreach (var step in cue.Steps) Validate(step);
        }
        return script;
    }

    private static void Validate(TheaterStep step)
    {
        if (step.Repeat < 1 || step.Seconds < 0 || step.Delay < 0 || step.Count < 0)
            throw new InvalidDataException("Negative theater duration/count or empty repeat.");
        if (step.Sequence != null && step.Parallel != null)
            throw new InvalidDataException("A theater step cannot be both sequence and parallel.");
        if (step.Zoom <= 0 || !float.IsFinite(step.Zoom) || !float.IsFinite(step.Height)
            || !double.IsFinite(step.Seconds) || !double.IsFinite(step.Delay))
            throw new InvalidDataException("Theater times and camera values must be finite, with positive zoom.");
        if (step.Sequence != null) foreach (var child in step.Sequence) Validate(child);
        if (step.Parallel != null) foreach (var child in step.Parallel) Validate(child);
        if (step.Sequence != null || step.Parallel != null) return;
        if (step.Action is not ("card" or "move" or "wait" or "roll_volley" or "knife_exchange"
            or "entrance" or "missiles" or "apology" or "takeover" or "form" or "clear_air"
            or "camera" or "power" or "remove_power" or "clear_block" or "block" or "aim"
            or "give_card" or "swap_sides" or "replace_enemy" or "speed" or "aim_motion"
            or "architect_compare" or "architect_execution" or "theft_round" or "blood_check" or "overhead_check"))
            throw new InvalidDataException($"Unknown theater action: {step.Action}");
        if (step.Action == "speed" && step.Mode is not ("Normal" or "Fast" or "Instant"))
            throw new InvalidDataException("Unknown playback speed.");
    }
}

internal sealed record TheaterCue
{
    public string Id { get; init; } = "";
    public double NotBefore { get; init; }
    public TheaterStep[] Steps { get; init; } = [];
}

internal sealed record TheaterStep
{
    public string Action { get; init; } = "wait";
    public string Actor { get; init; } = "ninja";
    public string Target { get; init; } = "enemy";
    public string? Card { get; init; }
    public string? Move { get; init; }
    public string? Monster { get; init; }
    public string? Mode { get; init; }
    public string? Form { get; init; }
    public string? Power { get; init; }
    public int Amount { get; init; } = 1;
    public int Energy { get; init; } = 6;
    public int Count { get; init; } = 1;
    public int Repeat { get; init; } = 1;
    public double Seconds { get; init; }
    public double Delay { get; init; }
    public float Zoom { get; init; } = 1;
    public float Height { get; init; }
    public bool FriendlyFire { get; init; }
    public bool Charge { get; init; }
    public string[] Covers { get; init; } = [];
    public int[] Selection { get; init; } = [];
    public TheaterStep[]? Sequence { get; init; }
    public TheaterStep[]? Parallel { get; init; }
}
