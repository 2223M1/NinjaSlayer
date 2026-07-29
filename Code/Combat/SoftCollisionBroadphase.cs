namespace NinjaSlayer.Code.Combat;

internal sealed class SoftCollisionBroadphase(float cellSize = 180f)
{
    private const int MaximumCellsPerAxis = 64;

    private readonly float _cellSize = Math.Max(32f, cellSize);
    private readonly Dictionary<(int X, int Y), List<SoftFragmentBody>> _cells = [];
    private readonly Stack<List<SoftFragmentBody>> _cellPool = [];
    private readonly HashSet<(int First, int Second)> _keys = [];
    private readonly List<(SoftFragmentBody First, SoftFragmentBody Second)> _pairs = [];

    internal int ActiveCellCount => _cells.Count;

    public IReadOnlyList<(SoftFragmentBody First, SoftFragmentBody Second)> BuildPairs(
        IReadOnlyList<SoftFragmentBody> bodies)
    {
        foreach (List<SoftFragmentBody> occupants in _cells.Values)
        {
            occupants.Clear();
            _cellPool.Push(occupants);
        }

        _cells.Clear();
        _keys.Clear();
        _pairs.Clear();
        for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
        {
            SoftFragmentBody body = bodies[bodyIndex];
            if (!body.CanCollide)
            {
                continue;
            }

            (BossFragmentPoint minimum, BossFragmentPoint maximum) = body.ResolveCollisionAabb();
            (BossFragmentPoint previousMinimum, BossFragmentPoint previousMaximum) =
                body.ResolvePreviousCollisionAabb();
            minimum = new BossFragmentPoint(
                Math.Min(minimum.X, previousMinimum.X),
                Math.Min(minimum.Y, previousMinimum.Y));
            maximum = new BossFragmentPoint(
                Math.Max(maximum.X, previousMaximum.X),
                Math.Max(maximum.Y, previousMaximum.Y));
            if (!TryResolveCellRange(minimum, maximum,
                    out int minimumX,
                    out int maximumX,
                    out int minimumY,
                    out int maximumY))
            {
                continue;
            }

            for (int y = minimumY; ; y++)
            {
                for (int x = minimumX; ; x++)
                {
                    if (!_cells.TryGetValue((x, y), out List<SoftFragmentBody>? occupants))
                    {
                        occupants = _cellPool.TryPop(out List<SoftFragmentBody>? pooled)
                            ? pooled
                            : [];
                        _cells[(x, y)] = occupants;
                    }

                    occupants.Add(body);
                    if (x == maximumX)
                    {
                        break;
                    }
                }

                if (y == maximumY)
                {
                    break;
                }
            }
        }

        foreach (List<SoftFragmentBody> occupants in _cells.Values)
        {
            for (int firstIndex = 0; firstIndex < occupants.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < occupants.Count; secondIndex++)
                {
                    SoftFragmentBody first = occupants[firstIndex];
                    SoftFragmentBody second = occupants[secondIndex];
                    (int First, int Second) key = first.Id < second.Id
                        ? (first.Id, second.Id)
                        : (second.Id, first.Id);
                    if (_keys.Add(key))
                    {
                        _pairs.Add(first.Id < second.Id ? (first, second) : (second, first));
                    }
                }
            }
        }

        return _pairs;
    }

    private bool TryResolveCellRange(
        BossFragmentPoint minimum,
        BossFragmentPoint maximum,
        out int minimumX,
        out int maximumX,
        out int minimumY,
        out int maximumY)
    {
        minimumX = 0;
        maximumX = 0;
        minimumY = 0;
        maximumY = 0;
        if (!float.IsFinite(minimum.X)
            || !float.IsFinite(minimum.Y)
            || !float.IsFinite(maximum.X)
            || !float.IsFinite(maximum.Y)
            || maximum.X < minimum.X
            || maximum.Y < minimum.Y)
        {
            return false;
        }

        double minX = Math.Floor(minimum.X / _cellSize);
        double maxX = Math.Floor(maximum.X / _cellSize);
        double minY = Math.Floor(minimum.Y / _cellSize);
        double maxY = Math.Floor(maximum.Y / _cellSize);
        if (minX < int.MinValue
            || maxX > int.MaxValue
            || minY < int.MinValue
            || maxY > int.MaxValue
            || maxX - minX + 1d > MaximumCellsPerAxis
            || maxY - minY + 1d > MaximumCellsPerAxis)
        {
            return false;
        }

        minimumX = (int)minX;
        maximumX = (int)maxX;
        minimumY = (int)minY;
        maximumY = (int)maxY;
        return true;
    }
}
