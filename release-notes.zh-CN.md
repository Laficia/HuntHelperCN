Hunt Helper 国服定制版 1.0.0.37

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 修复图片已下载但几乎不可见：旧配置和预设中的 0～1% 不透明度在首次升级时恢复到 100%，此后保留用户自行调整的数值。
- 地图缓存改为 pluginConfigs/HuntHelperCN/MapImages，升级时自动复用旧版本已下载的有效图片，避免重新下载。
- 状态窗口分别显示文件缓存、当前地图加载、纹理错误及不支持的地区，并提供「恢复地图显示」。
- 卫月插件列表「打开」显示 /hht 狩猎车界面，「设置」显示 /hh 狩猎地图界面；重复点击保持窗口打开。
- 保留国服 vnavmesh 寻路、服务器标签拖拽排序和各服记录隔离等功能。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 Hunt Helper CN（国服定制）。请停用不再使用的旧版，避免重复显示狩猎窗口。
从旧版升级前备份 Data/HuntTrain.json。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：fa21fdb3276eeecc186a3708cafb8b1271bc17dbff337c1276edd55dc5da4c7b
