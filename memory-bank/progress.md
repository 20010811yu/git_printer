# 项目进度 (Progress)

## ✅ 已完成功能

### 配方页修改功能测试与修复（2026-09-03）⭐ 最新

- [x] **新增 4 个空格规范化测试用例**（`RecipePageViewModelTests`）：编号带空格提交后规范化 / 带空格重复编号拒绝提交 / 保存校验拒绝带空格重复 / 普通文本列 Trim 与重载一致
- [x] **测试暴露并修复产品 Bug（ERR-016）**：带首尾空格的编号（如 `" R001 "`）绕过唯一性校验，重载 Trim 后与已有编号撞车破坏唯一不变量；且单元格输入的空格重载后悄然消失（数据漂移）——根因 = 读端 `LoadCoreAsync` 有 Trim 而写端/校验端无
- [x] **修复 `RecipePageViewModel` 四处对齐 Trim 口径**：`TryCommitCellEdit` 提交前规范化、`IsDuplicateRecipeId`/`ValidateRecipeIdUnique`/`GenerateUniqueRecipeId` 均 Trim 后比较
- [x] 验证：dotnet test **59/59 PASS**（55 + 4 新用例）+ dotnet build 0 警告 0 错误
- [x] Memory Bank 同步更新（errorlog 新增 ERR-016 + 防回归清单 #11 + activeContext 测试记录）

### 单元测试基础设施搭建（2026-09-03）

- [x] **测试项目** `tests/UiTopMachine.Tests`（xUnit 2.9.3，TFM=net10.0-windows + UseWindowsForms，引用主项目）
- [x] **解决方案** `UiTopMachine.slnx`（.NET 10 新格式，挂载主项目 + 测试项目，根目录一键 `dotnet test`）
- [x] **.gitignore**（排除 bin/obj/TestResults/IDE 文件）
- [x] **首批 55 用例**：RelayCommand/AsyncRelayCommand（CanExecute/IsBusy 防重复）、抽屉三态判定（Theory 4 组合）、xlsx 往返（ERR-014 空行保留/中间空行位置/内容一致/空格还原 + CreateBlank 时间戳防覆盖）、VM 业务（删除行列确认回填/列校验/编号唯一/ERR-013 命令恢复回归）
- [x] **测试暴露并修复产品 Bug（ERR-015）**：`LoadCoreAsync` 表头「预置+重命名」遇重名列抛 DuplicateNameException（自 0.8 潜伏），改「按文件实际表头重建列结构」；修复后 55/55 全绿
- [x] **主 csproj 排除 tests 目录**（`Compile Remove="tests\**\*.cs"`，防 glob 误收测试代码）
- [x] **测试工作流固化**（techContext.md）：每次任务修改功能必须配套测试并 `dotnet test` 全绿后才交付；结果记录 = 控制台 + trx + activeContext 测试记录表 + errorlog
- [x] 验证：dotnet test 55/55 PASS + dotnet build 0 警告 0 错误
- [x] Memory Bank 同步更新（errorlog 新增 ERR-015 + systemPatterns/techContext/activeContext）

### 新增行空行蒸发修复（2026-09-02）

- [x] **1.4c 修复**：「新增行/清空的行在刷新后消失」（ERR-014）——根因 = ClosedXML 空字符串单元格不落盘 + `LoadCoreAsync` 用 `RowsUsed()` 枚举跳过空行，空行往返后蒸发（日志铁证：四次「已新增第 19 行」刷新均回 18 行）
- [x] `RecipeFileService.SaveCoreAsync`：整行全空时首列写单个空格 `" "` 占位，保证空行在 xlsx 文件中真实存在
- [x] `RecipeFileService.LoadCoreAsync`：弃用 `RowsUsed()`，改 `LastRowUsed().RowNumber()` + for 循环逐行装载（空行/中间空行全保留），空格占位 `Trim` 还原
- [x] 附带：`LastRowUsed()` 空引用防护（CS8602 清零）；VM `AddRow` 无编号列时记 Info 日志提示
- [x] 验证：构建 0 警告 0 错误；临时控制台往返验证 8 PASS / 0 FAIL（空行保留/中间空行位置不变/有数据行完整/真实 Recipe.xlsx 可加载），验证项目用完即删
- [x] Memory Bank 同步更新（errorlog 新增 ERR-014 + systemPatterns 陷阱表 + activeContext）

