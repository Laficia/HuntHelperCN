# DR 狩猎刷新点来源

`DRSpawnPoints.json` 是以下固定提交的原始文件逐字节副本，未重新排序、转换坐标或筛选点位。

- 项目：[Dalamud-DailyRoutines/DailyRoutines](https://github.com/Dalamud-DailyRoutines/DailyRoutines)
- 固定提交：`f516a23d3f14c1d3eeedf342f92c4b99c6853306`
- [原始文件](https://github.com/Dalamud-DailyRoutines/DailyRoutines/blob/f516a23d3f14c1d3eeedf342f92c4b99c6853306/Resources/Modules/AutoMarksFinder/SpawnPoints.json)
- SHA256：`046BB96522F172FF76AED7AC57078B160E59BE8C02073123C91BA5F10C58E428`
- 内容：47 张地图，924 个三维点位。

HHT 仅将外层地图编号、逐点 `Zone` 和 `Position` 作为点位数据读取。`CancelAnimation` 字段保留在原始文件中以保持来源完整，但不会读取或执行其行为。点位没有怪物等级标记，HHT 的 DR 覆盖巡航仅用于 A 怪。

HHT 按地图将 924 个 DR 点与 `SpawnPointData.json` 的等级标记比对。两份数据的坐标在 0.75 地图单位内视为匹配。匹配到 B 怪且附近没有 A 怪标记的点会从 A 怪巡航中移除，B／S 共用点也是如此。现有数据共排除 14 张地图的 49 个点，A／B 共用点保留。

红玉海从自凝岛水晶先覆盖北侧两个点，再向西南短飞，在水面上方补扫（14.4，13.9）方向的范围。返回船头乘船后优先覆盖原始数据中的南侧三个 A 点，再向东北飞行。常规巡航的点位文件、候选计算与飞行高度算法沿用原版。

2026-11-16 巡航逻辑核对依据为官方 [Scanner 固定提交 29225845](https://github.com/Dalamud-DailyRoutines/DailyRoutines.ModulesPublic/blob/292258453537f62e9cd45f4d5c94d33775cc1367/Script/AutoMarksFinder/AutoMarksFinder.Scanner.cs)，完整提交为 `292258453537f62e9cd45f4d5c94d33775cc1367`。

- 固定检测假设400米、规划半径300米、到达半径20米、水平静止观察250毫秒；400米并非角色探测范围的实测值，实际加载的活怪才会记录。
- 候选位置、插值和圆交点按原版顺序生成，以玩家高度查询飞行网格，再按返回位置计算单位覆盖耗时；同分优先覆盖数量，再保留首个候选。
- 固定飞行估速12米/秒、传送估时10秒。只比较候选附近最近的允许水晶；DR模式不增加60秒同图传送冷却。
- 巡查水晶排除名单为 `[105, 148, 173, 175, 181, 201]`。不改原始刷新点，不加红玉海海面限制、固定高度或夏劳尼上抬。
- 从玩家位置上方2米开始可取消飞行寻路，保留首次进入锚点300米范围之前的路径；原生路径执行容差2米。
- 原版高低空和两环八方向重试保留。寻路15秒超时、路线提前停止或15秒无进展时重试；已在扫描范围内则观察。骑乘请求间隔1秒、骑乘超时20秒。
- DR模式按原版在路径中断后重试；HHT「停止找怪」结束任务并取消自己持有的计算和路径。

HHT保留自己的服务器／分线隔离、完整记录跳图、多服队列和窗口；没有直接加载整个DR模块。跨服另行调用DR的FastWorldTravel，不属于上述单图巡航算法。
