# 错误日志 (Error Log)

> 项目"问题—解决方式"的**唯一事实来源**。其他记忆库文件只留摘要 + 指向本文件编号，避免重复膨胀。
>
> **记录原则**：必须记录——导致任务中断或返工的错误、多次尝试才解决的问题、环境与依赖相关的坑、AI 生成代码的典型错误模式；不予记录——当场修复且无副作用的一次性笔误。
>
> **生命周期**：🔴 未解决 → 🟡 规避中 → 🟢 已解决（复现时更新原条目，不新建）

---

## 错误条目

### ERR-001：WinForms 透明背景 ArgumentException
- **错误现象**：自绘控件设置 `BackColor = Color.Transparent` 抛 `ArgumentException: 控件不支持透明的背景色`
- **发生上下文**：`DrawerIndicatorControl` / `TabItemControl` 等自绘控件绘制背景时
- **根本原因**：普通 Control 未通过 `SetStyle(ControlStyles.SupportsTransparentBackColor, true)` 启用透明支持，直接赋值 Transparent 非法
- **解决方式**：背景色用与父容器一致的具体色（白色）；半透明效果改用 GDI+ `Color.FromArgb` 画刷实现（不受背景色影响）
- **验证结果**：🟢 已解决——18 抽屉网格与 Tab 导航渲染正常
- **教训**：WinForms 自绘控件慎用 Transparent，GDI+ 半透明画刷是更可控的替代

### ERR-002：CS0104 Timer 引用歧义
- **错误现象**：编译错误 `CS0104: Timer 是 "System.Windows.Forms.Timer" 和 "System.Threading.Timer" 之间的不明确引用`
- **发生上下文**：Service 层（MockDrawerService）使用 Timer 时
- **根本原因**：ImplicitUsings + csproj 的 UseWindowsForms 同时引入两个命名空间，`Timer` 类型歧义
- **解决方式**：Service 层用 `System.Threading.Timer` 完全限定名
- **验证结果**：🟢 已解决——构建 0 警告 0 错误
- **教训**：WinForms 项目中后台定时器一律完全限定，避免依赖 using 顺序

### ERR-003：CS0067 事件未使用警告
- **错误现象**：编译警告 `CS0067: 事件 "EventHandler" 从未使用过`
- **发生上下文**：RelayCommand 的 `CanExecuteChanged` 事件声明后，编译器找不到触发点
- **根本原因**：编译器静态分析看不到事件被 invoke 的代码路径
- **解决方式**：`RaiseCanExecuteChanged()` 中实际调用 `EventHandler?.Invoke(this, EventArgs.Empty)`
- **验证结果**：🟢 已解决——构建 0 警告
- **教训**：ICommand 实现里 CanExecuteChanged 必须有真实触发点，WinForms 无 CommandManager 自动刷新

### ERR-004：参数化命令 CanExecute(null) 误判禁用
- **错误现象**：底部 Tab 全部被禁用，点击无响应
- **发生上下文**：CommandManagerHelper 统一用 `CanExecute(null)` 刷新所有绑定命令时
- **根本原因**：NavigateCommand 带 PageType 参数，`CanExecute` 校验 `parameter is PageType`，传 null 被误判为不可执行 → 控件 Enabled 全刷为 false
- **解决方式**：绑定时存储 `(Control, Parameter)` 元组，刷新时用**原参数**调 CanExecute；后续升级为 `(Control, Func<object?> 参数提供器)` 支持动态参数
- **验证结果**：🟢 已解决——Tab 导航正常切换
- **教训**：⚠️ WinForms ICommand 无泛型约束，参数化命令的刷新必须携带原参数；动态参数（如当前选中单元格）用 ProxyCommand + 参数提供器实时取值，CanExecute 与 Execute 取同一参数源保证一致性

