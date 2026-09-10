using Godot;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.Nodes;

internal static class ShadowBodyGeometry
{
    private static readonly Vector2[] Normal = CombatBodyContours.NinjaSlayer.Select(p => new Vector2(p.X, p.Y)).ToArray();
    private static readonly Vector2[] Naraku = CombatBodyContours.FullyReleasedNaraku.Select(p => new Vector2(p.X, p.Y)).ToArray();
    private static readonly Vector2[] Koki = CombatBodyContours.YamotoKoki.Select(p => new Vector2(p.X, p.Y)).ToArray();

    // Clockwise normalized solid-body contours; exclude long airborne weapons and
    // cloth, but retain Sawatari's lower cloak and Yukano's single grounded leg.
    private static readonly Vector2[] DarkCombat = [new(.17f,.94f), new(.29f,.54f), new(.35f,.34f), new(.54f,.27f), new(.74f,.31f), new(.86f,.45f), new(.80f,.63f), new(.88f,.96f), new(.78f,.98f), new(.72f,.78f), new(.43f,.74f), new(.22f,.95f)];
    private static readonly Vector2[] DarkStanding = [new(.30f,.93f), new(.37f,.80f), new(.34f,.64f), new(.29f,.49f), new(.34f,.27f), new(.38f,.13f), new(.50f,.20f), new(.58f,.26f), new(.65f,.46f), new(.64f,.59f), new(.57f,.64f), new(.65f,.85f), new(.72f,.93f), new(.68f,.962f), new(.63f,.92f), new(.61f,.83f), new(.47f,.73f), new(.42f,.65f), new(.40f,.87f), new(.36f,.93f)];
    private static readonly Vector2[] Sawatari = [new(.34f,.99f), new(.31f,.82f), new(.34f,.30f), new(.25f,.21f), new(.36f,.02f), new(.52f,.05f), new(.59f,.25f), new(.61f,.64f), new(.76f,.71f), new(.71f,.82f), new(.77f,.85f), new(.66f,.92f), new(.61f,.83f), new(.57f,.99f), new(.50f,1f), new(.48f,.85f), new(.43f,.84f), new(.43f,.98f)];
    private static readonly Vector2[] Yukano = [new(.02f,.97f), new(.09f,.70f), new(.16f,.54f), new(.32f,.40f), new(.29f,.25f), new(.36f,.08f), new(.48f,.08f), new(.55f,.17f), new(.71f,.22f), new(.63f,.28f), new(.52f,.45f), new(.67f,.56f), new(.60f,.64f), new(.41f,.69f), new(.37f,.57f), new(.24f,.73f), new(.08f,1f)];

    internal static Vector2[] Resolve(string rigName, bool fullNaraku, bool standing) => rigName switch
    {
        "NinjaSlayer" => fullNaraku ? Naraku : Normal,
        "YamotoKoki" => Koki,
        "DarkNinja" => standing ? DarkStanding : DarkCombat,
        "Sawatari" => Sawatari,
        "Yukano" => Yukano,
        _ => throw new InvalidOperationException($"No shadow body contour for {rigName}.")
    };

    internal static bool UsesNormalizedPixels(string rigName) => rigName is "DarkNinja" or "Sawatari" or "Yukano";
}
