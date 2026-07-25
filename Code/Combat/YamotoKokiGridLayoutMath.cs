namespace NinjaSlayer.Code.Combat;

internal readonly record struct YamotoKokiSlotPosition(float X, float Y, int Row);

internal static class YamotoKokiGridLayoutMath
{
    private const float DefaultSpacing = 70f;
    private const float MinimumSpacing = 5f;
    private const float CenterSafeZone = 150f;
    private const float PlayerSideWidth = 960f;
    private const float RowDepth = 120f;

    public static IReadOnlyList<YamotoKokiSlotPosition> Calculate(
        IReadOnlyList<float> widths,
        float scaling,
        bool fullyCenterPlayers)
    {
        if (widths.Count == 0)
        {
            return [];
        }

        int columns = (int)Math.Ceiling(Math.Sqrt(widths.Count));
        int rows = (int)Math.Ceiling((double)widths.Count / columns);
        float availableWidth = PlayerSideWidth / Math.Max(scaling, 0.01f);
        float rowYStep = rows > 1 ? RowDepth / (rows - 1) : 0f;
        float widestRow = 0f;
        for (int row = 0; row < rows; row++)
        {
            widestRow = Math.Max(
                widestRow,
                widths.Skip(row * columns).Take(columns).Sum());
        }

        float rowXStep = rows > 1 ? widestRow * 0.33f / (rows - 1) : 0f;
        List<YamotoKokiSlotPosition> result = new(widths.Count);
        for (int row = 0; row < rows; row++)
        {
            IReadOnlyList<float> rowWidths = widths.Skip(row * columns).Take(columns).ToList();
            float widthTotal = rowWidths.Sum();
            float spacing = DefaultSpacing;
            float rowSpan = widthTotal + Math.Max(0, rowWidths.Count - 1) * spacing;
            float startX = fullyCenterPlayers
                ? -widths[0] * 0.5f
                : Math.Max((availableWidth - rowSpan) * 0.5f, CenterSafeZone);

            if (!fullyCenterPlayers && startX + rowSpan > availableWidth && rowWidths.Count > 1)
            {
                spacing = Math.Max(
                    (availableWidth - CenterSafeZone - widthTotal) / (rowWidths.Count - 1),
                    MinimumSpacing);
                rowSpan = widthTotal + (rowWidths.Count - 1) * spacing;
                startX = (availableWidth - rowSpan) * 0.5f;
            }

            float cursor = startX + rowXStep * row;
            foreach (float width in rowWidths)
            {
                result.Add(new YamotoKokiSlotPosition(
                    -(cursor + width * 0.5f),
                    200f - rowYStep * row,
                    row));
                cursor += width + spacing;
            }
        }

        return result;
    }
}
