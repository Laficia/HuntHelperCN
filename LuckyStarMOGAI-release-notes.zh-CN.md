LuckyStarMOGAI 0.0.0.10，国服卫月 API 15。

- 恢复原版紧凑 UI：停止／状态、首怪延迟、开发者模式、农怪页签；六个原版项目保留勾选框与独立 Go 按钮。
- 在伊休妲与优昙婆罗花之间新增「红玉海 - 巨大鳐」一行。
- 将循环插件下拉选择放在首怪延迟选项右侧，可选 AE、RotationSolverReborn、Wrath Combo、PromeRotation。
- 打开指令与原版一致：`/luckystar`；支持 `start`、`stop`、`status` 子命令，旧 `/luckystarmogai` 保留兼容。请停用原版 LuckyStar 后使用，避免同名指令被占用。
- 插件描述改为：基于NiGuangOwO/LuckyStar新增红玉海农怪循环。
- 骑乘、飞行、洞穴选择与循环托管设置保留在「开发者模式 → Debug → 运行设置」。已有配置与自动农怪逻辑保留。

本次主界面与四项循环下拉菜单已在游戏内预览核对。133 项逻辑回归检查通过，Release 编译零警告、零错误。0.0.0.9 的完整三洞循环实测结果仍保留于源码说明；本次未重新进行整场数量目标实测。

安装：卫月插件安装器刷新现有第三方仓库，搜索并更新 LuckyStarMOGAI。
订阅地址：https://raw.githubusercontent.com/Laficia/HuntHelperCN/main/pluginmaster.json
需要国服 vnavmesh 与已配置的循环插件。数量完成不代表确认 S 怪刷新。

基于 NiGuangOwO/LuckyStar，保留原作者署名与 AGPL-3.0-or-later。完整源码、构建说明、许可证和 SHA-256 校验值随附件提供，仓库 LuckyStarMOGAI/ 目录包含对应源码。ECommons 采用 MIT 许可。
