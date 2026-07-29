namespace NinjaSlayer.Code.Combat;

internal readonly record struct SoftBodyMaterialProfile(
    float StructuralCompliance,
    float ShearCompliance,
    float BendCompliance,
    float AreaCompliance,
    float ShapeMemoryFrequencyHz,
    float ShapeMemoryDampingRatio,
    float MinimumAreaFraction,
    float MinimumEdgeRatio,
    float MaximumEdgeRatio,
    float MaximumResidualRmsRatio)
{
    public static SoftBodyMaterialProfile FountainJelly { get; } = new(
        StructuralCompliance: 3f,
        ShearCompliance: 9f,
        BendCompliance: 24f,
        AreaCompliance: 0.6f,
        ShapeMemoryFrequencyHz: 1.9f,
        // Constraint projection already adds numerical damping at 120 Hz.
        ShapeMemoryDampingRatio: 0.2f,
        MinimumAreaFraction: 0.1f,
        MinimumEdgeRatio: 0.22f,
        MaximumEdgeRatio: 2.2f,
        MaximumResidualRmsRatio: 0.448f);

    public static SoftBodyMaterialProfile ArchitectLead { get; } = new(
        StructuralCompliance: 0.00045f,
        ShearCompliance: 0.00125f,
        BendCompliance: 0.0048f,
        AreaCompliance: 0.000025f,
        ShapeMemoryFrequencyHz: 3.2f,
        ShapeMemoryDampingRatio: 0.3f,
        MinimumAreaFraction: 0.28f,
        MinimumEdgeRatio: 0.62f,
        MaximumEdgeRatio: 1.35f,
        MaximumResidualRmsRatio: 0.25f);
}
