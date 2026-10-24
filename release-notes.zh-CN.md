狩猎助手Hunt Helper 1.0.0.3

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 服务器标签下增加 7A、6A、5A、4A、3A、2A 版本标签，按最新版本优先排列。只显示有记录的分类，默认打开最新有记录的版本，并保留「全部」及「其它」分类。
- 服务器隔离保留，各服分别记住选中的版本；扫描结果始终保存到角色当前所在服。
- 标死、删除死亡、清空、恢复和下一只只作用于当前可见分类；行排序、导航仍对应原始记录，避免操作到隐藏版本。右键服务器标签仍可删除整服。
- 导出增加「当前标签」，保留当前服务器与全部服务器两种范围；分享码格式保持原版兼容。
- 保留找怪地图选择、路线录制、vnavmesh 自动巡点、正常水晶传送、死亡监测和地图缓存，无任意坐标 TP。

1.0.0.1～2 可通过原订阅正常更新。由于此前重置版本号，仍在使用 1.0.0.35～39 的用户需要保留配置并重新安装。
已备份 1.0.0.2 安装包、源码、订阅、配置、狩猎记录及已有自定义路线；旧版本 GitHub Release 保留。玩家记录和自定义路线不包含在发布包中。
固定 1.0.0.2 回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.2.json 。回退时停用原订阅，保留配置卸载后从旧版订阅重新安装；完整步骤见 README-CN.md。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/HuntTrain.json。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：ff203262b2079a845ae2241d1107b4b6175a9d0c10282531a16cc22b6a0110a5
