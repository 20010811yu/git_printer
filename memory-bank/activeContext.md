# 当前上下文 (Active Context)

## 当前工作焦点

**新建配方备份轮转 + 行序整理 + 自动补空白行（v1.7b，ERR-019）** —— 用户指出 v1.7「另存副本」语义不对，正确流程为**备份轮转**：原配方 `File.Move` 改名（原名+时间戳）备份 → 新空白配方**沿用原文件名**（Recipe.xlsx）→ 页面立即显示新配方。改造：① 接口 `CreateBlankAsync(headers, blankRowCount)` 移除 recipeName；② Service 轮转（同秒递增 `_2/_3` 防覆盖）；③ VM 经通用 `ConfirmationRequested`（替换 DeletionConfirmRequested，删除行/列迁移共用）确认后轮转 + 内存构造立即显示；④ 同批需求：`CompactRows` 行序整理（加载/编辑/删除后数据连续、空白垫底）+ `EnsureMinRows`（依表格可见高度补**真实可编辑**空白行）+ `RowHeight=36/RowHeightHeader=40`。教训详 errorlog ERR-019（文件生命周期需求必须先对齐文件流转语义）。dotnet test **79/79** 全绿。

## 测试记录

| 日期 | 任务 | 测试内容 | 结果 |
|------|------|---------|------|
| 2026-09-03 | 搭建单元测试基础设施（v1.5） | 首批 55 用例（RelayCommand/AsyncRelayCommand、三态判定、xlsx 往返 ERR-014 回归、删除行列/列校验/编号唯一/ERR-013 回归）；暴露并修复 ERR-015 | ✅ 55/55 PASS（trx 留档） |
| 2026-09-03 | 配方页修改功能测试与修复（v1.5b） | 新增 4 个空格规范化用例（编号 Trim 提交/带空格重复拒绝/保存校验/普通列 Trim）；暴露并修复 ERR-016 | ✅ 59/59 PASS（trx 留档） |
| 2026-09-03 | 单元格错位写入修复（v1.5c） | 新增 4 个位置正确性用例（乱序编辑逐格断言/首行可改/TableVersion 重建/保存重载原位）；运行时实证并修复 ERR-017 | ✅ 63/63 PASS（trx 留档） |
| 2026-09-03 | 单元格错位二次修复（v1.5d） | 新增 2 个边界用例（编号列重输自身原值不误报/末行可编辑）；二轮实证确证 1 基真根因并三处换算修复 ERR-017 复现 | ✅ 65/65 PASS（trx 留档） |
| 2026-09-03 | 编号查重生效 + 失败弹窗（v1.6） | 新增 6 用例（「编号」表头重复拒绝+弹窗/唯一不弹窗/新增行自动编号/手改重复拒绝/保存兜底+弹窗/候选兼容）；候选表头识别 + MessageRequested 弹窗修复 ERR-018 | ✅ 71/71 PASS（trx 留档） |
| 2026-09-03 | 新建空白配方改造（v1.7） | 新增 4 用例（表头一致+数据全空+指定行数/空白行占位保存重载不消失/空表头回退默认/VM 端到端表头沿用+10空行+保存往返）；CreateBlankAsync 接口变更同步 5 处调用 | ✅ 74/74 PASS（trx 留档） |
| 2026-09-03 | 备份轮转+行序整理+补空白行（v1.7b） | 改写 CreateBlank 7 个 Service 用例（备份轮转/数据完整/不覆盖/无原文件/取消不变）+ VM 3 个（确认轮转/取消不变/保存往返）+ 行序整理/补行 4 个；DeletionConfirmRequested→ConfirmationRequested 迁移 | ✅ 79/79 PASS（trx 留档） |

## 当前处理中的错误

> 仅列编号与状态，详情见 [errorlog.md](errorlog.md)

| 编号 | 错误 | 状态 |
|------|------|------|
| ERR-008 | Cline 终端 `&&` 分隔符不可用（实为 PowerShell） | 🟡 规避中 |
| ERR-009 | dotnet build 输出 GBK 乱码（仅显示问题） | 🟡 规避中 |
| ERR-011 | PowerShell `mkdir` 多参数不可用 | 🟡 规避中 |

> 其余历史错误（ERR-001~007、ERR-010~019，含 ERR-017 两轮修复）均已 🟢 解决，详见 errorlog.md

