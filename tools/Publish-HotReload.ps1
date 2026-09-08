$ErrorActionPreference = 'Stop'

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $workspace 'MasterOfPuppets\MasterOfPuppets.csproj'
$stage = Join-Path $workspace 'artifacts\hotreload-stage'
$release = Join-Path $workspace 'MasterOfPuppets\bin\Release'
$pluginDll = 'MasterOfPuppets.dll'

if (-not $stage.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The staging directory escaped the workspace.'
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage | Out-Null
New-Item -ItemType Directory -Path $release -Force | Out-Null

dotnet build $project -c Release --no-restore -o $stage
if ($LASTEXITCODE -ne 0) {
    throw "Release staging build failed with exit code $LASTEXITCODE."
}

# Publish all dependencies, manifests, and script payloads before the watched
# assembly. Copying the DLL last makes the hot-reload event observe a complete
# and internally consistent plugin directory.
Get-ChildItem -LiteralPath $stage -Recurse -File |
    Where-Object { $_.Name -ne $pluginDll } |
    ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($stage, $_.FullName)
        $destination = Join-Path $release $relative
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }

$stagedDll = Join-Path $stage $pluginDll
if (-not (Test-Path -LiteralPath $stagedDll)) {
    throw "Staged plugin DLL was not produced: $stagedDll"
}
Copy-Item -LiteralPath $stagedDll -Destination (Join-Path $release $pluginDll) -Force

$manifest = Get-Content -LiteralPath (Join-Path $release 'MasterOfPuppets.json') -Raw | ConvertFrom-Json
$publishedDll = Get-Item -LiteralPath (Join-Path $release $pluginDll)
if ($manifest.AssemblyVersion -ne $publishedDll.VersionInfo.FileVersion) {
    throw "Published manifest/DLL version mismatch: $($manifest.AssemblyVersion) vs $($publishedDll.VersionInfo.FileVersion)"
}

# Copy-Item preserves the staged source timestamp. Explicitly stamp the watched
# assembly only after the complete payload and version check are finished so
# this is both the final filesystem mutation and the unambiguous reload signal.
$publishedDll.LastWriteTimeUtc = [DateTime]::UtcNow

Write-Host "Published MasterOfPuppets $($manifest.AssemblyVersion); DLL updated last."
