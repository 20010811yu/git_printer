# 系统模式 (System Patterns)

## 系统架构

采用 **MVVM（Model-View-ViewModel）分层架构**，结合工业上位机场景扩展通信层与数据访问层：

```
d:\GitRepo\
├── Program.cs             # 入口：DI 组装 + 全局异常捕获
├── UiTopMachine.csproj    # net10.0-windows + WinForms + 高 DPI
├── Models\                # 数据模型层
│   ├── DrawerStatus.cs    # 抽屉三态枚举（Idle/Ready/Warning）
│   ├── DrawerModel.cs     # 抽屉实体（编号/有料/配方）
│   └── LogEntryModel.cs   # 日志实体 + LogLevel 枚举
├── ViewModels\            # 视图模型层
│   ├── MainViewModel.cs   # 抽屉集合/发送退出命令/日志/统计（单例，跨页共享）
│   ├── NavigationViewModel.cs  # 导航状态 CurrentPage + NavigateCommand（参数化）
│   ├── PrintPageViewModel.cs   # 打印页 VM（流水号自动递增持久化 + 码型选择 + 批量打印 + 失败弹窗）
│   ├── ImagePageViewModel.cs   # 图像页 VM（占位：模拟采集 + 计数）
│   ├── RecipePageViewModel.cs  # 配方页 VM（AntdUI Table 编辑 + 编号唯一校验 + 增删行列 + 确认弹框请求 + 备份轮转 + 行序整理 + 自动补行）
│   ├── DrawerItemViewModel.cs  # 单抽屉三态判定 + 配方双向绑定
│   └── LogEntryViewModel.cs    # 日志行（级别着色）
├── Views\                 # 视图层
│   ├── MainForm.cs        # 导航壳窗体（顶栏+底部Tab+页面容器+全局日志，零业务逻辑）
│   ├── Controls\
│   │   ├── DrawerIndicatorControl.cs  # 圆形状态灯（自绘）
│   │   ├── TabItemControl.cs          # 底部导航 Tab（文本+选中下划线，自绘）
│   │   ├── FlatButton.cs              # 圆角扁平按钮（Primary/Danger/Neutral）
│   │   └── LogPanelControl.cs         # 日志面板（级别着色）
│   ├── Dialogs\
│   │   ├── InputDialog.cs    # 通用输入弹框（纯 View，回车=确定/Esc=取消，零业务逻辑）
│   │   └── ConfirmDialog.cs  # 确认弹框（纯 View，⚠ 警示 + Error 红确定按钮，危险操作二次确认）
│   └── Pages\
│       ├── FeedDrawersPage.cs  # 进料抽屉监控页（18 抽屉网格）
│       ├── PrintPage.cs        # 打印管理页（流水号显示 + 码型下拉 + 张数 + 打印按钮，零业务逻辑）
│       ├── ImagePage.cs        # 图像管理页（占位）
│       └── RecipePage.cs       # 配方管理页（AntdUI Table 编辑 + RowHeight 行高 + 删除/新建确认弹框 + CellClick/CellFocused 焦点跟踪 + 行事件索引 1 基→0 基换算 + 可见高度补行）
├── Services\              # 业务服务层
│   ├── Interfaces\IDrawerService.cs   # 含统一 Result<T> 定义
│   ├── Interfaces\ILogService.cs
│   ├── Interfaces\IPrintService.cs       # 打印接口（TCP/Spooler 打印 + ZPL 生成 + 流水号持久化）
│   ├── Interfaces\IRecipeFileService.cs  # 配方文件接口（多配方：带路径加载/保存 + CreateBlankAsync(headers, blankRowCount) 备份轮转）
│   ├── MockDrawerService.cs           # 模拟实现（2 秒随机推送状态变化）
│   ├── LogService.cs                  # 事件推送 + 文件落盘
│   ├── ZplPrinterService.cs           # ZPL 打印（TCP 直连 + winspool RAW 私有封装 + 5 码型生成 + 流水号持久化，全 async）
│   └── RecipeFileService.cs           # ClosedXML 封装（xlsx 读写全 async，SDK 对象不外泄）
├── Communications\        # 通信层（⏳ 待接入真实 PLC 实现）
├── Common\
│   ├── Commands\          # MVVM 基础设施（已实现）
│   │   ├── ObservableObject.cs
│   │   ├── RelayCommand.cs    # ICommand + RelayCommand + AsyncRelayCommand
│   │   └── CommandManagerHelper.cs  # 命令↔控件绑定注册与刷新
│   ├── InputRequestEventArgs.cs  # VM↔View 输入请求事件参数（纯数据载体）
│   ├── ConfirmRequestEventArgs.cs  # VM↔View 确认请求事件参数（删除/新建配方等危险操作二次确认）
│   └── MessageRequestEventArgs.cs  # VM↔View 消息提示请求事件参数（校验失败弹窗，纯单向通知）
├── DataAccess\ / Configs\ / Resources\ / docs\   # ⏳ 待开发
├── tests\UiTopMachine.Tests\   # 单元测试（xUnit，net10.0-windows；100 用例覆盖命令/三态/xlsx往返/VM业务/编号查重/新建配方轮转/行序整理/ZPL打印）
└── memory-bank\           # 项目记忆文档
```

