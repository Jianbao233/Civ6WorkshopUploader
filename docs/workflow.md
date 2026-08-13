# 推荐工作流：AI 接管 Steam 创意工坊发布

> **仅供参考**。实际工作流应根据你的人机交互习惯、开发习惯与项目文件结构进行开展——本文是作者在自己的 Civ6 项目中沉淀出的一个可行方案，不是唯一答案，也不代表本工具的技术要求。

本工具（`Civ6WorkshopUploader`）是**确定性、非交互式**的 CLI：不弹窗、不要求人工确认、一次调用一个动作。这意味着一个 AI Agent（或脚本）可以完整接管"构建 → 发布 → 维护"链路。下面是一套经过实战验证的分工方式。

## 三个角色

```text
┌─ CLI（new / upload / remove / validate）  执行者：做动作，返回退出码与日志
├─ workspace（一个 mod 一个目录）            状态载体：可被 AI 直接读写修改
└─ 台账（ledger，纯文本 markdown）           记忆中心：AI 跨会话恢复上下文
```

AI 不直接操作 Steam 客户端，只做三件事：**改 workspace 文件 → 调 CLI → 回写台账**。

## workspace 结构

```text
<workspace>/
├── workshop.json   # 元数据：title/description/visibility/changeNote/tags/dependencies/localizations
├── image.png       # 可选：工坊预览图（存在才上传，对齐官方上传器）
├── content/        # mod 本体：.modinfo + 全部引用文件
└── mod_id.txt      # 首次上传后自动写入；永久保留，删除即丢失条目 ID
```

## 六步流程

### 1. 建 workspace（仅首建）

```powershell
Civ6WorkshopUploader.exe new -w <workspace>
```

然后放入真实文件：`workshop.json`、`content/`（完整 mod 目录）、可选 `image.png`。

### 2. 同步 content

把**待发布的构建产物**（staging 目录）1:1 拷入 `content/`。

- 只同步"本次要发布的内容"，来源保持唯一（建议用构建 staging，而不是历史发布包 zip）。
- 同步后跑一次 `validate` 兜底，确认 `.modinfo` 引用的文件都在。

### 3. 预检

```powershell
Civ6WorkshopUploader.exe validate -w <workspace>
```

- 检查 `.modinfo`：Mod ID 是否 GUID、Title/Description 是否非空、引用的每个 `<File>` 是否存在。
- **提示级不阻断**：退出码 `2` 表示有问题但可继续（对齐官方上传器"不上传你拦不住"的现实）；`1` 是硬错误必须先修。
- 语义：validate 是给 AI 看的"问题清单"，不是"上传许可"。

### 4. 上传

```powershell
Civ6WorkshopUploader.exe upload -w <workspace>
```

- 无 `mod_id.txt` → 创建新条目并自动回写 `mod_id.txt`；有则自动走更新路径。同一命令，AI 无需区分。
- 主流程**强制写入 `english` 语言变体**，与上传者 Steam 客户端语言无关——结果确定，不随执行环境漂移。
- 多语言（`schinese` 等）写在 `workshop.json.localizations` 数组，作为廉价的元数据更新自动应用，content/image 不重传。
- 失败处理：**不自动重试**。Steam 限流、账号未登录、条目被删等场景，先报告再决策（重试只会放大问题）。

### 5. 审核（切 public 前）

visibility 从 `private` 切到 `friends_only` / `public` 之前，跑一遍发布审核 checklist（内容随项目而变，至少覆盖）：

- `.modinfo` 结构合法、版本与 changeNote 一致
- 原生二进制是 Release 构建
- 在干净的部署目录实测加载、日志无错误
- 描述用 BBCode（不是 Markdown）、各语言变体排版一致
- dependencies 全部已上线

审核结论归档（建议保留审核报告文件），作为"能上线"的证据链。

### 6. 回写台账

把本次结果登记到台账：条目 ID、visibility、版本、上线语言、操作记录。**实时回写**——台账是 AI 的"外置记忆"，下次会话读台账即可恢复全部上下文，不必再翻 Steam。

## 纪律

| 原则 | 原因 |
|---|---|
| `mod_id.txt` 永不删除 | 删除即丢失条目 ID，无法找回 |
| 台账实时回写 | 跨会话上下文一致性；多条工作线协作时不打架 |
| 无审核报告不设 public | 防止把半成品暴露给玩家 |
| 失败不自动重试 | 防限流/账号问题被循环放大 |
| visibility 变更留痕 | 发布状态可审计、可回溯 |

## 适用边界

- 本文的 workspace 结构、staging 来源、台账格式都是**示例**：请按你的项目文件结构调整（比如你没有 staging 目录，就直接用发布目录；你的台账可能更喜欢 spreadsheet）。
- 工具的命令面与退出码是稳定的契约；工作流是**你的**，工具的用法建议，不是约束。