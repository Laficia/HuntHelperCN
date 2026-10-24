狩猎助手Hunt Helper 1.0.0.4

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 7A、6A、5A、4A 未扫描前预先显示各自完整 12 种 A 怪中文名单，标签显示已发现种类/总数，例如 7A (3/12)。3A 也使用 12 种，2A 按实际上游名单使用 17 种。
- 未找到的显示「未发现」，开启记录或找怪扫描后自动标出对应怪物并补上真实位置、分线与操作。死亡记录仍算已发现；跨分线同怪仅计一个种类，删除记录后恢复未发现。
- 预设名单只占界面位置，不生成假记录或坐标，不参与传送、移动、死亡监测或分享码导出。空服务器也可查看，版本标签在 0/12 时仍可选择。
- 服务器隔离及每服进度保留；清空、恢复、排序和下一只仍只操作真实记录。「全部」「其它」继续展示真实记录。
- 深色面板、圆角版本标签、细进度条与独立滚动名单；绿色已发现、红色未发现、灰色已死亡。固定表头与文字徽标提高辨识度，局部样式不改变卫月其它窗口。
- 保留找怪、路线录制、自动巡点、正常水晶传送和既有全部功能，无任意坐标 TP。

1.0.0.1～3 可通过原订阅正常更新。由于此前重置版本号，仍在使用 1.0.0.35～39 的用户需要保留配置并重新安装。
已备份 1.0.0.3 安装包、源码、订阅、配置、狩猎记录及已有自定义路线；旧版本 GitHub Release 保留。玩家记录和自定义路线不包含在发布包中。
固定 1.0.0.3 回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.3.json 。回退时停用原订阅，保留配置卸载后从旧版订阅重新安装；完整步骤见 README-CN.md。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/HuntTrain.json。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：7d18a43b21c63a884d5d9183fc5360a20cb8a1a593ef982e2874c3ec9247b169
