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

}
