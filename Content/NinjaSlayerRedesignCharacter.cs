using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Content;

[RegisterCharacter]
public sealed class NinjaSlayerRedesignCharacter : NinjaSlayerCharacterTemplate<NinjaSlayerRedesignCardPool>
{
    public override int StartingHp => RedesignV1Rules.StartingHp;
    public override bool HideFromVanillaCharacterSelect => true;
    public override bool AllowInVanillaRandomCharacterSelect => false;
    public override bool HideInCardLibraryCompendium => !NinjaSlayerSettings.UseRedesignV1;
}
