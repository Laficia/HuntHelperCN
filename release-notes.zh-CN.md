狩猎助手Hunt Helper 1.0.0.1

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 插件显示名称统一为「狩猎助手Hunt Helper」，内部标识仍为 HuntHelperCN，沿用原配置、记录与地图缓存目录。
- 单服务器分享码导入预览增加「导入服务器」选择，支持原版无区服代码；确认前可切换目标服，取消不会修改现有记录。
- 多服务器分享码仍按各自区服导入；覆盖与合并只影响选定目标服或代码涉及的服务器，保留原版分享码兼容性。
- 保留窗口缩放、单服/全服导出、标签拖拽/右键菜单、空标签清理、原地重新录制、地图缓存与国服 vnavmesh 寻路功能。

本次按要求将实际版本号从 1.0.0.39 重置为 1.0.0.1。卫月不会将高版本自动降级，订阅地址无法强制重新安装。
已安装 1.0.0.35～39 的用户必须在卫月手动卸载旧版，再从原订阅安装「狩猎助手Hunt Helper」；保留配置，不要使用清除配置选项。历次发布和本地更新前备份均保留。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/HuntTrain.json。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：6cd7ddebd1339c7f840e8689833f1e6d0480d352be594886ebe36fa48396502a
