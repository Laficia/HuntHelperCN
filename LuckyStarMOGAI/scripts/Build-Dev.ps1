param([switch]$Rebuild)
$ErrorActionPreference='Stop'
$project=Join-Path (Split-Path $PSScriptRoot -Parent) 'src/LuckyStar.csproj'
$buildArgs=@('build',$project,'-c','Debug')
if($Rebuild){$buildArgs+='-t:Rebuild'}
& dotnet @buildArgs
if($LASTEXITCODE){throw 'LuckyStar developer build failed'}
Write-Output 'Developer DLL updated. Dalamud automatically reloads the registered Debug build.'
