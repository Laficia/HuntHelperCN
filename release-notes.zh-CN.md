狩猎助手Hunt Helper 1.0.0.14

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 修复国服大区和服务器列表为空：有效国服服务器的IsPublic标志为false，现按正式服务器ID和有效国服大区归属读取，排除旧合并服及测试条目。
- 已只读核对本机国服数据：陆行鸟8服、莫古力8服、猫小胖7服、豆豆柴6服，共4大区29服；界面运行时按实际游戏数据加载。
- 采用已确认的列表预览：默认当前大区，支持全部大区，宽窗口双列显示；下方保留勾选顺序队列，切换大区不会清空选择，重选追加到末尾。
- 增加刷新服务器列表、空名单及读取错误提示。共用名单同时修正Sonar新服识别和导入目标选择。
- 保留Lifestream多服找怪、固定预设路线、近期现场记录齐全时跳过、智能水晶与远距补扫、夏劳尼实际飞行时目标上抬20米。
- 246项自动检查通过，并只读解析实际国服客户端World表；尚未进行游戏内扫描、跨服或飞行实测。中文手册同步更新。

1.0.0.1～13可通过原订阅更新。此前1.0.0.35～39用户需备份并保留配置重新安装。
更新前备份1.0.0.13源码、包、订阅及现有玩家文件；旧版Release保留，玩家数据与运行时快照不上传。
固定1.0.0.13回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.13.json 。停用原订阅，保留配置卸载后从旧版订阅重装，覆盖文件前另存最新数据。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。名单「移动」需要飞行时，请先正常骑乘并起飞；找怪窗口另有自动上坐骑/可飞行时自动起飞选项。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：ab77e3350d6b1482edfd2dee6e4b07a66e767d1fc31837cbbfd10e1af4ba6e10
