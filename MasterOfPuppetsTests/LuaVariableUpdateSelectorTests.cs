using MasterOfPuppets.LuaScripting;

using Xunit;

namespace MasterOfPuppetsTests;

public sealed class LuaVariableUpdateSelectorTests {
    [Theory]
    [InlineData("run-123", "Other Script", true)]
    [InlineData("RUN-123", "other script", true)]
    [InlineData("Target Script", "Target Script", true)]
    [InlineData("run", "Target", false)]
    [InlineData("missing", "Other Script", false)]
    public void SelectorMatchesExactRunIdOrScriptName(string selector, string scriptName, bool expected) {
        Assert.Equal(expected, LuaScriptManager.MatchesSelector("run-123", scriptName, selector));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingSelectorPreservesBroadcastToAllRuns(string? selector) {
        Assert.True(LuaScriptManager.MatchesSelector("run-123", "Any Script", selector));
    }
}