## 关键技术决策（已落地）

| 决策 | 说明 |
|------|------|
| MVVM on WinForms | 手写基础设施（ObservableObject/RelayCommand），零冗余依赖 |
| 分层解耦 | View 只做布局与绑定；业务在 VM；硬件交互在 Service；统一 `Result<T>` 返回 |
| 通信独立成层 | Services 面向 `IDrawerService` 接口，真实 PLC 实现可无侵入替换 Mock |
| DI 组装 | Program.cs 中 ServiceCollection 注册：Service 单例、VM/View 瞬态 |

## 设计模式（使用中）

- **观察者模式** ✅：INPC 属性通知 + 服务事件推送（DrawerChanged / LogEmitted）
- **命令模式** ✅：ICommand + CommandManagerHelper（WinForms 无原生 CommandManager，自建绑定注册表；命令 RaiseCanExecuteChanged 触发事件 → 管理器刷新控件 Enabled；**支持参数化绑定**：Bind(control, command, parameter) 存储参数元组，刷新用原参数调 CanExecute）
- **依赖注入** ✅：Microsoft.Extensions.DependencyInjection 构造注入
- **Result 模式** ✅：`Result<T>` 统一成功/失败返回，Service 不抛 UI 异常
- **Service 带路径重载数据源切换** ✅：`LoadAsync(path)/SaveAsync(table, path)` 成功后内部更新 `FilePath`（private set），无参重载始终作用于"当前工作文件"，ViewModel 无感切换
- **新建空白配方备份轮转模式** ✅：原文件 `File.Move` 改名（原名+时间戳，同秒递增防覆盖）备份，模板表（传入表头 + N 空白行）写入**原路径**——新配方沿用原文件名、FilePath 不变；VM 先经通用 `ConfirmationRequested` 确认再轮转 + 内存构造同构表立即显示（ERR-019 教训：文件生命周期需求先对齐流转语义）
- **行序整理 + 自动补行模式** ✅：`CompactRows`（数据连续、空白垫底，新表整体替换防半删态）在加载/编辑/删除后触发；`EnsureMinRows`（View 依可见高度计算）补真实可编辑空白行填满页面
- **ZPL 打印服务封装模式** ✅：Socket/winspool 句柄全私有封装在 Service 内部；双通道（TCP 直连带超时 / Spooler RAW）可配置（构造参数）；全 async（Task.Run）；流水号持久化（保留位数补零）由 VM 调度递增
- **外部标识符识别模式（候选列表 + 规范化）** ✅：业务规则锚定的列名/表头来自外部文件，识别必须候选列表（配方编号/编号）+ Trim + 忽略大小写，统一入口 `FindRecipeIdColumnIndex()` 定位；配套「校验拦截 + 弹窗告知」一体交付（详 ERR-018）
- **VM→View 消息提示请求模式** ✅：`MessageRequestEventArgs`（Title/Message 纯数据，无回填）→ View 弹 MessageBox（后台线程经 BeginInvoke 封送）；与输入请求（回填 InputText）/确认请求（回填 Confirmed）构成三类弹框交互模式
- **导航模式（页面路由）** ✅：NavigationViewModel 持有 CurrentPage（PageType 枚举），MainForm 订阅 PropertyChanged → 页面懒创建 + 可见性切换；Tab 点击经参数化命令回传 PageType
- **工厂/策略模式** ⏳：预留（真实多协议通信接入时启用）
- **测试桩模式** ✅：StubLogService（内存记录日志供断言）/ StubPrintService（记录 ZPL、可控失败）/ 事件参数回填模拟 View 弹框（VM 测试零 UI 依赖）/ 临时目录 + IDisposable 每测试隔离
- **配套测试模式** ✅：每次功能修改同步写/更新测试，用例名关联 ERR 编号（如 `ERR014_保存含末尾空行的表_重载后行数不变`），dotnet test 即自动回归全部历史修复

## 核心业务规则：抽屉三态判定

```
HasMaterial && HasRecipe   → Ready   (绿 #4CAF50)
!HasMaterial && !HasRecipe → Idle    (灰 #BDC3C7)
其余                        → Warning (黄 #FFC107)
```
实现位置：`DrawerItemViewModel.RefreshStatus()`；配方输入框双向绑定（DataSourceUpdateMode.OnPropertyChanged）即时联动状态灯。