### 删除行/列 + 确认弹框（2026-09-02）

- [x] **1.4b 修正**：修复新增行按钮永久禁用（ERR-013——`AddRowCommand.RaiseCanExecuteChanged` 从未被触发）；VM 提取 `RefreshAllCommandStates()` 全量刷新，三个属性 setter 统一接入（PID 24312）
- [x] **1.4a 修正**：新增行/列按钮 `Default` → `Success` 绿色（灰白样式被误读为禁用）；删除后焦点钳制到相邻行/列（`Math.Min`），连续删除无需重新点选 + `SelectedIndex` 恢复选中高亮（PID 16500）
- [x] 新增 `Common/ConfirmRequestEventArgs.cs`（VM↔View 确认请求事件参数）+ `Views/Dialogs/ConfirmDialog.cs`（⚠ 警示 + Error 红确定按钮）
- [x] `RecipePageViewModel`：`DeletionConfirmRequested` 事件 + `DeleteRowCommand`/`DeleteColumnCommand`（CanExecute 校验索引 → 无选中时按钮禁用）；删除链路 = 索引校验 → 确认弹框 → 移除 + TableVersion++ + 自动保存
- [x] `RecipePage`：工具栏 6→8 按钮（删除行/列 Error 红色）+ **`CellClick` + `CellFocused` 双事件焦点跟踪**（实测修正，详 ERR-012）+ 动态参数提供器 + 重建表格时重置焦点
- [x] 反射实证 AntdUI 2.4.7 `CellClick`（`TableClickEventArgs`）/ `CellFocused`（`TableCellFocusedEventArgs`）均含 RowIndex/ColumnIndex；`CellFocused` 鼠标单击不触发
- [x] 构建验证通过（0 警告 0 错误）+ 运行验证通过（PID 21960 / 修正后 PID 25012）
- [x] Memory Bank 同步更新（errorlog 新增 ERR-012）

### 构建修复与多配方接口同步（2026-09-02）

- [x] 诊断构建失败：3 个 CS0535（`RecipeFileService` 未实现 `IRecipeFileService` 的 `LoadAsync(string)` / `SaveAsync(DataTable, string)` / `CreateBlankAsync(string, string)`）
- [x] 重写 `Services/RecipeFileService.cs`：提取 `LoadCoreAsync/SaveCoreAsync` 核心；带路径重载成功后切换 `FilePath`；实现 `CreateBlankAsync(recipeName, recipeId)`（安全化文件名 + 时间戳 + 同秒递增防覆盖，写入默认表头 + 首行编号）
- [x] 修复 `ViewModels/RecipePageViewModel.cs`：新建配方改调接口方法 `CreateBlankAsync`（配方名/编号由 `GenerateUniqueRecipeId()` 生成）
- [x] 新增列弹框交互（v1.3）：`Views/Dialogs/InputDialog.cs` + `Common/InputRequestEventArgs.cs`，VM 事件请求 → View 弹框收集列名，空列名校验失败记 Error 日志
- [x] 构建验证通过（0 警告 0 错误）
- [x] Memory Bank 同步更新（新建 errorlog.md，11 条错误归档 + 防回归清单）

### 页面导航与配方管理（2026-09-01）

- [x] 底部 Tab 页面导航（打印/图像/进料抽屉/配方四页切换）
  - `Models/PageType.cs` 页面枚举 + `Views/Controls/TabItemControl.cs` 自绘 Tab
  - `ViewModels/NavigationViewModel.cs`：CurrentPage 状态 + NavigateCommand（参数化命令）
  - `MainForm` 重构为导航壳（页面懒创建 + 可见性切换 + 全局 Status 日志）
- [x] 页面拆分：`Views/Pages/`（FeedDrawers/Print/Image/Recipe 四个 UserControl）
- [x] 配方管理页：抽屉列表 + 配方编辑 + **单抽屉下发**（RecipePageViewModel 共享 MainViewModel 抽屉数据源）
- [x] 占位页 VM：PrintPageViewModel / ImagePageViewModel（异步模拟命令 + IsBusy）
- [x] CommandManagerHelper 增强：参数化命令绑定重载 + 按原参数刷新 CanExecute（关键修复）
- [x] 构建通过（0 警告 0 错误）并运行验证

