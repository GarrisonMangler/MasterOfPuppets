using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.ImGuiNotification;

using MasterOfPuppets.Extensions;
using MasterOfPuppets.Extensions.Dalamud;
using MasterOfPuppets.Formations;
using MasterOfPuppets.LuaScripting;
using MasterOfPuppets.LuaScripting.Runs;
using MasterOfPuppets.LuaScripting.Runtime;

namespace MasterOfPuppets.Ipc;

internal partial class IpcProvider {
    private const int LuaPeerRefreshDelayTicks = 90;
    private static readonly TimeSpan LuaStartLeadTime = TimeSpan.FromSeconds(1.25);
    private const int ChatSyncedLuaServerLeadSeconds = 4;

    public void StartLuaScript(string scriptName, Dictionary<string, string>? inlineVariables = null) {
        _ = DalamudApi.Framework.RunOnFrameworkThread(() => {
            var (runTargetObjectId, runTargetEntityId, runTargetName) = CaptureRunAnchor();
            RequestCharacterData();
            DalamudApi.Framework.RunOnTick(
                () => BroadcastLuaScript(scriptName, runTargetObjectId, runTargetEntityId, runTargetName, inlineVariables),
                delayTicks: LuaPeerRefreshDelayTicks);
        });
    }

    public void StartChatSyncedLuaScript(string scriptName, Dictionary<string, string>? inlineVariables = null) {
        var script = LuaScriptCatalog.Find(Plugin.Config, scriptName);
        if (script == null) {
            DalamudApi.ChatGui.PrintError($"[MoP] The configured Lua script '{scriptName}' was not found.");
            return;
        }

        try {
            script.Validate();
        } catch (Exception ex) {
            DalamudApi.ChatGui.PrintError($"[MoP] Lua script '{scriptName}' is invalid: {ex.Message}");
            return;
        }

        var chatPrefix = Plugin.Config.DefaultChatSyncPrefix?.Trim();
        if (string.IsNullOrWhiteSpace(chatPrefix)) {
            DalamudApi.ChatGui.PrintError("[MoP] Configure a default Chat Sync prefix before starting synchronized Lua.");
            return;
        }

        if (script.Name.Contains('"')) {
            DalamudApi.ChatGui.PrintError("[MoP] Synchronized Lua script names cannot contain quotation marks.");
            return;
        }

        Dictionary<string, string> variables;
        try {
            variables = ResolveSharedLuaVariables(script, inlineVariables);
        } catch (ArgumentException ex) {
            DalamudApi.ChatGui.PrintError($"[MoP] Lua parameters are invalid: {ex.Message}");
            return;
        }
        IReadOnlyList<ulong> participantCids;
        if (!LuaParticipantResolver.TryResolve(
                script,
                Plugin.Config.Formations,
                Plugin.Config.CidsGroups,
                out participantCids,
                out var participantError)) {
            // Compatibility for scripts created before participant formations existed.
            if (!TryResolveParticipantGroup(Plugin.Config.CidsGroups, variables, out _, out participantCids, out _)) {
                DalamudApi.ChatGui.PrintError($"[MoP] {participantError}");
                return;
            }
        }

        var startUtcTicks = DateTime.UtcNow.Ticks;
        var seed = Random.Shared.Next();
        var (_, selectedTargetEntityId, selectedTargetName) = CaptureRunAnchor();
        var runTargetName = variables.TryGetValue("anchor", out var configuredAnchor)
            && !string.IsNullOrWhiteSpace(configuredAnchor)
                ? configuredAnchor.Trim()
                : selectedTargetName;
        var senderName = MacroRuntimeVariables.FromCurrentGameState().Me;
        runTargetName = ResolveChatSyncedRunTarget(runTargetName, senderName);
        if (FormationCharacterName.MatchScore(selectedTargetName, runTargetName) < 0)
            selectedTargetEntityId = 0;
        variables["anchor"] = runTargetName;
        participantCids = CompactVisibleLaunchRoster(participantCids);
        var envelope = new LuaChatSyncEnvelope {
            MessageId = Guid.NewGuid().ToString("D"),
            CreatedUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StartUtcTicks = startUtcTicks,
            StartServerTimeSeconds = LuaChoreographyClock.GetServerTimeSeconds() + ChatSyncedLuaServerLeadSeconds,
            Seed = seed,
            ScriptHash = script.Hash,
            DependencyManifestHash = script.DependencyManifestHash,
            BundleHash = script.BundleHash,
            RequiredResources = script.RequiredResources,
            RunTargetName = runTargetName,
            RunTargetEntityId = selectedTargetEntityId,
            Variables = variables,
            ParticipantCids = participantCids.ToList(),
        };
        var command = $"{chatPrefix} mopluarun \"{script.Name}\" {EncodeLuaChatSyncEnvelope(envelope)}";
        if (Encoding.UTF8.GetByteCount(command) > 500) {
            DalamudApi.ChatGui.PrintError("[MoP] The synchronized Lua roster is too large for one chat message.");
            return;
        }
        Chat.SendMessage(command);
        var targetStatus = string.IsNullOrWhiteSpace(runTargetName) ? string.Empty : $" with target {runTargetName}";
        DalamudApi.ShowNotification(
            $"Synchronizing Lua '{script.Name}' across Chat Sync clients{targetStatus}",
            NotificationType.Success,
            6000);
    }

