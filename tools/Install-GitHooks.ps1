$ErrorActionPreference = 'Stop'
git config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Git hooks.' }
Write-Host 'Configured core.hooksPath=.githooks'