## 核心业务规则：配方编号唯一性

- **编号列识别**：候选表头 `{ "配方编号", "编号" }`，Trim + 忽略大小写匹配（用户真实表头为「编号」）；识别不到时编号校验不启用并记 Warn 日志
- **三道防线**：① 单元格编辑提交（`TryCommitCellEdit` → `IsDuplicateRecipeId`，排除自身行，重复拒绝 + 弹窗 + 强制还原显示）；② 保存兜底（`ValidateRecipeIdUnique`，重复拒绝落盘，手动保存弹窗/自动保存静默日志，空编号跳过不拦空白行）；③ 新增行自动编号（`GenerateUniqueRecipeId`，R001 起跳过已占用）
- **规范化口径**：编号值与表头一律 Trim 后比较（详 ERR-016/018）

## 核心业务规则：新建配方备份轮转（v1.7b）

- **流程**：用户确认 → 原文件 `File.Move` 改名 `原名_yyyyMMdd_HHmmss.xlsx` 备份（同秒递增 `_2/_3` 防覆盖，无原文件跳过）→ 模板表（表头沿用当前表 + 10 空白行，空格占位持久化）写入原路径 → FilePath 不变，页面立即显示新配方
- **原配方数据**：完整保留在备份文件中，绝不删除、不覆盖任何已有文件

## 核心业务规则：ZPL 打印与流水号（v1.8）

- **双通道打印**：TCP 直连（默认 192.168.1.200:9100，3s 超时）/ Windows Spooler RAW（默认打印机名 "zpl"）；Socket/句柄私有封装，全 async
- **流水号自动递增**：打印成功 +1 持久化 `D:\Printer\Data\SerialNumber.txt`（保留位数补零，999999→1000000 自然进位）；重开不断号；**批量中途失败流水号不前进**（防跳号，已打张由用户决定处理）；持久化失败弹窗警示
- **码型**：二维码 ^BQ / Code39 ^B3 / Code128 ^BC（源码 `"LL300"` 笔误已修正）/ PDF417 ^B7 / 数字文本 ^AO

## 核心业务规则：表格显示形态（v1.7b）

- **行序整理**：数据行连续排列（保持相对顺序），空白行只出现在末尾——加载完成/单元格修改后/删除行后自动触发（`CompactRows`），整理后落盘
- **页面填满**：View 依表格可见高度计算期望行数（`(Height-40)/36`），不足时 `EnsureMinRows` 在末尾补**真实可编辑空白行**（双击录入、过查重、自动保存、持久化）
- **行高**：`RowHeight=36` / `RowHeightHeader=40`（AntdUI 实证 API）

## 组件关系（数据流）

```
TabItemControl 点击 ──NavigateCommand(PageType)──▶ NavigationViewModel.CurrentPage ──PropertyChanged──▶ MainForm.ShowPage
                                                        │
                                                        ▼
MockDrawerService ──DrawerChanged事件──▶ MainViewModel ──ObservableCollection──▶ FeedDrawersPage 绑定
     (后台线程)       SynchronizationContext.Post 调度至 UI 线程      │
                                                                     ▼
用户输入配方 ▶ TextBox 双向绑定 ▶ DrawerItemViewModel.Recipe ▶ RefreshStatus() ▶ Status 变更
                                                                     │
DrawerIndicatorControl.Status 属性绑定 ◀─────────────────────────────┘ → Invalidate 重绘变色

打印页 ▶ PrintCommand ▶ PrintPageViewModel（流水号校验→GenerateZpl→PrintByIpAsync 逐张）▶ ZplPrinterService（TCP/Spooler）▶ 打印机
                    │ 成功后 +1 持久化 SerialNumber.txt

按钮点击 ▶ CommandManagerHelper.Bind ▶ command.Execute ▶ VM 业务 ▶ 日志/状态更新 ▶ 界面刷新
```

## 已知陷阱与规避模式

> 完整错误条目（现象/根因/解决/验证/教训）统一沉淀于 [errorlog.md](errorlog.md)，此处仅列高频陷阱速查：

