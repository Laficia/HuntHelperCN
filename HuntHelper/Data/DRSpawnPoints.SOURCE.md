# DR 狩猎刷新点来源

`DRSpawnPoints.json` 是以下固定提交的原始文件逐字节副本，未重新排序、转换坐标或筛选点位。

- 项目：[Dalamud-DailyRoutines/DailyRoutines](https://github.com/Dalamud-DailyRoutines/DailyRoutines)
- 固定提交：`f516a23d3f14c1d3eeedf342f92c4b99c6853306`
- [原始文件](https://github.com/Dalamud-DailyRoutines/DailyRoutines/blob/f516a23d3f14c1d3eeedf342f92c4b99c6853306/Resources/Modules/AutoMarksFinder/SpawnPoints.json)
- SHA256：`046BB96522F172FF76AED7AC57078B160E59BE8C02073123C91BA5F10C58E428`
- 内容：47 张地图，924 个三维点位。

HHT 仅将外层地图编号、逐点 `Zone` 和 `Position` 作为点位数据读取。`CancelAnimation` 字段保留在原始文件中以保持来源完整，但不会读取或执行其行为。点位没有怪物等级标记，HHT 的 DR 覆盖巡航仅用于 A 怪。

覆盖寻点依据为官方 [DailyRoutines.ModulesPublic/Script/AutoMarksFinder](https://github.com/Dalamud-DailyRoutines/DailyRoutines.ModulesPublic/tree/d074edb8c9ecc5cc0dcb91b29df7511c610e30bd/Script/AutoMarksFinder) 的 `d074edb8c9ecc5cc0dcb91b29df7511c610e30bd` 提交。采用其固定 400 米检测假设、300 米规划半径及 1.5 秒停车观察参数。400 米是算法假设，并非角色探测范围的实测值；游戏实际加载结果才会成为怪物记录。
