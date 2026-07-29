namespace NinjaSlayer.Code.Combat;

internal enum BossFountainLaunchLane
{
    Upward,
    Horizontal,
    Downward
}

internal readonly record struct BossFountainLaunch(
    BossFragmentPoint Velocity,
    float AngularVelocityDegrees,
    BossFountainLaunchLane Lane);

internal readonly record struct BossFountainLaunchPlan(
    IReadOnlyList<BossFountainLaunch> Launches,
    float Gravity,
    float MaximumCenterSpeed);

internal static class BossFountainLaunchProfile
{
    public const float LaunchActuatorSeconds = 0.06f;
    public const float MaximumDeformationSpeed = 520f;
    public const float MinimumLaunchSpeed = 860f;
    public const float MaximumLaunchSpeed = 1380f;
    public const float UpwardMinimumLaunchSpeed = 990f;
    public const float HorizontalMaximumLaunchSpeed = 1160f;
    public const float DownwardMaximumLaunchSpeed = 1050f;
    public const float Gravity = 1650f;
    public const float MaximumCenterSpeed = 2800f;
    public const float LinearAirDrag = 0.22f;
    public const float QuadraticAirDrag = 0.00035f;
    public const float CollisionMarginStartSeconds = 0.3f;
    public const float CollisionMarginFullSeconds = 0.5f;
    public const float MaximumHorizontalDrift = 60f;

    public static IReadOnlyList<BossFountainLaunch> Create(
        IReadOnlyList<float> massRatios,
        ulong seed)
    {
        int count = Math.Min(massRatios.Count, BossDismembermentMath.MaximumPieces);
        if (count <= 0)
        {
            return [];
        }

        int outliersPerKind = count < 8
            ? 0
            : Math.Max(1, (int)MathF.Round(count * 0.125f));
        outliersPerKind = Math.Min(outliersPerKind, count / 2);
        int upwardCount = count - outliersPerKind * 2;
        var lanes = new BossFountainLaunchLane[count];
        Array.Fill(lanes, BossFountainLaunchLane.Upward);
        for (int index = 0; index < outliersPerKind; index++)
        {
            lanes[upwardCount + index] = BossFountainLaunchLane.Horizontal;
            lanes[upwardCount + outliersPerKind + index] = BossFountainLaunchLane.Downward;
        }

        var random = new FountainRandom(seed);
        Shuffle(lanes, ref random);
        float[] upwardAngles = BuildStratifiedAngles(upwardCount, -65f, 65f, ref random);
        int upwardIndex = 0;
        int horizontalIndex = 0;
        int downwardIndex = 0;
        var launches = new BossFountainLaunch[count];
        for (int index = 0; index < count; index++)
        {
            BossFountainLaunchLane lane = lanes[index];
            float angleDegrees;
            float minimumSpeed;
            float maximumSpeed;
            switch (lane)
            {
                case BossFountainLaunchLane.Horizontal:
                {
                    float sign = horizontalIndex++ % 2 == 0 ? -1f : 1f;
                    angleDegrees = sign * random.Range(82f, 98f);
                    minimumSpeed = MinimumLaunchSpeed;
                    maximumSpeed = HorizontalMaximumLaunchSpeed;
                    break;
                }
                case BossFountainLaunchLane.Downward:
                {
                    float sign = downwardIndex++ % 2 == 0 ? -1f : 1f;
                    angleDegrees = sign * random.Range(115f, 145f);
                    minimumSpeed = MinimumLaunchSpeed;
                    maximumSpeed = DownwardMaximumLaunchSpeed;
                    break;
                }
                default:
                    angleDegrees = upwardAngles[upwardIndex++];
                    minimumSpeed = UpwardMinimumLaunchSpeed;
                    maximumSpeed = MaximumLaunchSpeed;
                    break;
            }

            float massFactor = 1f / MathF.Sqrt(Math.Clamp(massRatios[index], 0.2f, 3f));
            float speed = random.Range(minimumSpeed, maximumSpeed)
                * Math.Clamp(massFactor, 0.9f, 1.1f);
            speed = Math.Clamp(speed, minimumSpeed, maximumSpeed);
            float angle = angleDegrees * MathF.PI / 180f;
            BossFragmentPoint velocity = new(
                MathF.Sin(angle) * speed,
                -MathF.Cos(angle) * speed);
            float spin = random.Range(60f, 240f)
                * (random.NextFloat() < 0.5f ? -1f : 1f);
            launches[index] = new BossFountainLaunch(velocity, spin, lane);
        }

        LimitHorizontalDrift(launches, massRatios);
        return launches;
    }

