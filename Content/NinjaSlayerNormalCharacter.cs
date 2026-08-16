using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Content;

[RegisterCharacter]
public sealed class NinjaSlayerCharacter : NinjaSlayerCharacterTemplate<NinjaSlayerCardPool>
{
    public override bool HideInCardLibraryCompendium => NinjaSlayerSettings.UseRedesignV1;
}