### ERR-005：CS0535 接口升级后实现类未同步
- **错误现象**：`dotnet build` 报 3 个 CS0535——`RecipeFileService` 未实现 `IRecipeFileService.LoadAsync(string)` / `SaveAsync(DataTable, string)` / `CreateBlankAsync(string, string)`
- **发生上下文**：IRecipeFileService 升级为多配方接口（2026-09-02），实现类未同步
- **根本原因**：接口加了带路径重载与 CreateBlankAsync 带参版本，但实现类仍是旧版方法签名；且 ViewModel 还在调用接口上已不存在的旧方法 `CreateBlankFileAsync()`（修完 Service 还会在 VM 处二次报错）
- **解决方式**：① 重写 RecipeFileService——提取私有核心 `LoadCoreAsync/SaveCoreAsync` 消除重复，带路径重载成功后切换 `FilePath`（private set），实现 `CreateBlankAsync(recipeName, recipeId)`；② 全局搜索 VM 中旧方法名调用并同步修正
- **验证结果**：🟢 已解决——构建 0 警告 0 错误
- **教训**：⚠️ 接口升级必须同步实现类 + 全局搜索所有调用方（Service 修完不等于修完，VM 层旧调用是第二波编译错误）

### ERR-006：MSB3027 程序运行锁定 exe
- **错误现象**：构建错误 `MSB3027: 无法复制 bin\...\UiTopMachine.exe 到 ...，文件正被另一进程使用`
- **发生上下文**：程序仍在运行时执行 `dotnet build`
- **根本原因**：Windows 下运行中的 exe 被锁定，覆盖输出失败
- **解决方式**：`taskkill /f /im UiTopMachine.exe` 结束进程后重试构建
- **验证结果**：🟢 已解决——重试构建成功
- **教训**：构建前确认目标 exe 未在运行；GUI 调试循环"改代码→构建→运行"尤其易踩

### ERR-007：并发保存 xlsx 抛 IOException 文件锁冲突
- **错误现象**：`System.IO.IOException: The process cannot access the file ... because it is being used by another process`
- **发生上下文**：配方页验证时，单元格编辑触发的自动保存与手动保存按钮并发写同一 `Recipe.xlsx`
- **根本原因**：ClosedXML `SaveAs` 写盘期间独占文件句柄，两个保存并发时后者拿不到锁
- **解决方式**：ViewModel 层加 `SemaphoreSlim(1,1) _saveLock`，所有保存统一走 `SaveCoreAsync` 串行化写盘
- **验证结果**：🟢 已解决——连续快速编辑+保存不再报错
- **教训**：⚠️ 自动保存类后台 IO 必须考虑与用户手动操作并发，同一资源的写入用信号量排队

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
- **解决方式**：🟡 规避中——仅显示问题，凭关键字辨识："已成功生成"/"0 个警告"/"0 个错误" 即成功；错误码（CS/MSB）为 ASCII 不受影响；管道接 `| Out-String` 可读性更好
- **验证结果**：可正常解读构建结果
- **教训**：乱码不等于构建失败，先找 CS/MSB 错误码再判断

### ERR-010：AntdUI Table 不支持 DataTable 直接绑定
- **错误现象**：将 `DataTable` 传入 `table.Binding(...)` 编译/运行类型不匹配
- **发生上下文**：配方页 AntdUI Table 化时尝试直接绑定 DataTable 数据源
- **根本原因**：AntdUI 2.4.7 `Table.Binding<T>` 只接受 `AntList<T>` 或 `BindingList<T>`（反射确认签名）
- **解决方式**：View 层写 `BindTable(DataTable)` 适配方法——按 DataTable 列重建 `Column(key, title)`，逐行转 `AntItem(key, value)[]` 装入 `AntList<AntItem[]>` 后 Binding；TableVersion++ 通知 View 重建
- **验证结果**：🟢 已解决——Excel 任意表头动态列正常展示
- **教训**：⚠️ 第三方 UI 库 API 以反射/编译用例实证为准，不凭文档臆测；反射检查套路：临时控制台项目 LoadFrom DLL 导出签名（需 UseWindowsForms=true 解析依赖），用完即删

