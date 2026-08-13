# AGENTS.md — Civ6WorkshopUploader

本文件面向**接入本工具的开发者与 AI Agent**：说明命令面、workspace 约定、AI 如何接管 Civ6 创意工坊的完整发布/维护流程。

## 这是什么

`Civ6WorkshopUploader` 是一个确定性、非交互式的命令行工具，用于创建、更新、删除、校验 **Sid Meier's Civilization VI**（AppId `289070`）的 Steam 创意工坊条目，并抓取工坊留言。它不内置 GUI、不弹窗、不要求人工确认，一次调用一个动作——专为让 AI Agent（或脚本）完整接管发布流程而设计。

结构参考 [megacrit/sts2-mod-uploader](https://github.com/megacrit/sts2-mod-uploader)，代码为本项目独立实现（Civ6 适配 + `validate`/`comments` 增强）。

## 命令面

```text
Civ6WorkshopUploader.exe new -w <dir>                        # 从模板创建 workspace
Civ6WorkshopUploader.exe upload -w <dir> [-i <id>]           # 创建新条目或更新已有条目
Civ6WorkshopUploader.exe validate -w <dir>                   # 上传前预检 .modinfo（GUID/标题/引用文件）
Civ6WorkshopUploader.exe remove -w <dir> [-i <id>]           # 删除工坊条目（不可逆）
Civ6WorkshopUploader.exe comments -i <id>|-w <dir> [--since YYYY-MM-DD] [--until YYYY-MM-DD] [-o out.json] [--cookie "..."] [--proxy url]
                                                             # 拉取工坊留言（纯 HTTP，公开条目无需 Steam 登录）
Civ6WorkshopUploader.exe <目录路径>                          # 快捷方式 = upload -w <目录路径>
```

**退出码**：`0` 成功；`1` 硬错误（缺文件、参数错误、SteamAPI 初始化失败、条目不存在）；`2` validate 发现提示级问题（不阻断）或 comments 被 Steam 拒绝。

**日志**：全部输出同时写入 `civ6-uploader.log`（exe 旁）与控制台，Agent 可事后读取。

## workspace 约定

```text
<workspace>/
├── workshop.json   # 元数据：title/description/visibility/changeNote/tags/dependencies/localizations
├── image.png       # 工坊封面——必填，真实图片（PNG 方形 ≤1 MB）；工具不内置占位图
├── content/        # Civ6 mod 目录本身：.modinfo + 其引用的全部文件（Binaries/Data/UI/...）
└── mod_id.txt      # 首次上传后自动写入；永久保留，删除即丢失条目 ID
```

- 新建 workspace 用 `new -w`；把 mod 文件放入 `content/`；`image.png` 与 `workshop.json` 必须自行提供。
- **`mod_id.txt` 是条目的唯一身份凭证**：更新时上传器从它读取 ID，请勿删除，建议在外部台账冗余记录。

## AI 接管完整发布流程

1. 构建 mod 到 `content/`（.modinfo + 引用文件完整）。
2. 写 `workshop.json`：英文 title/description/changeNote 写顶层（主流程强制写入 `english` 语言变体，与上传者 Steam 客户端语言无关）；其它语言写 `localizations` 数组（如 `schinese`），每项 `language` + 可选 `title`/`description`/`changeNote`。
3. 提供 `image.png` 真实封面。
4. 预检：`validate -w <ws>` → 按退出码 0/2 决定是否继续（1 必须先修）。
5. 上传：`upload -w <ws>` → 成功返回 0 并写 `mod_id.txt`；`visibility` 由 `workshop.json` 控制（首建建议 `"private"`，确认无误后再切 `public` 重新上传）。
6. 后续更新：同步 `content/` 与 `workshop.json`，同一命令 `upload -w <ws>` 自动走更新路径。
7. 日常维护：`comments -w <ws> --since <日期> -o <out.json>` 批量收集玩家反馈。

### 留言抓取（comments）

- 纯 HTTP，不初始化 Steam；`-i` 直接给条目 ID，或 `-w` 读 workspace 的 `mod_id.txt`。
- `--since`/`--until` 按 UTC 日期过滤（分页自动提前终止）；`-o` 输出完整 JSON（每条含 comment_id/author/author_steamid/timestamp/body）。
- 网络：默认读 `HTTPS_PROXY`/`HTTP_PROXY` 环境变量与系统代理；可用 `--proxy http://127.0.0.1:7897` 显式指定。
- Steam 可能封锁匿名/数据中心出口（返回 `This profile is private.`，退出码 2）：用 `--cookie "<steamcommunity.com 的 Cookie 头>"`（浏览器 F12 → Network → steamcommunity.com 请求 → Cookie）提供登录会话后重试。

## 前置条件（upload / remove）

- Steam 客户端已启动并**前台登录**，账号拥有 Sid Meier's Civilization VI（AppId `289070`），否则 `SteamAPI.Init` 失败。
- `SteamAPI.Init` 使用的 `steam_appid.txt` 是 Civ6 SDK 工具 depot（`404350`，与官方上传器一致）；工坊条目归属 AppId 是 `289070`。两者不同是设计如此，勿改。
- 上传失败不要自动重试，先报告（Steam 后端限流/超时场景见 `README.md` 与工具输出）。
- `remove` 不可逆，执行前确认条目 ID。

## 构建

```powershell
dotnet publish -c Release -r win-x64 -p:PublishTrimmed=true --artifacts-path artifacts
.\artifacts\publish\Civ6WorkshopUploader\release_win-x64\Civ6WorkshopUploader.exe --help
```

依赖 `steam/steam_api64.dll` 与 `steam/steam_appid.txt`，产物目录需保持完整；运行 exe 时请留在该目录（或携带全部文件）。

## 行为边界

- 上传内容来自 `content/` 目录本身；`description` 使用 Steam 工坊 **BBCode**（不是 Markdown）。
- `visibility` 取值：`private` / `friends_only` / `unlisted` / `public`。
- `dependencies` 为工坊条目 ID 数组，上传时自动 diff 增删。
- 多语言变体（`localizations`）为纯元数据更新，不重传 `content/` 与 `image.png`。