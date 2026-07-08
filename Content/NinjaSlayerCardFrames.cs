using Godot;
using STS2RitsuLib.Utils;

namespace NinjaSlayer.Content;

public static class NinjaSlayerCardFrames
{
    public static Material? NarakuFrameMaterial { get; } =
        MaterialUtils.CreateHsvShaderMaterial(1f, 0f, 2.5f);
}
