using System.Numerics;

using MasterOfPuppets.LuaScripting;

using Xunit;

namespace MasterOfPuppetsTests;

public sealed class LuaTrajectoryControllerTests {
    [Fact]
    public void SamplesDoNotTrackCameraByDefault() {
        Assert.True(LuaTrajectorySample.TryCreate(1, 2, 0, 3, out var sample));

        Assert.False(sample.TrackCameraAnchor);
    }

    [Fact]
    public void TransformPositionAppliesAnchorRotationAndTranslation() {
        var transformed = LuaTrajectoryController.TransformPosition(
            new Vector3(1, 0, 0),
            new Vector3(10, 2, 20),
            MathF.PI / 2);

        Assert.Equal(10, transformed.X, 4);
        Assert.Equal(2, transformed.Y, 4);
        Assert.Equal(19, transformed.Z, 4);
    }

    [Fact]
    public void TransformFacingNormalizesWorldAngle() {
        var transformed = LuaTrajectoryController.TransformFacing(
            MathF.PI,
            MathF.PI / 2);

        Assert.Equal(-MathF.PI / 2, transformed, 4);
    }

    [Fact]
    public void ResolveFacingPrefersMeasuredPathTangent() {
        Assert.Equal(1.25f, LuaTrajectoryController.ResolveFacing(1.25f, 2.5f));
        Assert.Equal(2.5f, LuaTrajectoryController.ResolveFacing(null, 2.5f));
    }
}
