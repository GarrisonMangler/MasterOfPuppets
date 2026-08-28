using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

using MasterOfPuppets.Formations;
using MasterOfPuppets.Ipc;
using MasterOfPuppets.LuaScripting.Synchronization;
using MasterOfPuppets.Util;

using Lumina.Text.ReadOnly;

namespace MasterOfPuppets;

internal class ChatWatcher : IDisposable {
    private Plugin Plugin { get; }
    // private bool _isRegistered;

    public readonly HashSet<XivChatType> AllowedChatTypes = new()
    {
        // XivChatType.Say,
        XivChatType.Party,
        // XivChatType.CrossParty,
        XivChatType.FreeCompany,
        // XivChatType.Alliance,
        XivChatType.Ls1,
        XivChatType.Ls2,
        XivChatType.Ls3,
        XivChatType.Ls4,
        XivChatType.Ls5,
        XivChatType.Ls6,
        XivChatType.Ls7,
        XivChatType.Ls8,
        XivChatType.CrossLinkShell1,
        XivChatType.CrossLinkShell2,
        XivChatType.CrossLinkShell3,
        XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5,
        XivChatType.CrossLinkShell6,
        XivChatType.CrossLinkShell7,
        XivChatType.CrossLinkShell8,
    };

    private readonly Dictionary<string, Action<string[], string>> CommandHandlers;
    private readonly LuaReplayWindow _luaReplayWindow = new();
    internal LuaDistributedSessionRegistry LuaDistributedSessions { get; } = new();
    internal LuaDistributedLaunchController LuaDistributedLaunches { get; }

    public ChatWatcher(Plugin plugin) {
        Plugin = plugin;
        LuaDistributedLaunches = new LuaDistributedLaunchController(plugin, LuaDistributedSessions);
        CommandHandlers = new(StringComparer.OrdinalIgnoreCase) {
            ["moprun"] = HandleRunMacro,
            ["mopstop"] = HandleStopMacroExecution,
            ["mopbr"] = HandleBroadcastCommandExecution,
            ["mopbrn"] = HandleBroadcastNotMeCommandExecution,
            ["mopbrc"] = HandleBroadcastCharacterCommandExecution,
            ["mopbrg"] = HandleBroadcastGroupCommandExecution,
            ["mopformation"] = HandleFormationCommand,
            ["mopluarun"] = HandleLuaRun,
            ["mopluastop"] = HandleLuaStop,
            ["mopluaphase"] = HandleLuaPhase,
        };

        DalamudApi.ChatGui.ChatMessage += OnChatMessage;
        // UpdateRegistration();
    }

    public void Dispose() {
        LuaDistributedLaunches.CancelAll("plugin disposed");
        DalamudApi.ChatGui.ChatMessage -= OnChatMessage;
    }

    // public void UpdateRegistration() {
    //     if (Plugin.Config.UseChatSync && !_isRegistered) {
    //         DalamudApi.ChatGui.ChatMessage += OnChatMessage;
    //         _isRegistered = true;
    //     } else if (!Plugin.Config.UseChatSync && _isRegistered) {
    //         DalamudApi.ChatGui.ChatMessage -= OnChatMessage;
    //         _isRegistered = false;
    //     }
    // }

    // public void Dispose() {
    //     if (_isRegistered) {
    //         DalamudApi.ChatGui.ChatMessage -= OnChatMessage;
    //         _isRegistered = false;
    //     }
    // }

    private void OnChatMessage(IChatMessage message) {
        if (!Plugin.Config.UseChatSync) return;
        if (message.IsHandled)
            return;

        var senderName = GetSenderName(message);

        if (!AllowedChatTypes.Contains(message.LogKind)
            || !Plugin.Config.ListenedChatTypes.Contains(message.LogKind)
            || !IsAllowedSender(senderName)
        ) {
            return;
        }

        var parsedArgs = ArgumentParser.ParseChatArgs(ResolveTextWithIcons(message.Message));
        if (!parsedArgs.Any()) return;

#if DEBUG
        DalamudApi.PluginLog.Debug($"OnChatMessage ({senderName} - {message.LogKind}): [{parsedArgs[0]}]: {string.Join("|", parsedArgs.Skip(1))}");
#endif

        if (CommandHandlers.TryGetValue(parsedArgs[0], out var action)) {
            var suppressInternalLuaEnvelope = IsInternalLuaSyncEnvelope(parsedArgs);
            action.Invoke(parsedArgs.Skip(1).ToArray(), senderName);
            if (suppressInternalLuaEnvelope && message is IHandleableChatMessage handleable)
                handleable.PreventOriginal();
        }
    }

