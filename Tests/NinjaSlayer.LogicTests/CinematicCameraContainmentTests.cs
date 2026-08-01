using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.LogicTests;

public sealed class CinematicCameraContainmentTests
{
    [Fact]
    public void ClampCenterMakesTheOutwardViewportEdgeFlushWithTheScene()
    {
        float center = CinematicCameraContainment.ClampCenter(
            desiredCenter: 1800f,
            viewportPixels: 1920f,
            scale: 1.5f,
            sceneSize: 1920f);

        Assert.Equal(1280f, center, precision: 3);
        Assert.Equal(1920f, center + 1920f / (2f * 1.5f), precision: 3);
    }

    [Fact]
    public void SubjectFramingUsesSafeMarginsWhenItFits()
    {
        float center = CinematicCameraContainment.ResolveSubjectAwareCenter(
            desiredCenter: 960f,
            viewportPixels: 1920f,
            scale: 1.5f,
            sceneSize: 1920f,
            subjectMinimum: 700f,
            subjectMaximum: 1220f,
            safeMarginPixels: 64f);

        Assert.Equal(960f, center, precision: 3);
    }

    [Fact]
    public void SceneContainmentWinsWhenTheSubjectCannotFit()
    {
        float center = CinematicCameraContainment.ResolveSubjectAwareCenter(
            desiredCenter: 2000f,
            viewportPixels: 1920f,
            scale: 1.5f,
            sceneSize: 1920f,
            subjectMinimum: 1800f,
            subjectMaximum: 2200f,
            safeMarginPixels: 64f);

        Assert.Equal(1280f, center, precision: 3);
    }

    [Fact]
    public void ViewportLargerThanSceneFallsBackToSceneCenter()
    {
        float center = CinematicCameraContainment.ClampCenter(
            desiredCenter: 100f,
            viewportPixels: 2560f,
            scale: 1f,
            sceneSize: 1920f);

        Assert.Equal(960f, center, precision: 3);
    }
}