### ERR-011：PowerShell `mkdir` 多参数不可用
- **错误现象**：`mkdir a b c` 报错（cmd 语法可一次建多个，PowerShell 不行）
- **发生上下文**：批量创建目录结构时
- **根本原因**：PowerShell 的 mkdir 是 New-Item 别名，一次只接受一个路径
- **解决方式**：🟡 规避中——用 `New-Item -ItemType Directory -Force <路径>` 逐个创建，或多条命令 `;` 分隔
- **验证结果**：目录创建正常
- **教训**：环境类坑优先查实际 shell 类型（见 ERR-008）

### ERR-013：AddRowCommand 永久禁用（RaiseCanExecuteChanged 漏刷）
- **错误现象**：「新增行」按钮在数据加载完成后仍为灰色不可点击
- **发生上下文**：配方页 v1.4，`AddRowCommand.CanExecute = !IsLoading && RecipeTable.Columns.Count > 0`——初始空表（0 列）时绑定为禁用，但数据加载成功后**没有任何代码触发该命令的 CanExecuteChanged**，按钮停留在禁用态
- **根本原因**：VM 多个属性 setter（RecipeTable/IsLoading/IsSaving）各自手动列举要刷新的命令，`AddRowCommand` 被遗漏——逐个列举的维护方式天然易漏
- **解决方式**：VM 提取 `RefreshAllCommandStates()` 统一刷新全部 8 个命令，三个属性 setter 全部改调此方法；属性变化 → 全量刷新，简单可靠不再遗漏
- **验证结果**：🟢 已解决——构建 0 警告 0 错误，运行验证按钮恢复正常（PID 24312）
- **教训**：⚠️ WinForms 无自动命令刷新机制，**属性变化影响命令可用性时必须显式通知**；多个命令依赖同一属性时用「全量刷新」替代「逐个列举」，遗漏一次就是永久禁用；评审命令 CanExecute 时同步检查其依赖属性的每个 setter 是否触发了刷新

### ERR-014：新增行刷新后消失（ClosedXML 空行不落盘 + RowsUsed 跳过空行）
- **错误现象**：配方页点击「新增行」后日志显示「已新增第 19 行」且自动保存成功，但点击刷新后表格回到 18 行——新行凭空消失，数据无处录入（用户反馈「新增行不能添加数据，刷新之后不显示」）
- **发生上下文**：配方页 v1.4，配方文件为无「配方编号」列的工业参数表（26 列），新增行全空
- **发生时间**：2026-09-02 14:44（用户日志 4 次复现同一模式）
- **根本原因**：两端叠加——① **写入端**：ClosedXML 对纯空字符串单元格不落任何痕迹，整行全空时该行在 xlsx 文件 XML 层面不存在；② **读取端**：`LoadCoreAsync` 用 `usedRange.RowsUsed().Skip(1)` 枚举数据行，`RowsUsed()` **只返回有内容的行，空行被直接跳过**。保存的空行重新加载时被过滤 →「新增空行→保存→刷新」= 行蒸发
- **解决方式**：`RecipeFileService` 双端修复——① `SaveCoreAsync`：逐行检测整行全空时向首列写入单个空格 `" "` 占位，保证该行在文件中真实存在；② `LoadCoreAsync`：放弃 `RowsUsed()` 枚举，改用 `ws.LastRowUsed().RowNumber()` 确定末行行号，从第 2 行逐行循环装载（空行/中间空行全部保留），空格占位单元格经 `Trim` 还原为空字符串。附带修复：中间行被用户清空后刷新不再消失
- **解决时间**：2026-09-02 15:44
- **验证结果**：🟢 已解决——构建 0 警告 0 错误；临时控制台往返验证 8 PASS / 0 FAIL（空行保留/中间空行位置不变/有数据行完整/真实文件可加载）
- **教训**：⚠️ ClosedXML 的 `RowsUsed()` 语义是「有内容的行」不是「表格的行」，读写循环必须**自己维护行号**（`LastRowUsed().RowNumber()` + for 循环）而非依赖枚举器；空 DataTable 行写入 Excel 必须显式占位；单元格值 `Trim` 需评估业务影响（本例占位空格还原为空是预期行为）

