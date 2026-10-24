狩猎助手Hunt Helper 1.0.0.9

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- A/S版本标签只显示实际已录入数，例如5A (3)、7S (2)；下方摘要显示已录入3。
- 移除3/12式总数、进度条、未发现汇总和分线种类比例。不同分线分别计数，包含死亡记录，占位不计入。
- 保留可选占位、红色未发现行、区服版本隔离、Sonar录制、导入导出、正常水晶传送及vnavmesh移动。

1.0.0.1～8可通过原订阅正常更新。此前1.0.0.35～39用户需保留配置后重新安装。
更新前备份1.0.0.8源码、包、订阅与已有玩家配置、记录和路线；旧版Release保留，玩家文件不上传。
固定1.0.0.8回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.8.json 。停用原订阅，保留配置卸载后从旧版订阅重新安装，步骤见README-CN.md。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：3421a6c6b464b55876b2c3fa96bc0f0d2789c5e0d9b2cbd53ca7cb25f27e343e
