using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MasterOfPuppets.Formations;

internal sealed record FormationChatAnchorPayload(
    int SchemaVersion,
    string MessageId,
    long CreatedUnixMilliseconds,
    string FormationName,
    uint TerritoryId,
    string EligibleMemberBits,
    float X,
    float Y,
    float Z,
    float Rotation,
    string AnchorName,
    ulong AnchorGameObjectId,
    string MovementMode);

internal static class FormationChatSyncCodec {
    internal const string CommandName = "mopformationanchor";
    internal const int CurrentSchemaVersion = 3;

    internal static string Encode(FormationChatAnchorPayload payload) {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToBase64String(json);
    }

    internal static bool TryDecode(string token, out FormationChatAnchorPayload? payload) {
        payload = null;
        try {
            var json = Convert.FromBase64String(token);
            payload = JsonSerializer.Deserialize<FormationChatAnchorPayload>(json);
            return payload != null
                && payload.SchemaVersion == CurrentSchemaVersion
                && Guid.TryParse(payload.MessageId, out var messageId)
                && messageId != Guid.Empty
                && !string.IsNullOrWhiteSpace(payload.FormationName)
                && payload.TerritoryId != 0
                && TryDecodeMemberBits(payload.EligibleMemberBits, out _)
                && !string.IsNullOrWhiteSpace(payload.MovementMode)
                && float.IsFinite(payload.X)
                && float.IsFinite(payload.Y)
                && float.IsFinite(payload.Z)
                && float.IsFinite(payload.Rotation);
        } catch (FormatException) {
            return false;
        } catch (JsonException) {
            return false;
        }
    }

    internal static string EncodeEligibleMembers(
        Formation formation,
        IReadOnlyList<CidGroup>? groups,
        IEnumerable<ulong> eligibleContentIds) {
        var roster = GetOrderedRoster(formation, groups);
        var bits = new byte[(roster.Count + 7) / 8];
        var eligible = eligibleContentIds.ToHashSet();
        for (var i = 0; i < roster.Count; i++) {
            if (eligible.Contains(roster[i]))
                bits[i / 8] |= (byte)(1 << (i % 8));
        }
        return Convert.ToBase64String(bits);
    }

    internal static bool IsEligibleMember(
        Formation formation,
        IReadOnlyList<CidGroup>? groups,
        ulong contentId,
        string encodedBits) {
        var roster = GetOrderedRoster(formation, groups);
        var index = roster.IndexOf(contentId);
        if (index < 0 || !TryDecodeMemberBits(encodedBits, out var bits))
            return false;
        if (bits.Length != (roster.Count + 7) / 8)
            return false;
        return (bits[index / 8] & (1 << (index % 8))) != 0;
    }

    private static List<ulong> GetOrderedRoster(
        Formation formation,
        IReadOnlyList<CidGroup>? groups) =>
        formation.Points
            .SelectMany(point => point.GetEffectiveCids(groups))
            .Where(cid => cid != 0)
            .Distinct()
            .OrderBy(cid => cid)
            .ToList();

    private static bool TryDecodeMemberBits(string encodedBits, out byte[] bits) {
        bits = [];
        if (string.IsNullOrWhiteSpace(encodedBits))
            return false;
        try {
            bits = Convert.FromBase64String(encodedBits);
            return bits.Length is > 0 and <= 128;
        } catch (FormatException) {
            return false;
        }
    }
}
