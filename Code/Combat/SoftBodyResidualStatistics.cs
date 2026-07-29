namespace NinjaSlayer.Code.Combat;

internal sealed class SoftBodyResidualStatistics
{
    private const float BinWidth = 0.01f;
    private readonly int[] _bins = new int[51];
    private int _aboveVisibleThreshold;
    private double _sum;

    public int Count { get; private set; }
    public float Maximum { get; private set; }
    public double Average => Count == 0 ? 0d : _sum / Count;
    public double VisibleFraction => Count == 0 ? 0d : (double)_aboveVisibleThreshold / Count;

    public void Add(float ratio)
    {
        if (!float.IsFinite(ratio) || ratio < 0f)
        {
            return;
        }

        int bin = Math.Clamp((int)MathF.Floor(ratio / BinWidth), 0, _bins.Length - 1);
        _bins[bin]++;
        Count++;
        _sum += ratio;
        Maximum = Math.Max(Maximum, ratio);
        if (ratio >= 0.15f)
        {
            _aboveVisibleThreshold++;
        }
    }

    public float Percentile(float percentile)
    {
        if (Count == 0)
        {
            return 0f;
        }

        int target = Math.Clamp(
            (int)MathF.Ceiling(Math.Clamp(percentile, 0f, 1f) * Count),
            1,
            Count);
        int cumulative = 0;
        for (int index = 0; index < _bins.Length; index++)
        {
            cumulative += _bins[index];
            if (cumulative >= target)
            {
                return index * BinWidth;
            }
        }

        return (_bins.Length - 1) * BinWidth;
    }
}
