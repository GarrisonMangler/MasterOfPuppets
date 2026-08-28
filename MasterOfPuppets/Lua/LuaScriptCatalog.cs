using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using MasterOfPuppets.Extensions;

namespace MasterOfPuppets.LuaScripting;

internal static class LuaScriptCatalog {
    internal const int MaximumImportJsonBytes = 1024 * 1024;
    internal const int MaximumEncodedImportLength = 2 * 1024 * 1024;
    public const string DefaultBeeSwarmName = "Bee Swarm";
    private const string DefaultBeeSwarmFileName = "bee_swarm.lua";
    private const string PreviousBeeSwarmHash = "a311f2dce8840c78e04103309835e0ea87ed51998e0dd99666cdbd711117054e";
    private const string DefaultBeeSwarmVariables = "$radius = 1.45\n$spread = 0.38\n$speed = 1.0";
    public const string DefaultSixteenVoicesName = "Sixteen Voices";
    private const string DefaultSixteenVoicesFileName = "sixteen_voices.lua";
    public const string DefaultDynamicCongaName = "Dynamic Conga Line";
    private const string DefaultDynamicCongaFileName = "dynamic_conga.lua";
    public const string DefaultSwirlingVortexName = "Swirling Vortex";
    private const string DefaultSwirlingVortexFileName = "swirling_vortex.lua";
    public const string DefaultTripleRingVortexName = "Swirling Vortex - Triple Ring";
    private const string DefaultTripleRingVortexFileName = "swirling_vortex_triple_ring.lua";
    public const string DefaultEventDrivenCurtainCallName = "Event-Driven Curtain Call";
    private const string DefaultEventDrivenCurtainCallFileName = "event_driven_curtain_call.lua";
    private const string FirstSwirlingVortexHash = "b5f3d50bf92f71c22193f4a34f2970662d1e7934a0a1ee5834450055f7d9a9c0";
    private const string PreviousSwirlingVortexHash = "acbf6c84d72815215a78a8863777883b17ac491151ee00f7d90535a9b73586df";
    private const string CurrentSwirlingVortexHash = "3b7919ae9719319566f8661caa5504e7ae8315c7c5cccd3e9a0a85a10c6b1a21";
    private const string DefaultSwirlingVortexFormation = "Swarm - Honeycomb Hive (Wide)";
    private const string DefaultSwirlingVortexVariables = "$inner = 1.50\n$outer = 2.80\n$speed = 1.00\n$spacing = 0.80\n$stage = 0.0\n$ramp = 1.0";
    private const string DefaultTripleRingVortexVariables = "$radius = 1.60\n$spread = 0.75\n$pace = 2.70\n$stage = 7.0\n$ramp = 2.5";

    public static bool EnsureDefaults(Configuration configuration) {
        configuration.LuaScripts ??= new();
        configuration.Formations ??= new();
        var changed = false;
        changed |= EnsureDefault(
            configuration,
            DefaultBeeSwarmName,
            DefaultBeeSwarmFileName,
            "Fast, smooth arcs, loops, and spiral motion around a selected target.",
            DefaultBeeSwarmVariables);
        changed |= EnsureDefault(
            configuration,
            DefaultSixteenVoicesName,
            DefaultSixteenVoicesFileName,
            "A synchronized, pre-written conversation performed by the Artemis and Kazuko bands.");
        changed |= EnsureDefault(
            configuration,
            DefaultDynamicCongaName,
            DefaultDynamicCongaFileName,
            "A target-led Lua conga with dynamic fallback and automatic chain repair for sixteen performers.");
        changed |= EnsureDefault(
            configuration,
            DefaultSwirlingVortexName,
            DefaultSwirlingVortexFileName,
            "A tight pair of rings around a selected target. Visible roster members are compacted into even spacing; walkers inside, runners outside.",
            DefaultSwirlingVortexVariables,
            configuration.Formations.Any(formation => formation.Name.Equals(
                DefaultSwirlingVortexFormation,
                StringComparison.OrdinalIgnoreCase))
                    ? DefaultSwirlingVortexFormation
                    : string.Empty);
        changed |= EnsureDefault(
            configuration,
            DefaultTripleRingVortexName,
            DefaultTripleRingVortexFileName,
            "Three synchronized concentric walking rings with one shared target and equal ground pace.",
            DefaultTripleRingVortexVariables,
            configuration.Formations.Any(formation => formation.Name.Equals(
                DefaultSwirlingVortexFormation,
                StringComparison.OrdinalIgnoreCase))
                    ? DefaultSwirlingVortexFormation
                    : string.Empty);
        changed |= EnsureDefault(
            configuration,
            DefaultEventDrivenCurtainCallName,
            DefaultEventDrivenCurtainCallFileName,
            "Waits for a typed /say cue, optionally composes a saved formation and macro, then gives a synchronized response.",
            "$cue = places everyone\n$macro =\n$formation =");
        changed |= UpgradeUnmodifiedBeeSwarm(configuration);
        changed |= UpgradeUnmodifiedSwirlingVortex(configuration);
        changed |= AssignDefaultSwirlingVortexFormation(configuration);
        foreach (var script in configuration.LuaScripts)
            changed |= script.MigrateMetadata();
        return changed;
    }

