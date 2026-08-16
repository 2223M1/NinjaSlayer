namespace NinjaSlayer.Content;

public enum NinjaSlayerRulesVersion
{
    Legacy = 0,
    RedesignV1 = 1
}

public sealed class NinjaSlayerRunRules
{
    public NinjaSlayerRulesVersion RulesVersion { get; set; } = NinjaSlayerRulesVersion.RedesignV1;
}
