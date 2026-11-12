狩猎助手Hunt Helper 1.0.0.16

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 修正坐标宏的发送路径：提交纯文本聊天指令，让游戏原生解析旗标；不再把显示用地图链接直接嵌入输入。每行发送前设置对应怪物记录的临时旗标，提交后恢复原旗标；准备或发送失败不回退成本地打印。
- 按已确认的“紧凑按钮”布局加入宏集合：标签栏“＋”新增自定义宏，拖动标签固定到上方集合；按钮缩略显示名称与首行正文，悬停看全文，点击发送已保存的宏。支持左右拖动排序，蓝色提示，无黄色拖放框，拖动不会误发；顺序、名称和正文跨重启保留。
- A/S预设继续独立并对应HHT/HHS的坐标按钮。集合使用打开窗口时来源模块当前选中的真实怪物，界面显示来源；纯文字宏无需选择怪物，含变量的宏无目标时拒绝发送。右键可编辑、移出集合，删除自定义宏需确认。
- 移除队伍、喊话及本地回显快捷预设按钮。A/S统一默认模板为 /sh 下一站【<服务器>】<名字>\<flag\>，允许直接修改；旧内置预设仅迁移一次，自定义宏保留。
- 新增 \<name\>、\<map\>、\<lv\>、\<sever\>、\<ins\> 英文别名，大小写不敏感；旧 \<world\>、\<rank\>、\<instance\> 继续兼容。取消 \<x\>/\<y\>，旧宏含这些变量时提示改用旗标链接。变量始终来自选中记录，同服服务器显示“本地”。
- 明确区分本地显示与对外发送：旧的聊天坐标查看选项改标“本地显示（仅自己可见）”；只有明确使用 /echo 或 /e 时才作为本地回显。/sh 等频道提交给原生游戏聊天，界面不宣称其他玩家已收到。批次最多15行，间隔1.2秒，忙时不叠加，取消／登出／换服／卸载清除剩余消息。
- 更新前备份1.0.0.15源码、安装包、订阅和本机配置；固定回退订阅 rollback-1.0.0.15.json。原卫月订阅保持不变，玩家配置及备份不上传。使用说明同步更新。
- 编译与294项自动检查通过：210组回归（含发送管理器到原生提交边界）、43组原生ImGui、30组导航/Lifestream、11组原版分享兼容。尚未游戏内双端验证喊话接收，此次发布供用户测试。

1.0.0.1～15可通过原订阅更新。此前1.0.0.35～39用户需备份并保留配置重新安装。
固定1.0.0.15回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.15.json 。停用原订阅，保留配置卸载后从旧版订阅重装，覆盖配置前另存最新数据。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。名单「移动」需要飞行时，请先正常骑乘并起飞；找怪窗口另有自动上坐骑/可飞行时自动起飞选项。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：0e923863996bc536f9167180a1efb9148beedf1161e969819b046b84ed5aeabd
