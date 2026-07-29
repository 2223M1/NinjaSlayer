namespace NinjaSlayer.Code.Combat;

internal readonly record struct SoftBodyEnergyAuditResult(
    float Before,
    float After,
    bool Limited);

internal sealed class SoftBodyEnergyAudit
{
    private readonly BossFragmentPoint[] _firstVelocities =
        new BossFragmentPoint[SoftFragmentBody.ParticleCount];
    private readonly BossFragmentPoint[] _secondVelocities =
        new BossFragmentPoint[SoftFragmentBody.ParticleCount];

    public void Capture(SoftContactManifold manifold)
    {
        manifold.First.CopyParticleVelocities(_firstVelocities);
        manifold.Second.CopyParticleVelocities(_secondVelocities);
    }

    public void Capture(SoftFragmentBody body) =>
        body.CopyParticleVelocities(_firstVelocities);

    public SoftBodyEnergyAuditResult LimitContactEnergy(SoftContactManifold manifold)
    {
        float before = ResolveSnapshotEnergy(manifold.First, _firstVelocities)
            + ResolveSnapshotEnergy(manifold.Second, _secondVelocities);
        float after = manifold.First.ResolveKineticEnergy() + manifold.Second.ResolveKineticEnergy();
        if (!float.IsFinite(after))
        {
            manifold.First.RestoreParticleVelocities(_firstVelocities);
            manifold.Second.RestoreParticleVelocities(_secondVelocities);
            float restored = manifold.First.ResolveKineticEnergy()
                + manifold.Second.ResolveKineticEnergy();
            return new SoftBodyEnergyAuditResult(before, restored, true);
        }

        float allowed = before * 1.01f + 0.5f;
        if (after <= allowed)
        {
            return new SoftBodyEnergyAuditResult(before, after, false);
        }

        float low = 0f;
        float high = 1f;
        for (int iteration = 0; iteration < 10; iteration++)
        {
            float amount = (low + high) * 0.5f;
            float candidate = ResolveBlendedEnergy(
                manifold.First,
                _firstVelocities,
                amount)
                + ResolveBlendedEnergy(manifold.Second, _secondVelocities, amount);
            if (candidate <= allowed)
            {
                low = amount;
            }
            else
            {
                high = amount;
            }
        }

        manifold.First.BlendParticleVelocities(_firstVelocities, low);
        manifold.Second.BlendParticleVelocities(_secondVelocities, low);
        float limited = manifold.First.ResolveKineticEnergy() + manifold.Second.ResolveKineticEnergy();
        return new SoftBodyEnergyAuditResult(before, limited, true);
    }

    public SoftBodyEnergyAuditResult LimitBoundaryEnergy(SoftFragmentBody body)
    {
        float before = ResolveSnapshotEnergy(body, _firstVelocities);
        float after = body.ResolveKineticEnergy();
        if (!float.IsFinite(after))
        {
            body.RestoreParticleVelocities(_firstVelocities);
            return new SoftBodyEnergyAuditResult(before, body.ResolveKineticEnergy(), true);
        }

        float allowed = before * 1.01f + 0.5f;
        if (after <= allowed)
        {
            return new SoftBodyEnergyAuditResult(before, after, false);
        }

        float low = 0f;
        float high = 1f;
        for (int iteration = 0; iteration < 10; iteration++)
        {
            float amount = (low + high) * 0.5f;
            float candidate = ResolveBlendedEnergy(body, _firstVelocities, amount);
            if (candidate <= allowed)
            {
                low = amount;
            }
            else
            {
                high = amount;
            }
        }

        body.BlendParticleVelocities(_firstVelocities, low);
        return new SoftBodyEnergyAuditResult(before, body.ResolveKineticEnergy(), true);
    }

    private static float ResolveSnapshotEnergy(
        SoftFragmentBody body,
        IReadOnlyList<BossFragmentPoint> velocities)
    {
        double particleMass = body.Mass / SoftFragmentBody.ParticleCount;
        double sum = 0d;
        for (int index = 0; index < velocities.Count; index++)
        {
            sum += velocities[index].X * velocities[index].X
                + velocities[index].Y * velocities[index].Y;
        }

        double energy = 0.5d * particleMass * sum;
        return double.IsFinite(energy)
            ? (float)Math.Min(energy, float.MaxValue)
            : float.PositiveInfinity;
    }

    private static float ResolveBlendedEnergy(
        SoftFragmentBody body,
        IReadOnlyList<BossFragmentPoint> before,
        float amount)
    {
        double particleMass = body.Mass / SoftFragmentBody.ParticleCount;
        double sum = 0d;
        for (int index = 0; index < before.Count; index++)
        {
            BossFragmentPoint current = body.GetParticleVelocity(index);
            float x = before[index].X + (current.X - before[index].X) * amount;
            float y = before[index].Y + (current.Y - before[index].Y) * amount;
            sum += x * x + y * y;
        }

        double energy = 0.5d * particleMass * sum;
        return double.IsFinite(energy)
            ? (float)Math.Min(energy, float.MaxValue)
            : float.PositiveInfinity;
    }
}
