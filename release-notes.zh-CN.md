狩猎助手Hunt Helper 1.0.0.11

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- HHT/HHS支持左键拖动怪物名称调整车序，拖到目标名称上半部插入前、下半部插入后；统一蓝色边框与落点线，没有原生黄色条。
- 长名单拖到上下边缘自动滚动；Esc或名称区域外松开取消；扫描改变名单或切换服务器/分类时取消旧拖动。
- 仅调整当前服务器、当前分类的真实记录，保留其它版本位置和当前选中目标；拖动不发送坐标或触发传送，右键菜单保留。
- 每次有效拖放松开后才保存，自动备份一次并支持撤销；未改变顺序不生成快照。同步中文使用手册。
- 保留1.0.0.10的Sonar失败诊断、来源时间、自动备份与撤销，以及服务器/版本/分线隔离、正常水晶传送和vnavmesh移动。

1.0.0.1～10可通过原订阅更新。此前1.0.0.35～39用户需备份并保留配置重新安装。
更新前备份1.0.0.10源码、包、订阅及现有玩家文件；旧版Release保留，玩家数据与运行时快照不上传。
固定1.0.0.10回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.10.json 。停用原订阅，保留配置卸载后从旧版订阅重装，覆盖文件前另存最新数据。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：fbbb1cd577918eeade821c8cc756dc9d80e2f22c937dd21e51b64e58fa4678eb