## 最近变更（2026-09-02）

1.4c ✅ **修复新增行刷新后消失（空行蒸发）**（用户反馈：「新增行不能添加数据，在刷新之后不显示」）：
    - **根因（ERR-014）**：用户配方表无「配方编号」列 → 新增行为全空行；ClosedXML 对空字符串单元格不落盘（整行在 xlsx XML 层面不存在）+ `LoadCoreAsync` 用 `RowsUsed().Skip(1)` 枚举（空行被跳过）→「新增空行→自动保存成功→刷新」后行凭空消失（日志铁证：14:44~14:51 四次「已新增第 19 行」→ 刷新均回到 18 行）
    - **修复一（写端）**：`RecipeFileService.SaveCoreAsync` 逐行检测整行全空时向首列写入单个空格 `" "` 占位，保证空行在文件中真实存在
    - **修复二（读端）**：`LoadCoreAsync` 弃用 `RowsUsed()` 枚举，改 `ws.LastRowUsed().RowNumber()` 定末行 + for 循环逐行装载（空行/中间空行全保留），空格占位经 `Trim` 还原为空
    - **附带修复**：中间行被清空后刷新不再消失；`ws.LastRowUsed()` 空引用防护（CS8602）；VM `AddRow` 无编号列时记 Info 日志提示
    - **验证**：构建 0 警告 0 错误；临时控制台往返验证 8 PASS / 0 FAIL（空行保留/中间空行位置不变/有数据行完整/真实 Recipe.xlsx 可加载）
1.4b ✅ **修复新增行按钮永久禁用**（用户反馈：「新增行按钮依旧是灰色不可点击状态」）：
    - **根因（ERR-013）**：`AddRowCommand.CanExecute = !IsLoading && RecipeTable.Columns.Count > 0`，初始空表绑定为禁用；但数据加载后 **`AddRowCommand.RaiseCanExecuteChanged()` 从未被任何 setter 触发**，按钮永远停留在禁用态——属性 setter 逐个手动列举刷新命令的维护方式天然易漏
    - **修复**：VM 提取 `RefreshAllCommandStates()` 统一刷新全部 8 个命令；`RecipeTable`/`IsLoading`/`IsSaving` 三个 setter 全部接入，属性变化即全量刷新，杜绝遗漏
    - 构建 0 警告 0 错误，运行验证按钮恢复可用（PID 24312）
1.4a ✅ **按钮样式优化 + 连续删除焦点钳制**（用户反馈：「按钮状态为灰色」）：
    - **诊断结论**：日志证实删除功能实际正常（已成功删除 3 列并自动保存）；「灰色」痛点 = ① 新增行/列用 `TTypeMini.Default` 灰白样式被误认为禁用 ② 删除一次后焦点重置，按钮回到禁用态，连续删除需重新点选
    - **修复一（语义色）**：新增行/新增列按钮改 `TTypeMini.Success` 绿色（正向操作），与删除的 Error 红色形成语义对比，消除「灰色=禁用」误解
    - **修复二（焦点钳制）**：`BindTable` 重建表格时焦点索引不再重置为 -1，改 `Math.Min(旧焦点, 行/列数-1)` 钳制——删除后焦点自动落在相邻行/列，**连续删除无需重新点选**；初始 -1 保持 -1；恢复 `SelectedIndex` 行选中高亮
    - 构建 0 警告 0 错误，运行验证通过（PID 16500）
