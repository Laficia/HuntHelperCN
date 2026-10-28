狩猎助手Hunt Helper 1.0.0.12

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 新录入A怪使用指定地图顺序：4A边区→山区→湖区→红玉海→延夏→太阳神草原；5A雷克兰德→珂露西亚岛→安穆·艾兰→伊尔美格→拉凯提卡大森林→黑风海。
- 6A萨维奈岛→迷津→加雷马→叹息海→厄尔庇斯→天外天垓；7A奥阔帕恰山→克扎玛乌卡湿地→亚克特尔树海→夏劳尼荒野→遗产之地→活着的记忆。
- 现场扫描、Sonar新增均按来源服独立插入，同图保持录入先后，分线不合并。只插入新记录，已有记录相对顺序不变；重复扫描不重置手动车序，玩家仍可左键拖动。
- 插入前方记录时保留当前选中怪物；旧车序和导入代码保持原顺序，2A/3A及S模块不变。自动插入不占手动备份快照，拖动车序仍可撤销。
- 保留来源时间、Sonar失败提示、备份与撤销、服务器/版本/分线隔离、正常水晶传送和vnavmesh移动；同步中文使用手册。

1.0.0.1～11可通过原订阅更新。此前1.0.0.35～39用户需备份并保留配置重新安装。
更新前备份1.0.0.11源码、包、订阅及现有玩家文件；旧版Release保留，玩家数据与运行时快照不上传。
固定1.0.0.11回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.11.json 。停用原订阅，保留配置卸载后从旧版订阅重装，覆盖文件前另存最新数据。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：63519477fcd51b46749e0efaabd5bceb11c3aa3d26962e7a14856ddacf53a146
