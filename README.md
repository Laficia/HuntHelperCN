# 国服定制开发

本地定制分支：`codex/custom-optimizations`。基于上游 `img02/HuntHelper` 的 `0c07d87`。

## 适配环境

- 按 `C:\Users\PC\AppData\Roaming\XIVLauncherCN\addon\Hooks\dev` 中的国服 Dalamud `15.0.3.9` 程序集编译。
- 使用 `Dalamud.NET.Sdk/15.0.0`、`DalamudPackager/15.0.0` 和 `net10.0-windows`，平台为 x64。
- 玩家信息、聊天事件、区域切换事件和 FATE 状态保留上游 API 15 接口。
- 国服客户端默认加载 `simplified chinese.json`；保留上游中文计数规则与地图数据。

## 更新修正（1.0.0.36）

- 导航按国服 [AtmoOmen/ffxiv_navmesh-cn](https://github.com/AtmoOmen/ffxiv_navmesh-cn) 0.7.7.0（c81ddc20）的 IPC 签名接入。修复固定使用地面寻路的问题：已起飞时使用飞行路径，地面路线将起终点贴合可达网格，空路线最多重试一次。
- 删除列表的坐标列；当前区服与服务器标签仅显示服务器名称。
- 服务器标签可用鼠标左键拖到另一个标签上调整顺序，顺序随配置保存。
- 需要飞行跨越不连通地形时，请先骑乘并起飞，再点击「移动」。插件不会自动召唤坐骑或起飞；原生网格没有连通路线时仍会报告原因。

## 已有定制功能

1. A 车记录带访问区服、数据中心、分线和扫描时的三维坐标，`/hht` 按区服显示标签页。浏览其他服时扫描仍写入当前访问服。清空、标死、恢复和排序只作用于选中服，导入覆盖只替换分享码涉及的服。
2. 默认使用地图底图，首次打开地图自动下载缺少的图片并缓存。失败时雷达继续使用，状态窗口可重试；更新图片不删除已有缓存。
3. A 怪行的传送旁新增「移动」，调用国服 vnavmesh 寻路。仅当前区服、同地图、同分线且网格就绪时启用。显示禁用原因，并可「停止移动」；切换区服/地图/分线和卸载时取消本插件的寻路与移动。

先在 `/hht` 点击开始记录；记录开启时即使关闭主地图也继续扫描。
新扫描记录优先使用原始三维位置。旧分享码只有二维坐标时用 vnavmesh 地面查询，多层地图建议重新扫描以获得准确高度。

旧本地记录保留在「未标注区服」标签，避免错误绑定到登录服。旧分享码不含区服时导入到开始导入时选中的标签；新分享码保留其区服。
定制版使用独立标识 HuntHelperCN，记录保存到卫月 pluginConfigs/HuntHelperCN/HuntTrain.json。首次加载会复制可找到的原版记录并保留原件；请先禁用原版 Hunt Helper，避免 /hh、/hht 命令冲突。

vnavmesh 为可选依赖；未安装、未启用或导航网格未就绪时，「移动」置灰并显示原因。仅编译和独立逻辑验证无法替代游戏内实测。

## 编译

在仓库根目录执行：

```powershell
dotnet build HuntHelper.sln -c Debug
dotnet build HuntHelper.sln -c Release
dotnet run --project tests/HuntHelper.RegressionTests
dotnet run --project tests/HuntHelper.NavigationTests -c Release
```

仓库的 `Directory.Build.props` 在 SDK 解析引用路径前，将 Windows 下的 `DALAMUD_HOME` 设置为 `%APPDATA%\XIVLauncherCN\addon\Hooks\dev`。
因此普通编译命令直接引用国服目录；若需明确指定其他安装位置，使用命令行参数 `-p:DALAMUD_HOME=对应路径`。

本次发布使用 `-p:OutputPath=bin/x64/Release36/` 的独立输出目录。
Release 插件 DLL：`HuntHelper\bin\x64\Release36\HuntHelperCN.dll`。
完整压缩包：`HuntHelper\bin\x64\Release36\HuntHelperCN\latest.zip`，包含数据、语言资源及 MIT 许可。

## 当前验证范围

Debug、Release 编译通过；上游仍有可空性和未使用字段等编译警告。
独立回归测试通过：区服记录/导入/持久化/本地下载 17/17 组，导航 IPC 模拟与取消竞态 35/35 项。
测试直接链接生产源码，以最小宿主替身模拟游戏服务；下载测试只使用本机回环 HTTP，不连接游戏或修改已安装插件。
已核对打包清单的 `DalamudApiLevel=15`、.NET 10 目标及中文资源，并核对实际程序集引用来自上述 `XIVLauncherCN` 目录。
尚未进行游戏内加载、雷达、计数器、语音或传送联动实测。

先前引用 `%APPDATA%\XIVLauncher\addon\Hooks\dev` 中的 API 13 编译结果不适用于这次确认的国服环境；已撤回 API 13 改动并重新生成测试包。

后续功能修改继续在此分支进行；游戏内验证时使用这个包配套的 DLL、数据和依赖。

## 第三方插件库

订阅地址：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

安装时搜索 Hunt Helper CN（国服定制）。首次加载前请禁用原版。