    public static BossFountainLaunchPlan CreatePlan(
        IReadOnlyList<BossFountainLaunch> launches) =>
        new(
            launches.Take(BossDismembermentMath.MaximumPieces).ToArray(),
            Gravity,
            MaximumCenterSpeed);

    public static float ResolveCollisionMarginScale(float flightSeconds, float hullScale) =>
        SmoothStep(
            CollisionMarginStartSeconds,
            CollisionMarginFullSeconds,
            flightSeconds) * Math.Clamp(hullScale, 0f, 1f);

    private static float[] BuildStratifiedAngles(
        int count,
        float minimum,
        float maximum,
        ref FountainRandom random)
    {
        var result = new float[count];
        if (count == 0)
        {
            return result;
        }

        if (count == 1)
        {
            result[0] = random.Range(-4f, 4f);
            return result;
        }

        float step = (maximum - minimum) / (count - 1);
        for (int index = 0; index < count; index++)
        {
            result[index] = minimum + step * index + random.Range(-4f, 4f);
        }

        Shuffle(result, ref random);
        return result;
    }

    private static void LimitHorizontalDrift(
        BossFountainLaunch[] launches,
        IReadOnlyList<float> massRatios)
    {
        for (int pass = 0; pass < 32; pass++)
        {
            float totalMass = 0f;
            float momentum = 0f;
            for (int index = 0; index < launches.Length; index++)
            {
                float mass = Math.Max(0.1f, massRatios[index]);
                totalMass += mass;
                momentum += mass * launches[index].Velocity.X;
            }

            float drift = totalMass <= 0.001f ? 0f : momentum / totalMass;
            if (MathF.Abs(drift) <= MaximumHorizontalDrift + 0.001f)
            {
                return;
            }

            float correction = drift - MathF.CopySign(MaximumHorizontalDrift, drift);
            for (int index = 0; index < launches.Length; index++)
            {
                BossFragmentPoint velocity = launches[index].Velocity;
                velocity = new BossFragmentPoint(velocity.X - correction, velocity.Y);
                float speed = Length(velocity);
                (float minimumSpeed, float maximumSpeed) =
                    ResolveSpeedRange(launches[index].Lane);
                if (speed > maximumSpeed)
                {
                    velocity = Multiply(velocity, maximumSpeed / speed);
                }
                else if (speed < minimumSpeed && speed > 0.001f)
                {
                    velocity = Multiply(velocity, minimumSpeed / speed);
                }

                launches[index] = launches[index] with { Velocity = velocity };
            }
        }
    }

    private static (float Minimum, float Maximum) ResolveSpeedRange(
        BossFountainLaunchLane lane) => lane switch
        {
            BossFountainLaunchLane.Upward => (UpwardMinimumLaunchSpeed, MaximumLaunchSpeed),
            BossFountainLaunchLane.Horizontal =>
                (MinimumLaunchSpeed, HorizontalMaximumLaunchSpeed),
            BossFountainLaunchLane.Downward =>
                (MinimumLaunchSpeed, DownwardMaximumLaunchSpeed),
            _ => (MinimumLaunchSpeed, MaximumLaunchSpeed)
        };

    private static void Shuffle<T>(T[] values, ref FountainRandom random)
    {
        for (int index = values.Length - 1; index > 0; index--)
        {
            int swap = random.NextInt(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }

    private static float SmoothStep(float start, float end, float value)
    {
        float progress = Math.Clamp((value - start) / Math.Max(0.0001f, end - start), 0f, 1f);
        return progress * progress * (3f - 2f * progress);
    }

    private static float Length(BossFragmentPoint point) =>
        MathF.Sqrt(point.X * point.X + point.Y * point.Y);

    private static BossFragmentPoint Multiply(BossFragmentPoint point, float scalar) =>
        new(point.X * scalar, point.Y * scalar);

    private struct FountainRandom(ulong seed)
    {
        private ulong _state = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;

        public float NextFloat() => (NextUInt64() >> 40) * (1f / (1 << 24));

        public float Range(float minimum, float maximum) =>
            minimum + (maximum - minimum) * NextFloat();

        public int NextInt(int exclusiveMaximum) =>
            exclusiveMaximum <= 1
                ? 0
                : (int)(NextUInt64() % (uint)exclusiveMaximum);

        private ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
