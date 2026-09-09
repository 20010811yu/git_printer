# 错误日志 (Error Log)

> 项目“问题—解决方式”的**唯一事实来源**。其他记忆库文件只留摘要 + 指向本文件编号，避免重复膨胀。
>
> **记录原则**：必须记录——导致任务中断或返工的错误、多次尝试才解决的问题、环境与依赖相关的坑、AI 生成代码的典型错误模式；不予记录——当场修复且无副作用的一次性笔误。
>
> **生命周期**：🔴 未解决 → 🟡 规避中 → 🟢 已解决（复现时更新原条目，不新建）
>
> **防膨胀说明**：本文件执行 `.clinerules/memory-bank.md` §3.3——🟡 未解决条目保留完整版；🟢 已解决条目压缩为一行摘要（完整过程见 [archive/history-2026-09.md](archive/history-2026-09.md) 第七节）；**防回归清单完整保留永不压缩**。

---

## 错误条目（🟡 完整保留 / 🟢 一行摘要）

### ERR-008：Cline 终端 `&&` 命令分隔符报错
- **错误现象**：`cd xxx && dotnet build` 报错 `标记"&&"不是此版本中有效的语句分隔符`
- **发生上下文**：execute_command 执行多命令时
- **根本原因**：Cline 终端实际是 **PowerShell** 而非 cmd，`&&` 在旧版 PowerShell 中不可用
- **解决方式**：🟡 规避中——工作目录已在项目根时直接执行单命令；确需多命令用 `;` 分隔
- **验证结果**：单命令与 `;` 分隔均正常
- **教训**：执行命令前先按 PowerShell 语法书写；`mkdir` 多参数同样不可用（用 `New-Item -ItemType Directory -Force`）

### ERR-009：dotnet build 输出 GBK 乱码
- **错误现象**：PowerShell 中 `dotnet build` 中文输出显示为乱码
- **发生上下文**：所有构建命令
- **根本原因**：MSBuild 输出编码（GBK/CP936）与终端解码不匹配
- **解决方式**：🟡 规避中——仅显示问题，凭关键字辨识：“已成功生成”/"0 个警告"/"0 个错误" 即成功；错误码（CS/MSB）为 ASCII 不受影响
- **验证结果**：可正常解读构建结果
- **教训**：乱码不等于构建失败，先找 CS/MSB 错误码再判断

### ERR-011：PowerShell `mkdir` 多参数不可用
- **错误现象**：`mkdir a b c` 报错（cmd 语法可一次建多个，PowerShell 不行）
- **发生上下文**：批量创建目录结构时
- **根本原因**：PowerShell 的 mkdir 是 New-Item 别名，一次只接受一个路径
- **解决方式**：🟡 规避中——用 `New-Item -ItemType Directory -Force <路径>` 逐个创建，或多条命令 `;` 分隔
- **验证结果**：目录创建正常
- **教训**：环境类坑优先查实际 shell 类型（见 ERR-008）

---

### ERR-030：net10.0 直引 VM SDK 引擎进程内崩溃（VmSolution.Load 无声退出）
- **错误现象**：主程序直引 GAC 的 VM.Core/VM.PlatformSDKCS 后，`VmRenderControl` 实例化正常，但 `VmSolution.Load` 阶段进程**无声退出**（无异常、无输出、无 EventLog），PowerShell `$LASTEXITCODE` 亦不更新
- **发生上下文**：v1.26 用户要求「VMRenderControl 显示方案图」——`ModuleSource = VmProcedure` 要求引擎在本进程
- **根本原因**：VM 引擎内部模块为 C++/CLI 与 net48 混合程序集链，.NET 10 运行时无法完成其依赖解析与加载——**崩溃发生在原生层，托管异常机制完全捕获不到**
- **解决方式**：🟡 规避中——**混合架构**：引擎不动（检测仍走桥接进程回传 PNG），仅引入 `VmRenderControl`（VMControls，纯托管，net10.0 可加载）做显示层；Bitmap → 自实现 `IImageData`（`VmBitmapImageData`）→ `ImageSource` 渲染，平移/缩放为控件内置。**但**：控件静态依赖 VM.PlatformSDKCS（GAC），需 Program.cs 挂 `AssemblyResolve` 从 GAC 物理路径补加载
- **验证结果**：独立冒烟程序实证：ImageSource 全链路渲染 3s 无崩溃；主程序集成后 191/191 测试全绿
- **教训**：① 跨运行时 SDK 的「控件」与「引擎」要分开评估——纯托管控件可跨运行时复用，引擎不行；② 原生层崩溃无任何托管痕迹，冒烟验证必须用独立进程跑完整链路（本例无声退出直接证明不可行）；③ `AssemblyResolve` 兜底 GAC 解析是 net10.0 复用 GAC 程序集的通用手法

