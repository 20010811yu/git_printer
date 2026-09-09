# 当前上下文 (Active Context)

> 防膨胀说明：本文件执行 `.clinerules/memory-bank.md` §3.3 体积红线（≤150 行），仅描述「现在」；历史焦点/测试记录/历史变更/决策表已归档至 [archive/history-2026-09.md](archive/history-2026-09.md)，git 历史亦可回溯。

## 当前工作焦点

**发送按钮接入真实 PLC 通道（v1.31/31e）✅ 已完成** —— 用户要求点击发送后向 PLC 发送第一组配方的抽屉编号数组（不含配方值）。**协议已按用户指示切换为标准 ModbusTcpNet**（`useInovance: false`，纯数字地址，物料区同步改 `1000`×19），发送 = 一次批量写入 `Write("4000", array)`，PLC 侧 D4000 起已改 **16 位 INT 连续地址**（用户确认，v1.31h 去掉 DINT 2 字对齐），编号按顺序落 4000 起连续寄存器；**同时向 3000 区下发配方参数**（v1.31g）：注入 `IRecipeFileService`，按第一组配方名匹配配方表「编号」列（候选 编号/配方编号，Trim 比较），该行**除编号外所有列**依次写 3000 起连续寄存器（每参数占一个地址，非数字列按 0，超 short 范围截断），找不到匹配行/无编号列视为失败。流程：取第 1 组 → 写 4000 编号 → 写 3000 参数 → 全部成功后清空对应抽屉配方输入框（状态灯三态自动回落、分组移除该组）+ `MessageRequested` 弹窗「发送成功」（FeedDrawersPage 订阅）+ 日志输出编号与配方；任一失败则错误信息写入 Status 面板列表（listbox），输入框保留供重试。+4 守护用例（批量写地址/数组、无分组告警、失败进列表、IsBusy 复位），206/206 全绿。

### 上一焦点（v1.30，2026-09-09）

**VM 方案文件加密防外泄 ✅** —— SolutionProtector（AES-256 + PBKDF2，VMENC1 壳）+ 桥接加载时解密到临时明文用完即删 + SolutionEncryptor 工具；真实方案已加密替换 `D:\Printer\VisionTesting.dll`。202/202 全绿。

### 上一焦点（v1.29，2026-09-09）

**界面整体调整 ✅** —— ① 进料抽屉配方输入框加宽（220→300px、字体 12f）；② 无边框窗口可拉伸（`WndProc` WM_NCHITTEST 八方向边缘命中，8px Padding 外沿，ERR-033）；③ 全局字体放大 +1~2pt。196/196 测试全绿。

### 上一焦点（v1.28/28b，2026-09-09）

**图像页「保存图片」应用层实现 + 保存对话框失控修复（ERR-032/32a）✅** —— 右键菜单保存崩 OpenCvSharp（原生库未分发）→ GDI+ 自实现 SaveImageCommand（Clone 后台落盘）+ 成功/失败/无图弹窗；SaveFileDialog 误放 Bind 参数提供器致进页即弹 → 改 `SavePathRequestEventArgs` 请求回填模式 + 命令无参化。详 [errorlog.md](errorlog.md) ERR-032/32a。

> 更早焦点（v1.27 及之前 v0.x~v1.26 全部历史）：见 [archive/history-2026-09.md](archive/history-2026-09.md) 第一节。

## 测试记录

> 全部历史测试记录表见 [archive/history-2026-09.md](archive/history-2026-09.md) 第二节；最近 3 次如下。

| 日期 | 任务 | 结果 |
|------|------|------|
| 2026-09-09 | 发送按钮接真实 PLC（v1.31/31g：ModbusTcpNet + 写 4000 编号(DINT 对齐) + 同时写 3000 配方参数(编号外全列连续落址) + 成功弹窗 + 失败进列表；+5 用例） | ✅ 207/207 PASS |
| 2026-09-09 | VM 方案加密防外泄（v1.30：SolutionProtector + 桥接解密加载 + SolutionEncryptor 工具；+6 用例；真实方案已加密替换） | ✅ 202/202 PASS |
| 2026-09-09 | 保存对话框失控修复（v1.28b，ERR-032a；+1 守护用例） | ✅ 196/196 PASS |

## 当前处理中的错误

> 仅列编号与状态，详情见 [errorlog.md](errorlog.md)

| 编号 | 错误 | 状态 |
|------|------|------|
| ERR-008 | Cline 终端 `&&` 分隔符不可用（实为 PowerShell） | 🟡 规避中 |
| ERR-009 | dotnet build 输出 GBK 乱码（仅显示问题） | 🟡 规避中 |
| ERR-011 | PowerShell `mkdir` 多参数不可用 | 🟡 规避中 |
| ERR-030 | net10.0 直引 VM 引擎原生崩溃（混合架构规避） | 🟡 规避中 |

> 其余历史错误（已解决的 27 条）均已 🟢 压缩为摘要，详见 errorlog.md；完整过程见 [archive](archive/history-2026-09.md) 第七节。

## 下一步

1. **真机联调**（PLC 真机 192.168.1.88 + VisionMaster 真机出图联调；图像页 v1.26 新显示层待真机确认；**v1.31 发送通道 D4000 布局待 PLC 侧协议核对**）
2. 打印/图像页接入真实服务剩余项（IPrintService 已完成；图像 VisionMaster 桥接已完成——剩余为现场参数调优）
3. 配方管理页增强：抽屉列表显示配方名/状态列、批量下发
4. ~~用真实 PLC 通信实现替换 `MockDrawerService`~~ 大部分已落地（v1.10~v1.12、v1.31 发送方向）；剩余按需补齐

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