using System;
using System.Linq;
using System.Numerics;

using Dalamud.Game.ClientState.Objects.Types;

using MasterOfPuppets.Extensions;
using MasterOfPuppets.Extensions.Dalamud;
using MasterOfPuppets.Camera;
using MasterOfPuppets.Formations;
using MasterOfPuppets.LuaScripting.Choreography;
using MasterOfPuppets.LuaScripting.Runs;
using MasterOfPuppets.LuaScripting.Watches;
using MasterOfPuppets.Movement;

namespace MasterOfPuppets.LuaScripting;

internal sealed class LuaTrajectoryController {
    private const float TrajectoryPrecision = 0.08f;
    private const float MaximumSampleSpeed = 18f;
    private const long AnchorLossTimeoutMs = 1500;

    private readonly Plugin _plugin;
    private readonly LuaTrajectoryFollower _follower = new();
    private readonly object _sampleLock = new();
    private LuaTrajectorySample? _latestSample;
    private LuaTrajectorySample? _lastAcceptedSample;
    private string? _trackingKey;
    private string _anchorName = string.Empty;
    private ulong _anchorObjectId;
    private uint _anchorEntityId;
    private long _lastAnchorSeenMs;
    private double _lastSubmittedElapsedSeconds = double.NegativeInfinity;
    private LuaTrajectorySample? _previousSubmittedSample;
    private float? _pathFacing;
    private Vector3? _lastAnchorPosition;
    private long _lastAnchorPositionMs;
    private int _pathDirection = 1;
    private long _lastAnchorMovingMs;
    private bool? _savedWalking;

    public float AnchorSpeed { get; private set; }
    public LuaTrajectoryFollowerOutput? Diagnostics { get; private set; }

    public LuaTrajectoryDiagnosticsSnapshot? SnapshotDiagnostics() {
        if (Diagnostics is not { } diagnostics)
            return null;
        return new LuaTrajectoryDiagnosticsSnapshot(
            _anchorName,
            diagnostics.RequestedSpeed,
            diagnostics.Locomotion.ToString().ToLowerInvariant(),
            diagnostics.RadialError,
            diagnostics.PhaseError,
            diagnostics.Recovering);
    }

    public LuaTrajectoryController(Plugin plugin) {
        _plugin = plugin;
    }

    public bool IsActive => _trackingKey != null;

    public void PauseMovement() {
        if (_trackingKey != null && _plugin.SimpleInputMovement.IsActiveLiveFormationMove(_trackingKey))
            _plugin.SimpleInputMovement.StopMove();
    }

    public void Start(string runId, ulong anchorObjectId, uint anchorEntityId, string anchorName) {
        Stop(stopMovement: true);
        _trackingKey = $"LuaTrajectory:{runId}";
        _anchorObjectId = anchorObjectId;
        _anchorEntityId = anchorEntityId;
        _anchorName = FormationCharacterName.NormalizeWorldSeparator(anchorName);
        _lastAnchorSeenMs = Environment.TickCount64;
        _lastSubmittedElapsedSeconds = double.NegativeInfinity;
        _previousSubmittedSample = null;
        _pathFacing = null;
        _lastAnchorPosition = null;
        _lastAnchorPositionMs = 0;
        _lastAnchorMovingMs = 0;
        AnchorSpeed = 0f;
        _pathDirection = 1;
        _follower.Reset();
        Diagnostics = null;
        _savedWalking ??= SimpleMovementWalkState.IsWalking;
        // A shared trajectory must not inherit different per-client gait states.
        // Mixed walk/run speeds cause the same fixed slots to fall behind as pace
        // increases. Restore each user's original state when the run stops.
        SimpleMovementWalkState.IsWalking = false;
    }

    public void Publish(LuaTrajectorySample sample) {
        lock (_sampleLock) {
            if (_lastAcceptedSample is { } previous) {
                var elapsed = sample.ElapsedSeconds - previous.ElapsedSeconds;
                if (elapsed > 0.0001) {
                    var distance = Vector3.Distance(sample.RelativeOffset, previous.RelativeOffset);
                    if (distance / elapsed > MaximumSampleSpeed) {
                        DalamudApi.PluginLog.Warning("[Lua] rejected trajectory sample above speed limit");
                        return;
                    }
                }
            }

            _lastAcceptedSample = sample;
            _latestSample = sample;
        }
    }

