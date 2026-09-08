using System;
using System.Numerics;

using MasterOfPuppets.Extensions;
using MasterOfPuppets.Formations;

namespace MasterOfPuppets.LuaScripting;

/// <summary>
/// Coalesces high-frequency Lua path samples into safe native pet Place commands.
/// Lua owns the path; this controller owns action cadence, anchor tracking, and
/// redundant-command suppression.
/// </summary>
internal sealed class LuaPetTrajectoryController {
    private static readonly TimeSpan MaximumSilence = TimeSpan.FromSeconds(1);
    private readonly Plugin _plugin;
    private readonly object _sampleLock = new();
    private LuaPetTrajectorySample? _latestSample;
    private Vector3? _lastSubmittedPosition;
    private long _lastAttemptTick;
    private long _lastSubmittedTick;

    public LuaPetTrajectoryController(Plugin plugin) => _plugin = plugin;

    public bool IsActive { get; private set; }

    public void Publish(LuaPetTrajectorySample sample) {
        lock (_sampleLock)
            _latestSample = sample;
        IsActive = true;
    }

    public void Update() {
        LuaPetTrajectorySample? sample;
        lock (_sampleLock)
            sample = _latestSample;
        if (sample == null)
            return;

        var now = Environment.TickCount64;
        var minimumIntervalMs = (long)sample.Value.MinimumInterval.TotalMilliseconds;
        if (_lastAttemptTick != 0 && now - _lastAttemptTick < minimumIntervalMs)
            return;

        var anchorParse = FormationAnchorArgumentParser.ParseAnchorAndArrival(
            [sample.Value.Anchor],
            FormationAnchorReference.Target);
        if (!FormationAnchorResolver.TryResolve(
                _plugin,
                new Formation(),
                anchorParse.Anchor,
                out var resolved,
                out _,
                out _)) {
            if (anchorParse.Fallback == null || !FormationAnchorResolver.TryResolve(
                    _plugin,
                    new Formation(),
                    anchorParse.Fallback,
                    out resolved,
                    out _,
                    out _))
                return;
        }

        var worldPosition = sample.Value.RelativeOffset.ApplyLeaderRotation(
            resolved.Rotation,
            resolved.Position);
        worldPosition.Y = resolved.Position.Y + sample.Value.RelativeOffset.Y;

        var distance = _lastSubmittedPosition.HasValue
            ? Vector3.Distance(_lastSubmittedPosition.Value, worldPosition)
            : float.PositiveInfinity;
        var silenceMs = _lastSubmittedTick == 0 ? long.MaxValue : now - _lastSubmittedTick;
        if (distance < sample.Value.MinimumDistance
            && silenceMs < MaximumSilence.TotalMilliseconds)
            return;

        _lastAttemptTick = now;
        if (!GameActionManager.TryPlacePetImmediate(worldPosition))
            return;

        _lastSubmittedPosition = worldPosition;
        _lastSubmittedTick = now;
    }

    public void Stop() {
        IsActive = false;
        lock (_sampleLock)
            _latestSample = null;
        _lastSubmittedPosition = null;
        _lastAttemptTick = 0;
        _lastSubmittedTick = 0;
    }
}
