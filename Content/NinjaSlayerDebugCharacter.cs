using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Content;

#if NINJA_SLAYER_DEBUG_CONTENT
[RegisterCharacter]
#endif
public sealed class NinjaSlayerDebugCharacter : NinjaSlayerCharacterTemplate<NinjaSlayerDebugCardPool>
{
}
