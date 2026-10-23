# 发布国服定制版

源代码基于 [img02/HuntHelper](https://github.com/img02/HuntHelper)，保留上游作者与 MIT 许可。发布前先完成 `README-CN.md` 中的 Release 编译和对应验证；发布脚本只验证及准备产物，不执行 Git 提交、推送、仓库创建或上传。

## 准备清单与发布附件

在仓库根目录运行（将 `你的账号/你的仓库` 换成实际的 GitHub `owner/repo`）：

```powershell
pwsh -File ./scripts/Prepare-Release.ps1 -Repository '你的账号/你的仓库'
```

需要 PowerShell 7（推荐）或 Windows PowerShell 5.1。脚本以 UTF-8 BOM 编码保存以兼容两者，仅使用 PowerShell 与 .NET，不需要额外包。

脚本从项目 `AssemblyName` 读取插件内部名称（缺省时为 `HuntHelper`），再读取 `HuntHelper/bin/x64/Release/内部名称/内部名称.json` 和 `latest.zip`。也可以用 `-InternalName 'HuntHelperCN'` 显式指定，但必须与项目一致，避免误发旧包。脚本检查 API 15、项目与安装包版本、ZIP 内外清单一致、DLL/简体中文资源/原始 MIT 许可存在，并拒绝包含玩家 `HuntTrain.json` 的安装包。通过后生成：

- 根目录 `pluginmaster.json`：卫月使用的 JSON 数组清单。保留 `Author=img02` 及包内名称/内部名称；仓库和下载链接指向指定的 GitHub 仓库。
- `obj/release-staging/v版本/`：`latest.zip` 副本、原始插件 JSON 清单、订阅清单副本、`SHA256SUMS.txt` 及中文发布说明。该目录受现有 `.gitignore` 的 `obj/` 规则排除；脚本不修改编译产生的原包。

## GitHub 发布

1. 使用**公开仓库**，确保源码、原始 `LICENSE`、`README-CN.md`、发布脚本与根目录 `pluginmaster.json` 已提交到 `main` 分支。
2. 按脚本输出的 tag 创建 GitHub Release，例如 `v1.0.0.35`，目标指向包含对应源码的提交。可使用暂存目录里的中文发布说明。
3. 把暂存目录的 `latest.zip`、插件 JSON 清单、`SHA256SUMS.txt` 作为 Release 附件上传，保留 `latest.zip` 文件名。清单中的安装、更新、测试下载链接均固定到该版本 tag。
4. 发布 Release 后，确认 `releases/download/v版本/latest.zip` 可匿名下载，并核对附件 SHA-256。确认 `main/pluginmaster.json` 的 raw 链接可匿名返回 JSON 数组，数组版本与 Release 附件一致。

脚本生成的 `RepoUrl` 为你的定制仓库地址；原始包内清单、作者、许可证及上游图片来源保持原样，上游来源在订阅说明与 Release 说明中保留。

## 卫月第三方库链接

在卫月设置的第三方插件库输入框里添加这个形式的完整地址：

```text
https://raw.githubusercontent.com/你的账号/你的仓库/main/pluginmaster.json
```

**订阅链接必须指向公开仓库 `main` 分支的原始 JSON 清单。** GitHub 仓库主页、Release 页面和 `latest.zip` 都不是订阅链接。脚本会输出使用实际仓库名的完整地址。

发布时核对清单中的 `InternalName`：独立定制版应使用 `HuntHelperCN`，这样可与官方 `HuntHelper` 分开识别；请停用不再使用的旧版，避免重复窗口。如果仍使用与原版相同的 `HuntHelper`，应择一来源安装，并留意官方源的优先规则可能阻止第三方更新。升级前备份原插件目录内 `Data/HuntTrain.json`；安装包不应携带任何玩家记录。使用移动功能还需另外安装并启用 vnavmesh。

## 后续更新

修改项目版本、重新编译并验证，再运行脚本。上传新的版本 Release 和附件后更新 `main` 中的 `pluginmaster.json`，用户继续使用相同订阅链接。先确认对应附件可下载，再向用户宣布新清单可用；避免清单指向尚未发布的安装包。