### 页面开发：进料抽屉监控（2026-08-31）

- [x] `UiTopMachine.csproj`（net10.0-windows + WinForms + 高 DPI）
- [x] NuGet：Microsoft.Extensions.DependencyInjection 10.0.11
- [x] MVVM 基础设施（WinForms 手写实现）
  - ObservableObject（INotifyPropertyChanged）
  - RelayCommand / AsyncRelayCommand（ICommand + IsBusy 防重复）
  - CommandManagerHelper（命令↔按钮绑定与 Enabled 刷新）
- [x] 三态抽屉状态判定（DrawerItemViewModel）
  - 有料+有配方 → 绿（就绪）；无料+无配方 → 灰（空闲）；其余 → 黄（预警）
- [x] 配方输入框与抽屉 VM 双向绑定（输入即时联动状态灯颜色）
- [x] 自绘控件：DrawerIndicatorControl（渐变圆形状态灯+编号）、FlatButton（圆角扁平按钮：Primary/Danger/Neutral）、LogPanelControl（级别着色日志）
- [x] MainForm 布局（浅色现代风）：顶栏（左上退出按钮+公司名）、18 抽屉网格（Flow 6 列）、右侧 Status 日志、底部 Tab
- [x] Mock 服务模拟抽屉状态变化（2 秒随机推送，演示动态效果）
- [x] DI 组装 + 全局异常捕获 + 日志落盘（logs/yyyyMMdd.log）
- [x] 构建通过（0 警告 0 错误）并成功运行

### 项目基础结构（2026-08-31）

- [x] MVVM 分层目录结构 + .gitkeep 占位
- [x] Memory Bank 初始化（6 核心文档）

## 🔨 待构建功能

- [ ] 打印/图像页接入真实服务（IPrintService / VisionCameraService）
- [ ] 真实 PLC 通信实现（替换 MockDrawerService，放 Communications/，建议 HslCommunication/S7NetPlus）
- [ ] 配方管理页增强（列表显示配方名/状态列、批量下发）
- [x] ~~单元测试（tests/）~~ ✅ 2026-09-03 完成（xUnit，55 用例全绿，工作流固化）
- [x] ~~解决方案文件 .sln~~ ✅ 2026-09-03 完成（UiTopMachine.slnx）

## 📌 当前状态

**构建通过（0 警告 0 错误）**：`dotnet build` 后执行 `bin\Debug\net10.0-windows\UiTopMachine.exe`。
底部 Tab 可在打印/图像/进料抽屉/配方四页面间切换；进料抽屉页 18 抽屉三态实时变化（Mock），配方输入联动状态灯；
配方管理页为 AntdUI Table（双击编辑 + 编号唯一校验 + 增删行列 + 新建空白配方带时间戳不删原文件 + 打开文件夹）；
**新增列为弹框交互**（InputDialog 收集列名 + 空列名校验失败，v1.3）；
**删除行/列带确认弹框**（ConfirmDialog 二次确认 + 单元格单击选中驱动按钮可用态，v1.4）；
**新增行空行可持久化**（写端空格占位 + 读端逐行装载，空行/清空行刷新后不再消失，v1.4c/ERR-014）；
配方服务已升级多配方接口（带路径加载/保存重载 + `CreateBlankAsync(recipeName, recipeId)`）；全局 Status 日志跨页面共享。

## ⚠️ 已知问题

> 唯一事实来源见 [errorlog.md](errorlog.md)，此处仅列当前 🟡 规避中的条目：

- [ERR-008](errorlog.md)：Cline 终端为 PowerShell，`&&` 分隔符不可用（用 `;` 或单命令执行）
- [ERR-009](errorlog.md)：构建输出 GBK 乱码（仅显示问题，凭"已成功生成/0 错误"辨识）
- [ERR-011](errorlog.md)：PowerShell `mkdir` 多参数不可用（用 `New-Item -ItemType Directory`）
- Mock 服务每次启动随机生成初始状态（演示预期行为，非缺陷）

