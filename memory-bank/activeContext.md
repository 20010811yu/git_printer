# 当前上下文 (Active Context)

> 防膨胀说明：本文件执行 `.clinerules/memory-bank.md` §3.3 体积红线（≤150 行），仅描述「现在」；历史焦点/测试记录/历史变更/决策表已归档至 [archive/history-2026-09.md](archive/history-2026-09.md)，git 历史亦可回溯。

## 当前工作焦点

**界面整体调整（v1.29）✅ 已完成** —— 用户三项要求：① 进料抽屉配方输入框加宽（宽度上限 220→300px、字体 11→12f）；② 主窗口可拉伸——无边框窗体新增 `WndProc` WM_NCHITTEST 边缘命中（窗体留 8px Padding 外沿，八方向命中码，最大化不生效，ERR-033）；③ 全局字体放大 +1~2pt（窗体基字 11→12f，四页标题 20→22f，打印/图像/抽屉正文与 Tab/日志面板/弹窗同步上调）。196/196 测试全绿。注：检测功能已随加密狗恢复实测正常（真机轮询运行）。

### 上一焦点（v1.28/28b，2026-09-09）

**图像页「保存图片」应用层实现 + 保存对话框失控修复（ERR-032/32a）✅** —— 右键菜单保存崩 OpenCvSharp（原生库未分发）→ GDI+ 自实现 SaveImageCommand（Clone 后台落盘）+ 成功/失败/无图弹窗；SaveFileDialog 误放 Bind 参数提供器致进页即弹 → 改 `SavePathRequestEventArgs` 请求回填模式 + 命令无参化。详 [errorlog.md](errorlog.md) ERR-032/32a。

### 上一焦点（v1.27，2026-09-08）

**图像页按钮绑定修复（ERR-031）✅** —— v1.26 重构遗漏三个检测按钮 `CommandManagerHelper.Bind` 绑定，点击无响应永无图；补回绑定 + View 绑定守护用例。真机单次/连续检测出图实证。详 [errorlog.md](errorlog.md) ERR-031。

> 更早焦点（v1.26 及之前 v0.x~v1.25 全部历史）：见 [archive/history-2026-09.md](archive/history-2026-09.md) 第一节。

## 测试记录

> 全部历史测试记录表见 [archive/history-2026-09.md](archive/history-2026-09.md) 第二节；最近 3 次如下。

| 日期 | 任务 | 结果 |
|------|------|------|
| 2026-09-09 | 界面整体调整（v1.29：输入框加宽+窗口可拉伸+全局字体放大；纯 View 改动） | ✅ 196/196 PASS |
| 2026-09-09 | 保存对话框失控修复（v1.28b，ERR-032a；+1 守护用例） | ✅ 196/196 PASS |
| 2026-09-09 | 图像页保存图片应用层实现（v1.28，ERR-032；+3 用例；真机对话框实测） | ✅ 195/195 PASS |

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