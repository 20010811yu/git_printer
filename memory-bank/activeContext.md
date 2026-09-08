# 当前上下文 (Active Context)

> 防膨胀说明：本文件执行 `.clinerules/memory-bank.md` §3.3 体积红线（≤150 行），仅描述「现在」；历史焦点/测试记录/历史变更/决策表已归档至 [archive/history-2026-09.md](archive/history-2026-09.md)，git 历史亦可回溯。

## 当前工作焦点

**图像显示链路修复（v1.25e，ERR-029）✅ 已完成** —— 用户反馈「图像管理中还是黑的没有图片」并提供参考程序截图（OnWorkStatusEvent + vmRenderControl.ModuleSource + SyncRun）。落地：
- **排查（多层实证）**：① 桥接 `Run()` → `SyncRun()`（对齐参考程序）——仍间歇无帧；② `KeepModuleLastResult(true)`——无效；③ 桥接新增 `--grab` 调试模式（连续 Run 落盘 PNG + 亮度采样）——实证图像内容正常且尺寸各异（多分支演示方案，部分运行无分支产图属 ERR-028 常态）；④ VM 临时诊断证实 INPC 在 UI 线程正常触发但三个绑定（CurrentImage/CurrentVerdict/StatisticsText）**静默失效**（无异常无日志），而「方案已加载」绑定正常
- **修复（View 轮询兜底）**：图像/结论/统计弃用 INPC 绑定，改 `ImagePage` 内 500ms `Windows.Forms.Timer` 直接同步（`SyncDisplayFromViewModel`，Tick 固定 UI 线程对绑定免疫）；图像所有权移交 View（VM 不再 Dispose 旧图，View 替换引用时释放）；保留 `AttachUiMarshaller`（Control.BeginInvoke）；标题/方案状态绑定正常保留
- **附带**：发现本环境 dotnet 增量构建不可靠（报成功产物陈旧）——此后一律 clean 构建并验证产物符号；net48 无 `Math.Clamp`
- **验证**：dotnet test **191/191 PASS** + clean 构建 0 警告 0 错误；真机实证——图像区显示灰度测试图、统计「总数：13 OK：13」、结论 OK、连续检测稳定
- **下一步：真机联调**（不变）

### 上一焦点（v1.25d，2026-09-08）

**连续检测无新帧跳过修复（ERR-028）✅** —— 方案图像源帧率低于 1s 轮询频率，无新帧的运行输出为空属常态，上层误报为检测错误。修复三层降级：桥接无图+成功 → 成功无图响应；服务层 → `Image=null` OK 结果（新语义）；VM → 仅记 Info 跳过显示 + 输出键枚举回退。桥接新增 DiagLog（`log/VmBridge-diag.log`）。dotnet test **191/191 PASS**；真机 29 完成 + 18 静默跳过 + 0 失败。详 [errorlog.md](errorlog.md) ERR-028 / [archive](archive/history-2026-09.md)。

### 上一焦点（v1.25c，2026-09-08）

**桥接进程带病复用自愈修复（ERR-027）✅** —— VM SDK 授权登录每进程仅一次，桥接进程内失败永久带病；服务层 Load 失败响应即清理桥接进程（下次全新进程重试）；`IsDongleLicenseError` 识别 + `DongleLicenseHint` 指引。破案关键在子进程底层日志（SDK/授权层）。dotnet test **190/190 PASS**；真机端到端实证加载成功 + 连续检测出图。详 [errorlog.md](errorlog.md) ERR-027 / [archive](archive/history-2026-09.md)。

> 更早焦点（v1.25b 及之前 v0.x~v1.25 全部历史）：见 [archive/history-2026-09.md](archive/history-2026-09.md) 第一节。

## 测试记录

> 全部历史测试记录表见 [archive/history-2026-09.md](archive/history-2026-09.md) 第二节；最近 3 次如下。

| 日期 | 任务 | 结果 |
|------|------|------|
| 2026-09-08 | 图像显示链路修复（v1.25e，ERR-029；View 轮询纯 UI 行为，`--grab` 落盘实证） | ✅ 191/191 PASS |
| 2026-09-08 | 连续检测无新帧跳过（v1.25d，ERR-028；+1 用例；真机 29 完成+18 跳过+0 失败） | ✅ 191/191 PASS |
| 2026-09-08 | 桥接带病复用自愈（v1.25c，ERR-027；+10 加密狗识别用例；probe 回归） | ✅ 190/190 PASS |

## 当前处理中的错误

> 仅列编号与状态，详情见 [errorlog.md](errorlog.md)

| 编号 | 错误 | 状态 |
|------|------|------|
| ERR-008 | Cline 终端 `&&` 分隔符不可用（实为 PowerShell） | 🟡 规避中 |
| ERR-009 | dotnet build 输出 GBK 乱码（仅显示问题） | 🟡 规避中 |
| ERR-011 | PowerShell `mkdir` 多参数不可用 | 🟡 规避中 |

> 其余历史错误（ERR-001~029 中已解决的 26 条）均已 🟢 解决并压缩为摘要，详见 errorlog.md；完整过程见 [archive](archive/history-2026-09.md) 第七节。

## 下一步

1. **真机联调**（PLC 真机 192.168.1.88 + VisionMaster 真机出图联调；本地模拟器链路已全部验证）
2. 打印/图像页接入真实服务剩余项（IPrintService 已完成；图像 VisionMaster 桥接已完成——剩余为现场参数调优）
3. ~~用真实 PLC 通信实现替换 `MockDrawerService`~~ 大部分已落地（v1.10~v1.12 PLC 通讯+物料接入）；剩余 = 配方下发等写方向的真实 PLC 版
4. 配方管理页增强：抽屉列表显示配方名/状态列、批量下发

## 重要模式与偏好

- 语言：中文（代码注释、日志、UI 文案）
- 严格 MVVM：View 零业务逻辑，硬件交互全在 Service，统一 `Result<T>` 返回
- 异步规范：所有 IO 用 async/await，命令带 IsBusy 防重复
- UI 线程调度：`SynchronizationContext.Post`（构造时捕获 `_uiContext`，ERR-023 模式）
- 导航模式：VM 持有 CurrentPage 状态，View 订阅 PropertyChanged 切换可见性；Tab 点击经参数化命令回传 PageType
- **防膨胀工作流（v2.2 新增）**：写入新焦点 = 归档旧焦点（archive/history-YYYY-MM.md）；核心文件遵守 §3.3 体积红线，P4 交付前核对

## 经验索引

- **错误类教训**：全部归档 → [errorlog.md](errorlog.md)（防回归清单编码前必查）
- **模式沉淀**：[systemPatterns.md](systemPatterns.md)「设计模式」「已知陷阱与规避模式」
- **API 技巧**：主体已沉淀 systemPatterns / techContext；补充细节见 [archive](archive/history-2026-09.md) 第四节
- **用户已确认的需求决策**：见 [archive](archive/history-2026-09.md) 第三节决策表