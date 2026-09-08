using MasterOfPuppets.Ipc;

using Xunit;

namespace MasterOfPuppetsTests;

public sealed class PeerCharacterMetadataTests {
    [Fact]
    public void ExtendedBroadcastParsesRuntimeMetadataWithoutRecipient() {
        string[] fields = ["Character Alpha", "ExampleWorld", "ExampleWorld", "30,10,30", "777", "2", "1"];

        var metadata = IpcProvider.ParseRuntimeMetadata(fields);

        Assert.Equal([10UL, 30UL], metadata.PartyContentIds);
        Assert.Equal(777U, metadata.TerritoryId);
        Assert.Equal(2U, metadata.InstanceId);
        Assert.True(metadata.IsPerforming);
        Assert.False(IpcProvider.TryGetCharacterDataTarget(fields, out _));
    }

    [Fact]
    public void ExtendedTargetedFrameSeparatesRecipientFromPerformanceState() {
        string[] fields = ["Character Alpha", "ExampleWorld", "ExampleWorld", "10,20", "777", "2", "9001", "0"];

        var metadata = IpcProvider.ParseRuntimeMetadata(fields);

        Assert.False(metadata.IsPerforming);
        Assert.True(IpcProvider.TryGetCharacterDataTarget(fields, out var target));
        Assert.Equal(9001UL, target);
    }

    [Fact]
    public void LegacyTargetedFrameDoesNotTreatRecipientAsPartyMember() {
        string[] fields = ["Character Alpha", "ExampleWorld", "ExampleWorld", "9001"];

        var metadata = IpcProvider.ParseRuntimeMetadata(fields);

        Assert.Empty(metadata.PartyContentIds);
        Assert.Equal(0U, metadata.TerritoryId);
        Assert.False(metadata.IsPerforming);
        Assert.True(IpcProvider.TryGetCharacterDataTarget(fields, out var target));
        Assert.Equal(9001UL, target);
    }

    [Fact]
    public void LegacyBroadcastHasNoRuntimeMetadataOrRecipient() {
        string[] fields = ["Character Alpha", "ExampleWorld", "ExampleWorld"];

        var metadata = IpcProvider.ParseRuntimeMetadata(fields);

        Assert.Empty(metadata.PartyContentIds);
        Assert.False(IpcProvider.TryGetCharacterDataTarget(fields, out _));
    }
}