### 🟢 已解决条目摘要（26 条，完整过程见归档第七节）

| 编号 | 标题 | 一句话教训 |
|------|------|-----------|
| ERR-001 | WinForms 透明背景 ArgumentException | 自绘控件慎用 Transparent，GDI+ `FromArgb` 半透明画刷是更可控的替代 |
| ERR-002 | CS0104 Timer 引用歧义 | WinForms 项目中后台定时器一律完全限定 `System.Threading.Timer` |
| ERR-003 | CS0067 事件未使用警告 | ICommand 的 CanExecuteChanged 必须有真实触发点（WinForms 无 CommandManager） |
| ERR-004 | 参数化命令 CanExecute(null) 误判禁用 | 参数化命令刷新必须携带原参数；动态参数用参数提供器实时取值 |
| ERR-005 | CS0535 接口升级后实现类未同步 | 接口升级必须同步实现类 + 全局搜索所有调用方（VM 层旧调用是第二波编译错误） |
| ERR-006 | MSB3027 程序运行锁定 exe | 构建前确认目标 exe 未在运行（taskkill 后重试） |
| ERR-007 | 并发保存 xlsx IOException 文件锁冲突 | 自动保存类后台 IO 必须考虑并发，同一资源写入用 `SemaphoreSlim` 排队 |
| ERR-010 | AntdUI Table 不支持 DataTable 直接绑定 | 第三方 UI 库 API 以反射/编译用例实证为准，不凭文档臆测 |
| ERR-012 | AntdUI CellFocused 鼠标单击不触发 | 第三方 UI 库事件名不能望文生义，必须反射实证并运行时验证 |
| ERR-013 | AddRowCommand 永久禁用（漏刷命令） | 属性变化影响命令可用性必须显式通知；多命令共用时用「全量刷新」 |
| ERR-014 | 新增行刷新后消失（ClosedXML 空行蒸发） | `RowsUsed()` 语义是「有内容的行」，读写循环必须自己维护行号；空行写入必须显式占位 |
| ERR-015 | 表头重命名 DuplicateNameException（测试暴露） | 列名来自外部文件时用「重建」语义替代「重命名」；测试首轮即暴露数月潜伏 Bug 验证测试工作流价值 |
| ERR-016 | 带空格编号绕过唯一性校验 | 同一数据读写两端必须同一规范化（Trim）口径；不变量守护放在规范化后的值域 |
| ERR-017 | 单元格错位写入（AntdUI 行事件 1 基 INDEX，两轮实证） | 第三方索引基准必须对照实验实证；行/列基准可能不一致；一个索引错位会级联放大成多个看似无关的症状 |
| ERR-018 | 编号查重真实表头静默失效 | 业务规则关联外部标识符（列名）禁止硬编码——候选列表 + 规范化匹配；「拦截」必须配「告知」 |
| ERR-019 | 新建配方文件流转语义偏差（返工） | 涉及文件生命周期的需求，动手前必须先与用户对齐「文件流转语义」（候选矩阵确认） |
| ERR-020 | 测试桩通道与生产代码脱节 | 生产代码换实现路径时全局搜索测试桩同步迁移；「测试全绿才交付」是硬门槛 |
| ERR-021 | HslCommunication V12 API 变化 | 引入/升级第三方库大版本前，先以包内 XML 文档核对 API 签名与命名空间 |
| ERR-022 | InovanceTcpNet 地址格式假设错误（无限重连） | 协议地址格式必须离线实证（`TranslateToModbusAddress`）；「连接成功」≠「通讯正常」 |
| ERR-023 | 后台事件现取 SynchronizationContext + 退出死锁 | 上下文必须 UI 线程构造时捕获存字段，严禁后台事件里现取（Post 静默丢失）；UI 线程 Wait 异步用 `Task.Run` 包裹 |
| ERR-024 | Anchor=Right 在容器未定型时冻结负距离（按钮推出窗外） | Anchor=Right/Bottom 必须在加入容器且尺寸定型后设置（或 Resize 重算）；「UIA 可见+点击有效+屏幕看不见」先查 bounds |
| ERR-025 | 共享协议 BuildResponse 缺 `\n\n` 结束标记 | 混合协议「分隔符约定」必须构建/解析两端同步实现并以往返测试锁死 |
| ERR-026 | 桥接 exe 相对路径回溯级数错误（4 级应 3 级） | 相对路径回溯级数以 `AppContext.BaseDirectory` 实际值逐级推导；DI 路径参数只有运行时才暴露——报错必须打印完整解析路径 |
| ERR-027 | 桥接进程加密狗授权失败后被复用 | 子进程「一次性初始化」失败是永久性的——收到业务失败响应也必须换新进程重试；排障看子进程底层日志 |
| ERR-028 | 流程成功但输出图间歇为空（无新帧误报） | 「成功但无结果」≠「失败」——低速图像源无新帧是常态按跳过处理；间歇性问题先看子进程诊断日志 |
| ERR-029 | 图像页 INPC 绑定静默失效（黑屏） | WinForms 绑定失效无报错，逐层实证不猜测；显示类需求可用 UI 定时器轮询兜底（对线程/编组/绑定免疫） |
| ERR-031 | v1.26 重构遗漏按钮命令绑定（图像页点击无响应、永无图） | View 按钮必须经 CommandManagerHelper.Bind 绑定命令；重构页面时逐按钮核对绑定；排障先看「操作是否真的触发了业务」（日志零痕迹=命令没执行，而非执行失败） |
| ERR-032 | VmRenderControl 右键保存崩 OpenCvSharp 类型初始化 | VM 控件内置保存依赖 OpenCvSharp 原生库（OpenCvSharpExtern.dll 53MB x64，仅装机目录有）；显示类需求用应用层 GDI+（Image.Save）自实现，绕开第三方控件的原生依赖路径 |
| ERR-032a | 保存对话框失控（一进图像页就弹另存为） | SaveFileDialog 严禁放进 CommandManagerHelper.Bind 的参数提供器——提供器在绑定与每次命令状态刷新时都会被调用；对话框一律走 VM→View 请求事件回填模式（同 InputRequestEventArgs），命令保持无参 |
| ERR-033 | 无边框窗体（FormBorderStyle.None）不可拉伸 | 边缘拉伸 = 窗体留 Padding 外沿（8px）为自身表面 + WndProc 拦 WM_NCHITTEST 按位置返回 HTLEFT/HTRIGHT/HTTOP/HTBOTTOM 及四角命中码；LParam 屏幕坐标多显示器下为负必须按 16 位有符号截取；最大化状态不命中 |

