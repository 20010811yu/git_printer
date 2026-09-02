# 技能：可靠的搜索 / 替换工具包（replace-search）

> 整理自：`.skills/规则片段.md`
> **适用场景**：中文 / CRLF / C# 项目中，内置 `search_codebase`、`editor` 替换模式出现**空返回或匹配失败**时，改用本技能的命令行工具与工作流。

## 一、可靠搜索替代

| 工具 | 用法 | 说明 |
|:-----|:-----|:-----|
| `findstr`（系统自带） | `findstr /S /N "关键字" *.cs` | 递归子目录；把 `.` `*` `(` 当普通字符；中文稳定 |
| `Select-String`（PowerShell 内置） | `Select-String -Path *.cs -Pattern '关键字' -Encoding UTF8` | 支持正则、可指定编码 |
| 一键脚本 | `powershell -ExecutionPolicy Bypass -File 'skill\replace_or_search.ps1' -Action search -Pattern 关键字` | 字面量逐行匹配，结果带 `文件:行号:` |

## 二、可靠替换替代

| 场景 | 推荐做法 |
|:-----|:---------|
| 单文件少量改动 | `editor` 整文件重写模式（只传 path+new_text，省略 old_text） |
| 批量/跨文件替换 | `powershell -ExecutionPolicy Bypass -File 'skill\replace_or_search.ps1' -Action replace -Pattern 旧 -Replacement 新` |
| 只想预览不落地 | 脚本加 `-DryRun` |

⚠️ **编码是关键**：`.cs` 一般是 UTF-8（有/无 BOM 均可用 .NET File API 安全处理）。**禁止**用 PowerShell 5.1 `Set-Content` 默认编码（按 ANSI 写，中文乱码）。

## 三、高效定位 + 精确编辑工作流（省 token、不空返回）

改代码遵循「**先定位 → 精确读 → 精确改**」：

| 步骤 | 工具 | 说明 |
|:-----|:-----|:-----|
| ① 定位 | 找**定义**用 `-Action defs`；找**任意出现**用 `findstr /S /N "关键词" *.cs` | 拿 `文件:行号`；defs 只命中定义行（跳过调用点/事件连线），中文/CRLF 稳定 |
| ② 精确读 | `read_files` 用 start_line/end_line | 只读小段上下文，别整文件读 |
| ③ 精确改 | `editor` 按下表选模式 | |

| 场景 | `editor` 模式 | 省 token | 可靠性 |
|:-----|:--------------|:--------:|:------:|
| 新增/插入 | `insert_line`（只传 path+insert_line+new_text，不传 old_text） | ✅ 最大省 | ✅ 按行号最可靠 |
| 小段替换（≤5行） | old_text 替换定位到的局部小段 | ✅ 省 | ⚠️ 中文/CRLF 可能失败 |
| 大段重写 | 整文件重写（path+new_text） | ❌ 最费 | ✅ 最高 |

## 四、写文件优先用 `editor`，防 `run_commands` JSON 反斜杠边界

`run_commands` 以 JSON 传命令，`\` 是 JSON 转义符：路径单反斜杠会被剥（`scripts\publish`→`scriptspublish`）、`\n` 变真换行。所以：

1. 写/改含路径或换行的文本 → 用 `editor`（new_text/old_text 字面量），别塞进 shell 命令字符串。
2. 必须走 PowerShell 时：路径反斜杠双写 `\\`；换行用 `` `r`n ``，别写 `\n`。
3. 写完回读核对（防反斜杠/换行/中文被破坏）。

## 🚫 铁律

1. `insert_line` 只能插入，不能删/改已有行；改/删已有行用局部 old_text 或整文件重写。
2. 改已有行先 findstr 定位到那一行，再只对那 1~5 行做 old_text 替换。
3. **编辑前先 read_files 确认原文，编辑后回读验证（防覆写）。**
4. PowerShell 双引号字符串里反引号是转义符（`` `d `` → `d`），含反引号的文本用单引号或 editor old_text，别用双引号 -replace。

## 💡 本项目实测补充（Cline 环境经验）

- 本项目 Cline 终端实际为 **PowerShell**（非 cmd），`mkdir` 多参数不可用 → 用 `New-Item -ItemType Directory -Force -Path "a","b"`
- `dotnet build` 输出为 GBK 乱码，管道接 `| Out-String` 提升可读性（凭「0 个警告 0 个错误」辨结果）
- 搜索建议直接用 `Select-String -Pattern 'xxx' -Path *.cs` 或 Cline 内置 `search_files` 工具