1.4 ✅ **删除行/列 + 确认弹框**（用户需求：新增删除行/删除列功能，删除时弹框确认）：
    - **新增 `Common/ConfirmRequestEventArgs.cs`**：VM↔View 确认请求事件参数（纯数据载体：Title/Message 由 VM 设置，Confirmed 由 View 回填）
    - **新增 `Views/Dialogs/ConfirmDialog.cs`**：确认弹框（纯 View）——⚠ 警示图标 + 提示文字 + AntdUI 确定（**Error 红色危险语义**）/取消按钮，模态居中，回车=确定/Esc=取消
    - **`RecipePageViewModel` 新增**：`DeletionConfirmRequested` 事件 + `DeleteRowCommand`/`DeleteColumnCommand`（RelayCommand，CanExecute 校验索引有效性 → 无选中行/列时按钮自动禁用）；`DeleteRow/DeleteColumn` 业务链——索引校验→组装确认文案（删除行带配方编号）→ 触发确认请求 → 用户取消静默放弃 → 确认后移除行/列 + TableVersion++ 重建表格 + 自动保存
    - **`RecipePage` 改造**：工具栏 6→8 按钮（删除行/列用 `TTypeMini.Error` 红色）；反射实证 AntdUI 2.4.7 `CellClick`（`TableClickEventArgs.RowIndex/ColumnIndex`，鼠标单击）与 `CellFocused`（键盘焦点导航）**双事件订阅**维护 `_focusedRowIndex/_focusedColumnIndex`，经 CommandManagerHelper **动态参数提供器**实时取参（详 ERR-004 模式）；BindTable 重建表格时重置焦点索引（删除后按钮回到禁用态）；点击表头/空白（索引<0）视为取消选中
    - **⚠️ 实测修正**：初版仅订阅 `CellFocused`，用户反馈单击后按钮未启用 → 反射确认 `CellFocused` 鼠标单击不触发，改 `CellClick + CellFocused` 双订阅后修复
    - 删除行确认文案示例：「确定删除第 3 行（配方编号：R003）吗？删除后该行所有数据不可恢复。」
    - 构建 0 警告 0 错误，运行验证通过（PID 21960）
1.3 ✅ **新增列弹框交互 + 列名空校验**（用户需求：新增列按钮弹出输入弹框，列名为空则新增失败）：
    - **新增 `Views/Dialogs/InputDialog.cs`**：通用输入弹框（纯 View）——说明 Label + TextBox 输入框 + AntdUI 确定（Primary）/取消按钮，FixedDialog 模态居中，回车=确定/Esc=取消，仅收集输入零业务逻辑
    - **新增 `Common/InputRequestEventArgs.cs`**：VM↔View 输入请求事件参数（纯数据载体：Title/Prompt 由 VM 设置，Confirmed/InputText 由 View 回填）
    - **改造 `RecipePageViewModel.AddColumn()`**：不再自动生成"新列N"，改为触发 `ColumnNamingRequested` 事件向 View 请求列名（VM 不接触 UI 控件）；校验链——用户取消→静默放弃；**列名空/纯空白→新增列失败（记 Error 日志）**；列名重复→拒绝（DataTable 不允许重复列名）；通过→追加列 + TableVersion++ + 自动保存
    - **`RecipePage` 订阅事件**：`ShowInputDialog(request)` 弹模态框，`ShowDialog(FindForm())` 结果回填 Confirmed/InputText（纯 UI 转发）
    - 构建通过（0 错误）；编译期曾因程序运行锁定 exe（MSB3027）失败，taskkill 后重试成功
1.2 ✅ **构建失败修复（CS0535 接口实现缺失）**（用户需求：检查项目生成失败原因并修复）：
    - **错误现象**：`dotnet build` 报 3 个 CS0535——`RecipeFileService` 未实现 `IRecipeFileService.LoadAsync(string)` / `SaveAsync(DataTable, string)` / `CreateBlankAsync(string, string)`
    - **根因**：接口已升级为多配方管理（带路径重载 + CreateBlankAsync 带配方名/编号参数），但实现类仍是旧版（只有无参 LoadAsync、单参 SaveAsync、接口上不存在的 CreateBlankFileAsync）；ViewModel 也还在调用已被删除的 `CreateBlankFileAsync()`
    - **修复 RecipeFileService**（重写）：提取私有核心 `LoadCoreAsync(path)` / `SaveCoreAsync(table, path)` 消除重复；带路径重载成功后自动切换 `FilePath` 数据源；`CreateBlankAsync(recipeName, recipeId)` 实现——文件名 = `SanitizeFileName(配方名)_yyyyMMdd_HHmmss.xlsx`（非法字符替换下划线 + 同秒递增序号防覆盖），写入默认表头 + 首行配方编号
    - **修复 RecipePageViewModel**：`CreateBlankRecipeAsync()` 改调接口方法 `CreateBlankAsync(recipeName, recipeId)`，配方名/编号由 `GenerateUniqueRecipeId()` 生成（R001 起递增）
    - 修复后构建通过（0 警告 0 错误）→ `bin\Debug\net10.0-windows\UiTopMachine.dll`