    private static bool EnsureDefault(
        Configuration configuration,
        string name,
        string fileName,
        string description,
        string variables = "",
        string participantFormation = "") {
        if (configuration.LuaScripts.Any(script =>
                script.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return false;

        var source = LoadPackagedScript(fileName);
        if (string.IsNullOrWhiteSpace(source)) {
            DalamudApi.PluginLog.Warning($"[Lua] packaged default script was not found: {fileName}");
            return false;
        }

        configuration.LuaScripts.Add(new LuaScriptDefinition {
            Name = name,
            Description = description,
            Variables = variables,
            ParticipantFormation = participantFormation,
            Source = source,
        });
        return true;
    }

    private static bool UpgradeUnmodifiedBeeSwarm(Configuration configuration) {
        var script = configuration.LuaScripts.FirstOrDefault(candidate =>
            candidate.Name.Equals(DefaultBeeSwarmName, StringComparison.OrdinalIgnoreCase));
        if (script == null || !script.Hash.Equals(PreviousBeeSwarmHash, StringComparison.OrdinalIgnoreCase))
            return false;

        script.Source = LoadPackagedScript(DefaultBeeSwarmFileName);
        if (string.IsNullOrWhiteSpace(script.Variables))
            script.Variables = DefaultBeeSwarmVariables;
        return true;
    }

    private static bool UpgradeUnmodifiedSwirlingVortex(Configuration configuration) {
        var script = configuration.LuaScripts.FirstOrDefault(candidate =>
            candidate.Name.Equals(DefaultSwirlingVortexName, StringComparison.OrdinalIgnoreCase));
        if (script == null)
            return false;

        var packaged = LoadPackagedScript(DefaultSwirlingVortexFileName);
        if (string.Equals(script.Source, packaged, StringComparison.Ordinal))
            return false;

        var unmodified = script.Hash.Equals(FirstSwirlingVortexHash, StringComparison.OrdinalIgnoreCase)
            || script.Hash.Equals(PreviousSwirlingVortexHash, StringComparison.OrdinalIgnoreCase)
            || script.Hash.Equals(CurrentSwirlingVortexHash, StringComparison.OrdinalIgnoreCase)
            || script.Source.Contains("local roster = mop.get_group(group_name)", StringComparison.Ordinal)
            || script.Source.Contains("requested_inner", StringComparison.Ordinal)
            || script.Source.Contains("visible_circle", StringComparison.Ordinal);
        if (!unmodified)
            return false;

        script.Source = packaged;
        script.Variables = DefaultSwirlingVortexVariables;
        script.Description = "Two concentric rings around a selected target: walking members inside, running members outside, spaced by the live roster.";
        return true;
    }

    private static bool AssignDefaultSwirlingVortexFormation(Configuration configuration) {
        var script = configuration.LuaScripts.FirstOrDefault(candidate =>
            candidate.Name.Equals(DefaultSwirlingVortexName, StringComparison.OrdinalIgnoreCase));
        if (script == null
            || !string.IsNullOrWhiteSpace(script.ParticipantFormation)
            || !configuration.Formations.Any(formation => formation.Name.Equals(
                DefaultSwirlingVortexFormation,
                StringComparison.OrdinalIgnoreCase)))
            return false;

        script.ParticipantFormation = DefaultSwirlingVortexFormation;
        return true;
    }

    public static LuaScriptDefinition? Find(Configuration configuration, string name) {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var normalized = name.Trim();
        return configuration.LuaScripts?.FirstOrDefault(script =>
            script.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || (normalized.Equals(DefaultBeeSwarmFileName, StringComparison.OrdinalIgnoreCase)
                && script.Name.Equals(DefaultBeeSwarmName, StringComparison.OrdinalIgnoreCase))
            || (normalized.Equals(DefaultSixteenVoicesFileName, StringComparison.OrdinalIgnoreCase)
                && script.Name.Equals(DefaultSixteenVoicesName, StringComparison.OrdinalIgnoreCase))
            || (normalized.Equals(DefaultDynamicCongaFileName, StringComparison.OrdinalIgnoreCase)
                && script.Name.Equals(DefaultDynamicCongaName, StringComparison.OrdinalIgnoreCase))
            || (normalized.Equals(DefaultSwirlingVortexFileName, StringComparison.OrdinalIgnoreCase)
                && script.Name.Equals(DefaultSwirlingVortexName, StringComparison.OrdinalIgnoreCase))
            || (normalized.Equals(DefaultTripleRingVortexFileName, StringComparison.OrdinalIgnoreCase)
                && script.Name.Equals(DefaultTripleRingVortexName, StringComparison.OrdinalIgnoreCase))
            || (normalized.Equals(DefaultEventDrivenCurtainCallFileName, StringComparison.OrdinalIgnoreCase)
                && script.Name.Equals(DefaultEventDrivenCurtainCallName, StringComparison.OrdinalIgnoreCase)));
    }

    public static string Export(LuaScriptDefinition script) {
        ArgumentNullException.ThrowIfNull(script);
        return script.Clone().JsonSerialize().Compress();
    }

    public static LuaScriptDefinition Import(string text) {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Lua script import is empty.");

        text = text.Trim();
        if (text.Length > MaximumEncodedImportLength)
            throw new ArgumentException("Lua script import is too large.");
        var json = text.StartsWith('{') ? text : DecompressImport(text);
        if (Encoding.UTF8.GetByteCount(json) > MaximumImportJsonBytes)
            throw new ArgumentException("Lua script import expands beyond its size limit.");
        var script = json.JsonDeserialize<LuaScriptDefinition>()
            ?? throw new ArgumentException("Invalid Lua script data.");
        script.Validate();
        return script;
    }

    private static string DecompressImport(string text) {
        byte[] compressed;
        try {
            compressed = Convert.FromBase64String(text);
        } catch (FormatException exception) {
            throw new ArgumentException("Invalid Lua script share code.", nameof(text), exception);
        }
        if (compressed.Length > MaximumImportJsonBytes)
            throw new ArgumentException("Lua script import is too large.", nameof(text));

        try {
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0) {
                output.Write(buffer, 0, read);
                if (output.Length > MaximumImportJsonBytes)
                    throw new ArgumentException("Lua script import expands beyond its size limit.", nameof(text));
            }
            return new UTF8Encoding(false, true).GetString(output.ToArray());
        } catch (InvalidDataException exception) {
            throw new ArgumentException("Invalid compressed Lua script share code.", nameof(text), exception);
        } catch (DecoderFallbackException exception) {
            throw new ArgumentException("Lua script share code is not valid UTF-8.", nameof(text), exception);
        }
    }

    public static string LoadPackagedScript(string fileName) {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.Contains('/')
            || fileName.Contains('\\'))
            throw new ArgumentException("A packaged Lua script file name is required.", nameof(fileName));

        var assemblyDirectory = Path.GetDirectoryName(typeof(LuaScriptCatalog).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
            assemblyDirectory = DalamudApi.PluginInterface?.AssemblyLocation.DirectoryName;
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
            throw new DirectoryNotFoundException("Plugin assembly directory is unavailable.");
        var scriptsDirectory = Path.GetFullPath(Path.Combine(assemblyDirectory, "Scripts"));
        var scriptPath = Path.GetFullPath(Path.Combine(scriptsDirectory, fileName));
        if (!scriptPath.StartsWith(scriptsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Packaged Lua script path escaped its directory.");

        return File.ReadAllText(scriptPath);
    }
}
