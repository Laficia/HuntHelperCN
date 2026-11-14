[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'HuntHelper/HuntHelper.csproj'
$devDirectory = Join-Path $repoRoot 'obj/dev/HuntHelperCN'

# Dalamud's saved watcher reloads this exact DLL after a successful rebuild.
& dotnet build $projectPath -c Release -t:Rebuild --nologo "-p:OutputPath=$devDirectory/"
if ($LASTEXITCODE -ne 0) { throw 'Developer build failed; inspect the build output before testing.' }
Write-Host "Developer DLL: $(Join-Path $devDirectory 'HuntHelperCN.dll')"
