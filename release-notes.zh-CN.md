狩猎助手Hunt Helper 1.0.0.10

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- Sonar未录入原因提示：录制开关、地图链接、区服、名称和分线等问题在窗口显示，不刷普通聊天。
- 记录来源和时间分开：现场扫描、Sonar、导入、历史来源；悬停查看现场发现、Sonar接收和导入时间。接收/导入不当成现场确认，旧数据不伪造来源。
- A/S分别保留最近10次手动操作前的记录快照，增加撤销和折叠历史恢复；覆盖前再次确认服务器与范围，恢复前也备份，支持撤销恢复。
- 仅恢复涉及服的怪物记录，这些服的后续新增修改会被替换；其它服、另一模块、路线和窗口设置不动。备份失败阻止操作，记录保存失败回滚。自动扫描不逐条占用快照。
- 保留简洁计数、服务器/版本/分线隔离、Sonar录制、正常水晶传送和vnavmesh移动。同步中文使用手册。

1.0.0.1～9可通过原订阅更新。此前1.0.0.35～39用户需备份并保留配置重新安装。
更新前备份1.0.0.9源码、包、订阅及现有玩家文件；旧版Release保留，玩家数据与运行时快照不上传。
固定1.0.0.9回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.9.json 。停用原订阅，保留配置卸载后从旧版订阅重装，覆盖文件前另存最新数据。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：34d2486d113c337ac53e1d5265fe08b6c879838a912595ba0ee716596d2c8156