1.0 ✅ **配方页编辑功能全套**（用户需求：单元格修改/编号唯一/增行列/新建空白配方）：
   - **单元格修改**：`RecipePage` 开启 `EditMode = TEditMode.DoubleClick` + `EditLostFocus = true`；`CellEndEdit` 事件转发 `VM.TryCommitCellEdit(rowIndex, colIndex, newValue)`——写回 DataTable + 自动后台保存；返回 false 时 AntdUI 自动还原显示
   - **编号唯一不可重复**：VM 常量 `RecipeIdColumn = "配方编号"`；三处校验——单元格编辑时 `IsDuplicateRecipeId`（排除自身行，重复拒绝并还原）、保存/自动保存前 `ValidateRecipeIdUnique`（重复拒绝落盘）、新增行 `GenerateUniqueRecipeId`（R001 起跳过已占用号）；空编号不参与校验；无"配方编号"列时校验自动跳过
   - **新增行/列**：`AddRowCommand`（编号自动生成）/`AddColumnCommand`（"新列N"自增防重名）；VM `TableVersion` 自增通知 View 重建表格（AntdUI Table 绑定后新增行不会自动出现）
   - **新建空白配方**：Service `CreateBlankFileAsync()` 替代原 `CreateBlankAsync()`——文件名 = 原名 + `_yyyyMMdd_HHmmss`（同秒重复创建递增 `_2`、`_3` 序号），保存在当前配方目录（D:\Printer\Data），**原配方文件保留不删除**，创建后 `FilePath`（改为 private set）切换数据源；UI 用 `TTypeMini.Warn` 橙色按钮醒目提示
   - **并发保存修复**：验证时发现自动保存（单元格编辑/增删行列触发）与手动保存并发写文件锁冲突（IOException: being used by another process）→ VM 加 `SemaphoreSlim _saveLock`，`SaveCoreAsync` 串行化写盘
   - **View 工具栏**：FlowLayoutPanel 6 按钮（刷新/保存/新增行/新增列/新建配方/打开文件夹），`CommandManagerHelper.Bind` 绑定命令
   - **运行时验证 16/16 PASS**（临时控制台项目，已清理）：时间戳文件名/原文件保留/数据源切换/同秒防覆盖/重复编号拒绝/原值还原/唯一编号通过/非编号列修改/新增行自动编号/新增列/保存落盘/重载一致
   - 构建 0 警告 0 错误
0.9 ✅ **配方页 AntdUI Table 化**（用户需求：清空配方页 + 加入 Excel 表格 + AntdUI Table 展示）：
   - **重写 `ViewModels/RecipePageViewModel`**：移除抽屉下发业务（Drawers/SelectedDrawer/SendSingleCommand 全部删除），新增 RecipeTable（DataTable）+ LoadCommand（AsyncRelayCommand + IsLoading 防重复）+ InitializeAsync（页面 Load 触发）
   - **重写 `Views/Pages/RecipePage`**：头部（标题/数据源路径说明/刷新按钮 AntdUI.Button）+ AntdUI.Table（Dock.Fill，Bordered）
   - **AntdUI 2.4.7 Table API 实证**（反射 + 编译验证）：`Binding<T>(AntList<T>)` / `Binding<T>(BindingList<T>)` 二选一；动态列场景用 `AntList<AntItem[]>`，每行为 `AntItem(key, value)` 数组，key 匹配 `Column(key, title)` 的 key；DataTable 绑定不可用（类型不匹配）
   - `BindTable(DataTable)`：UI 数据适配方法——依 DataTable 列重建 AntdUI.Column，逐行转 AntItem[] 后 Binding（属于 View 层 UI 转发，业务仍在 VM）
   - 构建通过（0 警告 0 错误）