### ERR-015：加载特定列结构的配方文件失败（表头重命名 DuplicateNameException）
- **错误现象**：加载列名与默认表头部分重合的配方 xlsx（如「配方编号/配方名称/备注」3 列）时，`LoadAsync` 返回失败，用户无法打开正常配方文件
- **发生上下文**：2026-09-03 搭建单元测试（tests/UiTopMachine.Tests），首个 VM 业务测试套件运行时 17 个用例连锁失败，失败断言暴露「前置加载失败：A column named '备注' already belongs to this DataTable」
- **发生时间**：2026-09-03 10:38
- **根本原因**：`RecipeFileService.LoadCoreAsync` 读表头采用「预置默认表头 + 逐列重命名」方式——当文件某列名与默认表头中**其它列**重名时（本例：文件第 3 列「备注」重命名默认列时与默认第 5 列「备注」冲突），DataTable 抛 `DuplicateNameException`，整个加载失败。该缺陷自 0.8 版本引入后一直未被发现（无「文件列结构 ≠ 默认 5 列」场景的自动化验证），正是单元测试要捕获得回归类型
- **解决方式**：`LoadCoreAsync` 弃用重命名方式，改为按文件实际表头**新建 DataTable 重建列结构**（空表头用「列N」兜底），数据行装载与返回均基于文件真实列。附带收益：文件列结构与加载结果严格一致，不再残留默认表头多余列
- **解决时间**：2026-09-03 10:40
- **验证结果**：🟢 已解决——dotnet test 55/55 全部通过；dotnet build 0 警告 0 错误
- **教训**：⚠️ 「重命名」式适配表头隐含全局唯一性约束，列名来自外部文件时必须改用「重建」语义；**产品 Bug 被历史版本携带数月而测试首轮即暴露**，验证了「每次任务修改功能必须配套测试」工作流的价值；防回归测试用例 `ERR014_保存含末尾空行的表_重载后行数不变` 系列 + VM 套件已永久守护

### ERR-016：带首尾空格的编号绕过唯一性校验（重载 Trim 后撞车 / 数据漂移）
- **错误现象**：① 用户输入带首尾空格的编号（如 `" R001 "`）可通过单元格唯一性校验保存，刷新重载后被 `Trim` 成 `"R001"`，与已有行编号撞车，破坏「编号唯一」不变量；② 所有单元格输入的空格在重载后悄然消失（内存值 ≠ 重载值，数据漂移）
- **发生上下文**：2026-09-03 用户要求「对配方页面修改功能进行测试」——按测试工作流为 `TryCommitCellEdit` 修改链路补写空格规范化用例，4 个用例全部失败暴露缺陷
- **发生时间**：2026-09-03 10:53
- **根本原因**：`LoadCoreAsync` 重载时对**全部单元格**做 `Trim`（ERR-014 修复引入），但 VM 修改链路仍是**原文写入 + 原文比较**——两端口径不一致：`TryCommitCellEdit` 未 Trim、`IsDuplicateRecipeId`/`ValidateRecipeIdUnique`/`GenerateUniqueRecipeId` 均用原文 Ordinal 比较，`" R001 "` 与 `"R001"` 被判为「不同」
- **解决方式**：VM 修改链路四处对齐 Trim 口径——① `TryCommitCellEdit` 提交前 `(newValue ?? "").Trim()` 规范化（写盘值即最终值，杜绝漂移）；② `IsDuplicateRecipeId` 比较值 Trim；③ `ValidateRecipeIdUnique` 收集值 Trim；④ `GenerateUniqueRecipeId` 已占用收集 Trim
- **解决时间**：2026-09-03 10:54
- **验证结果**：🟢 已解决——dotnet test 59/59 全部通过（含 4 个新空格规范化回归用例）；dotnet build 0 警告 0 错误
- **教训**：⚠️ **同一数据在读写两端必须同一规范化口径**——读端有 Trim，写端/校验端就必须 Trim，否则「校验时不同、重载后相同」是必然漏洞；数据不变量（如编号唯一）的守护要放在**规范化后的值域**上，而非原始输入上

