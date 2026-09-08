# 当前上下文 (Active Context)

> 防膨胀说明：本文件执行 `.clinerules/memory-bank.md` §3.3 体积红线（≤150 行），仅描述「现在」；历史焦点/测试记录/历史变更/决策表已归档至 [archive/history-2026-09.md](archive/history-2026-09.md)，git 历史亦可回溯。

## 当前工作焦点

**图像页 v1.26 重构（ERR-030）✅ 已完成** —— 用户需求：删除「加载方案」按钮、程序启动自动加载、删除总数/OK/NG 统计显示、用 VMRenderControl 显示方案图（可拖拽平移、滚轮缩放）。落地：
- **关键探索（冒烟实证）**：net10.0 直引 VM SDK 引擎（VM.Core/VM.PlatformSDKCS）→ `VmRenderControl` 实例化 OK 但 `VmSolution.Load` **原生层无声崩溃**（引擎为 C++/CLI 混合程序集链，详见 [errorlog.md](errorlog.md) ERR-030）→ 采用**混合架构**：检测引擎留桥接进程（PNG 回传不变），显示层引入纯托管的 `VmRenderControl`（VMControls）——自实现 `IImageData`（`Views/Controls/VmBitmapImageData.cs`）包装 Bitmap 像素喂 `ImageSource`，**拖拽平移 + 滚轮缩放为控件内置能力**；控件静态依赖 VM.PlatformSDKCS（GAC）由 Program.cs `AssemblyResolve` 兜底补加载
- **改动**：`ImagePage`（删按钮/删统计/PictureBox→VmRenderControl）、`ImagePageViewModel`（删 LoadSolutionCommand/删 OkCount/NgCount/StatisticsText）、`MainForm.Load` 追加 `_imageViewModel.InitializeAsync()`（**启动即自动加载**，页面内调用幂等）、`Program.cs`（GAC resolver）、csproj（VMControls 直引）、`ImageInspectionResult.Image` 改 `Image?`（ERR-028 null 语义补正）
- **验证**：删 bin/obj 强制重建 0 警告 0 错误 + **191/191 测试全绿** + 产物符号实证（VMControls DLL 7 个随程序分发）
- **下一步：真机联调**（不变；图像页新版待用户真机确认平移/缩放手感与渲染效果）

### 上一焦点（v1.25e，2026-09-08）

**图像显示链路修复（ERR-029）✅** —— INPC 三绑定静默失效（黑屏），改 View 500ms `Windows.Forms.Timer` 轮询兜底（`SyncDisplayFromViewModel`，对线程/编组/绑定免疫）；图像所有权移交 View。dotnet test 191/191；真机实证出图。详 [errorlog.md](errorlog.md) ERR-029。

### 上一焦点（v1.25d，2026-09-08）

**连续检测无新帧跳过（ERR-028）✅** —— 桥接无图+成功 → 成功无图响应三层降级；新增 DiagLog。191/191 PASS；真机 29 完成+18 跳过。详 [errorlog.md](errorlog.md) ERR-028。

> 更早焦点（v1.25c 及之前 v0.x~v1.25 全部历史）：见 [archive/history-2026-09.md](archive/history-2026-09.md) 第一节。

## 测试记录

> 全部历史测试记录表见 [archive/history-2026-09.md](archive/history-2026-09.md) 第二节；最近 3 次如下。

| 日期 | 任务 | 结果 |
|------|------|------|
| 2026-09-08 | 图像页 v1.26（删按钮/启动自动加载/删统计/VmRenderControl 混合显示；测试桩改 Static/Shutdown 语义改引用稳定） | ✅ 191/191 PASS |
| 2026-09-08 | 图像显示链路修复（v1.25e，ERR-029；View 轮询纯 UI 行为，`--grab` 落盘实证） | ✅ 191/191 PASS |
| 2026-09-08 | 连续检测无新帧跳过（v1.25d，ERR-028；+1 用例；真机 29 完成+18 跳过+0 失败） | ✅ 191/191 PASS |

## 当前处理中的错误

> 仅列编号与状态，详情见 [errorlog.md](errorlog.md)

| 编号 | 错误 | 状态 |
|------|------|------|
| ERR-008 | Cline 终端 `&&` 分隔符不可用（实为 PowerShell） | 🟡 规避中 |
| ERR-009 | dotnet build 输出 GBK 乱码（仅显示问题） | 🟡 规避中 |
| ERR-011 | PowerShell `mkdir` 多参数不可用 | 🟡 规避中 |
| ERR-030 | net10.0 直引 VM 引擎原生崩溃（混合架构规避） | 🟡 规避中 |

> 其余历史错误（已解决的 26 条）均已 🟢 压缩为摘要，详见 errorlog.md；完整过程见 [archive](archive/history-2026-09.md) 第七节。

## 下一步

1. **真机联调**（PLC 真机 192.168.1.88 + VisionMaster 真机出图联调；图像页 v1.26 新显示层待真机确认）
2. 打印/图像页接入真实服务剩余项（IPrintService 已完成；图像 VisionMaster 桥接已完成——剩余为现场参数调优）
3. 配方管理页增强：抽屉列表显示配方名/状态列、批量下发
4. ~~用真实 PLC 通信实现替换 `MockDrawerService`~~ 大部分已落地（v1.10~v1.12）；剩余 = 配方下发等写方向的真实 PLC 版

## 重要模式与偏好

- 语言：中文（代码注释、日志、UI 文案）
- 严格 MVVM：View 零业务逻辑，硬件交互全在 Service，统一 `Result<T>` 返回
- 异步规范：所有 IO 用 async/await，命令带 IsBusy 防重复
- UI 线程调度：`AttachUiMarshaller`（Control.BeginInvoke，ERR-028）+ 构造期 `_uiContext` 兜底
- 导航模式：VM 持有 CurrentPage 状态，View 订阅 PropertyChanged 切换可见性；Tab 点击经参数化命令回传 PageType
- **混合渲染架构（v1.26）**：引擎（桥接进程）与控件（主程序）分离——引擎不跨运行时、控件纯托管可直引；GAC 依赖 `AssemblyResolve` 兜底
- **防膨胀工作流（v2.2）**：写入新焦点 = 归档旧焦点（archive/history-YYYY-MM.md）；核心文件遵守 §3.3 体积红线，P4 交付前核对

## 经验索引

- **错误类教训**：全部归档 → [errorlog.md](errorlog.md)（防回归清单编码前必查）
- **模式沉淀**：[systemPatterns.md](systemPatterns.md)「设计模式」「已知陷阱与规避模式」
- **API 技巧**：主体已沉淀 systemPatterns / techContext；补充细节见 [archive](archive/history-2026-09.md) 第四节
- **用户已确认的需求决策**：见 [archive](archive/history-2026-09.md) 第三节决策表