0.8 ✅ **配方页 Excel 表格化**（已被 0.9 覆盖，IRecipeFileService/RecipeFileService 保留复用）：
   - **NuGet 新增 ClosedXML 0.105.1**（免费 MIT，无需安装 Office，读写 xlsx）
   - **新增 `Services/Interfaces/IRecipeFileService.cs` + `Services/RecipeFileService.cs`**：Service 层封装 Excel 读写（Load/Save/CreateBlank/OpenFolder），所有 IO 用 Task.Run 异步，Result<T> 统一返回，SDK 对象不外泄
   - **重写 `ViewModels/RecipePageViewModel`**：DataTable 数据源（直接绑定 DataGridView 双向回写）、SaveCommand/AddRow/AddColumn/DeleteRow/DeleteColumn/CreateBlank/OpenFolder/Reload 七命令，CurrentCell 动态参数驱动删除命令 CanExecute
   - **重写 `Views/Pages/RecipePage`**：AntdUI 工具栏（8 按钮 FlowLayoutPanel）+ DataGridView（浅色表头/斑马纹/单元格选择模式）；删除行列经 **ProxyCommand + 动态参数提供器**实时取当前单元格 (row,col)
   - **CommandManagerHelper 三次演进**：绑定元组升级为 `(Control, Func<object?> 参数提供器)`，点击/刷新时实时取参（固定参数与动态参数统一）
   - 数据源确认存在：`D:\Printer\Data\Recipe.xlsx`（Test-Path = True）
   - 构建 0 警告 0 错误，运行验证通过（PID 5680）

0. ✅ **抽屉单元格布局重构**（用户截图反馈：圆圈被裁剪/编号不可见/输入框缺失）：
   - **指示灯控件完全自适应**：直径 = min(宽×85%, 编号区以下高×92%)，**移除最小直径钳制**（原 64px 下限导致小单元格时圆圈溢出被裁剪）；控件过小（<8px）时跳过绘制防畸形
   - **单元格改双行 TLP**：指示灯行（100% 填充）+ 输入框行（**固定 46px**），输入框 Anchor=None 居中、宽度随单元格伸缩（48~220px），任意窗口尺寸下输入框永远完整可见
   - **输入框 Text 默认空**：移除 PlaceholderText，初始 Size(120,30)
   - **Mock 服务初始配方改为空**（原随机配方名）：启动时 18 抽屉全部空闲态，配方完全由用户输入驱动
0.5 ✅ **指示灯样式二次调整**（用户反馈）：
   - **空闲色恢复 LightGray**（蔚蓝改回浅灰，描边灰 `RGB(200,203,207)`）
   - **编号位于圆圈左上角"一点点"**：先绘圆（顶部预留 26% 高度），编号左对齐圆左缘（内缩 4%）、底部轻微压住圆顶（重叠 8% 字高）
   - **编号字号放大**：随圆直径缩放（直径×30%），钳制 14~34px 加粗
   - 构建 0 警告 0 错误，运行验证通过（PID 928）
1. ✅ **底部 Tab 页面导航**：
   - 新增 `Models/PageType.cs`（页面类型枚举：Print/Image/FeedDrawers/Recipe）
   - 新增 `Views/Controls/TabItemControl.cs`（自绘 Tab：文本 + 选中下划线 + 高亮样式）
   - 新增 `ViewModels/NavigationViewModel.cs`（CurrentPage 状态 + NavigateCommand，CanExecute 校验参数为 PageType）
   - `MainForm` 重构为**导航壳**：顶栏 + 底部 Tab 导航 + 中央 `_pageHost` 页面容器（懒创建 + 可见性切换）+ 右侧全局 Status 日志
2. ✅ **页面拆分**（原 MainForm 布局迁移）：
   - `Views/Pages/FeedDrawersPage.cs`：18 抽屉网格页（从 MainForm 原样迁移，逻辑不变）
   - `Views/Pages/PrintPage.cs`：打印管理占位页（打印按钮模拟 + 计数）
   - `Views/Pages/ImagePage.cs`：图像管理占位页（采集按钮模拟 + 计数）
   - `Views/Pages/RecipePage.cs`：配方管理页（左侧抽屉 ListBox + 右侧配方编辑 + **单抽屉下发**按钮）
3. ✅ **配方单抽屉下发**（顺带完成待办）：
   - 新增 `ViewModels/RecipePageViewModel.cs`：共享 MainViewModel 抽屉集合（同一批 VM 实例），SelectedDrawer + SendSingleCommand（AsyncRelayCommand + IsBusy 防重复）
   - Program.cs 中 MainViewModel 注册为**单例**（保证跨页面共享抽屉数据源）
4. ✅ **页面 ViewModel**：
   - `PrintPageViewModel` / `ImagePageViewModel`：占位页 VM（Title/Description/IsBusy/计数 + 异步命令）
