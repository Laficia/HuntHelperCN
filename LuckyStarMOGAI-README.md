# LuckyStarMOGAI 0.0.0.2

基于 [NiGuangOwO/LuckyStar](https://github.com/NiGuangOwO/LuckyStar) 的独立国服 API 15 定制版。保留原作六个农怪项目，新增红玉海巨大鳐三洞循环。原作署名与 AGPL-3.0-or-later 许可证保留；完整可构建源码随发行版提供。

## 安装与打开

沿用原来的卫月第三方订阅地址：

```
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json
```

在卫月插件安装器中刷新仓库并搜索 **LuckyStarMOGAI**。程序集与配置独立于 LuckyStar、HHT。请先停止原 LuckyStar 和 HHT 找怪，避免同时控制导航。

- `/luckystarmogai`：打开或关闭窗口。
- `/luckystarmogai stop`：停止本插件的农怪、导航并释放本会话的循环控制。
- 需要 [国服 vnavmesh](https://github.com/AtmoOmen/ffxiv_navmesh-cn)（API 15）。自动循环现在可选 AE、RotationSolverReborn、Wrath Combo 或 PromeRotation，配置方式见下一节；不再强制依赖 AE。

## 自动循环选择（0.0.0.2）

窗口顶部的「自动循环」下拉框提供四种引擎，默认保留 AE。运行中不能改选；每次只调用所选引擎，不会自动回退到别的插件。请先安装适配当前国服的版本、启用插件并配置当前职业。

| 引擎 | 本插件如何控制 | 开始前要求 |
| --- | --- | --- |
| AE（原版） | 原有 mode7 选怪、自动拉怪命令 | 提供 `/aeTargetSelector`、`/aepull` |
| RotationSolverReborn | 已开启则沿用；允许托管时以 Henched 模式使用当前目标 | 提供 `AutorotationActive`、`ChangeOperatingMode` 公共 IPC |
| Wrath Combo | 使用本插件的临时租约，切换自动循环与临时手动目标／脱战拉怪选项 | 开启 IPC；当前职业单体、群体组合已配置并启用 Auto-Mode |
| PromeRotation | 已开启则沿用；允许托管时使用公开 Start/Stop | 选择有效 ACR，插件自身验证通过并已加载循环 |

**RSR 与 Prome 默认不接管开关**：先在它们自己的界面开启循环，再开始农怪，本插件选怪、走近并在必要时使用正常自动攻击发起战斗。停止农怪时，不关闭你原本已开启的循环。

勾选「允许临时开启循环（停止农怪时关闭）」后，循环尚未运行时，本插件会尝试启动；离开点位、停止、异常或卸载时关闭自己启动的循环。原本已运行的循环继续沿用。RSR/Prome 没有公开完整状态租约：无法精确恢复 Auto/Manual/Off/Hold 等历史状态，也不能判断运行中改动模式是否仍由本插件控制；托管期间请先停止农怪再改模式。Prome 的 IsRunning=false 也可能表示当前 ACR/状态不可用，因此托管仅在你明确勾选后启用，不默认猜测原状态。

Wrath 的租约被用户撤销、插件卸载、暂停或换职业时，下一次更新停止农怪，不会立即重新申请。停止时只释放本插件的租约，不调用全局关闭命令，不自动修改职业组合配置。若要严格使用本插件选中的目标，请在 Wrath 关闭 `AoEIgnoreManual`（群体忽略手动目标）；该设置没有公开租约选项，本插件不会写私有配置。

部分旧 RSR API 15 版本也缺少状态接口：已检查的上游 7.4.1.10／7.4.5.35 不具备 `AutorotationActive`，7.5.8.3 有该接口。这里列的是接口核对范围，不代表国际服二进制能直接用于国服；实际以已加载的国服兼容版本和界面接口检测为准。缺接口时拒绝启动并提示，不用可能切换开关的聊天命令冒充兼容。

Prome 按用户提供的 API 15 包 1.5.11.1 公共 IPC 接入，不修改其验证、ACR、AutoPull 或授权。它返回拒绝启动时会停止并提示，请在 Prome 自身界面排查。

接口来源：[RSR Provider](https://github.com/FFXIV-CombatReborn/RotationSolverReborn/blob/6462f8a9054039a092e64fa99d2fd1daab9cc419/RotationSolver/IPC/IPCProvider.cs)、[Wrath Provider](https://github.com/PunishXIV/WrathCombo/blob/a457838183c3cf5a1065ccbd3d70afd1851b3372/WrathCombo/Services/IPC/Provider.cs)、[Wrath 租约](https://github.com/PunishXIV/WrathCombo/blob/a457838183c3cf5a1065ccbd3d70afd1851b3372/WrathCombo/Services/IPC/Leasing.cs)、[Prome 国服库](https://raw.githubusercontent.com/Milkvio/DalamudPlugins/main/repo.json)。Prome 仅做公开接口的本地静态核对，其 DLL、反编译内容和个人配置均不随本项目发行。

## 红玉海：巨大鳐

1. 先到红玉海，进入三个目标海底洞穴中的任意一个，或在附近海底完成下潜。地表不能直接启动本模式。
2. 选择「红玉海 · 巨大鳐」，默认勾选观梦螺、无壳观梦螺和全部三个洞穴：

| 洞穴 | 用户指定地图坐标 |
| --- | --- |
| 采贝洞 | X:15，Y:14.5 |
| 石牡丹岩窟 | X:21，Y:26 |
| 镜仙洞 | X:25，Y:25.5 |

3. 点击「开始农怪」。在当前洞穴或最近勾选洞穴开始，之后按采贝洞 → 石牡丹岩窟 → 镜仙洞循环，跳过未勾选洞穴。只勾选一个时，在该洞穴循环等待刷新。
4. 洞内先步行到固定扫描区，再读取当前已加载的真实小怪位置，按距离巡点。路上被目标小怪攻击时使用所选循环处理后继续；遇到无法识别的其他怪物会停止并提示手动处理。
5. 洞间使用 vnavmesh 潜水寻路。洞口沿游戏 `ExitRange` 与 `PopRange` 对应的短路径走过正常触发区，等待游戏进出洞穴的加载动画。**不写角色坐标、不使用坐标 TP**。
6. 加载期间不重复发送寻路／战斗指令；恢复后等待场景稳定、身份与网格恢复，再继续下一阶段。加载超过 120 秒、路段卡住、导航被中断、死亡、切服／切图／切分线会停止。状态栏说明停止原因。
7. 发现已加载的巨大鳐时停止农怪。此检测受本机对象加载范围限制，不是全图刷新通知。

根据[中文维基的狩猎条件](https://ff14.huijiwiki.com/wiki/怪物狩猎)，巨大鳐需要击杀观梦螺和无壳观梦螺各 100 只，受满月与 S 怪重生冷却影响；ET 第 16 日 12:00 为满月触发起始参考。窗口显示 ET 日期与条件提示，不读取服务器隐藏冷却，也不保证完成本机计数就一定刷新。

「本次观测死亡」只统计本机看见的小怪从存活到死亡，不将离开加载范围当作死亡，不声称是你的个人击杀或全服总数。计数属于当前一次运行，重新开始会清零；不会跨服务器或分线合并。

## 原作项目

保留萨维奈岛颇胝迦、叹息海沉思之物、拉凯提卡大森林伊休妲、基拉巴尼亚边区优昙婆罗花、魔大陆阿济兹拉卢克洛塔、北萨纳兰蚓螈巨虫的原预设点位。选好小怪种类，在对应地图点击开始；这些项目沿用上坐骑、起飞和巡点流程。

## 验证与测试范围

已用本机国服客户端核对目标名字 ID、地图 ID，以及三个洞穴的原生进出触发区。使用国服 vnavmesh 0.7.7.0 和已有红玉海网格缓存，离线验证三个洞内扫描路段、三个返回出口路段与三个洞间潜水路段均为完整可达。

77 项逻辑测试覆盖四种自动循环、租约撤销与异常释放、既有开关保护、近身与交战追近、完整三洞循环、六次进出加载、黑屏暂停、身份恢复、切服／分线停止、超时、重复死亡计数与 自动循环生命周期。**尚未控制游戏角色实测**；实际洞口动画衔接、所选插件的实际战斗循环、导航网格差异仍需游戏内验证。洞口短路径使用原生执行器，遵循其玩家手动操作中断设置；发生停止后不会自动反复重提同一路线。

## 构建与源码

安装 .NET 10 SDK 和国服卫月 API 15 开发文件后：

```powershell
dotnet build src/LuckyStar.csproj -c Release
dotnet run --project tests/Regression.csproj -c Release
```

`Directory.Build.props` 默认引用 `%APPDATA%\XIVLauncherCN\addon\Hooks\dev`，可通过 `-p:DALAMUD_HOME=...` 覆盖。

可选离线网格检查（不会控制游戏角色）：

```powershell
dotnet run --project tools/VerifyNav/VerifyNav.csproj -c Release -- "国服vnavmesh插件版本目录" "红玉海.navmesh缓存文件"
```

该工具只支持已检查的 0.7.7.0 网格／查询格式，其他版本可能需要调整。不要把个人配置或网格缓存上传到源码仓库。

## 回退

停止并禁用／卸载 LuckyStarMOGAI 即可恢复使用原 LuckyStar；本次不会覆盖原 LuckyStar 的安装目录或配置。添加本插件前的订阅索引另存为 `rollback-before-luckystarmogai-0.0.0.1.json`；0.0.0.1 的固定回退索引为 `rollback-luckystarmogai-0.0.0.1.json`。原 HHT 的版本和下载地址保留。

## 署名与许可

原作者：NiGuangOwO。基准源码提交：`51c2a355a44200eb59c374c8211001c80f4b9d30`，上游版本 1.2.0.0。本次定制：Laficia，2026-11-13。

本项目按 **AGPL-3.0-or-later** 发布，完整文本见 `LICENSE.md`。包含的 ECommons 3.2.0.15 采用 MIT 许可，见 `THIRD-PARTY-LICENSES/ECommons-MIT.txt`。本库根目录中 HHT 的 MIT 许可不替代本项目的 AGPL 许可。