    internal static bool IsInternalLuaSyncEnvelope(IReadOnlyList<string> parsedArgs) {
        return (parsedArgs.Count == 3
                && parsedArgs[0].Equals("mopluarun", StringComparison.OrdinalIgnoreCase)
                && IpcProvider.TryDecodeLuaChatSyncEnvelope(parsedArgs[2], out _))
            || (parsedArgs.Count == 2
                && parsedArgs[0].Equals("mopluaphase", StringComparison.OrdinalIgnoreCase)
                && LuaDistributedWireCodec.TryDecode(parsedArgs[1], out _, out _));
    }

    private void HandleRunMacro(string[] args, string senderName) {
        if (args.Length < 1) {
            DalamudApi.ChatGui.PrintError($"Invalid command arguments expected 1 <macro name>");
            return;
        }

        var inlineVars = args.Length > 1
            ? ArgumentParser.ParseInlineVars(args[1])
            : null;

        if (!string.IsNullOrWhiteSpace(senderName)) {
            inlineVars ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!inlineVars.ContainsKey("mop_origin"))
                inlineVars["mop_origin"] = senderName;
        }

        string macroNameOrIndex = args[0];
        int macroIndex = Plugin.MacroManager.FindMacroIndex(macroNameOrIndex);
        // Keep action templates and their variable context intact. Resolving to strings here
        // would freeze every variable and prevent later ChatSync updates from taking effect.
        Plugin.MacroHandler.ExecuteMacro(macroIndex, inlineVars);
    }

    public void SendChatStopMacroExecution() {
        var message = $"/p mopstop";
        Chat.SendMessage(message);
    }

    private void HandleStopMacroExecution(string[] args, string senderName) {
        Plugin.IpcProvider.StopMacroExecution();
    }

    private void HandleBroadcastCommandExecution(string[] args, string senderName) {
        if (args.Length < 1) {
            DalamudApi.ChatGui.PrintError($"Invalid command arguments expected 1 <command>");
            return;
        }

        var textCommand = string.Join(" ", args);
        if (TryHandleImmediateMacroVariableUpdate(textCommand))
            return;
        Plugin.MacroHandler.EnqueueMacroActions("#mopbr-inline-macro", actions: [textCommand], delayBetweenActions: 0);
    }

    private void HandleBroadcastNotMeCommandExecution(string[] args, string senderName) {
        if (args.Length < 1) {
            DalamudApi.ChatGui.PrintError($"Invalid command arguments expected 1 <command>");
            return;
        }

        var localPlayerName = DalamudApi.PlayerState.CharacterName;
        if (string.Equals(localPlayerName, senderName, StringComparison.OrdinalIgnoreCase)) return;

        var textCommand = string.Join(" ", args);
        if (TryHandleImmediateMacroVariableUpdate(textCommand))
            return;
        Plugin.MacroHandler.EnqueueMacroActions("#mopbrn-inline-macro", actions: [textCommand], delayBetweenActions: 0);
    }

    private void HandleBroadcastCharacterCommandExecution(string[] args, string senderName) {
        if (args.Length < 2) {
            DalamudApi.ChatGui.PrintError($"Invalid command arguments expected 2 \"Character Name\" <command>");
            return;
        }

        var characterName = args[0];
        var textCommand = string.Join(" ", args.Skip(1));
        var localPlayerName = $"{DalamudApi.PlayerState.CharacterName}@{DalamudApi.PlayerState.HomeWorld.Value.Name}";
        if (!localPlayerName.Contains(characterName, StringComparison.InvariantCultureIgnoreCase)) return;

        if (TryHandleImmediateMacroVariableUpdate(textCommand))
            return;
        Plugin.MacroHandler.EnqueueMacroActions("#mopbrc-inline-macro", actions: [textCommand], delayBetweenActions: 0);
    }

    private void HandleBroadcastGroupCommandExecution(string[] args, string senderName) {
        if (args.Length < 2) {
            DalamudApi.ChatGui.PrintError($"Invalid command arguments expected 2 \"Group Name\" <command>");
            return;
        }

        var groupName = args[0];
        var textCommand = string.Join(" ", args.Skip(1));
        bool groupHasCid = Plugin.Config.CidsGroups.Any(group =>
            group.Name.Equals(groupName, StringComparison.InvariantCultureIgnoreCase) &&
            group.Cids.Contains(DalamudApi.PlayerState.ContentId)
        );
        if (!groupHasCid) return;
        if (TryHandleImmediateMacroVariableUpdate(textCommand))
            return;
        Plugin.MacroHandler.EnqueueMacroActions("#mop-inline-macro-group", actions: [textCommand], delayBetweenActions: 0);
    }

    private void HandleLuaRun(string[] args, string senderName) {
        // The short public form mirrors moprun. Only the sender's own client
        // expands it, preventing every listener from emitting a second run.
        if (args.Length is 1 or 2
            && (args.Length == 1 || args[1].StartsWith("-var=", StringComparison.OrdinalIgnoreCase))) {
            if (IsLocalPlayerSender(senderName)) {
                var inlineVariables = args.Length == 2
                    ? ArgumentParser.ParseInlineVars(args[1])
                    : null;
                Plugin.IpcProvider.StartChatSyncedLuaScript(args[0], inlineVariables);
            }
            return;
        }

        if (args.Length != 2
            || !IpcProvider.TryDecodeLuaChatSyncEnvelope(args[1], out var envelope)) {
            DalamudApi.ChatGui.PrintError(
                "Invalid synchronized Lua command envelope.");
            return;
        }

        if (!IsTrustedLuaConductor(senderName, out var trustError)) {
            DalamudApi.PluginLog.Warning($"[LuaSync] rejected run from {senderName}: {trustError}");
            DalamudApi.ChatGui.PrintError($"[MoP] Lua synchronization rejected: {trustError}.");
            return;
        }
        if (!_luaReplayWindow.TryAccept(
                envelope.MessageId,
                envelope.CreatedUnixMilliseconds,
                DateTimeOffset.UtcNow,
                out var replayError)) {
            DalamudApi.PluginLog.Warning($"[LuaSync] rejected message {envelope.MessageId} from {senderName}: {replayError}");
            return;
        }

        Plugin.IpcProvider.StartChatSyncedLuaScriptLocal(
            args[0],
            envelope,
            senderName);
    }

    private void HandleLuaStop(string[] args, string senderName) {
        if (!IsTrustedLuaConductor(senderName, out var reason)) {
            DalamudApi.PluginLog.Warning($"[LuaSync] rejected stop from {senderName}: {reason}");
            DalamudApi.ChatGui.PrintError($"[MoP] Lua stop rejected: {reason}.");
            return;
        }
        Plugin.IpcProvider.StopLuaScript();
    }

    private void HandleLuaPhase(string[] args, string senderName) {
        var decodeError = "expected one compact phase token";
        if (args.Length != 1 || !LuaDistributedWireCodec.TryDecode(args[0], out var envelope, out decodeError)) {
            DalamudApi.PluginLog.Warning($"[LuaSync] rejected phase frame from {senderName}: {decodeError}");
            return;
        }

        var localIdentity = new LuaDistributedSenderIdentity(
            DalamudApi.PlayerState.ContentId,
            FormationCharacterName.FormatPlayerNameWorld(
                DalamudApi.PlayerState.CharacterName,
                DalamudApi.PlayerState.HomeWorld.ValueNullable?.Name.ToString()));
        var configured = Plugin.Config.Characters
            .Where(character => character.Cid != 0)
            .Select(character => new LuaDistributedSenderIdentity(character.Cid, character.Name))
            .ToArray();
        if (!LuaDistributedSenderPolicy.MatchesClaimedContentId(
                senderName,
                envelope.SenderContentId,
                localIdentity,
                configured,
                out var identityError)) {
            DalamudApi.PluginLog.Warning($"[LuaSync] rejected phase sender {senderName}: {identityError}");
            return;
        }

        if ((envelope.Kind is LuaDistributedMessageKind.Prepare or LuaDistributedMessageKind.Go
                or LuaDistributedMessageKind.Stop or LuaDistributedMessageKind.ClockReply
                or LuaDistributedMessageKind.SharedVariable)
            && !IsTrustedLuaConductor(senderName, out var trustError)) {
            DalamudApi.PluginLog.Warning($"[LuaSync] rejected conductor phase from {senderName}: {trustError}");
            return;
        }
        if (!_luaReplayWindow.TryAccept(
                envelope.MessageId.ToString("D"),
                envelope.CreatedUnixMilliseconds,
                DateTimeOffset.UtcNow,
                out var replayError)) {
            DalamudApi.PluginLog.Warning($"[LuaSync] rejected phase replay {envelope.MessageId} from {senderName}: {replayError}");
            return;
        }
        if (!LuaDistributedSessions.TryAccept(
                envelope,
                FormationCharacterName.NormalizeWorldSeparator(senderName),
                DateTimeOffset.UtcNow,
                out var session,
                out var protocolError)) {
            DalamudApi.PluginLog.Warning($"[LuaSync] rejected {envelope.Kind} for {envelope.RunToken}: {protocolError}");
            return;
        }
        DalamudApi.PluginLog.Debug(
            $"[LuaSync] {envelope.Kind} {envelope.RunToken} from {senderName}; "
            + $"phase={session!.Protocol.Phase} ready={session.Protocol.ReadyCount}/{session.Protocol.Participants.Count}");
        LuaDistributedLaunches.OnPhaseAccepted(envelope);
        if (envelope.Kind == LuaDistributedMessageKind.Stop) {
            foreach (var run in Plugin.LuaScriptManager.ActiveRuns.Where(run =>
                         LuaDistributedWireCodec.CreateRunToken(run.RunId).Equals(envelope.RunToken, StringComparison.OrdinalIgnoreCase)))
                Plugin.LuaScriptManager.Stop(run.RunId, envelope.Detail, out _);
        }
    }

    private static bool IsLocalPlayerSender(string senderName) {
        if (DalamudApi.PlayerState.ContentId == 0)
            return false;

        var localName = FormationCharacterName.FormatPlayerNameWorld(
            DalamudApi.PlayerState.CharacterName,
            DalamudApi.PlayerState.HomeWorld.ValueNullable?.Name.ToString());
        return FormationCharacterName.MatchScore(senderName, localName) >= int.MaxValue - 1;
    }

    private bool IsTrustedLuaConductor(string senderName, out string reason) {
        if (IsLocalPlayerSender(senderName)) {
            reason = string.Empty;
            return true;
        }

        return LuaConductorTrustPolicy.IsTrusted(
            senderName,
            FormationCharacterName.FormatPlayerNameWorld(
                DalamudApi.PlayerState.CharacterName,
                DalamudApi.PlayerState.HomeWorld.ValueNullable?.Name.ToString()),
            Plugin.Config.LuaConductorTrustMode,
            Plugin.Config.LuaTrustedConductors,
            out reason);
    }

    /// <summary>
    /// Handles live-variable control messages outside the action queues. This is essential
    /// when the queue being controlled is already occupied by a long-running macro.
    /// </summary>
    private bool TryHandleImmediateMacroVariableUpdate(string textCommand) {
        const string mopPrefix = "/mop ";
        textCommand = textCommand.Trim();
        if (!textCommand.StartsWith(mopPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var parsedArgs = ArgumentParser.ParseCommandArgs(textCommand[mopPrefix.Length..]);
        if (parsedArgs.Count == 0 ||
            (!parsedArgs[0].Equals("setvar", StringComparison.OrdinalIgnoreCase) &&
             !parsedArgs[0].Equals("setvars", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (parsedArgs.Count < 2) {
            DalamudApi.ChatGui.PrintError("Invalid ChatSync variable update. Usage: mopbr /mop setvar -var=$name=value[;$other=value]");
            return true;
        }

        var variables = ArgumentParser.ParseInlineVars(parsedArgs[1]);
        if (variables.Count == 0) {
            DalamudApi.ChatGui.PrintError("ChatSync variable update contained no valid variables.");
            return true;
        }

        int updated = Plugin.MacroHandler.UpdateActiveMacroVariables(variables);
        DalamudApi.PluginLog.Debug($"[ChatSync] Updated {variables.Count} variable(s) on {updated} active macro queue(s)");
        return true;
    }

    private void HandleFormationCommand(string[] args, string senderName) {
        if (args.Length < 1) {
            DalamudApi.ChatGui.PrintError("Invalid command arguments expected 1 <formation name>");
            return;
        }

        var anchor = FormationAnchorArgumentParser.ParseAnchorAndArrival(
            args.Skip(1),
            FormationAnchorReference.Sender);
        if (anchor.InvalidArgument != null) {
            DalamudApi.ChatGui.PrintError($"Invalid command argument: {anchor.InvalidArgument}");
            return;
        }

        if (anchor.Anchor.Kind == FormationAnchorKind.Sender && string.IsNullOrWhiteSpace(anchor.Anchor.Name)) {
            anchor = anchor with { Anchor = anchor.Anchor with { Name = senderName } };
        }

        if (anchor.Anchor.Kind == FormationAnchorKind.FocusTarget) {
            DalamudApi.ChatGui.PrintError(
                "Focus target anchor is not supported for chat-sync formation commands. Use /mopformationmove or /mop formation instead.");
            return;
        }

        var fallbackAnchor = string.IsNullOrWhiteSpace(senderName)
            ? null
            : FormationAnchorReference.Named(senderName);

        _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
            FormationLocalMovementExecutor.ExecuteChatSyncedFormation(
                Plugin,
                args[0],
                anchor.Anchor,
                anchor.MovementMode,
                fallbackAnchor));
    }

    private static string SanitizeSenderName(string raw) {
        var i = 0;
        while (i < raw.Length && !char.IsLetter(raw[i])) i++;
        return i > 0 ? raw[i..] : raw;
    }

    private bool IsAllowedSender(string senderName) {
        if (!Plugin.Config.UseChatCommandSenderWhitelist)
            return true;

        return Plugin.Config.ChatCommandSenderWhitelist.Any(allowed =>
            string.Equals(
                FormationCharacterName.NormalizeWorldSeparator(allowed),
                senderName,
                StringComparison.OrdinalIgnoreCase));
    }

    internal static string GetSenderName(IChatMessage message) {
        var senderName = GetPlayerPayloadSenderName(message.Sender);
        if (!string.IsNullOrWhiteSpace(senderName))
            return senderName;

        senderName = GetExtractedSenderName(message.OriginalSender);
        if (!string.IsNullOrWhiteSpace(senderName))
            return senderName;

        return GetSenderTextName(message.Sender);
    }

    private static string GetPlayerPayloadSenderName(SeString sender) {
        foreach (var payload in sender.Payloads.OfType<PlayerPayload>()) {
            var formattedName = FormationCharacterName.FormatPlayerNameWorld(
                payload.PlayerName,
                payload.World.ValueNullable?.Name.ToString(),
                payload.DisplayedName);

            if (!string.IsNullOrWhiteSpace(formattedName))
                return formattedName;
        }

        return string.Empty;
    }

    private static string GetExtractedSenderName(ReadOnlySeString sender) =>
        FormationCharacterName.NormalizeWorldSeparator(SanitizeSenderName(sender.ExtractText()));

    private static string GetSenderTextName(SeString sender) {
        var senderName = FormationCharacterName.NormalizeWorldSeparator(SanitizeSenderName(ResolveTextWithIcons(sender)));
        return !string.IsNullOrWhiteSpace(senderName)
            ? senderName
            : FormationCharacterName.NormalizeWorldSeparator(SanitizeSenderName(ResolveTextWithIcons(sender)));
    }

    internal static string ResolveTextWithIcons(SeString seString) {
        var sb = new System.Text.StringBuilder();
        foreach (var payload in seString.Payloads) {
            if (payload is TextPayload textPayload) {
                sb.Append(textPayload.Text);
            } else if (payload is IconPayload iconPayload) {
                if (iconPayload.Icon == BitmapFontIcon.CrossWorld) {
                    sb.Append('@');
                }
            }
        }
        return sb.ToString();
    }

    private static string ExtractTextWithIcons(SeString seString) {
        var sb = new System.Text.StringBuilder();
        foreach (var payload in seString.Payloads) {
            if (payload is TextPayload textPayload) {
                sb.Append(textPayload.Text);
            } else if (payload is IconPayload iconPayload) {
                if (Enum.TryParse<SeIconChar>(iconPayload.Icon.ToString(), out var seIconChar)) {
                    sb.Append((char)seIconChar);
                }
            }
        }
        return sb.ToString();
    }
}
