狩猎助手Hunt Helper 1.0.0.8

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 补齐国服公共服务器名单，首次接收尚无 A/S 记录服务器的 Sonar 情报也能正确录制；换服刷新名单，未知服不猜测。
- 新增 /hhs 独立 S 怪模块：正常录制附近 S 怪，区服、版本和分线隔离，支持标签拖拽、标死与恢复、分享导入导出、正常水晶传送及同图 vnavmesh 移动。
- HHT 与 HHS 各自开启录制后，可接收 Sonar 点击橙色定位按钮输出的 A/S 怪聊天；A 入 HHT，S 入 HHS。按消息明确区服和分线去重，不将远服消息错误绑定当前服。
- 无明确区服、无法识别名称、地图不符或分线冲突的消息跳过；二维消息不伪造高度，不覆盖附近实扫高度或手动死亡状态。
- S 使用独立 SRankTrain.json 和界面设置，7S～3S各6种，2S为17种；不包含 SS 及随从。占位默认关闭，开启后沿用红色未发现和按地图分线设置。
- 保留1.0.0.6的停止找怪同位切换、中文补齐、空版本隐藏、统一拖拽、恢复存活反馈与420以下窗口缩放修正。无坐标 TP 或自动切线。

1.0.0.1～7 可通过原订阅正常更新。此前1.0.0.35～39用户需保留配置后重新安装。
更新前备份1.0.0.7安装包、源码、订阅与已有玩家配置、记录和路线；旧版 Release 保留，玩家文件不上传。
固定1.0.0.7回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.7.json 。停用原订阅，保留配置卸载后从旧版订阅重新安装，步骤见 README-CN.md。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：857ef24158b0c768a0a8aec6d1d6985f9937deddafe9f49f85a77445927db149