5. ✅ **CommandManagerHelper 增强**：
   - 新增 `Bind(control, command, parameter)` 重载（参数化命令绑定，导航 Tab 点击传 PageType）
   - **关键修复**：绑定元组 `(Control, Parameter)` 存储参数，刷新时用**原参数**调 CanExecute（否则 NavigateCommand 被 CanExecute(null) 误判禁用全部 Tab）
6. ✅ **消除 nullable 警告**：RecipePage 的 `BindingSource(data, string.Empty)` 替代 null；BindRecipeBox 空选中项时不绑定数据源（禁用输入框）
7. ✅ 构建通过（**0 警告 0 错误**），程序启动运行正常（PID 18676）

## 下一步

1. 打印/图像页接入真实服务（IPrintService / VisionCameraService，放 Services/）
2. 用真实 PLC 通信实现替换 `MockDrawerService`（实现 `IDrawerService` 即可，建议 HslCommunication/S7NetPlus 放入 `Communications/`）
3. 配方管理页增强：抽屉列表显示配方名/状态列、批量下发
4. ~~补充单元测试（tests/ 目录）~~ ✅ 已完成（2026-09-03，55 用例全绿；此后每次任务修改功能必须配套测试，详 techContext.md 测试工作流）
5. ~~创建解决方案文件 .sln~~ ✅ 已完成（2026-09-03，UiTopMachine.slnx）

## 用户已确认的需求决策

| 决策点 | 结论 |
|--------|------|
| .NET 版本 | **.NET 10**（SDK 10.0.400）✅ 已落地 |
| 界面风格 | **浅色现代扁平风** ✅ 已落地 |
| 抽屉三态 | 有料+有配方=绿；无料+无配方=**LightGray**；其余=黄 ✅ 已落地（空闲色经两轮反馈最终定为浅灰） |
| 配方输入框 | 每个抽屉下方独立输入框，Text 默认空，双向绑定即时联动状态灯 ✅ 已落地 |
| 抽屉编号 | 位于圆圈左上角一点点（轻压圆边），大字号加粗 ✅ 已落地 |
| 退出按钮 | 右上角，红色危险语义 ✅ 已落地 |
| Tab 导航 | 底部四 Tab（打印/图像/进料抽屉/配方）✅ 已落地 |
| 配方数据源 | Excel 文件 D:\Printer\Data\Recipe.xlsx（ClosedXML 读写，可编辑/增删行列/新建空白/打开文件夹）✅ 已落地 |
| 工具栏按钮语义色 | 绿=新增（Success）/红=删除（Error）/橙=新建配方（Warn）/蓝=刷新保存（Primary），避免灰白样式被误读为禁用 ✅ 已落地 |

## 重要模式与偏好

- 语言：中文（代码注释、日志、UI 文案）
- 严格 MVVM：View 零业务逻辑，硬件交互全在 Service，统一 `Result<T>` 返回
- 异步规范：所有 IO 用 async/await，命令带 IsBusy 防重复
- UI 线程调度：`SynchronizationContext.Post`（VM 事件来自后台线程）
- **导航模式**：VM 持有 CurrentPage 状态，View 订阅 PropertyChanged 切换可见性；Tab 点击经参数化命令绑定回传 PageType

## 经验与项目洞察

### 错误类教训（已归档 → errorlog.md）

错误详情、生命周期状态与防回归清单统一见 [errorlog.md](errorlog.md)，此处仅留索引：
ERR-001 透明背景 · ERR-002 Timer 歧义 · ERR-003 CS0067 · ERR-004 参数化命令误禁用 · ERR-005 接口升级不同步 · ERR-006 MSB3027 锁 exe · ERR-007 xlsx 并发锁 · ERR-008 PowerShell `&&` · ERR-009 GBK 乱码 · ERR-010 AntdUI 绑定 · ERR-011 mkdir 多参数 · ERR-012 CellFocused 单击不触发 · ERR-013 命令刷新漏刷 · ERR-014 ClosedXML 空行蒸发 · ERR-015 表头重命名 DuplicateNameException（单元测试暴露） · ERR-016 带空格编号绕过唯一性校验（读写端 Trim 口径不一致） · ERR-017 单元格错位写入（AntdUI 行事件索引为含表头 1 基 INDEX，两轮实证修正） · ERR-018 编号查重真实表头失效（硬编码「配方编号」vs 用户「编号」+ 失败无弹窗） · ERR-019 新建配方文件流转语义偏差（另存副本 vs 备份轮转，返工）

