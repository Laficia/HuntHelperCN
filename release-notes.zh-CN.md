狩猎助手Hunt Helper 1.0.0.5

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- /hht → 设置新增「显示未发现 A 怪占位」，默认关闭并立即保存，旧配置首次升级同样关闭；移除怪物名单中重复的区服列。
- 占位按服务器＋地图＋分线独立展开。自动结合本服真实记录和就绪地图实况，只扩展有证据的地图，不套用到整个版本；每图可手动设置自动／无分线／2～9线。
- 分线各自计算已发现与未发现；版本标签仍显示种类数。某图两只 A 怪有三线时，该版本总点位从 12 增至 16。同怪1线发现不会清除2/3线占位；未知分线的旧记录不会填满其它线。
- 自动识别无法远程获知尚未到访地图的完整分线数；未确认时先显示一份名单，发现新分线后补全，可手动修正运营调整与历史记录造成的差异。手动设置按服按图保存。
- 删除记录在本次加载期间保留已识别分线的占位，当前就绪地图确认为无分线后按单份显示；保留历史记录与红色未发现、绿色已发现、灰色已死亡。
- 占位只用于展示，不生成坐标或真实记录，不参与分享码导出或移动；正常水晶传送和找怪功能保留，不增加自动切线或坐标 TP。

1.0.0.1～4 可通过原订阅正常更新。此前 1.0.0.35～39 用户需保留配置后重新安装。
更新前备份了 1.0.0.4 安装包、源码、订阅和已有玩家配置、记录及路线，旧版 Release 保留。玩家文件不包含在发布包中。
固定 1.0.0.4 回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.4.json 。回退时停用原订阅，保留配置卸载后重新安装，步骤见 README-CN.md。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/HuntTrain.json。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：d8db3f0a2449e1d65a7f31518c66fa4d78d0378f6c0155a82c520eabea32a75c
