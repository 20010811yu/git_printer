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
│   ├── PrintPageViewModel.cs   # 打印页 VM（占位：模拟打印 + 计数）
│   ├── ImagePageViewModel.cs   # 图像页 VM（占位：模拟采集 + 计数）
│   ├── RecipePageViewModel.cs  # 配方页 VM（AntdUI Table 编辑 + 编号唯一校验 + 增删行列 + 确认弹框请求）
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
│   │   └── ConfirmDialog.cs  # 确认弹框（纯 View，⚠ 警示 + Error 红确定按钮，删除二次确认）
│   └── Pages\
│       ├── FeedDrawersPage.cs  # 进料抽屉监控页（18 抽屉网格）
│       ├── PrintPage.cs        # 打印管理页（占位）
│       ├── ImagePage.cs        # 图像管理页（占位）
│       └── RecipePage.cs       # 配方管理页（AntdUI Table 编辑 + 删除行/列确认弹框 + CellClick/CellFocused 焦点跟踪 + 行事件索引 1 基→0 基换算）
├── Services\              # 业务服务层
│   ├── Interfaces\IDrawerService.cs   # 含统一 Result<T> 定义
│   ├── Interfaces\ILogService.cs
│   ├── Interfaces\IRecipeFileService.cs  # 配方文件接口（多配方：带路径加载/保存 + CreateBlankAsync(recipeName, recipeId)）
│   ├── MockDrawerService.cs           # 模拟实现（2 秒随机推送状态变化）
│   ├── LogService.cs                  # 事件推送 + 文件落盘
│   └── RecipeFileService.cs           # ClosedXML 封装（xlsx 读写全 async，SDK 对象不外泄）
├── Communications\        # 通信层（⏳ 待接入真实 PLC 实现）
├── Common\
│   ├── Commands\          # MVVM 基础设施（已实现）
│   │   ├── ObservableObject.cs
│   │   ├── RelayCommand.cs    # ICommand + RelayCommand + AsyncRelayCommand
│   │   └── CommandManagerHelper.cs  # 命令↔控件绑定注册与刷新
│   ├── InputRequestEventArgs.cs  # VM↔View 输入请求事件参数（纯数据载体）
│   ├── ConfirmRequestEventArgs.cs  # VM↔View 确认请求事件参数（删除等危险操作二次确认）
│   └── MessageRequestEventArgs.cs  # VM↔View 消息提示请求事件参数（校验失败弹窗，纯单向通知）
├── DataAccess\ / Configs\ / Resources\ / docs\   # ⏳ 待开发
├── tests\UiTopMachine.Tests\   # 单元测试（xUnit，net10.0-windows；71 用例覆盖命令/三态/xlsx往返/VM业务/编号查重）
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
- **外部标识符识别模式（候选列表 + 规范化）** ✅：业务规则锚定的列名/表头来自外部文件，识别必须候选列表（配方编号/编号）+ Trim + 忽略大小写，统一入口 `FindRecipeIdColumnIndex()` 定位；配套「校验拦截 + 弹窗告知」一体交付（详 ERR-018）
- **VM→View 消息提示请求模式** ✅：`MessageRequestEventArgs`（Title/Message 纯数据，无回填）→ View 弹 MessageBox（后台线程经 BeginInvoke 封送）；与输入请求（回填 InputText）/确认请求（回填 Confirmed）构成三类弹框交互模式
- **导航模式（页面路由）** ✅：NavigationViewModel 持有 CurrentPage（PageType 枚举），MainForm 订阅 PropertyChanged → 页面懒创建 + 可见性切换；Tab 点击经参数化命令回传 PageType
- **工厂/策略模式** ⏳：预留（真实多协议通信接入时启用）
- **测试桩模式** ✅：StubLogService（内存记录日志供断言）/ 事件参数回填模拟 View 弹框（VM 测试零 UI 依赖）/ 临时目录 + IDisposable 每测试隔离（xlsx 测试不污染仓库）
- **配套测试模式** ✅：每次功能修改同步写/更新测试，用例名关联 ERR 编号（如 `ERR014_保存含末尾空行的表_重载后行数不变`），dotnet test 即自动回归全部历史修复

## 核心业务规则：抽屉三态判定

```
HasMaterial && HasRecipe   → Ready   (绿 #4CAF50)
!HasMaterial && !HasRecipe → Idle    (灰 #BDC3C7)
其余                        → Warning (黄 #FFC107)
```
实现位置：`DrawerItemViewModel.RefreshStatus()`；配方输入框双向绑定（DataSourceUpdateMode.OnPropertyChanged）即时联动状态灯。

## 核心业务规则：配方编号唯一性

- **编号列识别**：候选表头 `{ "配方编号", "编号" }`，Trim + 忽略大小写匹配（用户真实表头为「编号」，新建空白配方默认表头为「配方编号」）；识别不到时编号校验不启用并记 Warn 日志
- **三道防线**：① 单元格编辑提交（`TryCommitCellEdit` → `IsDuplicateRecipeId`，排除自身行，重复拒绝 + 弹窗 + 强制还原显示）；② 保存兜底（`ValidateRecipeIdUnique`，重复拒绝落盘，手动保存弹窗/自动保存静默日志）；③ 新增行自动编号（`GenerateUniqueRecipeId`，R001 起跳过已占用）
- **规范化口径**：编号值与表头一律 Trim 后比较（详 ERR-016/018）

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
| ClosedXML `RowsUsed()` 跳过空行致保存的空行蒸发 | 写端整行全空时首列写空格占位；读端 `LastRowUsed().RowNumber()` + for 循环逐行装载 | ERR-014 |
| 读外部文件建 DataTable 用「预置表头+重命名」遇重名列崩溃 | 按文件实际表头新建 DataTable 重建列结构（空表头「列N」兜底） | ERR-015 |
| 主项目 glob 误收 tests 目录测试代码（CS0246/CS0579） | 主 csproj 加 `Compile Remove="tests\**\*.cs"` + `<None Remove>` | 2026-09-03 测试搭建 |

## 关键实现路径（✅ 已完成 / ⏳ 待做）

1. ✅ `Common\Commands`：MVVM 基础设施（含参数化命令绑定）
2. ✅ 18 抽屉网格 + 三态指示灯 + 配方绑定 + Status 日志
3. ✅ 底部 Tab 页面导航（NavigationViewModel + 页面懒创建切换）
4. ✅ 配方单抽屉下发（RecipePageViewModel 共享主页面数据源，已被 0.9 配方 Table 化覆盖）
5. ✅ 配方文件服务多配方接口（IRecipeFileService 带路径重载 + CreateBlankAsync(recipeName, recipeId)，2026-09-02 构建修复同步补齐）
6. ✅ VM↔View 输入请求模式（ColumnNamingRequested 事件 + InputDialog 弹框，1.3 落地）
7. ✅ 删除行/列 + 确认弹框（DeletionConfirmRequested 事件 + ConfirmDialog + CellClick/CellFocused 双订阅焦点驱动动态参数，1.4 落地；CellFocused 单击不触发修正详 ERR-012）
8. ✅ 单元测试基础设施（tests/UiTopMachine.Tests：xUnit + .slnx + .gitignore，71 用例全绿；每次任务修改功能必须配套测试并全绿，2026-09-03 固化，详 techContext.md「测试工作流」）
9. ⏳ `Communications`：统一通信接口 ICommunication（PLC/串口/TCP）
10. ⏳ 打印/图像真实服务接入（IPrintService / VisionCameraService）
11. ⏳ DataAccess：数据库历史存储