using MasterOfPuppets.LuaScripting.Automation;
using MasterOfPuppets.LuaScripting.Runtime;

namespace MasterOfPuppets.LuaScripting.Providers;

/// <summary>
/// Opt-in marker for scripts that use the distributed Mirror v2 lifecycle.
/// The protocol is implemented by the launch and transport layers, so this
/// provider intentionally registers no additional Lua functions.
/// </summary>
public sealed class MirrorProtocolLuaCapabilityProvider : ILuaCapabilityProvider {
    public LuaCapabilityDescriptor Descriptor { get; } = new(
        MirrorRunTargetValidator.ProtocolCapability,
        "1.0.0",
        "Opts a script into distributed Mirror run-target, roster, resynchronization, and stop behavior.",
        ["distributed-control"]);

    public void Register(LuaApiRegistrationContext registration) { }
}