---

## 防回归清单（编码前必查）

1. **接口改动** → 同步实现类 + 全局搜索旧方法名调用（ERR-005）
2. **参数化命令** → 刷新用原参数，禁止统一 `CanExecute(null)`（ERR-004）
3. **同一文件写入** → 检查是否可能并发，加锁排队（ERR-007）
4. **构建失败** → 先确认 exe 未运行（MSB3027），再读 CS 错误码（ERR-006）
5. **AntdUI Table** → 只用 `AntList<AntItem[]>` 适配，DataTable 必须先转换（ERR-010）；跟踪鼠标选中用 `CellClick`（`CellFocused` 单击不触发，ERR-012）
6. **执行命令** → PowerShell 语法：单命令或 `;` 分隔，禁 `&&`（ERR-008/011）
6a. **命令 CanExecute 依赖属性** → 属性 setter 必须触发 RaiseCanExecuteChanged；多命令共用时用统一的全量刷新方法（ERR-013）
7. **WinForms 绑定** → 后台线程更新控件经 `SynchronizationContext.Post` / `BeginInvoke`；重绑前 `DataBindings.Clear()`
8. **自绘控件** → 禁用 `Color.Transparent` 背景色（ERR-001）；标注 `[DesignerSerializationVisibility(Hidden)]`
9. **测试项目隔离** → 主项目 csproj 必须 `Compile Remove="tests\**\*.cs"`，否则 glob 误收测试代码引发 CS0246/CS0579 连环报错（2026-09-03 搭建 xUnit 时踩坑）
10. **读外部文件建 DataTable** → 用「按文件实际表头重建列」，禁用「预置表头 + 重命名」（重名即 DuplicateNameException，ERR-015）
11. **数据规范化口径** → 读写两端必须一致：读端 Trim，则提交/唯一性校验/自动编号全部 Trim 后比较；**表头同样 Trim**（列名漂移会让识别失效）；不变量守护放在规范化后的值域（ERR-016）
11a. **编号列识别** → 候选表头 `{ "配方编号", "编号" }` Trim + 忽略大小写匹配（`FindRecipeIdColumnIndex` 统一入口），禁止硬编码单一列名；校验失败必须弹窗告知用户（`MessageRequested` 事件），拒绝提交同时 `TableVersion++` 强制还原显示（ERR-018）
12. **AntdUI Table 索引基准** → `CellEndEdit`/`CellClick`/`CellFocused` 的 **RowIndex 均为含表头的 1 基内部 INDEX（ColumnIndex 为 0 基）**，传给 0 基数据源（DataTable）前必须减 1；`SelectedIndex` 亦为 1 基（恢复高亮 +1）；`CellEndEdit` 恒返回 false 阻止内部落值，VM 提交后 TableVersion++ 重建表格同步显示（ERR-017，两轮实证）
13. **生产代码换实现通道** → 全局搜索测试桩中对应方法的桩逻辑并同步迁移（桩双通道行为不对称必须注释标明）；**交付硬门槛 = dotnet test 全绿**，只构建不测试的交付视为未验证（ERR-020）
14. **第三方库大版本接入/升级** → 先以 NuGet 包内 XML 文档核对 API 签名、命名空间与过时标记（如 Hsl V12 默认长连接、InovanceTcpNet 迁至 Profinet 命名空间，ERR-021）
15. **协议类地址格式** → 离线实证（`TranslateToModbusAddress`），不做前缀剥离等转换；InovanceTcpNet 用软元件格式且必须显式系列（H5U），ModbusTcpNet 用纯数字（ERR-022）
16. **后台线程更新 UI** → SynchronizationContext 必须构造时捕获存字段，严禁后台事件里现取 `Current ?? new`（Post 静默丢失）；UI 线程 Wait 异步任务用 `Task.Run` 包裹防死锁（ERR-023）
17. **跨运行时 SDK（.NET Framework）** → net10.0 禁止直引 GAC 的 Framework 程序集；用桥接进程（net48 独立项目承载 SDK + 命名管道 + **双侧共享同一份协议源码**）隔离；协议「分隔符约定」构建/解析两端同步实现并以往返测试锁死（ERR-025）；主 csproj 必须 `Compile Remove="tools\**"` 防 glob 误收（同 tests 教训）
18. **PowerShell 5.1 临时脚本** → 无 BOM UTF-8 按 ANSI 解析，中文字面量变乱码；传中文用环境变量 + Base64 / `GetFolderPath` / `[char]` 拼接，或干脆避免脚本内非 ASCII 字面量（v1.24 实测）
19. **相对路径回溯级数** → 以 `AppContext.BaseDirectory` 实际值逐级推导（`bin\Debug\<TFM>\` → 仓库根 = 3 级 `..`），禁止凭感觉多写；DI 传入的文件路径参数构建/测试不校验，运行时才暴露——服务报错必须打印完整解析路径（ERR-026）
20. **子进程一次性初始化** → SDK 授权/全局初始化每进程仅一次，进程内失败无法自愈；调用方收到子进程**业务失败响应**（ERR 帧）也必须清理并重启子进程再重试，不能只依赖「进程退出才重启」；排障直接看子进程底层日志（SDK/授权层），主程序日志只有回显（ERR-027）；共享源码需 net48 兼容——`string.Contains(str, StringComparison)` 重载不存在，用 `IndexOf`
21. **子进程诊断日志必须落文件** → 桥接进程由服务以 `CreateNoWindow` 启动，stderr/Console 输出无人重定向会静默丢失；关键排障信息（输出清单/错误码/回退路径）写 `log/VmBridge-diag.log`（ERR-028 破案关键）
22. **「成功但无结果」≠「失败」** → 低速图像源下无新帧的运行（ErrorCode=0 但输出图值为空）属常态，按跳过处理（保留上张图/不计数/仅 Info 日志），报错误会刷屏且语义错位（ERR-028）
23. **WinForms 绑定静默失效无报错** → 属性更新但界面不动时逐层实证（INPC 触发？哪线程？绑定收到？）；显示类需求用 View 端 `Windows.Forms.Timer` 轮询兜底（Tick 固定 UI 线程，对线程/编组/绑定免疫）；图像所有权移交 View（VM 不 Dispose，View 替换引用时释放）（ERR-029）
24. **本环境 dotnet 增量构建不可靠** → 报成功但产物可能是陈旧源码；验证产物必须 `dotnet clean` 后重建，并用 PowerShell 读产物字节搜新符号确认（方法名 ASCII / 字符串字面量 UTF-16）；net48 无 `Math.Clamp`（ERR-029）。**`dotnet clean` 也可能失效（报"均是最新的"不清理）——最可靠是直接删除 bin/obj 目录再构建**；符号检查用 **ASCII/UTF-8** 读字节（类型/方法名在 #UTF-8 堆），**UTF-16 只对字符串字面量（#US 堆）有效**（ERR-030 复现实证）
25. **跨运行时 SDK 控件与引擎必须分开评估** → 纯托管控件（如 VMControls）可被 net10.0 加载复用，引擎（C++/CLI 混合程序集链）不行——直调引擎在原生层无声崩溃，托管侧无异常可捕；混合架构 = 引擎留桥接进程 + 控件进主程序显示；控件自身的 GAC 静态依赖（VM.PlatformSDKCS）经 `AppDomain.AssemblyResolve` 从 GAC 物理路径补加载（ERR-030）
26. **View 按钮必须绑定命令** → 页面按钮一律 `CommandManagerHelper.Bind(button, vm命令)` 并逐按钮核对（v1.26 重构曾整体遗漏）；绑定缺失的症状 = 点击无反应且日志零痕迹（命令没执行而非执行失败）；守护手法 = 测试断言未加载方案时按钮 Enabled=false（绑定时按 CanExecute 立即同步控件态，缺绑定则默认可点）（ERR-031）
27. **第三方控件内置功能的原生依赖** → VmRenderControl 右键保存等内置路径依赖 OpenCvSharp 原生库（未随 net10.0 主程序分发，类型初始化必崩）；同类需求优先应用层自实现（GDI+ `Image.Save`），并在 View 尽力禁用控件右键菜单（`ContextMenuStrip = null`）；保存时机注意 ERR-028 所有权语义——先 Clone 再后台落盘，防轮播释放原图（ERR-032）

## 沉淀出口

- 普适性设计模式/架构教训 → 写入 `systemPatterns.md`（如 Service 带路径重载切换、并发保存串行化）
- 环境与工具链约束 → 写入 `techContext.md`（如 PowerShell 语法限制、构建排错流程）
- 🟢 已解决条目完整过程 → 归档 `archive/history-YYYY-MM.md`（防膨胀，§3.3）
- 本文件仅追加新错误条目并维护生命周期状态，避免教训在多处重复维护