# 当前上下文 (Active Context)

> 防膨胀说明：本文件执行 `.clinerules/memory-bank.md` §3.3 体积红线（≤150 行），仅描述「现在」；历史焦点/测试记录/历史变更/决策表已归档至 [archive/history-2026-09.md](archive/history-2026-09.md)，git 历史亦可回溯。

## 当前工作焦点

**进料抽屉页输入框换 AntdUI.Input + 宽度调整（v1.32）✅ 已完成** —— FeedDrawersPage 的 18 个配方输入框由原生 `TextBox` 换为 `AntdUI.Input`（圆角 Radius=6、居中、字体 12f），初始宽度 120→160px（`InputWidth`），自适应区间 60~320px（`InputMinWidth`/`InputMaxWidth`）。绑定链路不变（Recipe 双向 + IsInputReadOnly 只读）；实证 AntdUI.Input.SetText 会触发 OnTextChanged，WinForms 双向绑定可行，但绑定只在控件挂到**已显示窗体**后激活（无句柄即静默失效，测试须建宿主 Form 并 Show，同 ERR-029）。+3 守护用例（输入框类型与宽度区间 / 双向绑定 / 只读联动）；顺带修复 SolutionProtectorTests 全局 %TEMP% 断言被并行调度竞态误伤（ERR-035，DisableParallelization 集合串行化）。

### 上一焦点（v1.31/31h，2026-09-09）

**发送按钮接真实 PLC ✅** —— ModbusTcpNet + 一次批量写 4000 编号（16 位 INT 连续）+ 同时写 3000 配方参数 + 成功弹窗/失败进列表/清配方。207/207 全绿。详 progress.md。

> 更早焦点（v1.27 及之前 v0.x~v1.26 全部历史）：见 [archive/history-2026-09.md](archive/history-2026-09.md) 第一节。

## 测试记录

> 全部历史测试记录表见 [archive/history-2026-09.md](archive/history-2026-09.md) 第二节；最近 3 次如下。

| 日期 | 任务 | 结果 |
|------|------|------|
| 2026-09-10 | 进料抽屉输入框换 AntdUI.Input + 宽度调整（v1.32：InputWidth=160/60~320 自适应；+3 View 绑定守护用例；ERR-035 SolutionProtector 竞态修复） | ✅ 210/210 PASS |
| 2026-09-09 | 发送按钮接真实 PLC（v1.31/31g：ModbusTcpNet + 写 4000 编号(DINT 对齐) + 同时写 3000 配方参数(编号外全列连续落址) + 成功弹窗 + 失败进列表；+5 用例） | ✅ 207/207 PASS |
| 2026-09-09 | VM 方案加密防外泄（v1.30：SolutionProtector + 桥接解密加载 + SolutionEncryptor 工具；+6 用例；真实方案已加密替换） | ✅ 202/202 PASS |

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