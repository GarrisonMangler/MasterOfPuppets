using System.Threading;
using System.Threading.Tasks;

namespace MasterOfPuppets.LuaScripting.Automation;

public interface ILuaActionFacade {
    Task<LuaAutomationResult> ExecuteTextAsync(string text, string scope, CancellationToken cancellationToken);
    Task<LuaAutomationResult> UseActionAsync(string kind, uint id, string scope, CancellationToken cancellationToken);
    Task<LuaAutomationResult> ChangeGearsetAsync(int oneBasedIndex, string scope, CancellationToken cancellationToken);
    Task<LuaAutomationResult> SetWalkingAsync(string mode, string scope, CancellationToken cancellationToken);
    Task<LuaAutomationResult> StopMovementAsync(string scope, CancellationToken cancellationToken);
}
