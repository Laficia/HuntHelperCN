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
        if ([IO.Path]::GetFileName($normalizedName) -in @('HuntTrain.json', 'SRankTrain.json', 'ScanRoutes.json') -or
            $normalizedName -match '(^|/)Backups(/|$)') {
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
    if ($project.SelectSingleNode('/Project/ItemGroup/Reference[@Include="KamiToolKit"]')) {
        foreach ($nativeEntry in @('KamiToolKit.dll', 'FFXIVClientStructs.Expansion.dll', 'KamiToolKit.LICENSE', 'FFXIVClientStructs.Expansion.LICENSE', 'NativeUI.SOURCE.txt')) {
            if (-not $entries.ContainsKey($nativeEntry) -or $entries[$nativeEntry].Length -eq 0) { throw "Missing native UI dependency or provenance: $nativeEntry" }
        }
        foreach ($hostLibrary in @('Dalamud.dll', 'Dalamud.Common.dll', 'FFXIVClientStructs.dll', 'Lumina.dll', 'Lumina.Excel.dll')) {
            if ($entries.ContainsKey($hostLibrary)) { throw "Do not redistribute host SDK assembly: $hostLibrary" }
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
$releaseManifest['Punchline'] = '原生 A/S 狩猎车：紧凑列表、地图分组、Sonar 与 DR 巡航。'
$releaseManifest['Description'] = @'
基于 img02/HuntHelper 的国服定制版，保留上游狩猎雷达、A 车记录、通知和 S 怪工具。
HHT/HHS 使用 KamiToolKit 原生主窗口，默认紧凑列表，可切换地图分组；文字跟随游戏主题，保留标签与记录拖动、右键排序和删除。沿用原内部名称和记录，无需另装原生版。
A 怪记录按区服隔离并提供标签页；缺少的地图图片自动下载及缓存；传送旁的「移动」可调用 vnavmesh 前往 A 怪点位，仅在同区服、同地图、同分线且导航可用时启用。
/hht 顶部「找怪」可按版本、地图和服务器选择，使用固定预设路线与 vnavmesh 巡点扫描。/hhs 提供独立S怪记录；A/S坐标宏分别保存，Sonar明确A怪击杀可同步死亡标记。跨图仅使用已解锁水晶正常传送，无坐标 TP；扫描结果保存到角色当前所在服务器。
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
$indexPath = Join-Path $repositoryRoot 'pluginmaster.json'
$otherPlugins = @()
if (Test-Path -LiteralPath $indexPath) {
    $otherPlugins = @(([IO.File]::ReadAllText($indexPath) | ConvertFrom-Json) | Where-Object { $_.InternalName -ne $InternalName })
}
$indexJson = (ConvertTo-Json -InputObject @(@($releaseManifest) + $otherPlugins) -Depth 30) + "`n"
$stagedArchivePath = Join-Path $stagingDirectory 'latest.zip'
Copy-Item -LiteralPath $archivePath -Destination $stagedArchivePath -Force
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDirectory "$InternalName.json") -Force
[IO.File]::WriteAllText((Join-Path $stagingDirectory 'pluginmaster.json'), $indexJson, $utf8NoBom)
$archiveHash = (Get-FileHash -LiteralPath $stagedArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $stagingDirectory 'SHA256SUMS.txt'), "$archiveHash  latest.zip`n", $utf8NoBom)
$releaseNotes = @"
狩猎助手Hunt Helper $version

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 修正坐标宏的发送路径：提交纯文本聊天指令，让游戏原生解析旗标；不再把显示用地图链接直接嵌入输入。每行发送前设置对应怪物记录的临时旗标，提交后恢复原旗标；准备或发送失败不回退成本地打印。
- 按已确认的“紧凑按钮”布局加入宏集合：标签栏“＋”新增自定义宏，拖动标签固定到上方集合；按钮缩略显示名称与首行正文，悬停看全文，点击发送已保存的宏。支持左右拖动排序，蓝色提示，无黄色拖放框，拖动不会误发；顺序、名称和正文跨重启保留。
- A/S预设继续独立并对应HHT/HHS的坐标按钮。集合使用打开窗口时来源模块当前选中的真实怪物，界面显示来源；纯文字宏无需选择怪物，含变量的宏无目标时拒绝发送。右键可编辑、移出集合，删除自定义宏需确认。
- 移除队伍、喊话及本地回显快捷预设按钮。A/S统一默认模板为 /sh 下一站【<服务器>】<名字>\<flag\>，允许直接修改；旧内置预设仅迁移一次，自定义宏保留。
- 新增 \<name\>、\<map\>、\<lv\>、\<sever\>、\<ins\> 英文别名，大小写不敏感；旧 \<world\>、\<rank\>、\<instance\> 继续兼容。取消 \<x\>/\<y\>，旧宏含这些变量时提示改用旗标链接。变量始终来自选中记录，同服服务器显示“本地”。
- 明确区分本地显示与对外发送：旧的聊天坐标查看选项改标“本地显示（仅自己可见）”；只有明确使用 /echo 或 /e 时才作为本地回显。/sh 等频道提交给原生游戏聊天，界面不宣称其他玩家已收到。批次最多15行，间隔1.2秒，忙时不叠加，取消／登出／换服／卸载清除剩余消息。
- 更新前备份1.0.0.15源码、安装包、订阅和本机配置；固定回退订阅 rollback-1.0.0.15.json。原卫月订阅保持不变，玩家配置及备份不上传。使用说明同步更新。
- 编译与294项自动检查通过：210组回归（含发送管理器到原生提交边界）、43组原生ImGui、30组导航/Lifestream、11组原版分享兼容。尚未游戏内双端验证喊话接收，此次发布供用户测试。

1.0.0.1～15可通过原订阅更新。此前1.0.0.35～39用户需备份并保留配置重新安装。
固定1.0.0.15回退订阅：https://raw.githubusercontent.com/$Repository/main/rollback-1.0.0.15.json 。停用原订阅，保留配置卸载后从旧版订阅重装，覆盖配置前另存最新数据。

卫月第三方插件库订阅地址：
$subscriptionUrl

该版本 InternalName 为 $InternalName，界面名称为 $($manifest.Name)。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。名单「移动」需要飞行时，请先正常骑乘并起飞；找怪窗口另有自动上坐骑/可飞行时自动起飞选项。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：$archiveHash
"@
$reviewedNotes = Join-Path $repositoryRoot 'release-notes.zh-CN.md'
if (Test-Path -LiteralPath $reviewedNotes) { $releaseNotes = [IO.File]::ReadAllText($reviewedNotes) }
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
