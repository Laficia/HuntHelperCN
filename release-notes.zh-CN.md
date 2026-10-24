狩猎助手Hunt Helper 1.0.0.2

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- /hht 顶部新增「找怪 / 路线录制」，支持版本、地图及 A/B 等级选择。选择地图后点击「开始找怪」，扫描结果写入角色实际所在服务器，换服停止。
- 支持手动三维录点、按移动距离自动录点、顺序调整、排序、倒序、预览和独立路线码导入导出。路线保存在 pluginConfigs/HuntHelperCN/ScanRoutes.json，更新不会替换玩家路线。
- 巡点采用国服 vnavmesh，支持正常上坐骑及在允许时起飞；跨图保留已解锁水晶的普通传送。无指定坐标 TP。没有自定义路线时使用内置二维刷新点查询地面高度，多层地图建议录制三维路线。
- 新增当前服务器、地图、分线范围内的怪物死亡监测；结合已见受伤记录、近距离与消失宽限判断，保留手动状态优先。可在找怪窗口关闭。
- 扫描当前分线；无可用水晶、无法确定高度或寻路失败时停止并提示。进入战斗、死亡、换服或外部切图/切线时停止巡点。
- 保留原版狩猎车分享码互通、服务器标签、原有导航、地图缓存及所有既有功能。

1.0.0.1 可通过原订阅正常更新。由于此前重置版本号，仍在使用 1.0.0.35～39 的用户需要保留配置并重新安装。
已备份 1.0.0.1 安装包、源码、订阅、配置与狩猎记录；旧版本 GitHub Release 也保留，可用于回退。玩家记录和自定义路线不包含在发布包中。
固定 1.0.0.1 回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.1.json 。回退时停用原订阅，保留配置卸载后从旧版订阅重新安装；完整步骤见 README-CN.md。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/HuntTrain.json。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：771e19035d943fb37505b8329658fea41020de7b7419ef8d234b69b199816e4c
