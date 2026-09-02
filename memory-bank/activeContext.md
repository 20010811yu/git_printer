# 当前上下文 (Active Context)

## 当前工作焦点

**多配方文件管理 + 构建修复** —— IRecipeFileService 升级为多配方接口（带路径加载/保存 + 新建配方带配方名/编号），RecipeFileService 实现同步补齐，构建修复通过（0 错误）。
  
## 最近变更（2026-09-02）

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
4. 补充单元测试（tests/ 目录）
5. 创建解决方案文件 .sln（可选）

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

## 重要模式与偏好

- 语言：中文（代码注释、日志、UI 文案）
- 严格 MVVM：View 零业务逻辑，硬件交互全在 Service，统一 `Result<T>` 返回
- 异步规范：所有 IO 用 async/await，命令带 IsBusy 防重复
- UI 线程调度：`SynchronizationContext.Post`（VM 事件来自后台线程）
- **导航模式**：VM 持有 CurrentPage 状态，View 订阅 PropertyChanged 切换可见性；Tab 点击经参数化命令绑定回传 PageType

## 经验与项目洞察

- ⚠️ **WinForms 透明背景**：普通 Control 未启用 SupportsTransparentBackColor 时 `BackColor = Color.Transparent` 抛 ArgumentException；用具体色 + GDI+ 半透明画刷替代
- ⚠️ **参数化命令刷新陷阱**：CommandManagerHelper 若统一用 `CanExecute(null)` 刷新，参数化命令（PageType 参数）会被误判禁用 → 绑定时存储参数元组，刷新用原参数
- Cline 终端实际为 PowerShell：多命令用 `;` 分隔（`&` 报错）；`dotnet build` 输出为 GBK 乱码（可解读："已成功生成，0个警告，0个错误"）
- ImplicitUsings + UseWindowsForms 会引入 `System.Windows.Forms`，`Timer` 与 `System.Threading.Timer` 歧义需完全限定
- WinForms 无 WPF CommandManager，自建 `CommandManagerHelper` 维护命令↔控件绑定
- 自绘控件需标注 `[DesignerSerializationVisibility(Hidden)]` 避免设计器序列化警告
- ListBox 绑定 `ObservableCollection<T>` 用 BindingSource 包装，DisplayMember 显示 Index；选中项变化时 TextBox 重绑前必须 `DataBindings.Clear()` 防串数据
- ⚠️ **WinForms ICommand 无泛型约束**：删除行列等依赖运行时状态的命令用「动态参数提供器」（Bind 时存 Func<object?>，点击/刷新实时取参）+ View 局部 ProxyCommand 延迟解析命令实例，CanExecute 与 Execute 取同一参数源保证一致性
- **DataTable 直接绑定 DataGridView**：单元格编辑自动回写（无需手写 INPC）；整表替换时先 EndEdit + DataSource=null 再赋新表，DataBindingComplete 事件里做样式定制
- ClosedXML 写 xlsx：`ws.Columns().AdjustToContents()` 自适应列宽；表头样式用 `XLColor.FromHtml`；目录不存在时 `Directory.CreateDirectory` 兜底
- ⚠️ **AntdUI 2.4.7 Table 只接受 AntList<T> / BindingList<T>**：不支持 DataTable 直接 Binding；动态列（Excel 任意表头）用 `AntList<AntItem[]>`，行 = `AntItem(key, value)[]`，key 匹配 `Column(key, title).key`；反射确认签名为 `Binding<T>(AntList<T>)` 与 `Binding<T>(BindingList<T>)`
- **反射检查第三方 API 套路**：临时控制台项目 LoadFrom DLL 反射导出类型与方法签名（需 UseWindowsForms=true 解析 WinForms 依赖），或直接引用包写编译用例实证，用完即删
- ⚠️ **AntdUI 2.4.7 Table 单元格编辑**：`EditMode = TEditMode.DoubleClick`（None/Click/DoubleClick）+ `EditLostFocus = true`（失焦自动提交）；`CellEndEdit` 委托签名 `bool Handler(object, TableEndEditEventArgs)`，参数含 `Value/Record/RowIndex/ColumnIndex/Column`；返回 false 自动还原单元格显示（适合校验拒绝场景）；AntItem 是 class（key/value 属性）
- ⚠️ **并发写同一 xlsx 文件锁冲突**：自动保存（编辑/增删触发）与手动保存并发时 ClosedXML SaveAs 抛 IOException（being used by another process）→ ViewModel 层用 `SemaphoreSlim(1,1)` 串行化所有 SaveAsync 调用（SaveCoreAsync 统一入口）
- **新建文件防覆盖命名**：`原名_yyyyMMdd_HHmmss.xlsx` + 同秒递增序号（`_2`、`_3`）+ `File.Exists` 循环检测，保证多实例/快速连点均不覆盖
- ⚠️ **接口升级必须同步实现类**：CS0535（不实现接口成员）多因接口加方法后实现类未同步；修完 Service 后还要全局搜索 ViewModel 对旧方法名的调用（本次 `CreateBlankFileAsync` 已删但 VM 仍调用，Service 修完还会在 VM 处二次编译报错）
- **Cline 终端是 PowerShell 而非 cmd**：`cd xxx && cmd` 的 `&&` 分隔符报错（"标记&&不是有效语句分隔符"）→ 工作目录已在项目根时直接执行命令，或用 `;` 分隔
- **Service 带路径重载切换数据源模式**：`LoadAsync(path)/SaveAsync(table, path)` 成功后内部更新 `FilePath`（private set），ViewModel 无需感知路径切换细节，无参重载始终作用于"当前工作文件"
