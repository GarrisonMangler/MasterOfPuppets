param(
    [switch]$Index,
    [string]$Archive
)

$ErrorActionPreference = 'Stop'
$forbiddenPaths = @(
    '(?i)(^|/)AGENTS\.md$',
    '(?i)(^|/)\.stignore$',
    '(?i)(^|/)\.stfolder/',
    '(?i)(^|/)(live-backups?|backups?)/',
    '(?i)^MasterOfPuppets/Lua/LocalScripts/',
    '(?i)^MasterOfPuppets/Lua/Scripts/.*\.lua$',
    '(?i)^[^/]+\.(macro|lua|formation|import)\.json$',
    '(?i)^[^/]+\.formation\.txt$',
    '(?i)^[^/]+\.(bar\.json|bar\.blob\.txt|sharecode\.txt|blob\.txt)$',
    '(?i)(^|/)character-assignments\.csv$'
)
$forbiddenContent = @(
    '(?i)C:\\Users\\[^\\\s]+',
    '(?i)/Users/[^/\s]+',
    '(?i)/home/[^/\s]+',
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
)
$textExtensions = @(
    '.cs', '.csproj', '.json', '.md', '.txt', '.xml', '.yml', '.yaml',
    '.ps1', '.py', '.js', '.props', '.targets', '.sln', '.gitignore', '.sh'
)
$violations = [System.Collections.Generic.List[string]]::new()

function Test-PublicEntry([string]$name, [string]$content) {
    $normalized = $name.Replace('\', '/')
    foreach ($pattern in $forbiddenPaths) {
        if ($normalized -match $pattern) {
            $violations.Add("forbidden path: $normalized")
            break
        }
    }
    if ([string]::IsNullOrEmpty($content) -or $normalized -eq 'tools/Audit-PublicTree.ps1') { return }
    foreach ($pattern in $forbiddenContent) {
        if ($content -match $pattern) {
            $violations.Add("forbidden content in: $normalized")
            break
        }
    }
}

if ($Archive) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolvedArchive = (Resolve-Path -LiteralPath $Archive).Path
    $zip = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchive)
    try {
        foreach ($entry in $zip.Entries) {
            $extension = [IO.Path]::GetExtension($entry.FullName).ToLowerInvariant()
            $content = ''
            if ($textExtensions -contains $extension -or $entry.FullName.EndsWith('.gitignore')) {
                $reader = [IO.StreamReader]::new($entry.Open())
                try { $content = $reader.ReadToEnd() } finally { $reader.Dispose() }
            }
            Test-PublicEntry $entry.FullName $content
        }
    } finally {
        $zip.Dispose()
    }
} else {
    $paths = @(git ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    foreach ($path in $paths) {
        $extension = [IO.Path]::GetExtension($path).ToLowerInvariant()
        $content = ''
        if ($textExtensions -contains $extension -or $path.EndsWith('.gitignore')) {
            if ($Index) {
                $content = (git show ":$path") -join "`n"
                if ($LASTEXITCODE -ne 0) { throw "Could not read staged content: $path" }
            } else {
                $content = Get-Content -LiteralPath $path -Raw
            }
        }
        Test-PublicEntry $path $content
    }
}

if ($violations.Count -gt 0) {
    $violations | Sort-Object -Unique | ForEach-Object { Write-Host "ERROR: $_" -ForegroundColor Red }
    exit 1
}

Write-Host 'Public tree audit passed.'