### ERR-017：单元格修改错位写入（AntdUI 行事件索引为含表头的 1 基 INDEX——首轮误诊后二次实证修正）
- **错误现象**（两轮症状方向相反，均源于同一根因）：
  - 第一轮（返回 true 时）：值错位写到**上一行**、首行永远改不到（AntdUI 内部按事件索引 1 基落值 + UI 显示以其错误值为准）
  - 第二轮（恒返回 false + VM 提交后）：值错位写到**下一行同列**、编辑末行静默失败「改不动」、编号查重「失效」（重输自身原编号被误报已存在 / 输入与下一行相同编号被未变化短路跳过）——用户反馈「保存刷新之后修改的内容到了下一行同列，修改编号列未进行查重校验」
- **发生上下文**：2026-09-03 用户首轮反馈错位；修复后同日用户二次反馈「错到下一行 + 编号查重未生效」
- **发生时间**：2026-09-03 11:00（首轮）/ 2026-09-03 12:00 前后（二次复现）
- **根本原因**（第二轮运行时实证，bin/inspect/InspectRowIndex.cs 双实验对照）：AntdUI 2.4.7 `CellEndEdit`、`CellClick`、`CellFocused` 三事件的 **RowIndex 均为含表头的 1 基内部 INDEX**（内部 rows[0]=表头 INDEX=0，首条数据行 INDEX=1，事件值与内部 INDEX 一致），而 **ColumnIndex 为 0 基**、`SelectedIndex` 亦为 1 基 INDEX。首轮修复误诊「事件 0 基、内部提交 1 基」，保留「恒返回 false + VM 提交」但**仍把 1 基 RowIndex 直接传给 VM 的 0 基 DataTable** → 全部偏移 +1 写到下一行：末行（事件值=行数）越界静默返回 false；查重用错位行读 oldValue，被「未变化」分支短路跳过或 excludeRowIndex 排除错行误报。首轮第一版（恒 true）的症状方向相反是因为 AntdUI 内部落值与 VM 双写叠加
- **解决方式**（VM 唯一事实源 + 索引换算三处）：① View `CellEndEdit` **恒返回 false**（实证确认零内部写入）+ `TryCommitCellEdit(e.RowIndex - 1, e.ColumnIndex, ...)` 行减 1 换算（列 0 基原样）；② `UpdateFocus`（CellClick/CellFocused 共用）同样 RowIndex-1 换算（<1 即表头/空白视为取消选中）——**删除行/列链路同样存在此错位，一并修正**；③ `BindTable` 恢复高亮 `SelectedIndex = _focusedRowIndex + 1` 反向换算；VM 提交成功后 `TableVersion++` 重建表格
- **解决时间**：2026-09-03 12:20
- **验证结果**：🟢 已解决——dotnet test **65/65** 全绿（新增 2 个用例：编号列重输自身原值不误报 / 末行可编辑且落在末行）；dotnet build 0 警告 0 错误；实证工具双实验确认索引基准与零内部写入
- **教训**：⚠️ **第三方 UI 库的索引基准必须实证且不可想当然**——首轮「实证」实际只验证了内部落值行为，未对事件索引基准做对照实验（拿事件值直接当 0 基用），导致修复引入反向错位；正确套路 = 同一次交互同时捕获已知正确的基准事件（如坐标模拟双击的视觉行）与待测事件的索引做对照。⚠️ 行/列基准可能不一致（本例行 1 基、列 0 基），必须分别验证。⚠️ 一个索引错位会级联放大成多个「看似无关」的症状（错位写 + 查重失效 + 末行改不动 + 高亮偏移），排查时从共同根因入手而非逐症状打补丁

