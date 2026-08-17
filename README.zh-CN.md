# Civ6WorkshopUploader

> [English](README.md) | **简体中文**

用于创建、更新和删除 **《文明 VI》（Sid Meier's Civilization VI）** Steam 创意工坊条目的 CLI 工具，设计目标是让 AI Agent（或人类）能从命令行完整驱动"发布 → 维护"流程。

结构参考：[megacrit/sts2-mod-uploader](https://github.com/megacrit/sts2-mod-uploader)。本项目是为文明六做的独立实现，不是其代码的拷贝。

## 命令

```text
Civ6WorkshopUploader.exe new -w <dir>              从模板创建新的 workspace
Civ6WorkshopUploader.exe upload -w <dir> [-i <id>]  上传新条目或更新已有条目
Civ6WorkshopUploader.exe validate -w <dir>          上传前建议性检查（modinfo + 引用文件）
Civ6WorkshopUploader.exe remove -w <dir> [-i <id>]  从工坊删除条目
```

直接传目录路径也可以，等价于 `upload -w <dir>` 的快捷方式。

## Workspace 结构

```text
<workspace>/
├── workshop.json   # 元数据：title、description、visibility、changeNote、tags、dependencies、localizations
├── image.png       # 工坊预览图——可选（存在才上传，与官方上传器行为一致）
├── content/        # Civ6 mod 本体目录（.modinfo + Binaries/ + Data/ + UI/ 等）
└── mod_id.txt      # 首次上传后自动写入；切勿删除（删除即丢失条目 ID）
```

## 关键事实

- 条目创建在 Civ6 的 app id `289070` 下；工具自身注册为 Civ6 SDK 工具 depot（`404350`，见 `steam/steam_appid.txt`）。
- 主更新流程**始终写入 `english` 语言变体**，与你的 Steam 客户端语言无关。其他语言通过 `workshop.json` 的 `localizations` 数组写入，作为廉价的纯元数据更新应用。
- `SteamAPI.Init` 要求 Steam 客户端正在运行且已登录，账号需拥有 Civ6。

## 构建

```powershell
dotnet publish -c Release -r win-x64 -p:PublishTrimmed=true --artifacts-path artifacts
.\artifacts\publish\Civ6WorkshopUploader\release_win-x64\Civ6WorkshopUploader.exe --help
```

项目层面支持 Linux/macOS（条件性拷贝 steam 库），但本地只实测过 win-x64。

## 配合 AI Agent 使用

本工具是确定性的、非交互式的：一次调用一个动作，成功退出码为 0，日志写入可执行文件旁的 `civ6-uploader.log`。因此 Agent 可以完整接管发布流程——从 staging 构建 `content/`、运行 `validate`、再 `upload -w <workspace>`——之后回写工坊台账。

另见：

- [`docs/workflow.md`](docs/workflow.md) — 一个 AI 驱动发布工作流的实战示例（仅供参考，请按你自己的习惯与项目结构调整）。
- [`docs/ledger-template.md`](docs/ledger-template.md) — 追踪工坊条目的最小 markdown 台账模板（仅供参考）。

## 许可证

MIT
