Hunt Helper 国服定制版 1.0.0.36

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 按 AtmoOmen/ffxiv_navmesh-cn 0.7.7.0 的 IPC 接口修正导航；根据角色是否已起飞选择地面/飞行路线，地面起终点贴合可达网格，空路线最多重新查询一次。
- 删除 A 怪列表中的坐标列，保留点位数据供地图和导航使用。
- 当前区服与服务器标签只显示服务器名称。
- 服务器标签支持鼠标左键拖拽排序，顺序随配置保存。
- 保留各服记录隔离、自动下载及缓存地图图片、同区服/同地图/同分线限制和停止移动功能。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 Hunt Helper CN（国服定制）。请停用不再使用的旧版，避免重复显示狩猎窗口。
从旧版升级前备份 Data/HuntTrain.json。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：e9da4e841412171a612ac832193b4d11590fa61b832f2a1bd3d789b1e570cf74