### ERR-018：编号查重在真实配方表上静默失效（查重列名硬编码「配方编号」，用户表头为「编号」）
- **错误现象**：用户反馈「没有对编号列设置防重复——修改编号列/给新建行录入编号时，与现有编号重复仍成功保存并写进 Excel」；且校验失败仅写日志面板无弹窗，用户无法感知「被拒绝」
- **发生上下文**：2026-09-03 用户反馈编号防重未生效；经确认用户真实配方表编号列表头为「**编号**」
- **发生时间**：2026-09-03 12:44
- **根本原因**：`RecipePageViewModel.RecipeIdColumn = "配方编号"` 硬编码单列名精确匹配——用户表头「编号」≠「配方编号」→ 三道防线同时静默失效：① `TryCommitCellEdit` 的 `columnName == RecipeIdColumn` 恒 false 跳过编辑查重；② `ValidateRecipeIdUnique` 的 `IndexOf` 返回 -1 视为「无编号列」跳过保存兜底；③ `AddRow` 跳过自动编号（需手输，与用户描述吻合）。附带缺陷：校验失败仅记日志无用户可见反馈、拒绝后未强制重建表格（AntdUI 可能残留编辑值假象）、`LoadCoreAsync` 表头未 Trim（「编号␣」同样失效）
- **解决方式**（v1.6）：
  ① **编号列识别宽松化**：`RecipeIdColumnCandidates = { "配方编号", "编号" }` 候选列表 + Trim + 忽略大小写，新增 `FindRecipeIdColumnIndex()` 统一入口替换全部 6 处硬编码调用（TryCommitCellEdit/IsDuplicateRecipeId/ValidateRecipeIdUnique/GenerateUniqueRecipeId/AddRow/DeleteRow 文案）
  ② **失败弹窗反馈**：新增 `Common/MessageRequestEventArgs.cs`（纯数据）+ VM `MessageRequested` 事件（沿用 VM→View 弹框请求模式）；编辑重复拒绝 → 弹「编号已存在，修改失败」+ 拒绝也 `TableVersion++` 强制重建还原显示；手动保存兜底失败 → 弹「保存失败」（`SaveCoreAsync` 增加 `userInitiated` 参数，自动保存保持静默日志）；View 订阅经 `BeginInvoke` 封送 UI 线程弹 MessageBox
  ③ **Service 表头 Trim**：`LoadCoreAsync` 读表头 Trim 规范化，消除「编号␣」空格陷阱
- **解决时间**：2026-09-03 13:00
- **验证结果**：🟢 已解决——dotnet test **71/71** 全绿（新增 6 用例：「编号」表头编辑重复拒绝+弹窗断言/唯一通过不弹窗/新增行自动编号跳过占用/新增行手改重复拒绝/手动保存兜底拒绝+弹窗/「配方编号」候选兼容）；dotnet build 0 警告 0 错误
- **教训**：⚠️ **业务规则关联外部数据（列名/表头）时禁止硬编码单一精确名**——用户的文件结构是变的，识别逻辑必须候选列表 + 规范化（Trim/大小写）匹配；⚠️ **校验类功能的「拦截」与「告知」是一体的**——只拦截不告知，用户感知就是「没生效」；⚠️ 沉淀普适教训 → systemPatterns（外部标识符识别模式）

