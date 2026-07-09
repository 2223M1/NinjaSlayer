using Godot;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Utils;

namespace NinjaSlayer.Content;

public sealed class NinjaSlayerCardPool : TypeListCardPoolModel
{
    private static readonly Material? _poolFrameMaterial = MaterialUtils.CreateHsvShaderMaterial(1f, 1.2f, 0.65f);

    public override string Title => "NinjaSlayer";
    public override string EnergyColorName => Scripts.NinjaSlayerIds.EnergyColorName;
    public override string? TextEnergyIconPath => NinjaSlayerAssetPaths.Image("energy_ninja_slayer.png");
    public override string? BigEnergyIconPath => NinjaSlayerAssetPaths.Image("energy_ninja_slayer_big.png");
    public override Color DeckEntryCardColor => new("9D1F1FFF");
    public override Color EnergyOutlineColor => new("691A1BFF");
    public override Material? PoolFrameMaterial => _poolFrameMaterial;
    public override bool IsColorless => false;
}

public sealed class NinjaSlayerRelicPool : TypeListRelicPoolModel
{
    public override string? TextEnergyIconPath => NinjaSlayerAssetPaths.Image("energy_ninja_slayer.png");
    public override string? BigEnergyIconPath => NinjaSlayerAssetPaths.Image("energy_ninja_slayer_big.png");
    public override string EnergyColorName => Scripts.NinjaSlayerIds.EnergyColorName;
}

public sealed class NinjaSlayerPotionPool : TypeListPotionPoolModel
{
    public override string? TextEnergyIconPath => NinjaSlayerAssetPaths.Image("energy_ninja_slayer.png");
    public override string? BigEnergyIconPath => NinjaSlayerAssetPaths.Image("energy_ninja_slayer_big.png");
    public override string EnergyColorName => Scripts.NinjaSlayerIds.EnergyColorName;
}
