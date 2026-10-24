[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$')]
    [string]$Repository,

    [string]$InternalName,
    [string]$PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Repository.EndsWith('.git', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use owner/repository without a URL or the .git suffix.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$project = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'HuntHelper/HuntHelper.csproj'))
$projectAssemblyNames = @($project.SelectNodes('/Project/PropertyGroup/AssemblyName'))
if ($projectAssemblyNames.Count -gt 1) {
    throw 'Multiple project AssemblyName values found; use one unconditional AssemblyName for publishing.'
}
$projectAssemblyName = 'HuntHelper'
if ($projectAssemblyNames.Count -eq 1) {
    $projectAssemblyName = $projectAssemblyNames[0].InnerText.Trim()
}
if ([string]::IsNullOrWhiteSpace($InternalName)) {
    $InternalName = $projectAssemblyName
}
if ($InternalName -cnotmatch '^[A-Za-z][A-Za-z0-9_.-]*$' -or $InternalName -cne $projectAssemblyName) {
    throw 'InternalName must be a safe assembly name matching the project AssemblyName.'
}
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repositoryRoot "HuntHelper/bin/x64/Release/$InternalName"
}
$manifestPath = Join-Path $packageDirectory "$InternalName.json"
$archivePath = Join-Path $packageDirectory 'latest.zip'
$licensePath = Join-Path $repositoryRoot 'LICENSE'

foreach ($requiredPath in @($manifestPath, $archivePath, $licensePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required file not found: $requiredPath. Build the Release package first."
    }
}

