param([string]$Version='0.0.0.9')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$stage=Join-Path $root "obj/release-v$Version"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
dotnet run --project (Join-Path $root 'tests/Regression.csproj') -c Release
if($LASTEXITCODE){throw 'Regression failed'}
$buildOutput=Join-Path $root "obj/build-$Version-$([Guid]::NewGuid().ToString('N'))"
dotnet build (Join-Path $root 'src/LuckyStar.csproj') -c Release "-p:OutputPath=$buildOutput/"
if($LASTEXITCODE){throw 'Build failed'}
$zip=Join-Path $buildOutput 'LuckyStarMOGAI/latest.zip'
Copy-Item -LiteralPath $zip -Destination (Join-Path $stage 'latest.zip') -Force
$indexUrl='https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json'
$raw=(Invoke-WebRequest $indexUrl).Content
$index=@($raw | ConvertFrom-Json)
$stamp=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('yyyyMMdd-HHmmss')
$backup=Join-Path $root "obj/backups/before-LuckyStarMOGAI-$stamp"
New-Item -ItemType Directory -Force -Path $backup | Out-Null
[IO.File]::WriteAllText((Join-Path $backup 'pluginmaster.json'),$raw,[Text.UTF8Encoding]::new($false))
$previous=$index | Where-Object InternalName -EQ 'LuckyStarMOGAI' | Select-Object -First 1
$rollbackName=if($previous){"rollback-luckystarmogai-$($previous.AssemblyVersion).json"}else{"rollback-before-luckystarmogai-$Version.json"}
[IO.File]::WriteAllText((Join-Path $stage $rollbackName),$raw,[Text.UTF8Encoding]::new($false))
$entry=Get-Content (Join-Path $buildOutput 'LuckyStarMOGAI/LuckyStarMOGAI.json') -Raw | ConvertFrom-Json
if($entry.AssemblyVersion -ne $Version){throw 'Manifest version mismatch'}
$link="https://github.com/Laficia/HuntHelperCN/releases/download/luckystarmogai-v$Version/latest.zip"
$entry | Add-Member -NotePropertyMembers @{DownloadLinkInstall=$link;DownloadLinkUpdate=$link;DownloadLinkTesting=$link;LastUpdate=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds();IsHide=$false;IsTestingExclusive=$false;Changelog="$Version：修复到达落地点后下坐骑动画下降导致的误停，原生路线在已查询地面正上方1.5米内结束时接续正常下坐骑，保留真实状态确认与15秒动作超时。非AE循环按职业攻击距离和目标碰撞半径判断追近，能攻击时停止多余移动，远程不再强制贴到怪物中心2.5米。保留三洞骑乘飞行／潜水和快速进出洞衔接，133项回归检查通过。"} -Force
@($index | Where-Object InternalName -NE 'LuckyStarMOGAI') + @($entry) | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $stage 'pluginmaster.json') -Encoding utf8
Copy-Item (Join-Path $root 'README.md') (Join-Path $stage 'LuckyStarMOGAI-README.md') -Force
Copy-Item (Join-Path $root 'LICENSE.md') (Join-Path $stage 'LICENSE-AGPL-3.0.md') -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourcePath=Join-Path $stage 'LuckyStarMOGAI-source.zip'
$stream=[IO.File]::Open($sourcePath,[IO.FileMode]::Create)
$archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    $sources=@('src','tests','tools','scripts','.gitignore','Directory.Build.props','LICENSE.md','NOTICE.md','README.md','THIRD-PARTY-LICENSES')
    foreach($item in $sources){
        Get-ChildItem -LiteralPath (Join-Path $root $item) -Recurse -File -Force | Where-Object {$_.FullName -notmatch '[\\/](bin|obj|research|\.git|\.vs)[\\/]'} | ForEach-Object {
            $rel=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$_.FullName,"LuckyStarMOGAI/$rel") | Out-Null
        }
    }
} finally {$archive.Dispose();$stream.Dispose()}
$hashes=foreach($name in @('latest.zip','LuckyStarMOGAI-source.zip')){"$((Get-FileHash (Join-Path $stage $name) -Algorithm SHA256).Hash.ToLowerInvariant())  $name"}
$hashes | Set-Content (Join-Path $stage 'LuckyStarMOGAI-SHA256SUMS.txt') -Encoding ascii
[pscustomobject]@{Version=$Version;Backup=$backup;Stage=$stage;SHA256=(Get-FileHash (Join-Path $stage 'latest.zip')).Hash;Published=$false;InGameVerified=$false} | ConvertTo-Json | Set-Content (Join-Path $stage 'verification.json') -Encoding utf8
Write-Output $stage
