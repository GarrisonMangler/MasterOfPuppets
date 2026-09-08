using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

using Dalamud.Game.ClientState.Conditions;

namespace MasterOfPuppets.Ipc;

public record PeerCharacterInfo(
    ulong ContentId,
    string CharacterName,
    string HomeWorld,
    uint HomeWorldId,
    string CurrentWorld,
    uint CurrentWorldId,
    DateTime LastSeen = default,
    IReadOnlyList<ulong>? PartyContentIds = null,
    uint TerritoryId = 0,
    uint InstanceId = 0,
    bool IsPerforming = false
);

internal readonly record struct PeerCharacterRuntimeMetadata(
    IReadOnlyList<ulong> PartyContentIds,
    uint TerritoryId,
    uint InstanceId,
    bool IsPerforming);

internal partial class IpcProvider {
    private static readonly TimeSpan CharacterDataBroadcastInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan PeerCharacterDataMaxAge = TimeSpan.FromSeconds(15);
    private DateTime _nextCharacterDataBroadcast = DateTime.MinValue;

    /// <summary>
    /// Character info received from each peer. Key = ContentId.
    /// Populated when peers respond to <see cref="RequestCharacterData"/>
    /// </summary>
    public ConcurrentDictionary<long, PeerCharacterInfo> PeerCharacterData { get; } = new();

    /// <summary>
    /// Broadcasts a request asking all peers to send their character info.
    /// </summary>
    public void RequestCharacterData() {
        BroadCast(IpcMessage.Create(IpcMessageType.RequestCharacterData).Serialize(), includeSelf: true);
    }

    public void UpdateCharacterDataHeartbeat() {
        var now = DateTime.UtcNow;

        if (now < _nextCharacterDataBroadcast)
            return;

        _nextCharacterDataBroadcast = now + CharacterDataBroadcastInterval;
        PruneStaleCharacterData(now);
        BroadcastCharacterData(IpcMessageType.CharacterData);
    }

    public IReadOnlyList<PeerCharacterInfo> GetFreshPeerCharacterData() {
        var now = DateTime.UtcNow;
        PruneStaleCharacterData(now);
        return PeerCharacterData.Values
            .Where(peer => IsFreshPeer(peer, now))
            .OrderBy(peer => peer.ContentId)
            .ToList();
    }

    [IpcHandle(IpcMessageType.RequestCharacterData)]
    private void HandleRequestCharacterData(IpcMessage message) {
        DalamudApi.Framework.RunOnFrameworkThread(() => BroadcastCharacterData(IpcMessageType.CharacterData));
    }

    [IpcHandle(IpcMessageType.CharacterData)]
    private void HandleCharacterData(IpcMessage message) {
        PeerCharacterData[message.BroadcasterId] = ParseCharacterInfo(message) with { LastSeen = DateTime.UtcNow };
    }

    private void PruneStaleCharacterData(DateTime now) {
        foreach (var pair in PeerCharacterData) {
            if (!IsFreshPeer(pair.Value, now))
                PeerCharacterData.TryRemove(pair.Key, out _);
        }
    }

    private static bool IsFreshPeer(PeerCharacterInfo peer, DateTime now) =>
        peer.LastSeen != default && now - peer.LastSeen <= PeerCharacterDataMaxAge;

    internal void BroadcastCharacterData(IpcMessageType type) {
        if (!DalamudApi.PlayerState.IsLoaded) return;

        BroadCast(IpcMessage.Create(
            type,
            new CharacterDataPayload {
                ContentId = DalamudApi.PlayerState.ContentId,
                HomeWorldId = DalamudApi.PlayerState.HomeWorld.RowId,
                CurrentWorldId = DalamudApi.PlayerState.CurrentWorld.RowId,
            },
            DalamudApi.PlayerState.CharacterName,
            DalamudApi.PlayerState.HomeWorld.ValueNullable?.Name.ToString() ?? "",
            DalamudApi.PlayerState.CurrentWorld.ValueNullable?.Name.ToString() ?? "",
            EncodePartyContentIds(),
            DalamudApi.ClientState.TerritoryType.ToString(CultureInfo.InvariantCulture),
            DalamudApi.ClientState.Instance.ToString(CultureInfo.InvariantCulture),
            DalamudApi.Condition[ConditionFlag.Performing] ? "1" : "0"
        ).Serialize(), includeSelf: true);
    }