| 陷阱 | 规避模式 | 详见 |
|------|---------|------|
| 自绘控件 `BackColor = Color.Transparent` 抛异常 | 用具体色 + GDI+ `FromArgb` 半透明画刷 | ERR-001 |
| `Timer` 歧义 CS0104 | 后台定时器完全限定 `System.Threading.Timer` | ERR-002 |
| 参数化命令被 `CanExecute(null)` 误判禁用 | 绑定存原参数/参数提供器，刷新带参调 CanExecute | ERR-004 |
| 属性 setter 漏刷命令致按钮永久禁用 | VM 统一 `RefreshAllCommandStates()` 全量刷新 | ERR-013 |
| 接口升级后实现类/调用方未同步（CS0535） | 改接口 → 同步实现类 → 全局搜索旧方法名调用 | ERR-005 |
| 构建报 MSB3027 exe 被锁 | 先 taskkill 再构建 | ERR-006 |
| 自动保存与手动保存并发写 xlsx 冲突 | `SemaphoreSlim(1,1)` 统一入口串行化 | ERR-007 |
| PowerShell 环境 `&&` / `mkdir` 多参数不可用 | 单命令或 `;` 分隔；`New-Item -ItemType Directory` | ERR-008/011 |
| AntdUI Table 不接受 DataTable | View 层 `BindTable` 适配为 `AntList<AntItem[]>` | ERR-010 |
| AntdUI `CellFocused` 鼠标单击不触发 | 跟踪鼠标选中订阅 `CellClick`（双订阅共用处理） | ERR-012 |
| AntdUI 行事件索引传 0 基数据源偏移 +1（编辑写到下一行/删除删错行/末行改不动/查重被短路） | `CellEndEdit`/`CellClick`/`CellFocused` 的 RowIndex 均为含表头 1 基 INDEX（ColumnIndex 0 基）：传 DataTable 前行减 1；恢复高亮 SelectedIndex 反向 +1；第三方索引基准必须对照实验实证 | ERR-017 |
| 编号查重对真实文件静默失效（列名硬编码「配方编号」vs 用户表头「编号」） | 业务规则关联外部标识符（表头/列名）必须候选列表 + Trim + 忽略大小写匹配（`FindRecipeIdColumnIndex` 统一入口）；校验失败必须弹窗告知（`MessageRequested` 事件），拒绝提交同时 TableVersion++ 强制还原显示；识别不到编号列记 Warn 不静默 | ERR-018 |
| 新建配方文件流转语义错（另存副本 vs 备份轮转，返工） | 涉及文件生命周期（重命名/移动/删除/覆盖）的需求，动手前先列出「原文件去向 × 新文件命名」候选矩阵让用户确认 | ERR-019 |
| 数值型流水号 ToString 丢失前导零（打印内容 1 而非 000001） | 递增用 ulong、显示/打印前按原始位数 `PadLeft(digits, '0')` 还原；进位（999999→1000000）自然扩展 | 2026-09-03 v1.8 |
| ClosedXML `RowsUsed()` 跳过空行致保存的空行蒸发 | 写端整行全空时首列写空格占位；读端 `LastRowUsed().RowNumber()` + for 循环逐行装载 | ERR-014 |
| 读外部文件建 DataTable 用「预置表头+重命名」遇重名列崩溃 | 按文件实际表头新建 DataTable 重建列结构（空表头「列N」兜底） | ERR-015 |
| 主项目 glob 误收 tests 目录测试代码（CS0246/CS0579） | 主 csproj 加 `Compile Remove="tests\**\*.cs"` + `<None Remove>` | 2026-09-03 测试搭建 |

## 关键实现路径（✅ 已完成 / ⏳ 待做）

1. ✅ `Common\Commands`：MVVM 基础设施（含参数化命令绑定）
2. ✅ 18 抽屉网格 + 三态指示灯 + 配方绑定 + Status 日志
3. ✅ 底部 Tab 页面导航（NavigationViewModel + 页面懒创建切换）
4. ✅ 配方单抽屉下发（RecipePageViewModel 共享主页面数据源，已被 0.9 配方 Table 化覆盖）
5. ✅ 配方文件服务多配方接口（IRecipeFileService 带路径重载 + CreateBlankAsync(headers, blankRowCount) 备份轮转，v1.7 表头参数化 + 空白行持久化；v1.7b 沿用原名）
6. ✅ VM↔View 输入请求模式（ColumnNamingRequested 事件 + InputDialog 弹框，1.3 落地）
7. ✅ 危险操作确认模式（ConfirmationRequested 事件 + ConfirmDialog，删除行/列与新建配方共用，v1.4 落地 / v1.7b 通用化；CellFocused 单击不触发修正详 ERR-012）
8. ✅ 单元测试基础设施（tests/UiTopMachine.Tests：xUnit + .slnx + .gitignore，100 用例全绿；每次任务修改功能必须配套测试并全绿，2026-09-03 固化，详 techContext.md「测试工作流」）
9. ✅ ZPL 打印服务（IPrintService/ZplPrinterService：TCP/Spooler 双通道 + 5 码型生成 + 流水号持久化自动递增，打印页真实可用，v1.8）
10. ⏳ `Communications`：统一通信接口 ICommunication（PLC/串口/TCP）
11. ⏳ 图像真实服务接入（VisionCameraService）
12. ⏳ DataAccess：数据库历史存储