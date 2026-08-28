using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Game.ClientState.Objects.Types;

using MasterOfPuppets.Extensions.Dalamud;
using MasterOfPuppets.Formations;
using MasterOfPuppets.Movement;

namespace MasterOfPuppets.LuaScripting;

/// <summary>
/// Lua-owned actor following. It consumes names and movement options directly;
/// it does not execute macros or load saved formation definitions.
/// </summary>
internal sealed class LuaActorFollowController {
    private readonly Plugin _plugin;
    private LuaActorFollowRequest? _request;
    private string? _trackingKey;
    private ulong _runTargetObjectId;
    private uint _runTargetEntityId;
    private string _runTargetName = string.Empty;
    private ulong _currentAnchorObjectId;
    private FormationAnchorLocomotionTracker? _anchorLocomotion;

    public LuaActorFollowController(Plugin plugin) {
        _plugin = plugin;
    }

    public bool IsActive => _trackingKey != null;

    public void PauseMovement() {
        if (_trackingKey != null && _plugin.SimpleInputMovement.IsActiveLiveFormationMove(_trackingKey))
            _plugin.SimpleInputMovement.StopMove();
    }

    public void Start(
        string runId,
        LuaActorFollowRequest request,
        ulong runTargetObjectId,
        uint runTargetEntityId,
        string runTargetName) {
        Stop(stopMovement: true);
        _request = request.Validate();
        _trackingKey = $"LuaActorFollow:{runId}";
        _runTargetObjectId = runTargetObjectId;
        _runTargetEntityId = runTargetEntityId;
        _runTargetName = FormationCharacterName.NormalizeWorldSeparator(runTargetName);
        _currentAnchorObjectId = 0;
        _anchorLocomotion = null;
    }

    public void Update() {
        var request = _request;
        var trackingKey = _trackingKey;
        if (request == null || trackingKey == null)
            return;

        var anchor = ResolveFirstVisibleAnchor(request.AnchorCandidates);
        if (anchor == null) {
            if (_plugin.SimpleInputMovement.IsActiveLiveFormationMove(trackingKey))
                _plugin.SimpleInputMovement.StopMove();
            _currentAnchorObjectId = 0;
            _anchorLocomotion = null;
            return;
        }

        var now = Environment.TickCount64;
        if (_currentAnchorObjectId != anchor.GameObjectId || _anchorLocomotion == null) {
            _currentAnchorObjectId = anchor.GameObjectId;
            _anchorLocomotion = new FormationAnchorLocomotionTracker(anchor.Position, anchor.Rotation, now);
            DalamudApi.PluginLog.Debug($"[Lua] actor follow attached to {GetActorName(anchor)}");
        }

        var useAnchorRelativeMovement = request.FaceAnchor
            && _anchorLocomotion.Update(anchor.Position, anchor.Rotation, now);
        var destination = GetWorldDestination(anchor.Position, anchor.Rotation, request.RelativeOffset);
        var facing = request.FaceAnchor
            ? anchor.Rotation + request.FacingOffsetRadians
            : (float?)null;

        _plugin.SimpleInputMovement.MoveTo(
            destination,
            precision: request.Precision,
            faceDirection: facing,
            movementMode: SimpleMovementMode.Natural,
            stopOnStuck: false,
            trackingKey: trackingKey,
            useFormationRelativeMovement: useAnchorRelativeMovement,
            usePursuitTarget: request.PursuitPrediction,
            allowHoldWhileTargetMoving: request.BrakeAtPosition,
            rateLimitTravelFacing: !request.ImmediateSteering);
    }

    public void Stop(bool stopMovement) {
        var trackingKey = _trackingKey;
        _request = null;
        _trackingKey = null;
        _runTargetObjectId = 0;
        _runTargetEntityId = 0;
        _runTargetName = string.Empty;
        _currentAnchorObjectId = 0;
        _anchorLocomotion = null;
        if (stopMovement
            && trackingKey != null
            && _plugin.SimpleInputMovement.IsActiveLiveFormationMove(trackingKey))
            _plugin.SimpleInputMovement.StopMove();
    }

    internal static Vector3 GetWorldDestination(
        Vector3 anchorPosition,
        float anchorRotation,
        Vector3 relativeOffset) {
        var cos = MathF.Cos(anchorRotation);
        var sin = MathF.Sin(anchorRotation);
        var rotated = new Vector3(
            relativeOffset.X * cos + relativeOffset.Z * sin,
            relativeOffset.Y,
            -relativeOffset.X * sin + relativeOffset.Z * cos);
        return anchorPosition + rotated;
    }

    private IGameObject? ResolveFirstVisibleAnchor(IReadOnlyList<string> candidates) {
        var localPlayer = DalamudApi.ObjectTable.LocalPlayer;
        foreach (var candidate in candidates) {
            IGameObject? match = null;
            if (_runTargetEntityId != 0
                && _runTargetEntityId != 0xE0000000
                && FormationCharacterName.MatchScore(_runTargetName, candidate) >= 0) {
                match = DalamudApi.ObjectTable.FirstOrDefault(actor => actor.EntityId == _runTargetEntityId);
            }
            if (_runTargetObjectId != 0
                && match == null
                && FormationCharacterName.MatchScore(_runTargetName, candidate) >= 0) {
                match = DalamudApi.ObjectTable.FirstOrDefault(actor => actor.GameObjectId == _runTargetObjectId);
            }

            match ??= DalamudApi.ObjectTable.FirstOrDefault(actor =>
                IsUsable(actor)
                && FormationCharacterName.MatchScore(candidate, GetActorName(actor)) >= 0);
            if (match != null && match.GameObjectId != localPlayer?.GameObjectId)
                return match;
        }

        return null;
    }

    private static string GetActorName(IGameObject actor) =>
        actor.GetPlayerNameWorld() ?? actor.Name.TextValue;

    private static bool IsUsable(IGameObject? actor) =>
        actor != null && actor.Address != nint.Zero;
}
