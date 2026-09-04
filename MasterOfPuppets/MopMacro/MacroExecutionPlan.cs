using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MasterOfPuppets;

/// <summary>
/// Holds the unexpanded actions and mutable variables for one queued macro run.
/// Variables are resolved immediately before each action executes so an active
/// macro can receive updated values without being restarted.
/// </summary>
public sealed class MacroExecutionPlan {
    private readonly object _variablesLock = new();
    private readonly Dictionary<string, string> _variables;
    private volatile TaskCompletionSource? _loopControlChanged;

    public string[] ActionTemplates { get; }

    public MacroExecutionPlan(string[] actionTemplates, Dictionary<string, string>? variables = null) {
        ActionTemplates = actionTemplates ?? Array.Empty<string>();
        _variables = variables == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(variables);
    }

    public string ResolveAction(string actionTemplate) {
        lock (_variablesLock)
            return Command.SubstituteVariables([actionTemplate], ResolvedVariables())[0];
    }

    public string[] ResolveAllActions() {
        lock (_variablesLock)
            return Command.SubstituteVariables(ActionTemplates, ResolvedVariables());
    }

    public void UpdateVariables(IReadOnlyDictionary<string, string> variables) {
        lock (_variablesLock) {
            bool restartLoop = false;
            foreach (var (name, value) in variables) {
                if (_loopControlChanged != null
                    && name is "shape" or "mode" or "direction" or "anchor"
                    && (!_variables.TryGetValue(name, out var previous) || !string.Equals(previous, value, StringComparison.Ordinal)))
                    restartLoop = true;
                _variables[name] = value;
            }
            if (restartLoop) {
                var previousSignal = _loopControlChanged!;
                _loopControlChanged = NewLoopControlSignal();
                // Publish after the entire variable batch is applied. Never run
                // a macro continuation inline on the incoming IPC/game thread.
                previousSignal.TrySetResult();
            }
        }
    }

    /// <summary>
    /// V2 selects its literal path once per lap. Its live controls must wake the
    /// runner so it can reselect without reintroducing per-waypoint expansion.
    /// Other macros retain their existing live-variable behavior.
    /// </summary>
    internal bool EnableTornadoLiveControls(string macroName) {
        if (!string.Equals(macroName, "Carbuncle: Tornado V2", StringComparison.OrdinalIgnoreCase))
            return false;
        var start = Array.IndexOf(ActionTemplates, "/moploopstart");
        var end = Array.IndexOf(ActionTemplates, "/moploopend");
        // Restrict rewinds to V2's single unbounded loop and known top-level
        // selector. Do not alter arbitrary same-name or finite-loop macros.
        if (start < 0 || end <= start
            || Array.LastIndexOf(ActionTemplates, "/moploopstart") != start
            || Array.LastIndexOf(ActionTemplates, "/moploopend") != end
            || ActionTemplates[start + 1] != "/mopif \"[$mode]\" == \"[medium]\"")
            return false;
        lock (_variablesLock)
            _loopControlChanged ??= NewLoopControlSignal();
        return true;
    }

    internal Task? LoopControlChanged => _loopControlChanged?.Task;

    private static TaskCompletionSource NewLoopControlSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static Task DelayPhaseAsync(TimeSpan delay, Task? controlChanged, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        if (delay <= TimeSpan.Zero || controlChanged?.IsCompleted == true)
            return Task.CompletedTask;
        return controlChanged == null ? Task.Delay(delay, token) : WaitForControlOrDelayAsync(delay, controlChanged, token);
    }

    private static async Task WaitForControlOrDelayAsync(TimeSpan delay, Task controlChanged, CancellationToken token) {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var delayTask = Task.Delay(delay, delayCts.Token);
        try {
            var completed = await Task.WhenAny(delayTask, controlChanged);
            if (completed == delayTask)
                await delayTask;
            token.ThrowIfCancellationRequested();
        } finally {
            // Cancel an interrupted timer instead of leaving it running after
            // every shape change. The run/stop cancellation token is unchanged.
            delayCts.Cancel();
        }
    }

    public bool TryGetVariable(string name, out string? value) {
        lock (_variablesLock)
            return _variables.TryGetValue(name, out value);
    }

    // Re-derives variable values from the CURRENT raw definitions, so arithmetic
    // expressions (e.g. $totalWait = $count * $interval) recompute after a live
    // variable update instead of being frozen at plan creation.
    private Dictionary<string, string> ResolvedVariables() {
        var vars = new Dictionary<string, string>(_variables);
        Command.ResolveVariableExpressions(vars);
        return vars;
    }
}
