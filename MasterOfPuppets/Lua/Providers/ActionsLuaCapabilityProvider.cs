using System;

using Lua;

using MasterOfPuppets.LuaScripting.Automation;
using MasterOfPuppets.LuaScripting.Runtime;

namespace MasterOfPuppets.LuaScripting.Providers;

public sealed class ActionsLuaCapabilityProvider : ILuaCapabilityProvider {
    private static readonly LuaCapabilityDescriptor Capability = new(
        "mop.actions",
        "2.0.0",
        "Validated local/current-PC text commands, typed actions, gearsets, walk mode, and movement stop.",
        ["chat.action", "game.action", "movement"]);

    public LuaCapabilityDescriptor Descriptor => Capability;

    public void Register(LuaApiRegistrationContext registration) {
        var commands = registration.GetOrCreateModule("commands");
        commands["execute"] = new LuaFunction(async (call, cancellationToken) => {
            var text = call.GetArgument<string>(0);
            var scope = call.ArgumentCount > 1 ? call.GetArgument<string>(1) : "local";
            registration.Quota.ConsumeChatAction();
            return call.Return(ToLua(await Facade(registration).ExecuteTextAsync(text, scope, cancellationToken)));
        });

        var actions = registration.GetOrCreateModule("actions");
        actions["use"] = new LuaFunction(async (call, cancellationToken) => {
            var kind = call.GetArgument<string>(0);
            var id = ReadUInt(call.GetArgument<double>(1), "action ID");
            var scope = call.ArgumentCount > 2 ? call.GetArgument<string>(2) : "local";
            registration.Quota.ConsumeChatAction();
            return call.Return(ToLua(await Facade(registration).UseActionAsync(kind, id, scope, cancellationToken)));
        });
        actions["gearset"] = new LuaFunction(async (call, cancellationToken) => {
            var index = ReadInt(call.GetArgument<double>(0), "gearset index");
            var scope = call.ArgumentCount > 1 ? call.GetArgument<string>(1) : "local";
            registration.Quota.ConsumeChatAction();
            return call.Return(ToLua(await Facade(registration).ChangeGearsetAsync(index, scope, cancellationToken)));
        });
        actions["walk"] = new LuaFunction(async (call, cancellationToken) => {
            var mode = call.GetArgument<string>(0);
            var scope = call.ArgumentCount > 1 ? call.GetArgument<string>(1) : "local";
            registration.Quota.ConsumeChatAction();
            return call.Return(ToLua(await Facade(registration).SetWalkingAsync(mode, scope, cancellationToken)));
        });
        actions["stop_movement"] = new LuaFunction(async (call, cancellationToken) => {
            var scope = call.ArgumentCount > 0 ? call.GetArgument<string>(0) : "local";
            return call.Return(ToLua(await Facade(registration).StopMovementAsync(scope, cancellationToken)));
        });
    }

    private static ILuaActionFacade Facade(LuaApiRegistrationContext registration) =>
        registration.Script.Actions
        ?? throw new InvalidOperationException("game actions are unavailable in this Lua host context");

    private static uint ReadUInt(double value, string label) {
        if (!double.IsFinite(value) || value < 1 || value > uint.MaxValue || value != Math.Truncate(value))
            throw new ArgumentOutOfRangeException(label, $"{label} must be a positive unsigned integer");
        return (uint)value;
    }

    private static int ReadInt(double value, string label) {
        if (!double.IsFinite(value) || value < 1 || value > int.MaxValue || value != Math.Truncate(value))
            throw new ArgumentOutOfRangeException(label, $"{label} must be a positive integer");
        return (int)value;
    }

    private static LuaTable ToLua(LuaAutomationResult result) => new() {
        ["ok"] = result.Success,
        ["status"] = result.Status,
        ["message"] = result.Message,
    };
}
