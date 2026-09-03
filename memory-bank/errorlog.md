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

### ERR-017：单元格修改错位写入（AntdUI 内部提交 1 基 INDEX vs 事件 0 基 RowIndex）
- **错误现象**：配方页双击修改单元格后，值没有写到被编辑的格子——刷新后内容错位：被编辑行显示旧值、其上一行被改成新值（第 1 行永远改不到）；保存后 xlsx 同样串位
- **发生上下文**：2026-09-03 用户反馈「修改功能不能正确保存到对应的单元格，刷新之后位置错乱」；运行时实证复现确认
- **发生时间**：2026-09-03 11:00
- **根本原因**（运行时实证，bin/inspect/InspectRowIndex.cs）：AntdUI 2.4.7 `CellEndEdit` 事件的 `RowIndex`/`ColumnIndex` 是 **0 基视觉索引**（与 CellClick 一致），但事件返回 true 时 AntdUI 内部把 `e.Value` 提交写入**含表头的 1 基 INDEX 行**——用户编辑视觉行 N，内部却写到内部行 N（= 视觉行 N-1）→ 整体错位一行。RecipePage 旧代码把事件索引直接传 VM 且恒返回 true，VM 写对位置后 AntdUI 又错写一次、且 UI 显示以其错误值为准 → 位置错乱 + 刷新放大
- **解决方式**：「VM = 唯一事实源」双改——① View `CellEndEdit` 改为**恒返回 false**（实证确认 false 时 AntdUI 内部完全零写入），阻止错位落值；② VM `TryCommitCellEdit` 提交成功后 `TableVersion++`，View 订阅重建表格同步显示（重建同时保留焦点钳制与选中高亮）
- **解决时间**：2026-09-03 11:18
- **验证结果**：🟢 已解决——新增 4 个位置正确性测试（多单元格乱序编辑逐格断言/首行可改/TableVersion 重建/保存重载原位）全通过；dotnet test 63/63 全绿；dotnet build 0 警告 0 错误
- **教训**：⚠️ AntdUI 第三方事件「索引参数正确 ≠ 内部行为正确」——`CellEndEdit` 的 args 索引可靠但其内部提交基准不同，**带 bool 返回值的编辑事件应优先评估「恒 false + 自管数据源重建」模式**（VM 唯一事实源），勿让 UI 库内部状态与业务数据源双写竞争；编辑类 API 必须运行时实证（事件触发时数据未更新、返回后才写入的时序细节只有实证能发现）

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
11. **数据规范化口径** → 读写两端必须一致：读端 Trim，则提交/唯一性校验/自动编号全部 Trim 后比较；不变量守护放在规范化后的值域（ERR-016）
12. **AntdUI CellEndEdit** → 恒返回 false 阻止其内部错位写入（内部 1 基 INDEX vs 事件 0 基 RowIndex），VM 提交后 TableVersion++ 重建表格同步显示（ERR-017）

## 沉淀出口

- 普适性设计模式/架构教训 → 写入 `systemPatterns.md`（如 Service 带路径重载切换、并发保存串行化）
- 环境与工具链约束 → 写入 `techContext.md`（如 PowerShell 语法限制、构建排错流程）
- 本文件仅追加新错误条目并维护生命周期状态，避免教训在多处重复维护