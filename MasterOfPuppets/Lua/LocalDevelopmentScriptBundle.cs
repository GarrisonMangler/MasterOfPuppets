using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using MasterOfPuppets.LuaScripting.Runs;

namespace MasterOfPuppets.LuaScripting;

internal static class LocalDevelopmentScriptBundle {
    private const string ManifestResource = "MasterOfPuppets.LocalScripts.bundle.json";
    private const string ScriptResourcePrefix = "MasterOfPuppets.LocalScripts.";

    public static bool Apply(Configuration configuration) {
        var assembly = typeof(LocalDevelopmentScriptBundle).Assembly;
        using var stream = assembly.GetManifestResourceStream(ManifestResource);
        if (stream == null)
            return false;

        var manifest = JsonSerializer.Deserialize<BundleManifest>(stream, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("The local development script bundle manifest is empty.");
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported local script bundle schema {manifest.SchemaVersion}.");

        configuration.LuaScripts ??= [];
        var changed = false;
        foreach (var entry in manifest.Scripts ?? []) {
            entry.Validate();
            var source = ReadResource(assembly, entry.File);
            var candidate = entry.CreateDefinition(source);
            candidate.Validate();

            var existing = configuration.LuaScripts.FirstOrDefault(script =>
                script.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null) {
                configuration.LuaScripts.Add(candidate);
                changed = true;
                continue;
            }

            if (existing.Hash.Equals(candidate.Hash, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!entry.Overwrite
                && !(entry.PreviousHashes ?? []).Contains(existing.Hash, StringComparer.OrdinalIgnoreCase)) {
                DalamudApi.PluginLog.Warning(
                    $"[LocalScripts] Preserved edited script '{entry.Name}' hash={existing.Hash}.");
                continue;
            }

            existing.Source = candidate.Source;
            existing.Description = candidate.Description;
            existing.Variables = candidate.Variables;
            existing.ParticipantFormation = candidate.ParticipantFormation;
            existing.RequiredResources = candidate.RequiredResources;
            existing.DeclaredCapabilities = candidate.DeclaredCapabilities;
            existing.Revision = Math.Max(1, existing.Revision) + 1;
            changed = true;
        }
        return changed;
    }

    private static string ReadResource(Assembly assembly, string file) {
        var safeName = Path.GetFileName(file);
        if (!safeName.Equals(file, StringComparison.Ordinal) || !safeName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Invalid local script filename '{file}'.");
        using var stream = assembly.GetManifestResourceStream(ScriptResourcePrefix + safeName)
            ?? throw new FileNotFoundException($"Embedded local script '{safeName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class BundleManifest {
        public int SchemaVersion { get; set; }
        public List<BundleEntry>? Scripts { get; set; }
    }

    private sealed class BundleEntry {
        public string Name { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Variables { get; set; } = string.Empty;
        public string ParticipantFormation { get; set; } = string.Empty;
        public LuaResourceKind? RequiredResources { get; set; }
        public List<string>? DeclaredCapabilities { get; set; }
        public List<string>? PreviousHashes { get; set; }
        public bool Overwrite { get; set; }

        public void Validate() {
            if (string.IsNullOrWhiteSpace(Name))
                throw new InvalidDataException("A local script bundle entry requires a name.");
            if (string.IsNullOrWhiteSpace(File))
                throw new InvalidDataException($"Local script '{Name}' requires a filename.");
        }

        public LuaScriptDefinition CreateDefinition(string source) => new() {
            Name = Name,
            Description = Description,
            Variables = Variables,
            ParticipantFormation = ParticipantFormation,
            Source = source,
            RequiredResources = RequiredResources,
            DeclaredCapabilities = DeclaredCapabilities ?? [],
        };
    }
}