## 📈 项目决策演进记录

| 日期 | 决策 | 原因 |
|------|------|------|
| 2026-08-31 | 采用 MVVM 模式组织 WinForms 项目 | 用户要求，界面与逻辑解耦 |
| 2026-08-31 | 通信层独立成 Communications 目录 | 工业上位机核心是通信，按设备类型扩展 |
| 2026-08-31 | .NET 10（SDK 10.0.400） | 用户指定版本 |
| 2026-08-31 | 浅色现代扁平风 | 用户选择（否决深色工业风） |
| 2026-08-31 | 抽屉三态：绿=有料有配方/灰=无料无配方/黄=其余 | 用户定义业务规则 |
| 2026-08-31 | 退出按钮置于左上角（红色） | 用户要求（原版在右上） |
| 2026-08-31 | DI 用 Microsoft.Extensions.DependencyInjection | 规范的依赖注入，替代单例滥用 |
| 2026-09-01 | 导航状态放 NavigationViewModel（VM 驱动页面切换） | MVVM 解耦，View 只订阅 PropertyChanged 切可见性 |
| 2026-09-01 | MainViewModel 注册单例（跨页面共享抽屉集合） | RecipePage 与 FeedDrawersPage 数据源一致，避免状态分裂 |
| 2026-09-01 | 页面懒创建 + 缓存（首次导航才构建 UserControl） | 降低启动开销，保持页面状态 |
| 2026-09-02 | IRecipeFileService 多配方接口（带路径重载 + CreateBlankAsync 带参） | 支持多配方文件管理；Service 内部切换 FilePath，ViewModel 无感 |
| 2026-09-02 | 新建配方文件名 = 安全化配方名 + 时间戳 | 文件名可读且防覆盖；SanitizeFileName 过滤非法字符 |
| 2026-09-02 | 新增列改为 VM 事件请求 → View 弹 InputDialog 收集列名 | 严格 MVVM：VM 不接触 UI 控件；空列名校验失败并记 Error 日志 |
| 2026-09-02 | 删除行/列经 ConfirmDialog 二次确认（Error 红确定按钮） | 危险操作防误触；确认文案带行号/配方编号/列名便于核对 |
| 2026-09-02 | 删除命令用单元格焦点索引 + 动态参数提供器 | 无选中行/列时按钮自动禁用，重建表格后焦点重置 |
| 2026-09-02 | 焦点跟踪改 CellClick + CellFocused 双订阅（初版仅 CellFocused 实测单击不触发） | 反射实证 + 用户实测：鼠标选中必须走 CellClick（详 ERR-012） |
| 2026-09-02 | 新增按钮 Success 绿色 + 删除后焦点钳制（1.4a） | Default 灰白样式被误读为禁用；焦点重置导致连续删除需反复点选 |
| 2026-09-02 | VM 命令刷新改统一全量刷新 `RefreshAllCommandStates()`（1.4b，ERR-013） | 属性 setter 逐个列举刷新命令天然易漏，漏刷即按钮永久禁用 |
| 2026-09-02 | 建立 errorlog.md 作为错误唯一事实来源 | 按记忆库规范集中管理错误条目/防回归清单，其他文件只留摘要+链接 |
| 2026-09-02 | Excel 空行持久化：写端空格占位 + 读端 LastRowUsed 行号循环（1.4c，ERR-014） | ClosedXML 空字符串单元格不落盘 + RowsUsed 跳过空行，空行往返后蒸发 |
| 2026-09-03 | 搭建 xUnit 单元测试 + 固化「每次任务修改功能必须配套测试并全绿」工作流 | 历史修复需永久回归守护；首轮测试即暴露潜伏产品 Bug（ERR-015）验证其价值 |
| 2026-09-03 | LoadCoreAsync 表头改「按文件实际列重建」 | 「预置+重命名」遇重名列抛 DuplicateNameException（ERR-015） |
| 2026-09-03 | VM 修改链路四处对齐 Trim 口径（提交/编辑校验/保存校验/自动编号） | 读端 Trim 写端无 → 带空格编号绕过唯一性校验、重载后撞车（ERR-016） |