    public void Update() {
        if (_trackingKey == null)
            return;

        LuaTrajectorySample? sample;
        lock (_sampleLock)
            sample = _latestSample;
        if (sample == null)
            return;

        var anchor = ResolveAnchor();
        if (anchor == null) {
            GameCameraManager.SetTrackingAnchor(null);
            if (Environment.TickCount64 - _lastAnchorSeenMs >= AnchorLossTimeoutMs) {
                DalamudApi.PluginLog.Warning($"[Lua] script anchor lost: {_anchorName}");
                _plugin.LuaScriptManager.StopLocal("target lost");
            }
            return;
        }

        _lastAnchorSeenMs = Environment.TickCount64;
        GameCameraManager.SetTrackingAnchor(sample.Value.TrackCameraAnchor ? anchor.Position : null);
        // Lua publishes points in the anchor's local frame. Reapply the live
        // anchor transform every framework tick so both translation and turns
        // carry the entire shape with the target.
        var worldPosition = TransformPosition(
            sample.Value.RelativeOffset,
            anchor.Position,
            anchor.Rotation);
        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null)
            return;

        var nowMs = Environment.TickCount64;
        var rawSpeed = 0f;
        var anchorVelocity = Vector3.Zero;
        if (_lastAnchorPosition is { } lastAnchor && _lastAnchorPositionMs > 0) {
            var anchorDelta = Math.Clamp((nowMs - _lastAnchorPositionMs) / 1000f, 0.001f, 0.25f);
            anchorVelocity = (anchor.Position - lastAnchor) / anchorDelta;
            if (anchorVelocity.Length() > MaximumSampleSpeed)
                anchorVelocity = Vector3.Zero;
            rawSpeed = anchorVelocity.Length();
        }
        _lastAnchorPosition = anchor.Position;
        _lastAnchorPositionMs = nowMs;

        if (rawSpeed > 0.25f && rawSpeed <= MaximumSampleSpeed) {
            AnchorSpeed = MathF.Max(AnchorSpeed * 0.7f, rawSpeed);
            _lastAnchorMovingMs = nowMs;
        } else if (nowMs - _lastAnchorMovingMs > 300) {
            AnchorSpeed *= 0.7f;
            if (AnchorSpeed < 0.1f) AnchorSpeed = 0f;
        }

        if (sample.Value.ElapsedSeconds > _lastSubmittedElapsedSeconds) {
            if (_previousSubmittedSample is { } previousSample) {
                var pathDelta = sample.Value.RelativeOffset - previousSample.RelativeOffset;
                pathDelta.Y = 0f;
                if (pathDelta.LengthSquared() > 0.000001f)
                    _pathFacing = MathF.Atan2(pathDelta.X, pathDelta.Z);
            }
            Diagnostics = TryFollowCircularPath(
                anchor.Position,
                anchorVelocity,
                worldPosition,
                sample.Value,
                out var steering)
                    ? steering
                    : null;
            _lastSubmittedElapsedSeconds = sample.Value.ElapsedSeconds;
            _previousSubmittedSample = sample;
        }

        // Translate the slot with the target. Do not chase slot orbital velocity
        // or the ring stretches into a wake. Face immediately when far so a
        // moving target cannot peel people off the circle while they turn.
        var trackingPoint = worldPosition;
        if (AnchorSpeed > 0.25f) {
            var lead = new Vector3(anchorVelocity.X, 0f, anchorVelocity.Z) * 0.16f;
            trackingPoint += lead;
        }
        // Moving paths face along their measured tangent. Stationary authored
        // points, such as clock markers, retain the script-supplied facing.
        var tangentFacing = TransformFacing(
            ResolveFacing(_pathFacing, sample.Value.FacingRadians),
            anchor.Rotation);

