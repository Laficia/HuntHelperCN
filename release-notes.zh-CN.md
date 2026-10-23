Hunt Helper 国服定制版 1.0.0.38

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 重做服务器标签栏，左键拖拽后实际显示顺序跟随保存顺序；拖到目标标签左半边/右半边松开可插入其前/后。
- 移除默认黄色拖放框和浮动预览，改为轻微蓝色目标边框。
- 右键标签增加「选择」「向左移动」「向右移动」「删除该服记录及标签」。拖拽和菜单移动顺序均会保存。
- 没有 A 怪记录的服务器标签自动消失，包括当前所在服；顶部仍显示当前服名称。
- 录制每 250ms 独立扫描附近存活 A 怪，不再依赖 /hh 地图窗口的展开、关闭或绘制状态；原地删除后可重新扫描。
- 清空当前所在服或通过菜单删除该服会暂停录制，避免空标签立即重建；重新开始录制即可原地再扫。删除其他服不会中断当前服录制。
- 保留地图缓存修复、卫月「打开」到 /hht、「设置」到 /hh、国服 vnavmesh 寻路和各服记录隔离等功能。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 Hunt Helper CN（国服定制）。请停用不再使用的旧版，避免重复显示狩猎窗口。
从旧版升级前备份 Data/HuntTrain.json。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：b96e160ab4851288e7c3b0bd7fb2b0f623890bb2687799562a7d963c7aac742c
