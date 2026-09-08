using MasterOfPuppets;
using MasterOfPuppets.Ipc;

using Xunit;

public sealed class LuaAnchorPartyRoutingTests {
    private static readonly ulong[] Roster = [10, 20, 30, 40, 50];

    [Fact]
    public void ReportedPartyMembershipIsAuthoritative() {
        var groups = new[] { Group("Fallback", 20, 40) };

        var slots = IpcProvider.ResolveAnchorPartySlots(Roster, 20, [10, 20, 30], groups, "Fallback");

        Assert.Equal([0, 1, 2], slots);
    }

    [Fact]
    public void ExactConfiguredGroupIsUsedWhenReportedPartyIsUnavailable() {
        var groups = new[] { Group("Second Party", 20, 40, 50) };

        var slots = IpcProvider.ResolveAnchorPartySlots(Roster, 20, [], groups, "second party");

        Assert.Equal([1, 3, 4], slots);
    }

    [Fact]
    public void ConfiguredFallbackMustContainTheAnchor() {
        var groups = new[] { Group("Second Party", 30, 40, 50) };

        var slots = IpcProvider.ResolveAnchorPartySlots(Roster, 20, [], groups, "Second Party");

        Assert.Empty(slots);
    }

    [Fact]
    public void MissingPartyEvidenceDoesNotGuessFromGroupNames() {
        var groups = new[] { Group("Any Personal Naming Convention", 20, 40) };

        var slots = IpcProvider.ResolveAnchorPartySlots(Roster, 20, [], groups, null);

        Assert.Empty(slots);
    }

    private static CidGroup Group(string name, params ulong[] contentIds) => new() {
        Name = name,
        Cids = contentIds.ToList(),
    };
}
