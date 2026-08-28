using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MasterOfPuppets.LuaScripting.Runs;
using MasterOfPuppets.Movement;

namespace MasterOfPuppets.LuaScripting.Automation;

internal sealed class PluginLuaActionFacade : ILuaActionFacade {
    private readonly Plugin _plugin;
    private readonly Action<LuaResourceKind, string> _ensureResource;

    public PluginLuaActionFacade(Plugin plugin, Action<LuaResourceKind, string> ensureResource) {
        _plugin = plugin;
        _ensureResource = ensureResource;
    }

    public Task<LuaAutomationResult> ExecuteTextAsync(string text, string scope, CancellationToken cancellationToken) {
        _ensureResource(LuaResourceKind.ChatActionBudget | LuaResourceKind.GameActions, "commands.execute");
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Contains('\r') || text.Contains('\n') || text.Contains('\0'))
            return Task.FromResult(LuaAutomationResult.Rejected("text command must be one non-empty line"));
        if (Encoding.UTF8.GetByteCount(text) > 500)
            return Task.FromResult(LuaAutomationResult.Rejected("text command exceeds 500 UTF-8 bytes"));
        return OnFramework(() => {
            switch (NormalizeScope(scope)) {
                case "local":
                    Chat.SendMessage(text);
                    break;
                case "current_pc":
                    _plugin.IpcProvider.ExecuteTextCommand(text, includeSelf: true);
                    break;
                default:
                    return LuaAutomationResult.Rejected("command scope must be 'local' or 'current_pc'");
            }
            return LuaAutomationResult.Queued("text command dispatched");
        }, cancellationToken);
    }

    public Task<LuaAutomationResult> UseActionAsync(string kind, uint id, string scope, CancellationToken cancellationToken) {
        _ensureResource(LuaResourceKind.GameActions, "actions.use");
        if (id == 0)
            return Task.FromResult(LuaAutomationResult.Rejected("action ID must be greater than zero"));
        return OnFramework(() => {
            var normalizedKind = kind?.Trim().ToLowerInvariant().Replace('-', '_') ?? string.Empty;
            var normalizedScope = NormalizeScope(scope);
            if (normalizedScope is not ("local" or "current_pc"))
                return LuaAutomationResult.Rejected("action scope must be 'local' or 'current_pc'");
            if (normalizedScope == "local") {
                switch (normalizedKind) {
                    case "action": GameActionManager.UseAction(id); break;
                    case "general" or "general_action": GameActionManager.UseGeneralAction(id); break;
                    case "item": GameActionManager.UseItem(id); break;
                    default: return LuaAutomationResult.Rejected("action kind must be action, general_action, or item");
                }
            } else {
                switch (normalizedKind) {
                    case "action": _plugin.IpcProvider.ExecuteActionCommand(id); break;
                    case "general" or "general_action": _plugin.IpcProvider.ExecuteGeneralActionCommand(id); break;
                    case "item": _plugin.IpcProvider.ExecuteItemCommand(id); break;
                    default: return LuaAutomationResult.Rejected("action kind must be action, general_action, or item");
                }
            }
            return LuaAutomationResult.Queued($"{normalizedKind} {id} dispatched");
        }, cancellationToken);
    }

    public Task<LuaAutomationResult> ChangeGearsetAsync(int oneBasedIndex, string scope, CancellationToken cancellationToken) {
        _ensureResource(LuaResourceKind.GameActions, "actions.gearset");
        if (oneBasedIndex is < 1 or > 100)
            return Task.FromResult(LuaAutomationResult.Rejected("gearset index must be between 1 and 100"));
        return OnFramework(() => {
            switch (NormalizeScope(scope)) {
                case "local": GearsetManager.ChangeGearset(_plugin, oneBasedIndex - 1); break;
                case "current_pc": _plugin.IpcProvider.ChangeGearset(oneBasedIndex - 1); break;
                default: return LuaAutomationResult.Rejected("gearset scope must be 'local' or 'current_pc'");
            }
            return LuaAutomationResult.Queued($"gearset {oneBasedIndex} dispatched");
        }, cancellationToken);
    }

    public Task<LuaAutomationResult> SetWalkingAsync(string mode, string scope, CancellationToken cancellationToken) {
        _ensureResource(LuaResourceKind.Movement, "actions.walk");
        var normalizedMode = mode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedMode is not ("on" or "off" or "toggle"))
            return Task.FromResult(LuaAutomationResult.Rejected("walk mode must be on, off, or toggle"));
        return OnFramework(() => {
            switch (NormalizeScope(scope)) {
                case "local":
                    SimpleMovementWalkState.IsWalking = normalizedMode switch {
                        "on" => true,
                        "off" => false,
                        _ => !SimpleMovementWalkState.IsWalking,
                    };
                    break;
                case "current_pc":
                    _plugin.IpcProvider.ExecuteTextCommand($"/mop walk {normalizedMode}", includeSelf: true);
                    break;
                default:
                    return LuaAutomationResult.Rejected("walk scope must be 'local' or 'current_pc'");
            }
            return LuaAutomationResult.Completed($"walk mode {normalizedMode}");
        }, cancellationToken);
    }

    public Task<LuaAutomationResult> StopMovementAsync(string scope, CancellationToken cancellationToken) {
        _ensureResource(LuaResourceKind.Movement, "actions.stop_movement");
        return OnFramework(() => {
            switch (NormalizeScope(scope)) {
                case "local": _plugin.StopNonLuaMovementLocal(); break;
                case "current_pc": _plugin.IpcProvider.StopManagedMovement(); break;
                default: return LuaAutomationResult.Rejected("movement scope must be 'local' or 'current_pc'");
            }
            return LuaAutomationResult.Completed("movement stopped");
        }, cancellationToken);
    }

    private Task<T> OnFramework<T>(Func<T> action, CancellationToken cancellationToken) =>
        DalamudApi.Framework.RunOnFrameworkThread(action).WaitAsync(cancellationToken);

    private static string NormalizeScope(string? scope) =>
        string.IsNullOrWhiteSpace(scope) ? "local" : scope.Trim().ToLowerInvariant().Replace('-', '_');
}
