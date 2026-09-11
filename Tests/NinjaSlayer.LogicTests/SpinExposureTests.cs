using NinjaSlayer.Code.ExternalAnimations;
using Xunit;

namespace NinjaSlayer.LogicTests;

public sealed class SpinExposureTests
{
    [Fact]
    public void RestAgesOutPriorAnglesWithoutInventingAReverseTurn()
    {
        var history = new SpinExposureHistory();
        history.Record(0, 0);
        history.Record(.1, 1200, age => 1200d - 12000d * age);
        history.Record(.1, 1080, discontinuity: true);
        var samples = new float[25];
        Assert.True(history.SampleAngles(.11, samples));
        for (int i = 0; i < samples.Length; i++)
        {
            double time = .11 - SpinExposureHistory.ExposureSeconds * i / 24;
            double expected = time >= .1 ? 1080 : time * 12000;
            Assert.InRange(Math.Abs(samples[i] - expected), 0, .001);
        }
        Assert.False(history.SampleAngles(.15, samples));
        Assert.All(samples, angle => Assert.Equal(1080, angle));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(144)]
    public void ExposureUsesActualCurveAtEveryFrameRate(int fps)
    {
        foreach (double speed in new[] { 2400d, 4500d, 12000d, -12000d })
        {
            CheckCurve(fps, t => speed * t);
            CheckCurve(fps, t => speed * t * t * t);
            CheckCurve(fps, t => speed * (t < .98d ? t : 1.96d - t));
        }
    }

    private static void CheckCurve(int fps, Func<double, double> curve)
    {
        var history = new SpinExposureHistory();
        for (int frame = 0; frame <= fps; frame++)
        {
            double time = (double)frame / fps;
            history.Record(time, curve(time), age => curve(Math.Max(0d, time - age)));
            history.Record(time, curve(time)); // Same-frame render correction.
        }
        var samples = new float[25];
        Assert.True(history.SampleAngles(1d, samples));
        for (int i = 0; i < samples.Length; i++)
            Assert.InRange(Math.Abs(samples[i] - curve(1d - SpinExposureHistory.ExposureSeconds * i / 24)), 0, .002);
        Assert.False(history.SampleAngles(1d + SpinExposureHistory.ExposureSeconds, samples));
        history.Clear();
        Assert.False(history.SampleAngles(1d, samples));
    }

    [Theory]
    [InlineData(2400, 100.1)]
    [InlineData(4500, 187.6875)]
    [InlineData(12000, 500.5)]
    public void ExposureHasNoHalfTurnCap(double speed, double expectedDegrees)
    {
        var history = new SpinExposureHistory();
        history.Record(0, 0);
        history.Record(.1, speed * .1, age => speed * (.1 - age));
        var samples = new float[25];
        history.SampleAngles(.1, samples);
        Assert.InRange(Math.Abs(samples[0] - samples[^1] - expectedDegrees), 0, .001);
    }
}