### ERR-019：新建配方文件流转语义理解偏差（另存副本 vs 备份轮转）——返工
- **错误现象**：v1.7 将「新建配方」实现为「原文件不动 + 另存时间戳副本并切换工作区」，用户指出正确语义应为**备份轮转**：原配方文件改名（原名+时间戳）备份 → 新空白配方**沿用原文件名**（Recipe.xlsx）→ 页面显示新配方。首轮返工
- **发生上下文**：2026-09-03 用户连续追问「原配方文件怎么处理」后指出流程不对
- **发生时间**：2026-09-03 13:32
- **根本原因**：**设计前未与用户对齐「文件流转语义」**——「新建配方」涉及原文件去向/新文件命名两类决策，仅凭「原文件保留不删」的既有决策自行推演了「另存副本」方案（v1.7 直接实施），未先呈现「原文件改名备份、新文件沿用原名」的可能方案让用户确认
- **解决方式**（v1.7b）：① `CreateBlankAsync(headers, blankRowCount)` 移除 recipeName——新文件固定沿用当前文件名；② Service 轮转：`File.Move` 原文件 → `原名_yyyyMMdd_HHmmss.xlsx`（同秒递增 `_2/_3` 防覆盖），模板表写入原路径，FilePath 不变；③ VM 先经通用 `ConfirmationRequested`（替换 DeletionConfirmRequested，删除行/列迁移共用）请求用户确认，确认后轮转 + 内存构造空白表立即显示；④ 顺带完成同批需求：行序整理 `CompactRows`（加载/编辑/删除后数据连续、空白垫底）+ `EnsureMinRows`（依 View 可见高度补真实可编辑空白行）+ `RowHeight=36/RowHeightHeader=40`
- **解决时间**：2026-09-03 13:59
- **验证结果**：🟢 已解决——dotnet test **79/79** 全绿（改写 CreateBlank 7 个 Service 用例 + 新建配方 VM 用例 3 个 + 行序整理/补行用例 4 个）；构建 0 错误（2 个 xUnit 风格 warning 为历史遗留非本次引入）
- **教训**：⚠️ **涉及文件生命周期（重命名/移动/删除/覆盖）的功能需求，动手前必须先与用户对齐「文件流转语义」**——「原文件保留」存在多种实现（副本另存/改名备份/原位不动），语义错一个字就整体返工；正确流程 = 列出候选方案（原文件去向 × 新文件命名矩阵）让用户选择后再实施
- **状态**：🟢 已解决

### ERR-012：AntdUI CellFocused 鼠标单击不触发（删除按钮未启用）
- **错误现象**：用户单击 AntdUI Table 单元格后，「删除行/删除列」按钮保持禁用不变红
- **发生上下文**：配方页 v1.4 删除功能，初版仅订阅 `CellFocused` 事件跟踪焦点索引
- **根本原因**：AntdUI 2.4.7 的 `CellFocused` 事件在鼠标单击时**不触发**（偏向键盘焦点导航）；跟踪鼠标选中必须订阅 `CellClick`（`TableClickEventArgs` 含 RowIndex/ColumnIndex，继承 MouseEventArgs）
- **解决方式**：`CellClick + CellFocused` 双事件订阅，共用 `UpdateFocus` 处理函数（两者参数均含 RowIndex/ColumnIndex）；点击表头（索引 < 0）视为取消选中
- **验证结果**：🟢 已解决——反射实证两事件委托签名后修正，构建 0 错误
- **教训**：⚠️ 第三方 UI 库事件名不能望文生义（"Focused"≠鼠标点击），必须反射实证委托签名并用运行时行为验证；同名委托可能是其他组件的（`ClickEventHandler` 首轮全局按名搜索命中了 Chat 组件的同名委托，需从 `Table.GetEvent` 取真实类型）

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

## 沉淀出口

- 普适性设计模式/架构教训 → 写入 `systemPatterns.md`（如 Service 带路径重载切换、并发保存串行化）
- 环境与工具链约束 → 写入 `techContext.md`（如 PowerShell 语法限制、构建排错流程）
- 本文件仅追加新错误条目并维护生命周期状态，避免教训在多处重复维护