### API 知识与技巧（保留本体）

- WinForms 无 WPF CommandManager，自建 `CommandManagerHelper` 维护命令↔控件绑定
- 自绘控件需标注 `[DesignerSerializationVisibility(Hidden)]` 避免设计器序列化警告
- ListBox 绑定 `ObservableCollection<T>` 用 BindingSource 包装，DisplayMember 显示 Index；选中项变化时 TextBox 重绑前必须 `DataBindings.Clear()` 防串数据
- **DataTable 直接绑定 DataGridView**：单元格编辑自动回写（无需手写 INPC）；整表替换时先 EndEdit + DataSource=null 再赋新表，DataBindingComplete 事件里做样式定制
- ClosedXML 写 xlsx：`ws.Columns().AdjustToContents()` 自适应列宽；表头样式用 `XLColor.FromHtml`；目录不存在时 `Directory.CreateDirectory` 兜底
- **AntdUI 2.4.7 Table**：动态列用 `AntList<AntItem[]>`，行 = `AntItem(key, value)[]`，key 匹配 `Column(key, title).key`（详 ERR-010）；单元格编辑 `EditMode = TEditMode.DoubleClick` + `EditLostFocus = true`，`CellEndEdit` 委托 `bool Handler(object, TableEndEditEventArgs)` 返回 false 自动还原显示（适合校验拒绝场景）；AntItem 是 class（key/value 属性）
- **反射检查第三方 API 套路**：临时控制台项目 LoadFrom DLL 反射导出类型与方法签名（需 UseWindowsForms=true 解析 WinForms 依赖），或直接引用包写编译用例实证，用完即删
- ⚠️ **ClosedXML 空行语义（ERR-014）**：`RowsUsed()` 只返回有内容的行（空行被跳过）；空字符串单元格不落盘。Excel 往返必须「写端空行占位 + 读端自己维护行号循环」，不要依赖 RowsUsed 枚举
- **VM↔View 输入请求模式（1.3 落地）**：VM 触发事件（携带 Title/Prompt）→ View 弹模态 InputDialog → 结果回填事件参数（Confirmed/InputText）→ VM 按结果继续业务；VM 全程不接触 UI 控件，适合弹框收集输入类交互
- **VM↔View 确认请求模式（1.4 落地）**：同输入请求模式，ConfirmRequestEventArgs（Title/Message/Confirmed）→ View 弹 ConfirmDialog；适合删除等危险操作二次确认
- ⚠️ **AntdUI 2.4.7 Table 焦点/点击 API（反射实证）**：`CellFocused` 事件鼠标单击**不触发**（偏向键盘焦点导航），跟踪鼠标选中必须订阅 `CellClick`（`TableClickEventArgs` 含 `RowIndex/ColumnIndex/Button/Clicks`，继承 MouseEventArgs）；两者签名一致可共用处理逻辑双订阅；`FocusedCell` 是嵌套类型 `Table+CELL`（外部不可直接用）；`SelectedIndex`/`SelectedIndexs` 为行选中（int/int[]），删除单格所在列需用 ColumnIndex
- ⚠️ **AntdUI 2.4.7 Table 索引基准（第二轮运行时实证，ERR-017）**：`CellEndEdit`/`CellClick`/`CellFocused` 三事件的 **RowIndex 均为含表头的 1 基内部 INDEX**（内部 rows[0]=表头，首条数据行=1；点击表头时行索引为 0 或 -1），**ColumnIndex 为 0 基**；`SelectedIndex` 亦为 1 基 INDEX。传给 0 基数据源（DataTable）前行索引必须减 1，恢复高亮反向 +1；删除行/列与编辑共用此换算规则

### 模式沉淀（详见 systemPatterns.md）

- **新建文件防覆盖命名**：`原名_yyyyMMdd_HHmmss.xlsx` + 同秒递增序号（`_2`、`_3`）+ `File.Exists` 循环检测
- **Service 带路径重载切换数据源模式**：`LoadAsync(path)/SaveAsync(table, path)` 成功后内部更新 `FilePath`（private set），ViewModel 无需感知路径切换细节，无参重载始终作用于"当前工作文件"
- **并发保存串行化**：`SemaphoreSlim(1,1)` 统一入口串行化写盘（详 ERR-007）