        _plugin.SimpleInputMovement.MoveTo(
            trackingPoint,
            precision: TrajectoryPrecision,
            faceDirection: tangentFacing,
            movementMode: SimpleMovementMode.Natural,
            stopOnStuck: false,
            trackingKey: _trackingKey,
            useFormationRelativeMovement: true,
            usePursuitTarget: false,
            // Permit the Natural strategy's 0.08 / 0.16 hysteretic hold around
            // the live slot. Without this, full run input crosses the slot and
            // reverses on successive frames, shaking a camera attached to a
            // participating main character.
            allowHoldWhileTargetMoving: true,
            rateLimitTravelFacing: true);
    }

    internal static float ResolveFacing(float? pathFacing, float authoredFacing) =>
        pathFacing ?? authoredFacing;

    internal static Vector3 TransformPosition(
        Vector3 relativeOffset,
        Vector3 anchorPosition,
        float anchorRotation) =>
        anchorPosition + FormationMath.RotateOffset(relativeOffset, anchorRotation);

    internal static float TransformFacing(float localFacing, float anchorRotation) {
        var worldFacing = localFacing + anchorRotation;
        return MathF.Atan2(MathF.Sin(worldFacing), MathF.Cos(worldFacing));
    }

    public void Stop(bool stopMovement) {
        GameCameraManager.SetTrackingAnchor(null);
        _trackingKey = null;
        _lastSubmittedElapsedSeconds = double.NegativeInfinity;
        lock (_sampleLock) {
            _latestSample = null;
            _lastAcceptedSample = null;
        }
        _previousSubmittedSample = null;
        _pathFacing = null;
        _lastAnchorPosition = null;
        _lastAnchorPositionMs = 0;
        _lastAnchorMovingMs = 0;
        AnchorSpeed = 0f;
        _follower.Reset();
        Diagnostics = null;
        if (_savedWalking.HasValue) {
            SimpleMovementWalkState.IsWalking = _savedWalking.Value;
            _savedWalking = null;
        }
        if (stopMovement && _plugin.SimpleInputMovement.IsMoving)
            _plugin.SimpleInputMovement.StopMove();
    }

    private bool TryFollowCircularPath(
        Vector3 anchorPosition,
        Vector3 anchorVelocity,
        Vector3 worldPosition,
        LuaTrajectorySample sample,
        out LuaTrajectoryFollowerOutput output) {
        output = default;
        if (_previousSubmittedSample is not { } previous)
            return false;
        var delta = sample.ElapsedSeconds - previous.ElapsedSeconds;
        var radius = new Vector2(sample.RelativeOffset.X, sample.RelativeOffset.Z).Length();
        var previousRadius = new Vector2(previous.RelativeOffset.X, previous.RelativeOffset.Z).Length();
        if (delta is <= 0.0001 or > 0.25 || radius < 1f || previousRadius < 1f)
            return false;

        // The follower compares this phase with the player's world-space phase,
        // so derive it from the already transformed destination.
        var worldOffset = worldPosition - anchorPosition;
        var desiredPhase = MathF.Atan2(worldOffset.X, worldOffset.Z);
        // Orbit direction and speed remain properties of the Lua-authored local
        // path; an anchor turn must not masquerade as hand motion.
        var localPhase = MathF.Atan2(sample.RelativeOffset.X, sample.RelativeOffset.Z);
        var previousPhase = MathF.Atan2(previous.RelativeOffset.X, previous.RelativeOffset.Z);
        var phaseDelta = MathF.Atan2(
            MathF.Sin(localPhase - previousPhase),
            MathF.Cos(localPhase - previousPhase));
        if (MathF.Abs(phaseDelta) > 0.00001f)
            _pathDirection = phaseDelta >= 0f ? 1 : -1;
        var tangentialSpeed = Math.Clamp(radius * MathF.Abs(phaseDelta) / (float)delta, 0f, MaximumSampleSpeed);

        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null)
            return false;
        output = _follower.Step(new LuaTrajectoryFollowerInput(
            anchorPosition,
            anchorVelocity,
            player.Position,
            player.Rotation,
            worldPosition,
            desiredPhase,
            radius,
            tangentialSpeed,
            _pathDirection,
            (float)delta));
        return output.IsFinite;
    }

    private IGameObject? ResolveAnchor() {
        // Object-table wrappers are snapshots on some Dalamud/game versions.
        // Reacquire the actor for every published trajectory point so Position
        // always comes from the target's current transform rather than launch time.
        if (_anchorEntityId != 0 && _anchorEntityId != 0xE0000000) {
            var entityMatch = DalamudApi.ObjectTable.FirstOrDefault(actor =>
                IsUsable(actor) && actor.EntityId == _anchorEntityId);
            if (entityMatch != null)
                return entityMatch;
        }

        if (_anchorObjectId != 0) {
            var idMatch = DalamudApi.ObjectTable.FirstOrDefault(actor =>
                IsUsable(actor) && actor.GameObjectId == _anchorObjectId);
            if (idMatch != null)
                return idMatch;
        }

        if (_anchorName.Length == 0)
            return null;

        // A name fallback must never bind according to object-table order.
        // Ambiguous names remain unresolved until the original ID is visible
        // again or a unique Name@World match exists.
        return LuaActorQueryResolver.ResolvePlayer(DalamudApi.ObjectTable, _anchorName).Actor;
    }

    private bool IsUsable(IGameObject? actor) =>
        actor != null
        && actor.Address != nint.Zero;
}
