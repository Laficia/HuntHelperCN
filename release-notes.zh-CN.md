狩猎助手Hunt Helper 1.0.0.6

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 找怪窗口「开始找怪」原位置在运行后切换为「停止找怪」，停止操作不再被扫描设置的禁用状态覆盖；路线录制页在巡点期间也提供停止入口。
- 补齐地图、设置、语音测试、插件联动、语言选择、计数器与危命任务提示等中文；保留命令、技术名称及分享码格式。
- 关闭占位时自动隐藏无真实记录的版本标签；开启占位则保留完整版本名单，以查看尚未发现的怪物。
- 版本标签支持左键前后拖拽和右键移动，顺序按服务器保存；与服务器标签共用蓝色目标轮廓反馈，不使用黄色拖放框。
- 恢复存活按钮会显示恢复条数或无需恢复的提示，仅影响当前标签中的死亡记录。修复关闭死亡监测后再启用时手动恢复保护丢失的问题。
- 窗口允许缩小到420像素以下，适配界面缩放及主题最小尺寸；窄窗口名单可横向滚动，宽高控件自动换行。
- 保留服务器／地图／分线隔离、占位默认关闭、红色未发现，以及正常水晶传送、找怪和路线录制功能。不加入坐标 TP 或自动切线。

1.0.0.1～5 可通过原订阅正常更新。此前 1.0.0.35～39 用户需保留配置后重新安装。
更新前备份1.0.0.5安装包、源码、订阅与已有玩家配置、记录和路线；旧版 Release 保留，玩家文件不上传。
固定1.0.0.5回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.5.json 。停用原订阅，保留配置卸载后从旧版订阅重新安装，步骤见 README-CN.md。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/HuntTrain.json。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。需要飞行跨越地形时，请先骑乘并起飞，再点击「移动」；插件不会自动召唤坐骑或起飞。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：95601059a39b9cd13a48b21a5b0688d494361ac6b11607c79436ad1eba8a693d
