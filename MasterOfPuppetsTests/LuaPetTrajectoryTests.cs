using MasterOfPuppets;
using MasterOfPuppets.Ipc;
using MasterOfPuppets.LuaScripting;
using Xunit;

namespace MasterOfPuppetsTests;

public sealed class LuaPetTrajectoryTests {
    [Fact]
    public void SampleClampsSafetyControlsAndRadius() {
        Assert.True(LuaPetTrajectorySample.TryCreate(
            100, 0, "  Anchor Name@World  ", 0.01, 5, 12.5, out var sample));

        Assert.Equal(LuaPetTrajectorySample.MaximumRadius, sample.RelativeOffset.Length(), 3);
        Assert.Equal("Anchor Name@World", sample.Anchor);
        Assert.Equal(0.12, sample.MinimumInterval.TotalSeconds, 3);
        Assert.Equal(2f, sample.MinimumDistance);
        Assert.Equal(12.5, sample.ElapsedSeconds);
    }

    [Fact]
    public void SampleRejectsNonFiniteGeometry() {
        Assert.False(LuaPetTrajectorySample.TryCreate(
            double.NaN, 0, "target", 0.2, 0.08, 0, out _));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void ExcludePerforming_IsControlledByLaunchVariable(string value, bool expected) {
        var variables = new Dictionary<string, string> { ["exclude_performing"] = value };

        Assert.Equal(expected, IpcProvider.ExcludePerformingRequested(variables));
    }

    [Fact]
    public void ExcludePerforming_DefaultsOffForUnrelatedScripts() {
        Assert.False(IpcProvider.ExcludePerformingRequested(new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    public void ActiveFormationRoster_IsControlledByLaunchVariable(string value, bool expected) {
        var variables = new Dictionary<string, string> { ["active_formation_roster"] = value };

        Assert.Equal(expected, IpcProvider.ActiveFormationRosterRequested(variables));
    }

    [Fact]
    public void ActiveFormationRoster_DefaultsOffForUnrelatedScripts() {
        Assert.False(IpcProvider.ActiveFormationRosterRequested(new Dictionary<string, string>()));
    }
}