    public void StartChatSyncedLuaScriptLocal(
        string scriptName,
        LuaChatSyncEnvelope envelope,
        string senderName) {
        var script = LuaScriptCatalog.Find(Plugin.Config, scriptName);
        if (script == null) {
            DalamudApi.PluginLog.Warning(
                $"[Lua] synchronized script '{scriptName}' from {senderName} is not installed locally");
            return;
        }

        try {
            script.Validate();
        } catch (Exception ex) {
            DalamudApi.PluginLog.Warning(ex, $"[Lua] synchronized script '{scriptName}' is invalid");
            return;
        }

        var bundleHashMatch = envelope.BundleHash.Length == 32
            ? script.BundleHash.StartsWith(envelope.BundleHash, StringComparison.OrdinalIgnoreCase)
            : string.Equals(script.BundleHash, envelope.BundleHash, StringComparison.OrdinalIgnoreCase);
        if (!bundleHashMatch) {
            DalamudApi.PluginLog.Warning(
                $"[Lua] synchronized bundle contract mismatch for '{scriptName}' from {senderName}; " +
                $"expected={envelope.BundleHash} local={script.BundleHash}");
            DalamudApi.ChatGui.PrintError($"[MoP] Lua script '{scriptName}' capabilities differ from the conductor's copy.");
            return;
        }
        envelope.BundleHash = script.BundleHash;

        if (string.IsNullOrEmpty(envelope.ScriptHash)) {
            envelope.ScriptHash = script.Hash;
            envelope.DependencyManifestHash = script.DependencyManifestHash;
        } else {
            if (!string.Equals(script.Hash, envelope.ScriptHash, StringComparison.OrdinalIgnoreCase)) {
                DalamudApi.PluginLog.Warning(
                    $"[Lua] synchronized script hash mismatch for '{scriptName}' from {senderName}; " +
                    $"expected={envelope.ScriptHash} local={script.Hash}");
                DalamudApi.ChatGui.PrintError($"[MoP] Lua script '{scriptName}' differs from the conductor's copy.");
                return;
            }
            if (!string.IsNullOrEmpty(envelope.DependencyManifestHash) &&
                !string.Equals(
                    script.DependencyManifestHash,
                    envelope.DependencyManifestHash,
                    StringComparison.OrdinalIgnoreCase)) {
                DalamudApi.PluginLog.Warning(
                    $"[Lua] synchronized dependency manifest mismatch for '{scriptName}' from {senderName}; " +
                    $"expected={envelope.DependencyManifestHash} local={script.DependencyManifestHash}");
                DalamudApi.ChatGui.PrintError($"[MoP] Lua script '{scriptName}' modules differ from the conductor's copy.");
                return;
            }
        }
        if (script.RequiredResources != envelope.RequiredResources) {
            DalamudApi.PluginLog.Warning(
                $"[Lua] synchronized resource declaration mismatch for '{scriptName}' from {senderName}; " +
                $"expected={envelope.RequiredResources?.ToString() ?? "legacy"} " +
                $"local={script.RequiredResources?.ToString() ?? "legacy"}");
            DalamudApi.ChatGui.PrintError($"[MoP] Lua script '{scriptName}' resource declaration differs from the conductor's copy.");
            return;
        }

        var localCid = DalamudApi.PlayerState.ContentId;
        if (localCid == 0)
            return;

        if (envelope.ParticipantCids.Count > 0) {
            if (!string.IsNullOrWhiteSpace(script.ParticipantFormation)
                && LuaParticipantResolver.TryResolve(
                    script,
                    Plugin.Config.Formations,
                    Plugin.Config.CidsGroups,
                    out var localParticipants,
                    out _)) {
                var allowed = localParticipants.ToHashSet();
                if (envelope.ParticipantCids.Any(cid => !allowed.Contains(cid))) {
                    DalamudApi.ChatGui.PrintError(
                        $"[MoP] Participant Formation '{script.ParticipantFormation}' differs from the conductor's copy.");
                    return;
                }
            }
        } else if (!string.IsNullOrWhiteSpace(script.ParticipantFormation)) {
            if (!LuaParticipantResolver.TryResolve(
                    script,
                    Plugin.Config.Formations,
                    Plugin.Config.CidsGroups,
                    out var localParticipants,
                    out var participantError)) {
                DalamudApi.ChatGui.PrintError($"[MoP] {participantError}");
                return;
            }
            envelope.ParticipantCids = localParticipants.ToList();
        } else if (envelope.ParticipantCids.Count == 0 && envelope.Variables.ContainsKey("group")) {
            if (TryResolveParticipantGroup(Plugin.Config.CidsGroups, envelope.Variables, out _, out var groupParticipants, out _)) {
                envelope.ParticipantCids = groupParticipants.ToList();
            }
        }

        if (!envelope.ParticipantCids.Contains(localCid))
            return;

        var effectiveRunTargetName = ResolveChatSyncedRunTarget(envelope.RunTargetName, senderName);
        var effectiveVariables = AddLocalRuntimeVariables(envelope.Variables);

        if (Plugin.ChatWatcher.LuaDistributedLaunches.QueueLaunch(
                script,
                envelope,
                senderName,
                effectiveRunTargetName,
                effectiveVariables,
                out var stagingError)) {
            if (!string.IsNullOrWhiteSpace(stagingError)) {
                DalamudApi.PluginLog.Warning($"[LuaSync] staging rejected for '{script.Name}': {stagingError}");
                DalamudApi.ChatGui.PrintError($"[MoP] Lua staging rejected: {stagingError}.");
            }
            return;
        }

        _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
            Plugin.LuaScriptManager.StartScript(
                script.Name,
                script.Hash,
                script.Source,
                0,
                envelope.RunTargetEntityId,
                effectiveRunTargetName,
                envelope.ParticipantCids,
                envelope.StartUtcTicks,
                envelope.Seed,
                effectiveVariables,
                startServerTimeSeconds: envelope.StartServerTimeSeconds,
                modules: script.Modules,
                requiredResources: script.RequiredResources,
                declaredCapabilities: script.DeclaredCapabilities,
                conductorName: senderName));
    }

    private void BroadcastLuaScript(
        string scriptName,
        ulong runTargetObjectId,
        uint runTargetEntityId,
        string runTargetName,
        Dictionary<string, string>? inlineVariables) {
        var script = LuaScriptCatalog.Find(Plugin.Config, scriptName);
        if (script == null) {
            var error = $"The configured Lua script '{scriptName}' was not found.";
            DalamudApi.ChatGui.PrintError($"[MoP] {error}");
            DalamudApi.ShowNotification(error, NotificationType.Error, 6000);
            return;
        }
        try {
            script.Validate();
        } catch (Exception ex) {
            DalamudApi.ChatGui.PrintError($"[MoP] Lua script '{scriptName}' is invalid: {ex.Message}");
            return;
        }

        Dictionary<string, string> variables;
        try {
            variables = ResolveLuaVariables(script, inlineVariables);
        } catch (ArgumentException ex) {
            DalamudApi.ChatGui.PrintError($"[MoP] Lua parameters are invalid: {ex.Message}");
            return;
        }
        if (variables.TryGetValue("anchor", out var configuredAnchor)
            && !string.IsNullOrWhiteSpace(configuredAnchor)) {
            if (FormationCharacterName.MatchScore(runTargetName, configuredAnchor) < 0)
                runTargetObjectId = 0;
            if (FormationCharacterName.MatchScore(runTargetName, configuredAnchor) < 0)
                runTargetEntityId = 0;
            runTargetName = configuredAnchor.Trim();
        }

        var freshPeers = GetFreshPeerCharacterData();
        IReadOnlyList<ulong> orderedParticipants;
        if (!string.IsNullOrWhiteSpace(script.ParticipantFormation)) {
            if (!LuaParticipantResolver.TryResolve(
                    script,
                    Plugin.Config.Formations,
                    Plugin.Config.CidsGroups,
                    out orderedParticipants,
                    out var participantError)) {
                DalamudApi.ChatGui.PrintError($"[MoP] {participantError}");
                return;
            }
        } else if (variables.ContainsKey("group")) {
            if (!TryResolveParticipantGroup(Plugin.Config.CidsGroups, variables, out var groupName, out orderedParticipants, out var groupError)) {
                DalamudApi.ChatGui.PrintError($"[MoP] {groupError}");
                return;
            }
            variables["group"] = groupName;
        } else {
            var participants = freshPeers
                .Where(peer => peer.ContentId != 0)
                .Select(peer => peer.ContentId)
                .ToHashSet();

            var localCid = DalamudApi.PlayerState.ContentId;
            if (localCid != 0)
                participants.Add(localCid);

            orderedParticipants = participants.OrderBy(cid => cid).ToArray();
        }
        orderedParticipants = CompactVisibleLaunchRoster(orderedParticipants);
        if (orderedParticipants.Count == 0) {
            const string error = "No active Lua clients were found.";
            DalamudApi.PluginLog.Warning(
                $"[Lua] no clients found; runTarget={runTargetName}; freshPeers={freshPeers.Count}");
            DalamudApi.ChatGui.PrintError($"[MoP] {error}");
            DalamudApi.ShowNotification(error, NotificationType.Error, 6000);
            return;
        }

        DalamudApi.PluginLog.Information(
            $"[Lua] broadcasting script; runTarget={runTargetName}; " +
            $"freshPeers={freshPeers.Count}; participants={orderedParticipants.Count}");

        var startUtcTicks = (DateTime.UtcNow + LuaStartLeadTime).Ticks;
        var seed = Random.Shared.Next();
        BroadCast(IpcMessage.Create(
            IpcMessageType.RunLuaScript,
            script.Name,
            script.Hash,
            script.Source,
            runTargetObjectId.ToString(CultureInfo.InvariantCulture),
            runTargetEntityId.ToString(CultureInfo.InvariantCulture),
            runTargetName,
            string.Join(',', orderedParticipants),
            startUtcTicks.ToString(CultureInfo.InvariantCulture),
            seed.ToString(CultureInfo.InvariantCulture),
            EncodeLuaVariablesToken(variables),
            EncodeLuaModulesToken(script.Modules),
            EncodeLuaResourcesToken(script.RequiredResources),
            script.BundleHash,
            MacroRuntimeVariables.FromCurrentGameState().Me).Serialize(), includeSelf: true);

        var targetContext = string.IsNullOrWhiteSpace(runTargetName) ? string.Empty : $"; target: {runTargetName}";
        DalamudApi.ShowNotification(
            $"Starting Lua '{script.Name}': {orderedParticipants.Count} clients{targetContext}",
            NotificationType.Success,
            6000);
    }

    public void StopLuaScript(string? selector = null) {
        BroadCast(IpcMessage.Create(
            IpcMessageType.StopLuaScript,
            string.IsNullOrWhiteSpace(selector) ? string.Empty : selector.Trim()).Serialize(), includeSelf: true);
    }

    public void PauseLuaScript(string? selector = null) {
        BroadCast(IpcMessage.Create(
            IpcMessageType.PauseLuaScript,
            string.IsNullOrWhiteSpace(selector) ? string.Empty : selector.Trim()).Serialize(), includeSelf: true);
    }

    public void ResumeLuaScript(string? selector = null) {
        BroadCast(IpcMessage.Create(
            IpcMessageType.ResumeLuaScript,
            string.IsNullOrWhiteSpace(selector) ? string.Empty : selector.Trim()).Serialize(), includeSelf: true);
    }

    [IpcHandle(IpcMessageType.RunLuaScript)]
    private void HandleRunLuaScript(IpcMessage message) {
        if (message.StringData is not { Length: >= 9 } data)
            return;
        var scriptName = data[0];
        var scriptHash = data[1];
        var scriptSource = data[2];
        if (scriptSource.Length > LuaScriptDefinition.MaximumSourceLength
            || !string.Equals(
                LuaScriptDefinition.ComputeHash(scriptSource),
                scriptHash,
                StringComparison.OrdinalIgnoreCase)) {
            DalamudApi.PluginLog.Warning($"[Lua] rejected script payload with invalid source/hash: {scriptName}");
            return;
        }

        if (!ulong.TryParse(data[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetObjectId))
            return;
        if (!uint.TryParse(data[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetEntityId))
            return;
        if (!TryParseCids(data[6], out var participantCids))
            return;
        if (!long.TryParse(data[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startUtcTicks))
            return;
        if (!int.TryParse(data[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
            return;

        var targetName = data[5];
        IReadOnlyDictionary<string, string> variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (data.Length >= 10 && !TryDecodeLuaVariablesToken(data[9], out variables))
            return;
        IReadOnlyDictionary<string, string> modules = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data.Length >= 11 && !TryDecodeLuaModulesToken(data[10], out modules))
            return;
        LuaResourceKind? requiredResources = null;
        if (data.Length >= 12 && !TryDecodeLuaResourcesToken(data[11], out requiredResources))
            return;
        if (data.Length < 13 || data[12].Length != 64 || data[12].Any(character => !Uri.IsHexDigit(character))) {
            DalamudApi.PluginLog.Warning($"[Lua] rejected local IPC run '{scriptName}': missing trusted bundle contract hash");
            return;
        }
        var conductorName = data.Length >= 14 ? data[13] : string.Empty;
        var localScript = LuaScriptCatalog.Find(Plugin.Config, scriptName);
        if (!LuaTrustedBundlePolicy.TrySelectInstalled(
                localScript,
                scriptHash,
                modules,
                requiredResources,
                data[12],
                out var trustedScript,
                out var trustError)) {
            DalamudApi.PluginLog.Warning($"[Lua] rejected local IPC run '{scriptName}': {trustError}");
            DalamudApi.ChatGui.PrintError($"[MoP] Lua run rejected: {trustError}.");
            return;
        }
        var localCid = DalamudApi.PlayerState.ContentId;
        if (localCid == 0 || !participantCids.Contains(localCid))
            return;
        _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
            Plugin.LuaScriptManager.StartScript(
                trustedScript.Name,
                trustedScript.Hash,
                trustedScript.Source,
                targetObjectId,
                targetEntityId,
                targetName,
                participantCids,
                startUtcTicks,
                seed,
                variables,
                modules: trustedScript.Modules,
                requiredResources: trustedScript.RequiredResources,
                declaredCapabilities: trustedScript.DeclaredCapabilities,
                conductorName: conductorName));
    }

    [IpcHandle(IpcMessageType.StopLuaScript)]
    private void HandleStopLuaScript(IpcMessage message) {
        var selector = message.StringData?.FirstOrDefault();
        _ = DalamudApi.Framework.RunOnFrameworkThread(() => {
            Plugin.LuaScriptManager.Stop(
                string.IsNullOrWhiteSpace(selector) ? null : selector,
                "stopped by operator",
                out _);
        });
    }

    [IpcHandle(IpcMessageType.PauseLuaScript)]
    private void HandlePauseLuaScript(IpcMessage message) {
        var selector = message.StringData?.FirstOrDefault();
        _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
            Plugin.LuaScriptManager.Pause(string.IsNullOrWhiteSpace(selector) ? null : selector, out _));
    }

    [IpcHandle(IpcMessageType.ResumeLuaScript)]
    private void HandleResumeLuaScript(IpcMessage message) {
        var selector = message.StringData?.FirstOrDefault();
        _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
            Plugin.LuaScriptManager.Resume(string.IsNullOrWhiteSpace(selector) ? null : selector, out _));
    }

    private static bool TryParseCids(string serialized, out IReadOnlyList<ulong> cids) {
        var parsed = new List<ulong>();
        foreach (var value in serialized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid)) {
                cids = Array.Empty<ulong>();
                return false;
            }
            parsed.Add(cid);
        }
        cids = parsed;
        return parsed.Count > 0;
    }

    private static (ulong ObjectId, uint EntityId, string Name) CaptureRunAnchor() {
        var target = DalamudApi.TargetManager.Target;
        var localPlayer = DalamudApi.ObjectTable.LocalPlayer;
        if (target == null) {
            if (localPlayer == null)
                return (0, 0, MacroRuntimeVariables.FromCurrentGameState().Me);

            return (
                localPlayer.GameObjectId,
                localPlayer.EntityId,
                FormationCharacterName.FormatPlayerNameWorld(
                    DalamudApi.PlayerState.CharacterName,
                    DalamudApi.PlayerState.HomeWorld.ValueNullable?.Name.ToString(),
                    localPlayer.Name.TextValue));
        }

        var isSelf = localPlayer != null
            && (target.GameObjectId == localPlayer.GameObjectId || target.Address == localPlayer.Address);
        var playerName = isSelf
            ? FormationCharacterName.FormatPlayerNameWorld(
                DalamudApi.PlayerState.CharacterName,
                DalamudApi.PlayerState.HomeWorld.ValueNullable?.Name.ToString(),
                target.Name.TextValue)
            : target.GetPlayerNameWorld();
        return (target.GameObjectId, target.EntityId, playerName ?? target.Name.TextValue);
    }

    internal static string EncodeRunTargetToken(string runTargetName) =>
        string.IsNullOrWhiteSpace(runTargetName)
            ? "-"
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(runTargetName));

    internal static string ResolveChatSyncedRunTarget(string runTargetName, string senderName) =>
        string.IsNullOrWhiteSpace(runTargetName)
            ? senderName?.Trim() ?? string.Empty
            : runTargetName.Trim();

    internal static bool TryDecodeRunTargetToken(string token, out string runTargetName) {
        if (token == "-") {
            runTargetName = string.Empty;
            return true;
        }

        try {
            runTargetName = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return true;
        } catch (FormatException) {
            runTargetName = string.Empty;
            return false;
        }
    }

    internal static string EncodeLuaVariablesToken(IReadOnlyDictionary<string, string> variables) {
        if (variables.Count == 0)
            return "-";
        var serialized = variables.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase).JsonSerialize();
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(serialized));
    }

    internal static bool TryDecodeLuaVariablesToken(
        string token,
        out IReadOnlyDictionary<string, string> variables) {
        if (token == "-") {
            variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return true;
        }

        try {
            var serialized = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parsed = serialized.JsonDeserialize<Dictionary<string, string>>()
                ?? new Dictionary<string, string>();
            variables = new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
            return true;
        } catch (Exception ex) when (ex is FormatException || ex is Newtonsoft.Json.JsonException) {
            variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return false;
        }
    }

    internal static string EncodeLuaModulesToken(IReadOnlyDictionary<string, string> modules) {
        if (modules == null || modules.Count == 0)
            return "-";
        var normalized = LuaModuleManifest.NormalizeAndValidate(modules);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized.JsonSerialize()));
    }

    internal static bool TryDecodeLuaModulesToken(
        string token,
        out IReadOnlyDictionary<string, string> modules) {
        if (token == "-") {
            modules = new Dictionary<string, string>(StringComparer.Ordinal);
            return true;
        }

        try {
            var bytes = Convert.FromBase64String(token);
            if (bytes.Length > LuaModuleManifest.MaximumTotalSourceLength * 2)
                throw new InvalidDataException("Lua module payload is too large.");
            var parsed = Encoding.UTF8.GetString(bytes).JsonDeserialize<Dictionary<string, string>>()
                ?? new Dictionary<string, string>();
            modules = LuaModuleManifest.NormalizeAndValidate(parsed);
            return true;
        } catch (Exception ex) when (ex is FormatException
            or Newtonsoft.Json.JsonException
            or ArgumentException
            or InvalidDataException) {
            modules = new Dictionary<string, string>(StringComparer.Ordinal);
            return false;
        }
    }

    internal static string EncodeLuaResourcesToken(LuaResourceKind? resources) =>
        resources.HasValue
            ? ((int)LuaResourceKinds.ValidateMask(resources.Value)).ToString(CultureInfo.InvariantCulture)
            : "-";

    internal static bool TryDecodeLuaResourcesToken(string token, out LuaResourceKind? resources) {
        if (token == "-") {
            resources = null;
            return true;
        }
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw)) {
            resources = null;
            return false;
        }
        try {
            resources = LuaResourceKinds.ValidateMask((LuaResourceKind)raw);
            return true;
        } catch (ArgumentOutOfRangeException) {
            resources = null;
            return false;
        }
    }

    internal static string EncodeLuaChatSyncEnvelope(LuaChatSyncEnvelope envelope) {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8, leaveOpen: true)) {
            writer.Write((byte)8);
            writer.Write(envelope.StartUtcTicks);
            writer.Write(envelope.StartServerTimeSeconds);
            writer.Write(envelope.Seed);
            writer.Write(Convert.FromHexString(envelope.BundleHash)[..16]);
            writer.Write(envelope.RequiredResources.HasValue);
            if (envelope.RequiredResources.HasValue)
                writer.Write((int)LuaResourceKinds.ValidateMask(envelope.RequiredResources.Value));
            writer.Write(Guid.Parse(envelope.MessageId).ToByteArray());
            writer.Write(envelope.CreatedUnixMilliseconds);
            writer.Write(envelope.RunTargetName ?? string.Empty);
            writer.Write(envelope.RunTargetEntityId);
            var vars = envelope.Variables
                .Where(pair => !pair.Key.Equals("anchor", StringComparison.OrdinalIgnoreCase) || !string.Equals(pair.Value, envelope.RunTargetName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            writer.Write((ushort)vars.Count);
            foreach (var (key, value) in vars) {
                writer.Write(key);
                writer.Write(value ?? string.Empty);
            }
            writer.Write((ushort)envelope.ParticipantCids.Count);
            ulong prev = 0;
            foreach (var cid in envelope.ParticipantCids) {
                writer.Write7BitEncodedInt64((long)cid - (long)prev);
                prev = cid;
            }
        }
        return Convert.ToBase64String(compressed.ToArray());
    }

    internal static bool TryDecodeLuaChatSyncEnvelope(string token, out LuaChatSyncEnvelope envelope) {
        try {
            var compressedBytes = Convert.FromBase64String(token);
            if (compressedBytes.Length > 1024)
                throw new InvalidDataException("Lua sync envelope is too large.");
            using var compressed = new MemoryStream(compressedBytes);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            var buffer = new byte[1024];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0) {
                decompressed.Write(buffer, 0, read);
                if (decompressed.Length > 16 * 1024)
                    throw new InvalidDataException("Lua sync envelope expands beyond its limit.");
            }
            decompressed.Position = 0;
            using var reader = new BinaryReader(decompressed, Encoding.UTF8);
            var version = reader.ReadByte();
            if (version is < 1 or > 8)
                throw new InvalidDataException("Unsupported Lua sync envelope version.");
            var startUtcTicks = reader.ReadInt64();
            var startServerTimeSeconds = version >= 2 ? reader.ReadInt64() : 0;
            var seed = reader.ReadInt32();
            string hash;
            string dependencyManifestHash;
            string bundleHash;
            if (version >= 8) {
                hash = string.Empty;
                dependencyManifestHash = string.Empty;
                bundleHash = Convert.ToHexString(reader.ReadBytes(16)).ToLowerInvariant();
            } else if (version >= 7) {
                hash = string.Empty;
                dependencyManifestHash = string.Empty;
                bundleHash = Convert.ToHexString(reader.ReadBytes(32)).ToLowerInvariant();
            } else {
                hash = Convert.ToHexString(reader.ReadBytes(32)).ToLowerInvariant();
                dependencyManifestHash = version >= 3
                    ? Convert.ToHexString(reader.ReadBytes(32)).ToLowerInvariant()
                    : LuaModuleManifest.ComputeHash(null);
                bundleHash = version >= 5
                    ? Convert.ToHexString(reader.ReadBytes(32)).ToLowerInvariant()
                    : string.Empty;
            }
            LuaResourceKind? requiredResources = null;
            if (version >= 4) {
                var hasRequiredResources = reader.ReadBoolean();
                if (hasRequiredResources)
                    requiredResources = LuaResourceKinds.ValidateMask((LuaResourceKind)reader.ReadInt32());
            }
            var messageId = version >= 6
                ? new Guid(reader.ReadBytes(16)).ToString("D")
                : string.Empty;
            var createdUnixMilliseconds = version >= 6 ? reader.ReadInt64() : 0;
            var runTargetName = reader.ReadString();
            var runTargetEntityId = version >= 2 ? reader.ReadUInt32() : 0;
            var variableCount = reader.ReadUInt16();
            if (variableCount > 64)
                throw new InvalidDataException("Too many Lua variables.");
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < variableCount; index++) {
                var key = reader.ReadString();
                var value = reader.ReadString();
                if (key.Length is 0 or > 100 || value.Length > 1000)
                    throw new InvalidDataException("Invalid Lua variable.");
                variables[key] = value;
            }
            if (version >= 8 && !string.IsNullOrWhiteSpace(runTargetName) && !variables.ContainsKey("anchor")) {
                variables["anchor"] = runTargetName;
            }
            var participantCount = reader.ReadUInt16();
            if (participantCount > 256)
                throw new InvalidDataException("Invalid Lua participant count.");
            var participantCids = new List<ulong>(participantCount);
            if (version >= 7) {
                ulong current = 0;
                for (var index = 0; index < participantCount; index++) {
                    var delta = reader.Read7BitEncodedInt64();
                    current = (ulong)((long)current + delta);
                    participantCids.Add(current);
                }
            } else {
                for (var index = 0; index < participantCount; index++)
                    participantCids.Add(reader.ReadUInt64());
            }
            if (decompressed.Position != decompressed.Length)
                throw new InvalidDataException("Unexpected Lua sync envelope data.");

            envelope = new LuaChatSyncEnvelope {
                MessageId = messageId,
                CreatedUnixMilliseconds = createdUnixMilliseconds,
                StartUtcTicks = startUtcTicks,
                StartServerTimeSeconds = startServerTimeSeconds,
                Seed = seed,
                ScriptHash = hash,
                DependencyManifestHash = dependencyManifestHash,
                BundleHash = bundleHash,
                RequiredResources = requiredResources,
                RunTargetName = runTargetName,
                RunTargetEntityId = runTargetEntityId,
                Variables = variables,
                ParticipantCids = participantCids,
            };
            var validBundleHashLength = version >= 8 ? 32 : 64;
            if (envelope.StartUtcTicks <= 0
                || (version >= 2 && envelope.StartServerTimeSeconds <= 0)
                || (version < 7 && (envelope.ScriptHash.Length != 64 || envelope.ScriptHash.Any(character => !Uri.IsHexDigit(character))))
                || (version < 7 && (envelope.DependencyManifestHash.Length != 64 || envelope.DependencyManifestHash.Any(character => !Uri.IsHexDigit(character))))
                || version < 6
                || !Guid.TryParse(envelope.MessageId, out var messageGuid)
                || messageGuid == Guid.Empty
                || envelope.CreatedUnixMilliseconds <= 0
                || envelope.BundleHash.Length != validBundleHashLength
                || envelope.BundleHash.Any(character => !Uri.IsHexDigit(character))
                || envelope.ParticipantCids.Count > 256
                || envelope.ParticipantCids.Any(cid => cid == 0)
                || envelope.ParticipantCids.Distinct().Count() != envelope.ParticipantCids.Count) {
                envelope = new LuaChatSyncEnvelope();
                return false;
            }
            return true;
        } catch {
            envelope = new LuaChatSyncEnvelope();
            return false;
        }
    }

    private static Dictionary<string, string> ResolveLuaVariables(
        LuaScriptDefinition script,
        Dictionary<string, string>? inlineVariables) {
        var runtime = MacroRuntimeVariables.FromCurrentGameState();
        var variables = new Dictionary<string, string>(runtime.ToDictionary(), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ResolveSharedLuaVariables(script, inlineVariables))
            variables[key] = value;
        return variables;
    }

    private static Dictionary<string, string> ResolveSharedLuaVariables(
        LuaScriptDefinition script,
        Dictionary<string, string>? inlineVariables) {
        var runtime = MacroRuntimeVariables.FromCurrentGameState();
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(script.Variables)) {
            foreach (var (key, value) in Command.ExtractVariables(Command.PreprocessLines(script.Variables)))
                variables[key] = value;
        }
        foreach (var parameter in script.Parameters ?? [])
            if (!variables.ContainsKey(parameter.Name) && parameter.DefaultValue.Length > 0)
                variables[parameter.Name] = parameter.DefaultValue;
        foreach (var (key, value) in runtime.ResolveInlinePlaceholders(inlineVariables))
            variables[key] = value;
        foreach (var parameter in script.Parameters ?? [])
            parameter.ValidateValue(variables.GetValueOrDefault(parameter.Name) ?? string.Empty);
        return variables;
    }

    private static IReadOnlyDictionary<string, string> AddLocalRuntimeVariables(
        IReadOnlyDictionary<string, string> sharedVariables) {
        var result = new Dictionary<string, string>(
            MacroRuntimeVariables.FromCurrentGameState().ToDictionary(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in sharedVariables)
            result[key] = value;
        return result;
    }

    internal static bool TryResolveParticipantGroup(
        IReadOnlyList<CidGroup> groups,
        IReadOnlyDictionary<string, string> variables,
        out string groupName,
        out IReadOnlyList<ulong> participantCids,
        out string error) {
        groupName = variables.FirstOrDefault(pair => pair.Key.Equals("group", StringComparison.OrdinalIgnoreCase)).Value?.Trim()
            ?? string.Empty;
        if (groupName.Length == 0) {
            participantCids = Array.Empty<ulong>();
            error = "Synchronized Lua requires -var=$group=\"Group Name\".";
            return false;
        }

        var requestedGroupName = groupName;
        var group = groups.FirstOrDefault(candidate =>
            candidate.Name.Equals(requestedGroupName, StringComparison.OrdinalIgnoreCase));
        if (group == null) {
            participantCids = Array.Empty<ulong>();
            error = $"Character group '{groupName}' was not found.";
            return false;
        }

        groupName = group.Name;
        participantCids = group.Cids.Where(cid => cid != 0).Distinct().ToArray();
        if (participantCids.Count == 0) {
            error = $"Character group '{groupName}' has no characters.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private IReadOnlyList<ulong> CompactVisibleLaunchRoster(IReadOnlyList<ulong> roster) {
        var alwaysInclude = GetFreshPeerCharacterData()
            .Select(peer => peer.ContentId)
            .Append(DalamudApi.PlayerState.ContentId)
            .Where(cid => cid != 0)
            .ToHashSet();
        return LuaParticipantResolver.CompactVisible(
            roster,
            LuaParticipantResolver.CharacterNames(Plugin.Config.Characters),
            alwaysInclude);
    }
}

internal sealed class LuaChatSyncEnvelope {
    public string MessageId { get; set; } = string.Empty;
    public long CreatedUnixMilliseconds { get; set; }
    public long StartUtcTicks { get; set; }
    public long StartServerTimeSeconds { get; set; }
    public int Seed { get; set; }
    public string ScriptHash { get; set; } = string.Empty;
    public string DependencyManifestHash { get; set; } = LuaModuleManifest.ComputeHash(null);
    public string BundleHash { get; set; } = string.Empty;
    public LuaResourceKind? RequiredResources { get; set; }
    public string RunTargetName { get; set; } = string.Empty;
    public uint RunTargetEntityId { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ulong> ParticipantCids { get; set; } = new();
}
