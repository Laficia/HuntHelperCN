狩猎助手Hunt Helper 1.0.0.15

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 修复“移动”被 /vnav stop 停止后重新启动：改用国服 vnavmesh 原生 SimpleMove，寻路与执行由同一原生任务管理；遵循其手动输入取消、暂停、镜头和容差设置，不再由 HHT 接管迟到路线。外部中止后不会自动重启找怪。
- HHT/HHS 每条记录增加“坐标”；顶部增加“预设宏”，A/S 两类独立保存，提供队伍、喊话和本地回显预设。只有点击坐标才发送，最多15行，按1.2秒间隔交给游戏聊天。
- 宏支持 <flag>、<服务器>、<名字>、<等级>、<地图>、<分线>、<x>、<y>。点位与服务器始终来自点击的怪物记录；同当前所在服时 <服务器> 输出“本地”。S默认：/sh 下一站【<服务器>】S怪<flag>，请等待触发倒计时/定，不要抢开！
- 确认导入 A 记录时，按同服务器、地图、分线优化地图距离车序；当前同图同线从玩家位置出发，其它图以已解锁水晶为候选入口。12条以内求最短开放路线，较大自定义组用近邻与局部改进；不承诺地形实际飞行最快。组间位置不变，S导入不重排，仍可拖动与撤销。
- 默认同步 Sonar 明确的 A 怪击杀播报：不要求录制开启，只标记能唯一匹配的已有服务器／地图／分线记录。缺少或冲突的信息跳过并提示；不新建记录。单独保存击杀接收时间，保持原坐标、来源和现场时间。
- 旧版1.0.0.14源码、安装包、订阅和本机玩家配置已备份；固定回退订阅 rollback-1.0.0.14.json。原更新订阅不变，中文使用手册同步更新。
- 验证：183组回归（含真实 Sonar 聊天处理入口）、35组原生ImGui、24组导航/Lifestream IPC、11组原版分享兼容，共253项。使用隔离宿主与本机国服SDK，未进行游戏内发送、寻路、跨服实测。

1.0.0.1～14可通过原订阅更新。此前1.0.0.35～39用户需备份并保留配置重新安装。
固定1.0.0.14回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.14.json 。停用原订阅，保留配置卸载后从旧版订阅重装，覆盖文件前另存最新数据。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。名单「移动」需要飞行时，请先正常骑乘并起飞；找怪窗口另有自动上坐骑/可飞行时自动起飞选项。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：7d2543db137d0d4746e4e13fbf8cc7aaf1ad975a35c0b6a5a6d46fc9d8f3c020
