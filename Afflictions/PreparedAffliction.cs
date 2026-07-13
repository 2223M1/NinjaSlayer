using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Afflictions;

[RegisterAffliction]
public sealed class PreparedAffliction : ModAfflictionTemplate
{
    public const string OverlayScenePath = "res://NinjaSlayer/scenes/card_overlays/prepared/prepared.tscn";

    public override bool HasExtraCardText => true;

    public override AfflictionAssetProfile AssetProfile => new(OverlayScenePath);
}