$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
foreach ($requiredProperty in @('Author', 'Name', 'InternalName', 'AssemblyVersion', 'DalamudApiLevel')) {
    if (-not $manifest.PSObject.Properties[$requiredProperty]) {
        throw "Package manifest is missing $requiredProperty."
    }
}
if ($manifest.InternalName -cne $InternalName) {
    throw "The release package must use InternalName $InternalName."
}
if (-not ([string]$manifest.Author).Contains('img02')) {
    throw 'The upstream Author credit (img02) must be retained.'
}
if ([int]$manifest.DalamudApiLevel -ne 15) {
    throw "Expected Dalamud API 15, found $($manifest.DalamudApiLevel)."
}
if ([string]$manifest.AssemblyVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw 'AssemblyVersion must have four numeric parts.'
}
$version = [version]$manifest.AssemblyVersion
$projectVersions = @($project.SelectNodes('/Project/PropertyGroup/Version'))
if ($projectVersions.Count -ne 1 -or [version]$projectVersions[0].InnerText -ne $version) {
    throw 'The package version does not match the project version. Rebuild Release before publishing.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @{}
    foreach ($entry in $archive.Entries) {
        $normalizedName = $entry.FullName.Replace('\', '/')
        if ($normalizedName.StartsWith('/') -or $normalizedName -match '(^|/)\.\.(/|$)' -or $normalizedName.Contains(':')) {
            throw "Unsafe archive path: $normalizedName"
        }
        if ([IO.Path]::GetFileName($normalizedName) -in @('HuntTrain.json', 'SRankTrain.json', 'ScanRoutes.json')) {
            throw 'The release archive must not contain player hunt records or recorded routes.'
        }
        if ($entries.ContainsKey($normalizedName)) {
            throw "Duplicate archive entry: $normalizedName"
        }
        $entries[$normalizedName] = $entry
    }

    foreach ($requiredEntry in @("$InternalName.json", "$InternalName.dll", 'LICENSE', 'Data/Localisation/simplified chinese.json')) {
        if (-not $entries.ContainsKey($requiredEntry) -or $entries[$requiredEntry].Length -eq 0) {
            throw "Required nonempty archive entry not found: $requiredEntry"
        }
    }

    function Read-ArchiveText([IO.Compression.ZipArchiveEntry]$Entry) {
        $reader = [IO.StreamReader]::new($Entry.Open(), [Text.Encoding]::UTF8, $true)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    $archiveManifest = (Read-ArchiveText $entries["$InternalName.json"]) | ConvertFrom-Json
    # Compare every field, including API/version/identity, instead of checking only a subset.
    $manifestProperties = @($manifest.PSObject.Properties.Name | Sort-Object)
    $archiveProperties = @($archiveManifest.PSObject.Properties.Name | Sort-Object)
    if (($manifestProperties -join "`n") -cne ($archiveProperties -join "`n")) {
        throw 'The ZIP manifest and external package manifest have different fields.'
    }
    foreach ($property in $manifestProperties) {
        $expected = ConvertTo-Json -InputObject $manifest.$property -Depth 30 -Compress
        $actual = ConvertTo-Json -InputObject $archiveManifest.$property -Depth 30 -Compress
        if ($expected -cne $actual) {
            throw "The ZIP manifest differs from the external package manifest: $property."
        }
    }

    $originalLicense = [IO.File]::ReadAllText($licensePath).Replace("`r`n", "`n").Trim()
    $archiveLicense = (Read-ArchiveText $entries['LICENSE']).Replace("`r`n", "`n").Trim()
    if ($archiveLicense -cne $originalLicense -or $archiveLicense -notmatch 'MIT License' -or $archiveLicense -notmatch 'imaginary-png') {
        throw 'The archive must contain the original MIT license and copyright notice.'
    }
    $null = (Read-ArchiveText $entries['Data/Localisation/simplified chinese.json']) | ConvertFrom-Json
}
finally {
    $archive.Dispose()
}

$tag = "v$version"
$repositoryUrl = "https://github.com/$Repository"
$downloadUrl = "$repositoryUrl/releases/download/$tag/latest.zip"
$subscriptionUrl = "https://raw.githubusercontent.com/$Repository/main/pluginmaster.json"
$releaseManifest = [ordered]@{}
foreach ($property in $manifest.PSObject.Properties) {
    $releaseManifest[$property.Name] = $property.Value
}
$releaseManifest['RepoUrl'] = $repositoryUrl
$releaseManifest['Punchline'] = '国服狩猎助手：区服隔离、路线录制、自动巡点找怪与死亡监测。'
$releaseManifest['Description'] = @'
基于 img02/HuntHelper 的国服定制版，保留上游狩猎雷达、A 车记录、通知和 S 怪工具。
A 怪记录按区服隔离并提供标签页；缺少的地图图片自动下载及缓存；传送旁的「移动」可调用 vnavmesh 前往 A 怪点位，仅在同区服、同地图、同分线且导航可用时启用。
/hht 顶部「找怪 / 路线录制」可按版本与地图选择，支持路线录制、vnavmesh 巡点扫描及当前区服死亡监测。跨图仅使用已解锁水晶正常传送，无坐标 TP；扫描结果保存到角色当前所在服务器。
原作者 img02；上游 https://github.com/img02/HuntHelper；MIT License，Copyright (c) 2022 imaginary-png。
'@.Trim()
$releaseManifest['DownloadLinkInstall'] = $downloadUrl
$releaseManifest['DownloadLinkUpdate'] = $downloadUrl
$releaseManifest['DownloadLinkTesting'] = $downloadUrl
$releaseManifest['LastUpdate'] = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$releaseManifest['IsHide'] = $false
$releaseManifest['IsTestingExclusive'] = $false

# Keep binary artifacts out of Git. The repository's existing obj/ rule covers this directory.
$stagingDirectory = Join-Path $repositoryRoot "obj/release-staging/$tag"
$null = New-Item -ItemType Directory -Path $stagingDirectory -Force
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$indexJson = (ConvertTo-Json -InputObject @($releaseManifest) -Depth 30) + "`n"
$indexPath = Join-Path $repositoryRoot 'pluginmaster.json'
$stagedArchivePath = Join-Path $stagingDirectory 'latest.zip'
Copy-Item -LiteralPath $archivePath -Destination $stagedArchivePath -Force
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDirectory "$InternalName.json") -Force
[IO.File]::WriteAllText((Join-Path $stagingDirectory 'pluginmaster.json'), $indexJson, $utf8NoBom)
$archiveHash = (Get-FileHash -LiteralPath $stagedArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $stagingDirectory 'SHA256SUMS.txt'), "$archiveHash  latest.zip`n", $utf8NoBom)
$releaseNotes = @"
狩猎助手Hunt Helper $version

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- A/S版本标签只显示实际已录入数，例如5A (3)、7S (2)；下方摘要显示已录入3。
- 移除3/12式总数、进度条、未发现汇总和分线种类比例。不同分线分别计数，包含死亡记录，占位不计入。
- 保留可选占位、红色未发现行、区服版本隔离、Sonar录制、导入导出、正常水晶传送及vnavmesh移动。

1.0.0.1～8可通过原订阅正常更新。此前1.0.0.35～39用户需保留配置后重新安装。
更新前备份1.0.0.8源码、包、订阅与已有玩家配置、记录和路线；旧版Release保留，玩家文件不上传。
固定1.0.0.8回退订阅：https://raw.githubusercontent.com/$Repository/main/rollback-1.0.0.8.json 。停用原订阅，保留配置卸载后从旧版订阅重新安装，步骤见README-CN.md。

卫月第三方插件库订阅地址：
$subscriptionUrl

该版本 InternalName 为 $InternalName，界面名称为 $($manifest.Name)。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：$archiveHash
"@
[IO.File]::WriteAllText((Join-Path $stagingDirectory 'release-notes.zh-CN.md'), $releaseNotes + "`n", $utf8NoBom)
# Write the root index only after all package validation and staging succeed.
[IO.File]::WriteAllText($indexPath, $indexJson, $utf8NoBom)

[pscustomobject]@{
    Repository = $repositoryUrl
    Version = $version.ToString()
    InternalName = $InternalName
    Tag = $tag
    Index = $indexPath
    Staging = $stagingDirectory
    Subscription = $subscriptionUrl
    Download = $downloadUrl
    SHA256 = $archiveHash
}