    internal void BroadcastTargetedCharacterData(IpcMessageType type, long targetId) {
        if (!DalamudApi.PlayerState.IsLoaded) return;

        BroadCast(IpcMessage.Create(
            type,
            new CharacterDataPayload {
                ContentId = DalamudApi.PlayerState.ContentId,
                HomeWorldId = DalamudApi.PlayerState.HomeWorld.RowId,
                CurrentWorldId = DalamudApi.PlayerState.CurrentWorld.RowId,
            },
            DalamudApi.PlayerState.CharacterName,
            DalamudApi.PlayerState.HomeWorld.ValueNullable?.Name.ToString() ?? "",
            DalamudApi.PlayerState.CurrentWorld.ValueNullable?.Name.ToString() ?? "",
            EncodePartyContentIds(),
            DalamudApi.ClientState.TerritoryType.ToString(CultureInfo.InvariantCulture),
            DalamudApi.ClientState.Instance.ToString(CultureInfo.InvariantCulture),
            targetId.ToString(),
            DalamudApi.Condition[ConditionFlag.Performing] ? "1" : "0"
        ).Serialize(), includeSelf: true);
    }

    internal static PeerCharacterInfo ParseCharacterInfo(IpcMessage message) {
        var p = message.DataStruct<CharacterDataPayload>();
        var name = message.StringData?.ElementAtOrDefault(0) ?? "";
        var homeWorld = message.StringData?.ElementAtOrDefault(1) ?? "";
        var currentWorld = message.StringData?.ElementAtOrDefault(2) ?? "";
        var metadata = ParseRuntimeMetadata(message.StringData);
        return new PeerCharacterInfo(
            p.ContentId,
            name,
            homeWorld,
            p.HomeWorldId,
            currentWorld,
            p.CurrentWorldId,
            PartyContentIds: metadata.PartyContentIds,
            TerritoryId: metadata.TerritoryId,
            InstanceId: metadata.InstanceId,
            IsPerforming: metadata.IsPerforming);
    }

    internal static PeerCharacterRuntimeMetadata ParseRuntimeMetadata(
        IReadOnlyList<string>? fields) {
        // Legacy character-data frames contain three fields, or four when
        // targeted. Extended frames contain seven, or eight when targeted.
        // Do not reinterpret a legacy recipient ID as party membership.
        if (fields == null || fields.Count < 7)
            return new PeerCharacterRuntimeMetadata(Array.Empty<ulong>(), 0, 0, false);

        var partyContentIds = (fields.ElementAtOrDefault(3) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => ulong.TryParse(value, out var contentId) ? contentId : 0)
            .Where(contentId => contentId != 0)
            .Distinct()
            .OrderBy(contentId => contentId)
            .ToArray();
        _ = uint.TryParse(
            fields.ElementAtOrDefault(4),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var territoryId);
        _ = uint.TryParse(
            fields.ElementAtOrDefault(5),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var instanceId);
        // Targeted character-data messages reserve index 6 for the recipient.
        var performanceIndex = fields.Count >= 8 ? 7 : 6;
        var isPerforming = (fields.ElementAtOrDefault(performanceIndex) ?? string.Empty) == "1";
        return new PeerCharacterRuntimeMetadata(
            partyContentIds,
            territoryId,
            instanceId,
            isPerforming);
    }

    internal static bool TryGetCharacterDataTarget(
        IReadOnlyList<string>? fields,
        out ulong targetContentId) {
        targetContentId = 0;
        if (fields == null)
            return false;
        var targetIndex = fields.Count switch {
            4 => 3,
            >= 8 => 6,
            _ => -1,
        };
        return targetIndex >= 0 && ulong.TryParse(fields[targetIndex], out targetContentId);
    }

    private static string EncodePartyContentIds() => string.Join(',', DalamudApi.PartyList
        .Select(member => member.ContentId)
        .Where(contentId => contentId != 0)
        .Distinct()
        .OrderBy(contentId => contentId));

    [StructLayout(LayoutKind.Sequential)]
    private struct CharacterDataPayload {
        public ulong ContentId;
        public uint HomeWorldId;
        public uint CurrentWorldId;
    }
}
