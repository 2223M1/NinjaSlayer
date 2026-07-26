namespace NinjaSlayer.Code.Combat;

internal static class YamotoKokiFacingPolicy
{
    public static bool ResolveCompanionFacing(
        bool ownerFacesLeft,
        bool hasEnemyOnLeft,
        bool hasEnemyOnRight) =>
        hasEnemyOnLeft && hasEnemyOnRight
            ? !ownerFacesLeft
            : ownerFacesLeft;
}
