狩猎助手Hunt Helper 1.0.0.13

适配国服 XIVLauncherCN / Dalamud API 15；按本机 Dalamud 15.0.3.9 编译。

- 移除路线录制和编辑，固定按内置预设点位顺序巡查；旧ScanRoutes.json保留供回退，新版不读写。
- 新增多服务器找怪：按勾选顺序逐服执行，需要下载并启用国服API15版Lifestream。核对目标服已到达、加载完成且Lifestream空闲后才扫描。失败、超时或停止会终止队列，完成后留在最后一服。
- 本服本图本线最近60分钟的现场存活记录计入已发现，齐全跳过；只有一只继续找另一种。Sonar、导入、未知/过期时间、死亡和其它服/线不充当已扫证据。
- 智能选择靠近下一预设点的已解锁水晶；同图比较直达和传送后移动的估计耗时，至少节省10秒且20%才传送，60秒内不重复同图传送。读条加载默认估计25秒，可调整；实际分线变化会停止。
- 按本图实际A怪探测三维距离保守估计停靠范围；已确认点位附近怪物后不再继续飞到点位，远距未确认仍抵近补扫，不能把未加载当作未刷新。
- 夏劳尼荒野实际飞行时临时巡点目标上抬20米，减少贴地遭遇；不改变记录坐标或其它地图，仍由vnavmesh寻路，不保证完全免战斗。
- 固定顶部开始/停止按钮，保留服务器/分线隔离、A/S录制、分享码、地图顺序、拖拽和备份撤销。中文使用手册同步更新。

1.0.0.1～12可通过原订阅更新。此前1.0.0.35～39用户需备份并保留配置重新安装。
更新前备份1.0.0.12源码、包、订阅及现有玩家文件；旧版Release保留，玩家数据与运行时快照不上传。
固定1.0.0.12回退订阅：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/rollback-1.0.0.12.json 。停用原订阅，保留配置卸载后从旧版订阅重装，覆盖文件前另存最新数据。

卫月第三方插件库订阅地址：
https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json

该版本 InternalName 为 HuntHelperCN，界面名称为 狩猎助手Hunt Helper。请停用不再使用的旧版，避免重复显示狩猎窗口。
重新安装前备份 pluginConfigs/HuntHelperCN.json 和 pluginConfigs/HuntHelperCN/ 目录（HuntTrain.json、SRankTrain.json、ScanRoutes.json）。安装包不包含玩家记录，更新时请保留原有记录。
国服 vnavmesh：https://github.com/AtmoOmen/ffxiv_navmesh-cn 。未安装或网格未就绪时「移动」不可用。名单「移动」需要飞行时，请先正常骑乘并起飞；找怪窗口另有自动上坐骑/可飞行时自动起飞选项。

验证范围请参见仓库 README-CN.md；尚未进行游戏内实测。

原作者 img02。基于 https://github.com/img02/HuntHelper。
MIT License，Copyright (c) 2022 imaginary-png；完整许可随安装包提供。

latest.zip SHA-256：afa90fabea3832afa50aeaa50e19aa3b197fc038995aca12f549423a56edecfe
