# 记忆库历史归档 —— 2026-09 分卷

> **分卷范围**：2026-08-31 ~ 2026-09-08（v0.x ~ v1.25e）
> **归档时间**：2026-09-08（防膨胀专项，依据 `.clinerules/memory-bank.md` §3.3 体积红线）
> **内容来源**：activeContext.md（历史焦点/测试记录表/历史变更/决策表/API 技巧）＋ progress.md（已完成功能详情/决策演进表）＋ errorlog.md（🟢 已解决错误完整条目）
> **⚠️ 本文件不属于 P0 必读 7 文件**，仅考古（追溯历史决策/错误完整过程/实现细节）时按需检索；核心文件中的被裁剪内容已搬移至此，未删除任何信息。同一版本在「历史焦点」与「历史变更」中的重复描述已合并保留一份。

---

## 一、历史工作焦点（源自 activeContext.md）

### v1.27：图像页按钮绑定修复（ERR-031）✅ 2026-09-08

- v1.26 重构遗漏三个检测按钮 `CommandManagerHelper.Bind` 绑定，点击无响应永无图；补回绑定 + View 绑定守护用例（未加载方案时按钮 Enabled=false 断言缺绑定）。真机单次/连续检测出图实证。192/192 测试全绿。详 [errorlog.md](../errorlog.md) ERR-031。

### v1.25b：桥接 exe 路径回溯级数修复（ERR-026）✅ 2026-09-07

- **根因**：Program.cs 桥接 exe 相对路径从 `bin\Debug\net10.0-windows\` 回溯到仓库根只需要 **3 级 `..`**（net10.0-windows→Debug→bin→GitRepo），v1.24 引入时误写 4 级多退一级解析到 `D:\tools\...`（不存在）→ `File.Exists` 检查如实报「桥接进程不存在」（错误信息中打印的完整解析路径即为破案线索）
- **修复**：回溯级数 4→3 并注释推导链；主程序与桥接项目构建各 0 警告 0 错误；桥接 `--probe` 模式端到端实证 `PROBE_OK procedures=流程1`（VisionTesting.sol 加载成功 + 流程名匹配）

### v1.25：VM 方案路径切换 VisionTesting.sol ✅ 2026-09-07

- 用户需求：「将项目中vm方案替换成路径为D:\Printer\VisionTesting.sol方案」
- **路径统一收敛到 Program.cs（DI 单点维护）**：`solutionPath` 改为 `D:\Printer\VisionTesting.sol`（文件已确认存在）；流程名「流程1」不变（VisionTesting.sol 的 VmServer.xml 与 Test.sol 容器一致，probe 实测见 v1.24）
- **VM 层路径感知清除（顺带修正 v1.20 遗留隐患）**：`ImagePageViewModel` 删除硬编码 `@"D:\test\DetectionProcess.sol"`，改调无参 `LoadSolutionAsync()`——方案路径由服务层 DI 配置统一提供；接口签名升级 `LoadSolutionAsync(string? solutionPath = null)`，null = 用服务自身配置路径；Mock/桥接实现/测试桩三处同步
- **验证**：目标 .sol 存在（Test-Path True）；dotnet build **0 警告 0 错误**；dotnet test **180/180 PASS**

### v1.24：VM 方案加载换真实 .sol（VisionMaster 桥接进程）✅ 2026-09-07

- 用户需求：「vm方案加载换成实际的sol」——图像页从 Mock 模拟图切换为真实海康 VisionMaster 4.4.0 加载 `D:\OneDrive\桌面\Test.sol` 并运行检测。因 VM SDK 为 .NET Framework 程序集（GAC，net10.0 无法直引），采用**桥接进程方案**（用户确认方案 A）：
- **新增 `tools/VmVisionBridge/`（net48 x64 桥接进程）**：承载 VmSolution SDK——服务模式监听命名管道 `UiTopMachine.VmBridge`，按二进制帧协议（命令1B+长度4B+payload）处理 Ping/Load/Run/Close/ListProcedures；检测运行 `VmSolution.Load(path,pwd,false)` → `Instance["流程1"] as VmProcedure` → `Run()` → `ModuResult.GetOutputImageV2("ImageData")` → `ImageBaseData.ToBitmap()` → PNG 回帧；`--probe "sol路径"` 探测模式枚举流程名
- **新增 `Common/VmBridge/VmBridgeProtocol.cs`（共享源码）**：主程序与桥接项目 link 同一份文件编译，帧编解码 + 响应构建/解析（头部 key=value 行 + `\n\n` 分隔 + PNG 二进制，中文 Base64 编码），杜绝两侧漂移
- **新增 `Services/VisionMasterBridgeInspectionService.cs`**：实现 `IImageInspectionService`——懒启动桥接进程 + `NamedPipeClientStream` 收发（带 20s 启动/120s 加载/30s 运行超时）；进程异常退出下次调用自动重启；全部失败走 `Result.Fail` 不抛 UI 异常；SDK 对象全封装在桥接进程内
- **DI 切换**：Program.cs 注册 `VisionMasterBridgeInspectionService`（方案路径 + 流程名 probe 实测枚举结果）；Mock `ImageInspectionService` 保留可随时切回；csproj 排除 tools 目录；slnx 挂载桥接项目
- **端到端联调实证**：桥接进程 + 管道客户端全链路——Ping OK → Load Test.sol OK → List 返回「流程1」→ **Run 返回 986×645 PNG 结果图（isok=1）** → Close OK
- **测试**：新增 `VmBridgeProtocolTests` 15 用例；测试暴露并修复协议真 Bug（BuildResponse 头部缺 `\n\n` 结束标记致 PNG 解析丢失，详 ERR-025）；dotnet test **180/180 PASS**

### v1.23b：窗口按钮布局抽取 + 测试守护 ✅ 2026-09-07

- **新增 `Views/WindowButtonLayout.cs`**：布局常量（ButtonWidth=56/ButtonHeight=42/RightMargin=16/Spacing=8/TopBarHeight=76）+ 纯函数 `GetCloseLocation/GetMaximizeLocation/GetMinimizeLocation(containerWidth)`——纯函数无 UI 依赖，可直接单测
- **MainForm 改造**：按钮尺寸改用常量；彻底禁用 Anchor（ERR-024 教训）；`_topBar.Resize → LayoutWindowButtons()` 实时重算三按钮位置，初始手动调用一次
- **测试守护**：新增 `WindowButtonLayoutTests` 10 用例（常量自洽性 1 + 任意宽度右对齐 Theory 6 含 ERR-024 元凶宽度 200/最大化宽度 1870/2K 屏 2560 + 从右向左排列间距一致 Theory 3）；dotnet test **165/165 PASS**

### v1.23：右上角窗口控制按钮图标化 ✅ 2026-09-04

- 用户需求：「在窗口的右上角增加窗口缩小，全屏，以及退出图标」（此前按钮功能在但视觉不可见）
- **图标化**：三按钮符号从 Marlett 10pt 改为 YaHei UI 13f Bold Unicode 几何符号——`—` 最小化 / `□` 最大化全屏（Maximized 时 `❐` 还原）/ `✕` 关闭退出（hover 红底白字）；尺寸 48×34
- **根因修复（ERR-024）**：按钮 UIA 可见可点但屏幕上看不到——`Anchor=Top|Right` 在控件未加入容器时设置，冻结负右缘距离把按钮排到窗口外 1156px（x=3026）；去掉 Anchor 改 `_topBar.Resize → LayoutWindowButtons()` 重算位置
- **验证**：UIA bounds 实证三按钮紧贴窗口右上角；点击最小化按钮窗口真实最小化；dotnet test **155/155 PASS**

### v1.22：品牌化改造 Logo/标题/程序图标 ✅ 2026-09-04

- 用户需求：① 顶栏公司名文本替换为 Resources/tittle.png ② 程序名称「进料抽屉监控系统」改为「上海寅铠」 ③ Resources/Ic.ico 设为程序图标
- **发现并处理**：`Ic.ico` 实为 PNG（文件头魔数证实）→ 转换生成真 ICO `Resources/App.ico`（64×64，原 Ic.ico 保留）
- **csproj**：`ApplicationIcon=Resources\App.ico`；`Resources\tittle.png` CopyToOutputDirectory；Description 同步「上海寅铠」
- **MainForm**：`Text="上海寅铠"`；`Icon=Icon.ExtractAssociatedIcon(exe)`；顶栏 `_companyLabel` → `_companyLogo` PictureBox（284×48 Zoom，加载失败静默留白）；移除 CompanyTitle 绑定；MainViewModel 删除已无引用的 `CompanyTitle` 属性
- **验证**：dotnet test **155/155 PASS**；重启截图实证（窗口标题/任务栏图标/顶栏 logo 全部生效）；AssemblyName 未改（exe 文件名仍 UiTopMachine.exe）

### v1.21：面板重定义为设备对接与运行错误状态 ✅ 2026-09-04

- 面板定位四类信息：① PLC 连接成功（绿）/失败含原因（红）② 心跳丢失/检测连续失败/物料读取失败（红）③ **视觉方案加载成功（绿）/失败含原因（红）**（新增）④ **程序运行时错误**——全局异常处理（ThreadException/UnhandledException）在弹窗同时发布面板 Error 条目（新增）；检测完成的 Info 与「连接中」过程信息仍只落文件防刷屏
- **实现**：新增 `IPanelStatusPublisher.PublishPanelEntry(level, message)` 接口（MainViewModel 实现，内部 _uiContext.Post + InsertPanelEntry，任意线程可调）；ImagePageViewModel 注入发布者；Program.cs 提前解析 MainViewModel 并在全局异常处理器中发布
- **测试**：StubPanelPublisher 桩；新增 3 用例；dotnet test **155/155 PASS**

### v1.20：仿参考程序编写图像页 ✅ 2026-09-04

- 仿 Form.txt 视觉流程：方案加载 → 加载成功回调 → 采集运行 → 结果图渲染 → 连续轮询 → 退出停止
- `IImageInspectionService`（IsSolutionLoaded/SolutionLoaded 事件/LoadSolutionAsync/RunInspectionAsync/Shutdown）+ Mock 实现（GDI+ 生成 640×480 模拟检测图、检测框+十字线+OK绿/NG红+随机缺陷圈、~20% NG、1s 间隔）
- `ImagePageViewModel` 重写（自动加载/单次检测/连续启停/OK·NG 计数/CurrentImage 替换释放旧图，_uiContext 调度）；`ImagePage` 重写（方案状态+PictureBox 结果区+OK/NG 角标+统计+控制按钮；Load → InitializeAsync 自动加载；Disposed → Shutdown）
- **AsyncRelayCommand 增补 ExecuteAsync**（可等待版，异常上抛不弹窗，供测试/编程调用）
- 测试：ImageInspectionServiceTests 5 + ImagePageViewModelTests 5；dotnet test **152/152 PASS**

### v1.19：抽屉配方分组数据层 ✅ 2026-09-04

- 用户需求：「对已有配方的抽屉进行分组，分组依据为配方类型；同组内编号按填入先后顺序；编号不重复；同抽屉多次写入只保留最后一次配方」。确认决策：**仅数据层**（不显示）、发送按钮暂不改
- **Models/RecipeGroupModel**：`RecipeName`（Trim 后配方值）+ `DrawerIndexes`（填入顺序，编号不重复）
- **MainViewModel**：`_recipeSequences`（编号→次序，重写即刷新）+ `RecipeGroups` 派生属性——有配方（Trim 非空）抽屉 GroupBy 配方值；组内次序升序=填入顺序；组间组内最小次序=形成顺序；空白=无配方移出
- **重复写入相同配方**：INPC 值未变不触发通知 → 新增 `RefreshRecipeSequence(编号)` 公共方法，FeedDrawersPage 输入框 **Leave 事件**调用（与参考 textBox_Leave→AddToList 对应）
- **测试**：新增 RecipeGroupingTests 8 用例；dotnet test **142/142 PASS**

### v1.18：抽屉输入框编辑权限联动 + 启动默认灰 ✅ 2026-09-04

- **启动默认灰**：抽屉配方无持久化（Mock 内存 + VM 内存），v1.17 默认无料无配方 → 启动即全灰；加测试锁定
- **编辑权限联动（仿参考 ReadOnly = !hasMaterial）**：`DrawerItemViewModel.IsInputReadOnly => !HasMaterial`（有料黄/绿可编辑、无料灰只读），HasMaterial setter 通知该属性；`FeedDrawersPage` 输入框加 `TextBox.ReadOnly` 单向绑定 → PLC 物料推送实时切换编辑权限
- **测试**：新增 4 用例；dotnet test **134/134 PASS**；运行截图+无障碍树实证（黄=可编辑、灰=只读）

### v1.17：托盘默认状态改为无料无配方 ✅ 2026-09-04

- **MockDrawerService 构造**：`HasMaterial = false`（原 60% 随机有料）+ 配方空 → 18 托盘启动即灰色空闲态；PLC 连接后由物料轮询首读推送真实状态覆盖（PLC 为唯一真值源）
- **顺手清理**：`StartMonitoring` 随机演示逻辑清空为空操作；删除 `_random/_timer/_recipeNames` 死代码；`DrawerChanged` 事件加 pragma 抑制 CS0067
- **测试**：新增 MockDrawerServiceTests 2 用例；dotnet test **130/130 PASS**；重启程序截图验证托盘默认灰、模拟器真实有料位正常联动黄色

### v1.16：UI 状态不更新修复 + 退出进程残留（ERR-023）✅ 2026-09-04

- 用户反馈「plc 还是显示未连接」，深挖发现两个线程调度 bug（程序实际已连接，日志正常但 UI 假死）：
- **根因一（UI 永远未连接）**：PLC 状态/物料事件来自后台线程，事件处理现取 `SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext()`——后台线程 Current 为 null，新建上下文无消息泵，**Post 回调永不执行**（状态行/面板条目/抽屉物料推送全部静默丢失；日志同步写文件正常，形成「日志正常 UI 不动」假象）
- **根因二（退出进程残留）**：`OnFormClosing` 在 UI 线程直接 `Wait` 异步停止任务 → 延续需回被阻塞的 UI 线程 → 死锁，窗体关了进程残留
- **修复**：① MainViewModel **构造时捕获** UI 上下文存 `_uiContext` 字段，4 处后台事件处理改用；② OnFormClosing 改 `Task.Run(() => ShutdownAsync()).Wait(3s)`；③ 守护测试 `后台线程触发PLC事件_状态行仍更新`
- **验证**：dotnet test **128/128 PASS**；重启程序截图实证状态行绿色已连接、消息流恢复、抽屉物料联动正常

### v1.15：仿参考程序重构 PLC 连接/心跳 ✅ 2026-09-04

- 用户需求：「仿照文件中plc连接，重连，开启心跳，心跳检测，取消心跳的方式，重构plc连接」（参考 D:\OneDrive\桌面\Form.txt 旧程序）
- **心跳与物料合一（核心变化）**：删除独立写心跳（D100 递增）/读心跳（D101）与独立物料轮询三个循环 → **一个心跳循环**周期读 M1000×19：读成功即通讯正常（失败计数清零）+ 推送物料变化（首读即推送）；读失败计连续次数
- **连续失败阈值重连（仿参考 _maxRetryCount）**：连续 `maxRetryCount=3` 次读失败 → 判「心跳丢失」→ 断开 → 10 秒后重连；重连后心跳新循环，计数/基线天然重置
- **连接参数仿参考**：连接超时 10s / 收发 5s；连接失败重试延时默认 10s
- **保留已实证约定（不搬参考配置）**：地址 M1000×19 + H5U 系列 + 偏移默认（搬参考 MX9002+AM+AddressStartWithZero=false+CDAB 会使实际访问地址错位）
- **测试重构**：删除 3 个旧机制用例；新增 2 用例；dotnet test **127/127 PASS**；运行验证连接稳定、物料联动正常

### v1.14：取消双向心跳（单向可配）✅ 2026-09-04

- 用户需求：「取消双向连接」；背景：本地模拟器不动 D101 致读监测反复判丢失断连
- **`PlcCommunicationService` 加 `monitorPlcAlive` 开关（默认 false = 单向心跳）**：只周期写 D100 递增（证明 PC 在线），不读 D101 不判丢失；`true` 恢复双向监测；Program.cs 显式 `monitorPlcAlive: false`
- **运行验证**：连接保持 2 分钟+ 零重连（对比 v1.13 每 10 秒一轮）；**物料轮询首读推送实证生效**——模拟器 M1000 区 12 个位为 true，对应抽屉联动黄色
- dotnet test **129/129 PASS**

### v1.13：面板常驻 PLC 状态行 ✅ 2026-09-04

- 用户需求：「在listbox 展示plc的连接状态」——LogPanelControl 顶部自绘状态行（颜色圆点 + 粗体文字：绿=已连接 {Target} / 橙=连接中 / 红=心跳丢失与未连接）；消息流保持 v1.11 语义
- **实现链**：`IPlcCommunicationService` 加 `Target` 描述 → MainViewModel 加 `PlcStatusText`/`PlcStatusLevel`（事件内 SynchronizationContext.Post 刷新）→ MainForm 订阅 PropertyChanged 转发 → `LogPanelControl.UpdatePlcStatus`（UI 线程直接 Invalidate）
- **测试**：新增状态行流转用例；dotnet test **128/128 PASS**；运行截图验证渲染

### v1.12b：PLC 地址格式修复 + 连接实证（ERR-022）✅ 2026-09-04

- 运行程序检查连接状态发现无限循环——TCP 连上 → 0.5 秒心跳丢失 → 断开 → 5 秒重连；日志报「写入寄存器 100 失败：地址解析失败」
- **根因（三层地址假设全错）**：① InovanceTcpNet 要求汇川软元件格式（位 "M1000"/字 "D100"），纯数字解析失败——v1.10 心跳地址 "100"/"101" 从未真正可用；② 默认构造（AM 系列）不支持 D 字地址，必须显式 `InovanceSeries.H5U`；③ v1.12 的 ResolveBitAddress 剥 M 前缀方向相反
- **修复**：删 ResolveBitAddress 地址原样透传；心跳默认地址 "D100"/"D101"；transport 显式 `InovanceSeries.H5U`；`TranslateToModbusAddress` 离线实证固化为 HslModbusAddressTests 6 守护用例
- **全链路实证（本机模拟器 127.0.0.1:502）**：连接 ✓、写 D101=567 回读 567 ✓、读 M1000×19 返回 19 位 ✓
- **实测确认的行为**：双向心跳第二向——D101 需 **PLC 侧程序周期改变**才证明存活，被动模拟器不动 D101 → 5 周期后按设计判 HeartbeatLost 断开重连
- dotnet test **127/127 PASS**

### v1.12：PLC 连续读取 M1000 物料数组驱动 18 抽屉 ✅ 2026-09-04

- 用户需求：「增加plc读取方法，从起始地址为M1000连续读取一个19位长度bool类型的数组，从数组元素位置1开始，每个位置分别与抽屉编号相对应，数组数值代表着抽屉是否有料，true为有料，false为无料状态，读取方式为连续一直读取」
- **传输层**：`IPlcTransport`/`HslModbusTransport` 加 `ReadBoolsAsync(address, length)` 批量位读
- **服务层**：`DrawerMaterialsChanged` 事件（仅抽屉位有变化时触发，首读即推送）+ 构造参数 `materialAddress="M1000"`/`materialLength=19`/`materialPollPeriodMs=1000`；连接成功后与心跳并列自动启动轮询（SemaphoreSlim 串行化）；读失败断开重连
- **顺带修复（v1.10 隐患）**：断开重连前 `StopCyclesAsync()` 显式取消并等待心跳/物料任务退出
- **UI**：MainViewModel 订阅物料事件 → Post 内批量构造 DrawerModel 复用 OnDrawerChanged（UpdateFromModel 只同步物料、配方保留用户输入）；**InitializeAsync 移除 `_drawerService.StartMonitoring()`**（Mock 随机翻转与 PLC 真值打架）
- **测试**：FakePlcTransport 加位读桩；新增 4 个服务用例 + 1 个 VM 用例；dotnet test **121/121 PASS**

### v1.11：Status 列表面板改为 PLC 专用 ✅ 2026-09-04

- 用户需求：「修改listbox 的作用，不再存入系统操作信息，只存入与plc对接时的错误显示，以及连接成功的提示信息」
- **MainViewModel**：取消订阅 `LogService.LogEmitted`——一般系统操作日志仅经 LogService 落文件，不再进入 `Logs` 集合
- **面板新数据源**：`OnPlcConnectionStateChanged` 直接驱动 `AddPlcPanelEntry`——Connected=成功绿条、HeartbeatLost/Disconnected=错误红条（最新置顶、上限 200 条）；Connecting「连接中…」过程信息只写文件不进面板
- **双通道留痕**：全部 PLC 状态仍经 `_logService` 写文件日志，面板只是过滤视图
- **测试**：新增 `MainViewModelPlcPanelTests` 6 用例（ImmediateSynchronizationContext 替代 WinForms 消息泵）；dotnet test **116/116 PASS**

### v1.10：PLC Modbus TCP 连接 + 双向心跳 ✅ 2026-09-04

- 用户需求：「创建plc连接，plc ip为192.168.1.88，端口502，站号1，实现心跳启动，心跳监听，关闭心跳」；确认决策：HslCommunication + InovanceTcpNet、双向心跳、后台自动连接、状态入 Status 列表
- **依赖**：HslCommunication 12.9.2；客户端类 **InovanceTcpNet**（命名空间 `HslCommunication.Profinet.Inovance`，ERR-021）
- **Communications/Plc/**：`IPlcTransport` 抽象（Connect/ReadShort/WriteShort/Close）+ `HslModbusTransport` 实现（超时各 3s；OperateResult 在此层转换，SDK 对象不外泄）
- **Services**：`IPlcCommunicationService` + `PlcCommunicationService`——后台自动连接循环（失败 5s 重试、断线自动重连、幂等启动）+ 双向心跳 + 手动启停 + 基础读写；SemaphoreSlim 串行化 IO；CancellationTokenSource 驱动循环
- **接线**：Program.cs DI 单例注册；MainViewModel 订阅 ConnectionStateChanged 按级别写日志；InitializeAsync 自动启动；MainForm.OnFormClosing → ShutdownAsync（3s 超时兜底）
- **测试**：新增 7 用例（FakePlcTransport 测试桩）；dotnet test **110/110 PASS**

### v1.9：打印页自定义打印内容 ✅ 2026-09-03

- 用户需求：「修改打印页面，增加输入框，打印内容用户输入的内容」
- **VM**：`CustomContent` 属性 + `PrintAsync` 内容来源分支——Trim 后非空走自定义（每张相同、流水号不动），留空走流水号原路径（递增+持久化）；自定义路径跳过流水号校验
- **View**：新增「打印内容（留空则打印流水号）」输入行；顺带修复三个说明标题未参与 `CenterLayout` 布局的缺陷
- **测试**：新增 3 用例 + ERR-020 修复（桩迁移至 Spooler 通道）；dotnet test **103/103 PASS**
- 另：v1.8b 打印通道已从 TCP 直连切换为 **Spooler RAW 为主**（TCP 192.168.1.200:9100 不可达持续超时，Spooler 实测打印成功）

### v1.8：ZPL 打印机集成 ✅ 2026-09-03

- **Service 层**：`IPrintService` + `ZplPrinterService`——整合用户 ZplPrinter 源码：TCP 直连（默认 192.168.1.200:9100）+ Windows Spooler RAW（打印机名 "zpl"）+ ZPL 生成 5 码型（二维码 ^BQ / Code39 ^B3 / Code128 ^BC / PDF417 ^B7 / 数字文本 ^AO）+ 流水号校验；全 async + Result 返回；修正源码 Code128 笔误（`"LL300"` → `"^LL300"`）
- **流水号自动递增**：打印成功 +1 持久化 `D:\Printer\Data\SerialNumber.txt`（6 位补零）；**批量中途失败流水号不前进**（防跳号）+ 持久化失败弹窗
- **测试**：新增 `ZplPrinterServiceTests` 21 用例；**流水号补零位数保留修复**；dotnet test **100/100 PASS**

### v1.7 / v1.7b：新建空白配方改造 + 备份轮转 ✅ 2026-09-03

- **接口变更**：`IRecipeFileService.CreateBlankAsync(recipeName, headers, blankRowCount=10)`——表头由调用方传入，不再写死默认 5 列；数据全空等待录入
- **语义修正（ERR-019 返工）**：v1.7「另存副本」→ v1.7b **备份轮转**：原配方 `File.Move` 改名（原名+时间戳，同秒递增 `_2/_3` 防覆盖）备份 → 新空白配方**沿用原文件名**（Recipe.xlsx）→ 页面立即显示；VM 先经通用 `ConfirmationRequested`（替换 DeletionConfirmRequested，删除行/列迁移共用）确认
- **行序整理 `CompactRows`**：加载/编辑/删除后自动将中间空行稳定移到末尾（数据连续、空白垫底），新表整体替换防半删态；**自动补空白行 `EnsureMinRows`**：依 View 可见高度 `(Height-40)/36` 补真实可编辑空白行；`RowHeight=36/RowHeightHeader=40`
- **测试**：改写 CreateBlank 7 用例 + VM 3 用例 + 行序整理/补行 4 用例；dotnet test **79/79 PASS**

### v1.6：编号查重真实表头生效 + 失败弹窗（ERR-018）✅ 2026-09-03

- 用户反馈「没有对编号列设置防重复」；用户真实配方表编号列表头为「**编号**」
- **编号列识别宽松化**：`RecipeIdColumnCandidates = { "配方编号", "编号" }` 候选列表 + Trim + 忽略大小写，新增 `FindRecipeIdColumnIndex()` 统一入口替换全部 6 处硬编码
- **失败弹窗反馈**：新增 `Common/MessageRequestEventArgs.cs` + VM `MessageRequested` 事件；编辑重复拒绝 → 弹「编号已存在，修改失败」+ 拒绝也 `TableVersion++` 强制重建；手动保存兜底失败弹窗（`SaveCoreAsync` 增加 `userInitiated` 参数）；View 订阅经 `BeginInvoke` 封送 UI 线程弹 MessageBox
- **Service 表头 Trim**：`LoadCoreAsync` 读表头 Trim 规范化
- **测试**：新增 6 用例；dotnet test **71/71 PASS**

### v1.5 系列：配方页修改功能测试 + 单元格错位修复（ERR-015/016/017）✅ 2026-09-03

- **v1.5 搭建单元测试**：tests/UiTopMachine.Tests（xUnit 2.9.3，net10.0-windows）+ UiTopMachine.slnx + .gitignore + 主 csproj 排除 tests glob；首批 55 用例；**测试暴露 ERR-015**（表头「预置+重命名」遇重名列 DuplicateNameException，自 0.8 潜伏）→ 改「按文件实际表头重建列结构」；**固化「每次任务修改功能必须配套测试并全绿」工作流**
- **v1.5b**：4 个空格规范化用例暴露 **ERR-016**（带空格编号绕过唯一性校验）→ VM 修改链路四处对齐 Trim 口径；59/59
- **v1.5c**：4 个位置正确性用例 + 运行时实证（bin/inspect/InspectRowIndex.cs）→ **ERR-017 首轮修复**（CellEndEdit 恒返回 false + VM 提交 + TableVersion++ 重建；当时误诊「事件索引 0 基」）；63/63
- **v1.5d**：2 个边界用例 + 二轮双实验对照 → **ERR-017 真根因修正**（三事件 RowIndex 均为含表头 1 基 INDEX，列 0 基；SelectedIndex 亦 1 基）→ 三处索引换算（行减 1 / 高亮 +1 / 删除链路同步）；65/65

### v1.4 系列：配方页删除行/列 + 修复 ✅ 2026-09-02

- **v1.4 删除行/列 + 确认弹框**：新增 `Common/ConfirmRequestEventArgs.cs` + `Views/Dialogs/ConfirmDialog.cs`（⚠ 警示 + Error 红确定）；VM `DeletionConfirmRequested` 事件 + DeleteRow/DeleteColumn 命令；`RecipePage` 工具栏 6→8 按钮 + CellClick/CellFocused 双事件焦点跟踪（ERR-012 修正：CellFocused 鼠标单击不触发）
- **v1.4a 按钮样式**：新增行/列按钮改 `TTypeMini.Success` 绿色（消除「灰色=禁用」误解）；删除后焦点钳制 `Math.Min(旧焦点, 行/列数-1)` 连续删除无需重新点选
- **v1.4b 修复新增行按钮永久禁用（ERR-013）**：属性 setter 逐个列举刷新命令天然易漏 → VM 提取 `RefreshAllCommandStates()` 统一刷新全部 8 个命令
- **v1.4c 修复新增行刷新后消失（ERR-014 空行蒸发）**：ClosedXML 空字符串单元格不落盘 + `RowsUsed()` 跳过空行 → 写端整行全空时首列写空格 `" "` 占位 + 读端 `LastRowUsed().RowNumber()` + for 循环逐行装载；临时控制台往返验证 8 PASS

### v1.3：新增列弹框交互 ✅ 2026-09-02

- 用户需求：新增列按钮弹出输入弹框，列名为空则新增失败
- **新增 `Views/Dialogs/InputDialog.cs`**（纯 View，回车=确定/Esc=取消）+ **`Common/InputRequestEventArgs.cs`**（VM↔View 输入请求事件参数）
- **改造 `RecipePageViewModel.AddColumn()`**：触发 `ColumnNamingRequested` 事件向 View 请求列名；校验链——取消静默放弃 / 空列名失败记 Error / 重复列名拒绝 / 通过→追加列 + TableVersion++ + 自动保存

### v1.2：构建失败修复（CS0535 接口实现缺失，ERR-005）✅ 2026-09-02

- `RecipeFileService` 未实现升级后接口的 `LoadAsync(string)`/`SaveAsync(DataTable, string)`/`CreateBlankAsync(string, string)`
- **修复**：重写 Service——提取私有核心 `LoadCoreAsync/SaveCoreAsync`；带路径重载成功后切换 `FilePath`（private set）；VM 全局搜索旧方法名同步修正

### v1.0/0.9/0.8：配方页编辑功能全套 + AntdUI Table 化 ✅ 2026-09-02

- **v1.0**：单元格修改（EditMode=DoubleClick + EditLostFocus + CellEndEdit 转发）/编号唯一三道校验/新增行列/新建空白配方；并发保存修复（SemaphoreSlim `_saveLock` 串行化，ERR-007）；运行时验证 16/16 PASS
- **0.9 AntdUI Table 化**：重写 VM（RecipeTable DataTable + LoadCommand 等）与 View；AntdUI 2.4.7 Table API 反射实证（`Binding<T>(AntList<T>)`，动态列用 `AntList<AntItem[]>`，DataTable 直接绑定不可用，详 ERR-010）
- **0.8 Excel 表格化**（被 0.9 覆盖）：引入 ClosedXML 0.105.1；新增 IRecipeFileService/RecipeFileService

### 早期版本（2026-08-31 ~ 09-01）

- **抽屉单元格布局重构**：指示灯直径自适应（移除 64px 最小直径钳制）；单元格双行 TLP（指示灯行 + 46px 输入框行）；Mock 初始配方改空
- **0.5 指示灯样式二次调整**：空闲色恢复 LightGray；编号位于圆圈左上角轻压圆边；字号随直径缩放钳制 14~34px
- **v1.x 基础架构（2026-09-01）**：底部 Tab 页面导航（PageType 枚举 + TabItemControl 自绘 + NavigationViewModel + MainForm 导航壳）；页面拆分四页；配方单抽屉下发；CommandManagerHelper 参数化绑定重载（关键修复：绑定存原参数刷新，否则 NavigateCommand 被 CanExecute(null) 误判禁用全部 Tab，详 ERR-004）
- **项目基础（2026-08-31）**：csproj（net10.0-windows + WinForms + 高 DPI）；MVVM 基础设施（ObservableObject/RelayCommand/AsyncRelayCommand/CommandManagerHelper）；三态抽屉判定；自绘控件三件套（DrawerIndicatorControl/FlatButton/LogPanelControl）；DI 组装 + 全局异常捕获 + 日志落盘；Memory Bank 初始化

---

## 二、测试记录表（源自 activeContext.md）

| 日期 | 任务 | 测试内容 | 结果 |
|------|------|---------|------|
| 2026-09-08 | 图像页按钮绑定修复（v1.27，ERR-031） | 补回三个按钮 Bind + View 绑定守护用例（Enabled=false 断言） | ✅ 192/192 PASS |
| 2026-09-03 | 搭建单元测试基础设施（v1.5） | 首批 55 用例（RelayCommand/AsyncRelayCommand、三态判定、xlsx 往返 ERR-014 回归、删除行列/列校验/编号唯一/ERR-013 回归）；暴露并修复 ERR-015 | ✅ 55/55 PASS（trx 留档） |
| 2026-09-03 | 配方页修改功能测试与修复（v1.5b） | 新增 4 个空格规范化用例；暴露并修复 ERR-016 | ✅ 59/59 PASS（trx 留档） |
| 2026-09-03 | 单元格错位写入修复（v1.5c） | 新增 4 个位置正确性用例；修复 ERR-017 首轮 | ✅ 63/63 PASS（trx 留档） |
| 2026-09-03 | 单元格错位二次修复（v1.5d） | 新增 2 个边界用例；二轮实证确证 1 基真根因 | ✅ 65/65 PASS（trx 留档） |
| 2026-09-03 | 编号查重生效 + 失败弹窗（v1.6） | 新增 6 用例；修复 ERR-018 | ✅ 71/71 PASS（trx 留档） |
| 2026-09-03 | 新建空白配方改造（v1.7） | 新增 4 用例；CreateBlankAsync 接口变更同步 5 处调用 | ✅ 74/74 PASS（trx 留档） |
| 2026-09-03 | 备份轮转+行序整理+补空白行（v1.7b） | 改写 CreateBlank 7 用例 + VM 3 用例 + 行序整理/补行 4 用例 | ✅ 79/79 PASS（trx 留档） |
| 2026-09-03 | ZPL 打印机集成（v1.8） | 新增 ZplPrinterServiceTests 21 用例；流水号补零位数保留修复 | ✅ 100/100 PASS（trx 留档） |
| 2026-09-03 | 打印页自定义内容（v1.9） | 新增 3 用例；修复 ERR-020 + 1 个历史 xUnit2013 警告 | ✅ 103/103 PASS |
| 2026-09-04 | PLC 连接+双向心跳（v1.10） | 新增 PlcCommunicationServiceTests 7 用例（FakePlcTransport 桩） | ✅ 110/110 PASS |
| 2026-09-04 | Status 面板改 PLC 专用（v1.11） | 新增 MainViewModelPlcPanelTests 6 用例（ImmediateSynchronizationContext） | ✅ 116/116 PASS |
| 2026-09-04 | PLC 连续读取 M1000 物料数组（v1.12） | 新增 4 服务用例（位读桩）+ 1 VM 用例 | ✅ 121/121 PASS |
| 2026-09-04 | PLC 地址格式修复（v1.12b，ERR-022） | 新增 HslModbusAddressTests 6 守护用例；模拟器全链路实证 | ✅ 127/127 PASS |
| 2026-09-04 | 面板常驻 PLC 状态行（v1.13） | 新增 1 用例（状态行流转+级别断言）；运行截图验证 | ✅ 128/128 PASS |
| 2026-09-04 | 取消双向心跳（v1.14） | 停滞判丢失用例改显式 monitorPlcAlive:true + 新增单向用例 | ✅ 129/129 PASS |
| 2026-09-04 | 仿参考程序重构心跳（v1.15） | 删 3 旧用例；新增 2 用例；运行验证连接稳定 | ✅ 127/127 PASS |
| 2026-09-04 | UI 状态不更新修复（v1.16，ERR-023） | 新增守护用例「后台线程触发PLC事件_状态行仍更新」；截图实证 | ✅ 128/128 PASS |
| 2026-09-04 | 托盘默认无料无配方（v1.17） | 新增 MockDrawerServiceTests 2 用例；运行截图验证 | ✅ 130/130 PASS |
| 2026-09-04 | 输入框编辑权限联动（v1.18） | 新增 4 用例；运行截图+无障碍树实证 | ✅ 134/134 PASS |
| 2026-09-04 | 抽屉配方分组数据层（v1.19） | 新增 RecipeGroupingTests 8 用例；启动冒烟正常 | ✅ 142/142 PASS |
| 2026-09-04 | 图像页编写（v1.20） | 新增 ImageInspectionServiceTests 5 + ImagePageViewModelTests 5 用例 | ✅ 152/152 PASS |
| 2026-09-04 | 品牌 Logo/标题/图标（v1.22） | 无新增逻辑用例；转换真 ICO 实证；运行截图实证 | ✅ 155/155 PASS |
| 2026-09-07 | 窗口按钮布局抽取+测试守护（v1.23b） | 新增 WindowButtonLayoutTests 10 用例；布局抽取纯函数静态类 | ✅ 165/165 PASS |
| 2026-09-07 | VM 方案加载换真实 .sol（v1.24） | 新增 VmBridgeProtocolTests 15 用例；暴露修复 BuildResponse 头部缺 `\n\n` 真 Bug；端到端管道联调实证 | ✅ 180/180 PASS |
| 2026-09-07 | VM 方案路径切换 VisionTesting.sol（v1.25） | 无新增逻辑用例；接口签名升级后全量回归；目标 .sol 存在性实证 | ✅ 180/180 PASS |
| 2026-09-08 | 桥接带病复用自愈修复（v1.25c，ERR-027） | 新增 10 用例（加密狗识别 Theory/普通错误/null/指引文案）；probe 回归 + 真机端到端实证 | ✅ 190/190 PASS |
| 2026-09-08 | 连续检测无新帧跳过（v1.25d，ERR-028） | 新增 1 用例；桥接 DiagLog 诊断基础设施；真机实证 29 完成+18 跳过+0 失败 | ✅ 191/191 PASS |
| 2026-09-08 | 图像显示链路修复（v1.25e，ERR-029） | 无新增逻辑用例（View 轮询纯 UI 行为）；真机实证；`--grab` 落盘 PNG 实证 | ✅ 191/191 PASS |

---

## 三、用户已确认的需求决策（源自 activeContext.md）

| 决策点 | 结论 |
|--------|------|
| .NET 版本 | **.NET 10**（SDK 10.0.400）✅ 已落地 |
| 界面风格 | **浅色现代扁平风** ✅ 已落地 |
| 抽屉三态 | 有料+有配方=绿；无料+无配方=**LightGray**；其余=黄 ✅ 已落地（空闲色经两轮反馈最终定为浅灰） |
| 配方输入框 | 每个抽屉下方独立输入框，Text 默认空，双向绑定即时联动状态灯 ✅ 已落地 |
| 抽屉编号 | 位于圆圈左上角一点点（轻压圆边），大字号加粗 ✅ 已落地 |
| 退出按钮 | 右上角，红色危险语义 ✅ 已落地 |
| Tab 导航 | 底部四 Tab（打印/图像/进料抽屉/配方）✅ 已落地 |
| 配方数据源 | Excel 文件 D:\Printer\Data\Recipe.xlsx（ClosedXML 读写）✅ 已落地 |
| 工具栏按钮语义色 | 绿=新增（Success）/红=删除（Error）/橙=新建配方（Warn）/蓝=刷新保存（Primary）✅ 已落地 |
| PLC 连接参数 | **192.168.1.88:502 站号 1** ✅ 已落地（v1.10） |
| PLC 通讯库 | **HslCommunication，客户端类保留 InovanceTcpNet** ✅ 已落地（v1.10） |
| PLC 心跳机制 | 双向心跳（v1.10）→ v1.14 单向可配 → v1.15 仿参考重构（心跳物料合一）✅ |
| PLC 连接/状态 UI | **后台自动连接**（不加页面/输入框），状态入主窗体右侧 Status 列表面板 ✅ 已落地（v1.10） |
| Status 列表面板语义 | **只存 PLC 对接信息**（v1.11）→ v1.21 扩展为设备对接与运行错误四类信息 ✅ |
| VM 方案接入方式 | 桥接进程方案（用户确认方案 A，v1.24）；方案路径 D:\Printer\VisionTesting.sol（v1.25） |
| 新建配方文件流转 | **备份轮转**：原文件改名备份、新配方沿用原文件名（v1.7b，ERR-019 返工后确认） |
| 配方分组 | 仅数据层（不显示）、发送按钮暂不改（v1.19） |

---

## 四、API 知识与技巧（源自 activeContext.md，主要条目已沉淀至 systemPatterns/techContext）

- WinForms 无 WPF CommandManager，自建 `CommandManagerHelper` 维护命令↔控件绑定
- 自绘控件需标注 `[DesignerSerializationVisibility(Hidden)]` 避免设计器序列化警告
- ListBox 绑定 `ObservableCollection<T>` 用 BindingSource 包装；选中项变化时 TextBox 重绑前必须 `DataBindings.Clear()` 防串数据
- **DataTable 直接绑定 DataGridView**：单元格编辑自动回写；整表替换时先 EndEdit + DataSource=null 再赋新表，DataBindingComplete 事件里做样式定制
- ClosedXML 写 xlsx：`ws.Columns().AdjustToContents()` 自适应列宽；表头样式用 `XLColor.FromHtml`；目录不存在时 `Directory.CreateDirectory` 兜底
- **AntdUI 2.4.7 Table**：动态列用 `AntList<AntItem[]>`，行 = `AntItem(key, value)[]`；单元格编辑 `EditMode = TEditMode.DoubleClick` + `EditLostFocus = true`，`CellEndEdit` 委托返回 false 自动还原显示；AntItem 是 class（key/value 属性）
- **反射检查第三方 API 套路**：临时控制台项目 LoadFrom DLL 反射导出类型与方法签名（需 UseWindowsForms=true 解析 WinForms 依赖），或直接引用包写编译用例实证，用完即删
- **AntdUI 2.4.7 Table 焦点/点击 API（反射实证）**：`CellFocused` 事件鼠标单击**不触发**（偏向键盘焦点导航），跟踪鼠标选中必须订阅 `CellClick`（`TableClickEventArgs` 含 RowIndex/ColumnIndex/Button/Clicks，继承 MouseEventArgs）；两者签名一致可共用处理逻辑双订阅；`FocusedCell` 是嵌套类型 `Table+CELL`；`SelectedIndex`/`SelectedIndexs` 为行选中
- **VM↔View 弹框请求三模式**：输入请求（InputRequestEventArgs 回填 InputText + InputDialog）/ 确认请求（ConfirmRequestEventArgs 回填 Confirmed + ConfirmDialog）/ 消息提示（MessageRequestEventArgs 纯单向 + MessageBox）——VM 全程不接触 UI 控件

---

## 五、已完成功能详情（源自 progress.md，2026-09-08 起改为索引表）

> 2026-09-09 起索引表超 80 行红线，裁剪的子迭代行搬移至此：
> | 2026-09-03 | v1.5b | 配方页修改测试（空格规范化，Trim 口径对齐） | ERR-016 | 59/59 |

> 以下为索引表裁剪前的详情记录；每项的关键教训已沉淀 errorlog（ERR 编号）与 systemPatterns（模式）。

### 连续检测无新帧跳过（2026-09-08，v1.25d/ERR-028）

- **问题**：用户确认 sol 方案流程 1 存在输出图像后，连续检测仍约 1/3~1/2 运行报「流程无输出图」
- **诊断**：桥接 DiagLog 文件日志实证——失败运行输出键存在但值为空、ErrorCode=0、重试无效 → 方案内图像源帧率低于轮询频率，无新帧属常态，上层误报为错误
- **修复**：桥接无图+成功 → 成功无图响应；服务层 Image=null 的 OK 结果（新语义：无新帧）；VM 层静默跳过（保留上张图/不计数/Info 日志）+ `GetAllOutputNameInfo` 输出键枚举回退
- **诊断基础设施**：桥接 `DiagLog`（`log/VmBridge-diag.log`）——stderr 无重定向会静默丢失
- **验证**：dotnet test **191/191 PASS**；真机实证 29 完成 + 18 静默跳过 + 0 失败

### 桥接进程带病复用自愈修复（2026-09-08，v1.25c/ERR-027）

- **问题**：用户反馈「vm方案加载失败」——加密狗确认正常但加载永远报 `VmException 0xE0000700: IMVS_EC_ENCRYPT_DONGLE_OUTDATE:Dongle not detected!`
- **根因**：VM SDK 授权登录每进程仅一次，桥接进程内失败永久带病；服务层 Load 失败响应不清理进程 → 带病进程被无限复用（主程序重试 15 次全失败，全新进程 probe 一次成功）
- **修复**：服务层 `LoadCore` 收到失败响应即清理桥接进程 + `VmBridgeProtocol.IsDongleLicenseError` 识别与 `DongleLicenseHint` 用户指引
- **测试**：新增 10 个加密狗识别用例；暴露并修复共享源码 net48 兼容问题（`Contains(str,StringComparison)` → `IndexOf`）
- **验证**：dotnet test **190/190 PASS**；probe 回归；真机端到端实证

### VM 方案路径切换 VisionTesting.sol（2026-09-07，v1.25）

- **需求**：「将项目中vm方案替换成路径为D:\Printer\VisionTesting.sol方案」
- **路径收敛 DI 单点维护**：Program.cs `solutionPath` → `D:\Printer\VisionTesting.sol`；流程名「流程1」不变
- **VM 层路径感知清除**：`ImagePageViewModel` 删除硬编码路径，改调无参 `LoadSolutionAsync()`；接口签名升级 `LoadSolutionAsync(string? solutionPath = null)`，Mock/桥接实现/测试桩三处同步
- **验证**：dotnet test **180/180 PASS** + dotnet build **0 警告 0 错误**

### VM 方案加载换真实 .sol（2026-09-07，v1.24）

- **架构决策**：VM SDK 为 .NET Framework 程序集（GAC，net10.0 无法直引）→ 用户确认**桥接进程方案**——tools/VmVisionBridge（net48 x64）承载 SDK，主程序经命名管道按共享二进制帧协议收发
- **实现**：`tools/VmVisionBridge/` + `Common/VmBridge/VmBridgeProtocol.cs`（共享协议源码）+ `Services/VisionMasterBridgeInspectionService.cs`（懒启动/超时/崩溃自动重启/Result 全捕获）；csproj 排除 tools；slnx 挂载
- **端到端联调实证**：Ping OK → Load Test.sol OK → List「流程1」→ Run 返回 986×645 PNG（isok=1）→ Close OK
- **测试**：新增 `VmBridgeProtocolTests` 15 用例；暴露并修复 BuildResponse 头部缺 `\n\n` 结束标记的真 Bug（ERR-025）
- **验证**：dotnet test **180/180 PASS**（主项目与桥接项目均 0/0）

### 窗口按钮布局抽取 + 测试守护（2026-09-07，v1.23b）

- **实现**：新增 `Views/WindowButtonLayout.cs`（常量 + `GetXxxLocation(containerWidth)` 纯函数）；MainForm 尺寸改用常量、彻底禁用 Anchor（ERR-024 教训）、`_topBar.Resize → LayoutWindowButtons()` 实时重算
- **测试**：新增 `WindowButtonLayoutTests` 10 用例（Theory 覆盖 ERR-024 元凶宽度 200/最大化 1870/2K 屏 2560）
- **验证**：dotnet test **165/165 PASS**

### 面板重定义为设备对接与运行错误状态（2026-09-04，v1.21）

- **需求**：listbox 显示 ① PLC 连接成功/失败（含原因）② 心跳错误 ③ Vision 方案加载 ④ 程序运行时错误（全局异常）
- **实现**：新增 `IPanelStatusPublisher.PublishPanelEntry(level, message)`；ImagePageViewModel 注入发布者；Program.cs 全局异常处理器弹窗同时发布面板 Error 条目
- **测试**：StubPanelPublisher 桩 + 新增 3 用例；dotnet test **155/155 PASS**

### 图像页编写（2026-09-04，v1.20）

- **需求**：仿参考程序视觉流程：方案加载 → 加载成功回调 → 采集运行 → 结果图渲染 → 连续轮询 → 退出停止
- **服务抽象 + Mock**：`IImageInspectionService` + Mock 实现（GDI+ 模拟检测图）
- **ImagePageViewModel/ImagePage 重写**：自动加载/单次检测/连续启停/OK·NG 计数/结果图管理；`AsyncRelayCommand` 增补 `ExecuteAsync`
- **测试**：新增 10 用例；dotnet test **152/152 PASS**

### 抽屉配方分组数据层（2026-09-04，v1.19）

- **需求**：按配方类型分组/组内按填入顺序/编号不重复/同抽屉多次写入保留最后一次；确认仅数据层
- **实现**：`Models/RecipeGroupModel`；MainViewModel `_recipeSequences` + `RecipeGroups` 派生属性 + `RefreshRecipeSequence`（Leave 刷新）
- **测试**：新增 8 用例；dotnet test **142/142 PASS**

### 抽屉输入框编辑权限联动（2026-09-04，v1.18）

- **需求**：启动默认灰；有料可编辑/无料只读
- **实现**：`DrawerItemViewModel.IsInputReadOnly => !HasMaterial`；`FeedDrawersPage` 输入框 ReadOnly 单向绑定
- **测试**：新增 4 用例；dotnet test **134/134 PASS**；截图+无障碍树实证

### 托盘默认状态改为无料无配方（2026-09-04，v1.17）

- **实现**：`MockDrawerService` 构造 `HasMaterial = false`；清空 StartMonitoring 随机演示逻辑；删除死代码
- **测试**：新增 2 用例；dotnet test **130/130 PASS**

### 修复 UI 状态不更新 + 退出进程残留（2026-09-04，v1.16/ERR-023）

- **根因**：① 后台事件现取 SynchronizationContext——Post 回调永不执行；② OnFormClosing UI 线程 Wait 异步任务死锁
- **修复**：MainViewModel 构造时捕获 `_uiContext`（4 处统一改用）；OnFormClosing 改 `Task.Run(...).Wait(3s)`；守护测试
- **验证**：dotnet test **128/128 PASS**；截图实证

### 仿参考程序重构 PLC 连接/心跳（2026-09-04，v1.15）

- **心跳与物料合一**：三循环合并为一个心跳循环（周期读 M1000×19）；连续 3 次读失败判丢失 → 断开 → 10 秒重连
- **保留差异**：地址 M1000×19 + H5U + 默认偏移（不搬参考 MX9002+AM 配置避免地址错位）
- **测试重构**：删 3 旧用例 + 新增 2 用例；dotnet test **127/127 PASS**

### 取消双向心跳（2026-09-04，v1.14）

- **实现**：`monitorPlcAlive` 开关（默认 false 单向心跳）；Program.cs 显式 false
- **验证**：连接保持 2 分钟+ 零重连；物料首读推送抽屉联动；dotnet test **129/129 PASS**

### Status 面板常驻 PLC 状态行（2026-09-04，v1.13）

- **实现**：LogPanelControl 顶部自绘状态行 + `UpdatePlcStatus`；链路 Target → PlcStatusText/Level → MainForm 转发 → Invalidate
- **测试**：新增状态行流转用例；dotnet test **128/128 PASS**

### PLC 地址格式修复（2026-09-04，v1.12b/ERR-022）

- **根因**：InovanceTcpNet 软元件格式 + 显式 H5U 系列 + ResolveBitAddress 方向相反（三层全错）
- **修复**：地址原样透传；心跳地址 "D100"/"D101"；显式 `InovanceSeries.H5U`；`TranslateToModbusAddress` 固化 6 守护用例；模拟器全链路实证
- **验证**：dotnet test **127/127 PASS**

### PLC 连续读取 M1000 物料数组（2026-09-04，v1.12）

- **实现**：`ReadBoolsAsync` 批量位读；`DrawerMaterialsChanged` 事件（仅变化触发/首读即推送）；物料轮询循环；StopCyclesAsync 防误断开；VM 批量更新 18 抽屉
- **测试**：FakePlcTransport 位读桩 + 5 用例；dotnet test **121/121 PASS**

### Status 列表面板改为 PLC 专用（2026-09-04，v1.11）

- **实现**：MainViewModel 取消订阅 LogService.LogEmitted；面板仅由 PlcConnectionStateChanged 驱动；双通道留痕（面板=过滤视图，文件=完整留痕）
- **测试**：新增 MainViewModelPlcPanelTests 6 用例；dotnet test **116/116 PASS**

### PLC Modbus TCP 连接 + 双向心跳（2026-09-04，v1.10）

- **依赖接入**：HslCommunication 12.9.2 + InovanceTcpNet；**通信抽象层**：IPlcTransport + HslModbusTransport；**Service 层**：自动连接循环 + 双向心跳 + 手动启停 + 基础读写；**UI 接线**：MainViewModel.InitializeAsync 自动启动 + OnFormClosing → ShutdownAsync
- **测试**：PlcCommunicationServiceTests 7 用例（FakePlcTransport 桩）；dotnet test **110/110 PASS**

### 打印页自定义打印内容 + 通道切换 Spooler（2026-09-03，v1.8b/v1.9）

- **通道切换（v1.8b）**：`PrintByIpAsync` → `PrintBySpoolerAsync`（Spooler RAW 主通道，TCP 备用）
- **自定义内容（v1.9）**：`CustomContent` 双路径——非空每张打印该内容（流水号不动），留空走流水号递增；ERR-020 桩迁移修复
- **测试**：新增 3 用例；dotnet test **103/103 PASS**

### ZPL 打印机集成（2026-09-03，v1.8）

- **Service 层**：`ZplPrinterService`——TCP 直连 + Spooler RAW + 5 码型生成 + 流水号校验；**流水号自动递增**持久化（补零、批量失败不前进防跳号）
- **测试**：新增 21 用例；**流水号补零位数保留修复**；dotnet test **100/100 PASS**

### 备份轮转 + 行序整理 + 自动补空白行（2026-09-03，v1.7b/ERR-019）

- **语义修正**：「另存副本」返工 → **备份轮转**（原文件改名+时间戳备份、新配方沿用原名、ConfirmationRequested 确认）
- **行序整理 `CompactRows`**（数据连续空白垫底）+ **自动补空白行 `EnsureMinRows`**（依可见高度补真实可编辑行）+ RowHeight=36/40
- **测试**：改写 CreateBlank 7 用例 + VM 3 用例 + 行序/补行 4 用例；dotnet test **79/79 PASS**

### 新建空白配方改造（2026-09-03，v1.7）

- **接口变更**：`CreateBlankAsync(recipeName, headers, blankRowCount=10)` 表头参数化；VM 内存构造立即显示
- **测试**：新增 4 用例；dotnet test **74/74 PASS**

### 编号查重真实表头生效 + 失败弹窗（2026-09-03，v1.6/ERR-018）

- **编号列识别宽松化**：候选表头 + Trim + 忽略大小写 + `FindRecipeIdColumnIndex()` 统一入口；**失败弹窗反馈**：MessageRequested 事件 + 拒绝也 TableVersion++ 强制还原；Service 表头 Trim
- **测试**：新增 6 用例；dotnet test **71/71 PASS**

### 单元格错位二次修复（2026-09-03，v1.5d/ERR-017 二轮）

- **二次实证**：三事件 RowIndex 均为含表头 1 基内部 INDEX、ColumnIndex 0 基、SelectedIndex 1 基 → 三处索引换算
- **测试**：新增 2 边界用例；dotnet test **65/65 PASS**

### 单元格错位写入修复（2026-09-03，v1.5c/ERR-017 首轮）

- **运行时实证**（bin/inspect/InspectRowIndex.cs）：CellEndEdit 返回 true 时 AntdUI 内部落值错位；恒返回 false + VM 提交 + TableVersion++ 重建
- **测试**：新增 4 位置正确性用例；dotnet test **63/63 PASS**

### 配方页修改功能测试与修复（2026-09-03，v1.5b/ERR-016）

- **测试暴露产品 Bug**：带空格编号绕过唯一性校验（读写端 Trim 口径不一致）→ VM 四处对齐 Trim
- **验证**：dotnet test **59/59 PASS**

### 单元测试基础设施搭建（2026-09-03，v1.5）

- tests/UiTopMachine.Tests（xUnit）+ UiTopMachine.slnx + .gitignore + 主 csproj 排除 tests glob；首批 55 用例；**测试暴露 ERR-015**（表头重命名 DuplicateNameException）→「按文件实际表头重建列结构」；**固化「每次任务修改功能必须配套测试并全绿」工作流**

### 新增行空行蒸发修复（2026-09-02，v1.4c/ERR-014）

- **根因**：ClosedXML 空字符串单元格不落盘 + RowsUsed() 跳过空行 → 空行往返后蒸发
- **修复**：写端整行全空时首列写空格占位 + 读端 LastRowUsed 行号循环逐行装载；临时控制台往返验证 8 PASS

### 删除行/列 + 确认弹框（2026-09-02，v1.4/v1.4a/v1.4b）

- **v1.4**：ConfirmRequestEventArgs + ConfirmDialog + DeletionConfirmRequested 事件 + 删除命令（CanExecute 校验索引）+ CellClick/CellFocused 双订阅
- **v1.4a**：按钮 Success 绿色语义 + 焦点钳制连续删除；**v1.4b（ERR-013）**：RefreshAllCommandStates() 全量刷新修复按钮永久禁用

### 构建修复与多配方接口同步（2026-09-02，v1.2/v1.3）

- **v1.2（ERR-005）**：CS0535 修复——重写 RecipeFileService（LoadCoreAsync/SaveCoreAsync 核心 + CreateBlankAsync）；VM 调用方同步
- **v1.3**：新增列弹框交互（InputDialog + InputRequestEventArgs + ColumnNamingRequested 事件 + 空列名校验）

### 页面导航与配方管理（2026-09-01）

- 底部 Tab 导航（PageType + TabItemControl + NavigationViewModel + MainForm 导航壳）；页面拆分四页；配方单抽屉下发（MainViewModel 单例共享数据源）；CommandManagerHelper 参数化绑定重载（存原参数刷新，修 ERR-004 误禁用）

### 页面开发：进料抽屉监控（2026-08-31）

- csproj + NuGet（DI 容器）；MVVM 基础设施四件套；三态抽屉判定；配方双向绑定；自绘控件三件套（DrawerIndicatorControl/FlatButton/LogPanelControl）；MainForm 浅色布局（顶栏/18 抽屉网格/Status 日志/底部 Tab）；Mock 服务；DI 组装 + 全局异常捕获 + 日志落盘

### 项目基础结构（2026-08-31）

- MVVM 分层目录结构 + .gitkeep 占位；Memory Bank 初始化（6 核心文档）

---

## 六、项目决策演进记录（源自 progress.md）

| 日期 | 决策 | 原因 |
|------|------|------|
| 2026-08-31 | 采用 MVVM 模式组织 WinForms 项目 | 用户要求，界面与逻辑解耦 |
| 2026-08-31 | 通信层独立成 Communications 目录 | 工业上位机核心是通信，按设备类型扩展 |
| 2026-08-31 | .NET 10（SDK 10.0.400） | 用户指定版本 |
| 2026-08-31 | 浅色现代扁平风 | 用户选择（否决深色工业风） |
| 2026-08-31 | 抽屉三态：绿=有料有配方/灰=无料无配方/黄=其余 | 用户定义业务规则 |
| 2026-08-31 | 退出按钮置于左上角（红色）（后 v1.23 迁右上角图标化） | 用户要求（原版在右上） |
| 2026-08-31 | DI 用 Microsoft.Extensions.DependencyInjection | 规范的依赖注入，替代单例滥用 |
| 2026-09-01 | 导航状态放 NavigationViewModel（VM 驱动页面切换） | MVVM 解耦，View 只订阅 PropertyChanged 切可见性 |
| 2026-09-01 | MainViewModel 注册单例（跨页面共享抽屉集合） | RecipePage 与 FeedDrawersPage 数据源一致 |
| 2026-09-01 | 页面懒创建 + 缓存 | 降低启动开销，保持页面状态 |
| 2026-09-02 | IRecipeFileService 多配方接口（带路径重载 + CreateBlankAsync 带参） | 支持多配方文件管理；Service 内部切换 FilePath |
| 2026-09-02 | 新建配方文件名 = 安全化配方名 + 时间戳 | 文件名可读且防覆盖（v1.7b 改为备份轮转沿用原名） |
| 2026-09-02 | 新增列改为 VM 事件请求 → View 弹 InputDialog | 严格 MVVM：VM 不接触 UI 控件 |
| 2026-09-02 | 删除行/列经 ConfirmDialog 二次确认 | 危险操作防误触 |
| 2026-09-02 | 删除命令用单元格焦点索引 + 动态参数提供器 | 无选中时按钮自动禁用 |
| 2026-09-02 | 焦点跟踪改 CellClick + CellFocused 双订阅 | CellFocused 鼠标单击不触发（ERR-012） |
| 2026-09-02 | 新增按钮 Success 绿色 + 删除后焦点钳制 | 灰白样式被误读为禁用；连续删除体验 |
| 2026-09-02 | VM 命令刷新改统一全量刷新 RefreshAllCommandStates() | 逐个列举天然易漏（ERR-013） |
| 2026-09-02 | 建立 errorlog.md 作为错误唯一事实来源 | 集中管理错误条目/防回归清单 |
| 2026-09-02 | Excel 空行持久化：写端空格占位 + 读端行号循环 | ClosedXML 空行语义（ERR-014） |
| 2026-09-03 | 搭建 xUnit 单元测试 + 固化测试工作流 | 历史修复永久回归守护；首轮即暴露 ERR-015 验证价值 |
| 2026-09-03 | LoadCoreAsync 表头改「按文件实际列重建」 | 重名列 DuplicateNameException（ERR-015） |
| 2026-09-03 | VM 修改链路四处对齐 Trim 口径 | 读写端口径不一致漏洞（ERR-016） |
| 2026-09-03 | AntdUI CellEndEdit 恒返回 false + VM 提交后重建 | 内部落值与业务数据源双写竞争（ERR-017） |
| 2026-09-03 | AntdUI 行事件索引统一减 1 换算 | 1 基 INDEX 实证（ERR-017 二轮） |
| 2026-09-03 | 编号列识别候选化 + 校验失败弹窗 | 硬编码列名静默失效（ERR-018）；拦截必须配告知 |
| 2026-09-03 | CreateBlankAsync 表头参数化 + 数据全空 | 新表与已有表结构一致（v1.7） |
| 2026-09-03 | 新建配方改备份轮转 + 行序整理 + 补行 | 文件流转语义返工教训（ERR-019） |
| 2026-09-03 | 打印接入 ZplPrinterService + 流水号持久化 | 用户提供源码整合；重开不断号防跳号 |
| 2026-09-03 | 打印通道切换 Spooler RAW 为主 | 现场 TCP 不可达；Spooler 实测成功（v1.8b） |
| 2026-09-03 | 打印内容双路径：自定义优先、留空走流水号 | 自定义路径不动流水号避免无意义消耗（v1.9） |
| 2026-09-04 | PLC 抽象层 IPlcTransport + 服务层自动连接/心跳 | 可测试性（Fake 桩）+ 工业可靠性（v1.10） |
| 2026-09-04 | 面板语义收缩为 PLC 专用（后 v1.21 扩展四类） | 一般操作日志只落文件防刷屏（v1.11） |
| 2026-09-04 | 物料轮询 M1000×19 驱动抽屉（PLC 唯一真值源） | 用户需求连续读取；Mock 随机与真值冲突停用（v1.12） |
| 2026-09-04 | 地址原样透传 + 显式 InovanceSeries.H5U | 软元件格式实证（ERR-022，v1.12b） |
| 2026-09-04 | 心跳与物料合一循环 + 阈值重连 | 仿参考程序重构（v1.15）；单向可配（v1.14） |
| 2026-09-04 | SynchronizationContext 构造时捕获存字段 | 后台事件现取静默丢失（ERR-023，v1.16） |
| 2026-09-04 | 启动默认无料无配方 + 编辑权限联动 | 用户需求启动灰态；ReadOnly=!HasMaterial（v1.17/1.18） |
| 2026-09-04 | 配方分组仅数据层 | 用户确认界面暂不显示（v1.19） |
| 2026-09-07 | VM 桥接进程方案（net48 + 命名管道 + 共享协议） | 跨运行时 SDK 隔离（v1.24）；路径 DI 单点化（v1.25） |
| 2026-09-07 | 布局计算抽取纯函数 WindowButtonLayout | 单测锁死不变量；禁用 Anchor（ERR-024，v1.23b） |

---

## 七、已解决错误完整条目（源自 errorlog.md，🟢 压缩前的完整版）

> 2026-09-08 防膨胀专项将 errorlog.md 中 26 个 🟢 条目压缩为一行摘要；完整原文搬移至此。教训本体已同步沉淀于 errorlog「防回归清单」（完整保留）与 systemPatterns「已知陷阱」。

### ERR-001：WinForms 透明背景 ArgumentException

- **错误现象**：自绘控件设置 `BackColor = Color.Transparent` 抛 `ArgumentException: 控件不支持透明的背景色`
- **发生上下文**：`DrawerIndicatorControl` / `TabItemControl` 等自绘控件绘制背景时
- **根本原因**：普通 Control 未通过 `SetStyle(ControlStyles.SupportsTransparentBackColor, true)` 启用透明支持
- **解决方式**：背景色用与父容器一致的具体色（白色）；半透明效果改用 GDI+ `Color.FromArgb` 画刷实现
- **教训**：WinForms 自绘控件慎用 Transparent，GDI+ 半透明画刷是更可控的替代

### ERR-002：CS0104 Timer 引用歧义

- **错误现象**：编译错误 `CS0104: Timer 是 "System.Windows.Forms.Timer" 和 "System.Threading.Timer" 之间的不明确引用`
- **根本原因**：ImplicitUsings + csproj 的 UseWindowsForms 同时引入两个命名空间
- **解决方式**：Service 层用 `System.Threading.Timer` 完全限定名
- **教训**：WinForms 项目中后台定时器一律完全限定

### ERR-003：CS0067 事件未使用警告

- **错误现象**：编译警告 `CS0067: 事件 "EventHandler" 从未使用过`（RelayCommand 的 CanExecuteChanged）
- **解决方式**：`RaiseCanExecuteChanged()` 中实际调用 `EventHandler?.Invoke(this, EventArgs.Empty)`
- **教训**：ICommand 实现里 CanExecuteChanged 必须有真实触发点，WinForms 无 CommandManager 自动刷新

### ERR-004：参数化命令 CanExecute(null) 误判禁用

- **错误现象**：底部 Tab 全部被禁用（CommandManagerHelper 统一 `CanExecute(null)` 刷新）
- **根本原因**：NavigateCommand 带 PageType 参数，传 null 被误判为不可执行
- **解决方式**：绑定时存储 `(Control, Parameter)` 元组，刷新用原参数调 CanExecute；后续升级为 `(Control, Func<object?> 参数提供器)`
- **教训**：WinForms ICommand 无泛型约束，参数化命令的刷新必须携带原参数；动态参数用 ProxyCommand + 参数提供器实时取值

### ERR-005：CS0535 接口升级后实现类未同步

- **错误现象**：`dotnet build` 报 3 个 CS0535——RecipeFileService 未实现升级后的 IRecipeFileService 新签名；且 VM 还在调用接口上已不存在的旧方法
- **解决方式**：① 重写 RecipeFileService（提取 LoadCoreAsync/SaveCoreAsync 核心 + 带路径重载切换 FilePath + CreateBlankAsync 实现）；② 全局搜索 VM 中旧方法名调用同步修正
- **教训**：接口升级必须同步实现类 + 全局搜索所有调用方（Service 修完不等于修完，VM 层旧调用是第二波编译错误）

### ERR-006：MSB3027 程序运行锁定 exe

- **错误现象**：构建错误 `MSB3027: 无法复制 UiTopMachine.exe，文件正被另一进程使用`（程序运行时构建）
- **解决方式**：`taskkill /f /im UiTopMachine.exe` 后重试
- **教训**：构建前确认目标 exe 未在运行；GUI 调试循环尤其易踩

### ERR-007：并发保存 xlsx 抛 IOException 文件锁冲突

- **错误现象**：单元格编辑自动保存与手动保存并发写同一 Recipe.xlsx 抛 IOException
- **根本原因**：ClosedXML `SaveAs` 写盘期间独占文件句柄
- **解决方式**：VM 层加 `SemaphoreSlim(1,1) _saveLock`，所有保存统一走 SaveCoreAsync 串行化
- **教训**：自动保存类后台 IO 必须考虑与用户手动操作并发，同一资源写入用信号量排队

### ERR-010：AntdUI Table 不支持 DataTable 直接绑定

- **错误现象**：DataTable 传入 `table.Binding(...)` 类型不匹配
- **根本原因**：AntdUI 2.4.7 `Table.Binding<T>` 只接受 `AntList<T>` 或 `BindingList<T>`
- **解决方式**：View 层写 `BindTable(DataTable)` 适配——按列重建 Column，逐行转 `AntItem(key, value)[]` 装入 `AntList<AntItem[]>`；TableVersion++ 通知重建
- **教训**：第三方 UI 库 API 以反射/编译用例实证为准，不凭文档臆测

### ERR-012：AntdUI CellFocused 鼠标单击不触发

- **错误现象**：单击 AntdUI Table 单元格后「删除行/列」按钮保持禁用（初版仅订阅 CellFocused）
- **根本原因**：AntdUI 2.4.7 `CellFocused` 事件鼠标单击不触发（偏向键盘焦点导航）
- **解决方式**：`CellClick + CellFocused` 双事件订阅，共用 `UpdateFocus`；点击表头（索引 < 0）视为取消选中
- **教训**：第三方 UI 库事件名不能望文生义，必须反射实证委托签名并运行时验证；同名委托可能是其他组件的，需从 `Table.GetEvent` 取真实类型

### ERR-013：AddRowCommand 永久禁用（RaiseCanExecuteChanged 漏刷）

- **错误现象**：「新增行」按钮数据加载完成后仍为灰色
- **根本原因**：VM 多个属性 setter 各自手动列举要刷新的命令，AddRowCommand 被遗漏
- **解决方式**：VM 提取 `RefreshAllCommandStates()` 统一刷新全部 8 个命令，三个属性 setter 全部改调
- **教训**：WinForms 无自动命令刷新机制，属性变化影响命令可用性必须显式通知；多命令共用时用「全量刷新」替代「逐个列举」

### ERR-014：新增行刷新后消失（ClosedXML 空行不落盘 + RowsUsed 跳过空行）

- **错误现象**：新增行后日志显示成功且自动保存成功，刷新后表格回到 18 行——新行凭空消失（用户反馈「新增行不能添加数据，刷新之后不显示」）
- **根本原因**：① 写入端 ClosedXML 对纯空字符串单元格不落任何痕迹；② 读取端 `RowsUsed()` 只返回有内容的行，空行被跳过
- **解决方式**：SaveCoreAsync 整行全空时首列写单个空格 `" "` 占位；LoadCoreAsync 弃用 RowsUsed()，改 `LastRowUsed().RowNumber()` 确定末行 + for 循环逐行装载，空格占位经 Trim 还原
- **教训**：ClosedXML 的 RowsUsed() 语义是「有内容的行」不是「表格的行」，读写循环必须自己维护行号；空 DataTable 行写入 Excel 必须显式占位

### ERR-015：加载特定列结构的配方文件失败（表头重命名 DuplicateNameException）

- **错误现象**：加载列名与默认表头部分重合的配方 xlsx 时 LoadAsync 失败（单元测试首轮 17 用例连锁失败暴露）
- **根本原因**：LoadCoreAsync 表头采用「预置默认表头 + 逐列重命名」，文件列名与默认表头其它列重名时抛 DuplicateNameException（自 0.8 潜伏）
- **解决方式**：改为按文件实际表头**新建 DataTable 重建列结构**（空表头「列N」兜底）
- **教训**：「重命名」式适配表头隐含全局唯一性约束，列名来自外部文件时必须改用「重建」语义；产品 Bug 被历史版本携带数月而测试首轮即暴露，验证「每次任务修改功能必须配套测试」工作流价值

### ERR-016：带首尾空格的编号绕过唯一性校验

- **错误现象**：① 带空格编号（" R001 "）通过校验保存，重载 Trim 后与已有编号撞车；② 所有输入空格重载后消失（数据漂移）
- **根本原因**：LoadCoreAsync 重载时全部单元格 Trim，但 VM 修改链路仍是原文写入+原文比较——两端口径不一致
- **解决方式**：VM 修改链路四处对齐 Trim 口径（TryCommitCellEdit 提交前规范化 / IsDuplicateRecipeId / ValidateRecipeIdUnique / GenerateUniqueRecipeId）
- **教训**：同一数据在读写两端必须同一规范化口径；数据不变量的守护要放在规范化后的值域上

### ERR-017：单元格修改错位写入（AntdUI 行事件索引为含表头的 1 基 INDEX——两轮实证修正）

- **错误现象**：两轮症状方向相反——首轮（返回 true 时）值错位写到上一行；二轮（恒返回 false + VM 提交后）值错位写到下一行同列、末行静默失败、查重失效
- **根本原因**（二轮运行时实证）：AntdUI 2.4.7 CellEndEdit/CellClick/CellFocused 三事件的 RowIndex 均为含表头的 1 基内部 INDEX（内部 rows[0]=表头），ColumnIndex 为 0 基、SelectedIndex 亦 1 基；首轮误诊「事件 0 基」保留恒 false + VM 提交但仍把 1 基 RowIndex 直接传给 0 基 DataTable → 全部偏移 +1
- **解决方式**：① CellEndEdit 恒返回 false + `TryCommitCellEdit(e.RowIndex - 1, e.ColumnIndex, ...)`；② UpdateFocus 同样行减 1 换算（删除链路一并修正）；③ BindTable 恢复高亮 `SelectedIndex = _focusedRowIndex + 1` 反向换算
- **教训**：第三方 UI 库的索引基准必须实证且不可想当然；正确套路 = 同一次交互同时捕获已知正确的基准事件与待测事件的索引做对照；行/列基准可能不一致必须分别验证；一个索引错位会级联放大成多个「看似无关」的症状，排查时从共同根因入手

### ERR-018：编号查重在真实配方表上静默失效

- **错误现象**：用户反馈编号防重未生效（用户表头为「编号」≠ 硬编码「配方编号」）；且校验失败仅写日志无弹窗
- **根本原因**：`RecipeIdColumn = "配方编号"` 硬编码单列名精确匹配 → 编辑查重/保存兜底/自动编号三道防线同时静默失效
- **解决方式**（v1.6）：① 候选列表 `{ "配方编号", "编号" }` + Trim + 忽略大小写 + `FindRecipeIdColumnIndex()` 统一入口替换 6 处硬编码；② MessageRequested 弹窗反馈 + 拒绝也 TableVersion++ 强制还原；手动保存兜底失败弹窗（userInitiated 参数）；③ Service 表头 Trim
- **教训**：业务规则关联外部数据（列名/表头）时禁止硬编码单一精确名——必须候选列表 + 规范化匹配；校验类功能的「拦截」与「告知」是一体的

### ERR-019：新建配方文件流转语义理解偏差（另存副本 vs 备份轮转）——返工

- **错误现象**：v1.7 将「新建配方」实现为「原文件不动 + 另存时间戳副本并切换工作区」，用户指出正确语义应为**备份轮转**（原配方改名备份 → 新空白配方沿用原文件名）
- **根本原因**：设计前未与用户对齐「文件流转语义」——「原文件保留」存在多种实现
- **解决方式**（v1.7b）：① CreateBlankAsync 移除 recipeName，新文件固定沿用当前文件名；② Service 轮转 File.Move 原文件 → 原名_yyyyMMdd_HHmmss.xlsx（同秒递增 _2/_3 防覆盖）；③ VM 先经通用 ConfirmationRequested 确认；④ 顺带完成行序整理 CompactRows + EnsureMinRows + RowHeight
- **教训**：涉及文件生命周期（重命名/移动/删除/覆盖）的功能需求，动手前必须先与用户对齐「文件流转语义」；正确流程 = 列出候选方案矩阵让用户选择后再实施

### ERR-020：测试桩通道与生产代码脱节

- **错误现象**：v1.8 测试桩的记录/失败注入挂在 `PrintByIpAsync` 上，生产 VM 切到 Spooler 通道后 4 个 VM 打印用例不再经过桩逻辑；该次交付未执行 dotnet test，缺陷被带入下一任务
- **根本原因**：① 测试桩与生产代码的实现通道无一致性约束；② 违反「测试全绿才交付」工作流
- **解决方式**（v1.9）：桩逻辑迁移到 `PrintBySpoolerAsync`（生产通道）；补跑 dotnet test 103/103 全绿
- **教训**：生产代码改换实现路径时必须全局搜索测试桩中对应方法；「测试全绿才交付」是硬门槛，构建通过 ≠ 验证通过

### ERR-021：HslCommunication V12 大版本 API 变化

- **错误现象**：接入 HslCommunication 12.9.2 后 CS0612 警告「SetPersistentConnection() 已过时」；InovanceTcpNet 不在旧文档记载的命名空间
- **根本原因**：Hsl V12 起默认即长连接；大版本升级后协议类命名空间重新组织（实际为 `HslCommunication.Profinet.Inovance`）
- **解决方式**：删除 SetPersistentConnection() 调用；使用新命名空间；API 真实签名以 NuGet 包内 XML 文档核对为准
- **教训**：引入/升级第三方库大版本前，先以包内 XML 文档核对关键 API 签名与命名空间，不能凭记忆或旧文档写代码

### ERR-022：InovanceTcpNet 地址格式假设错误（心跳写失败导致无限重连循环）

- **错误现象**：TCP 握手成功但约 0.5 秒后心跳丢失 → 断开 → 5 秒重连无限循环；日志报地址解析失败
- **根本原因**：三层地址假设全错——① InovanceTcpNet 要求汇川软元件格式（位 "M1000"、字 "D100"）；② 默认构造（AM 系列）不支持 D 字地址，必须显式 `InovanceSeries.H5U`；③ v1.12 的 ResolveBitAddress 剥 M 前缀方向相反
- **解决方式**：① 删除 ResolveBitAddress 地址原样透传；② 心跳默认地址 "D100"/"D101"；③ 显式 `InovanceSeries.H5U`；④ `TranslateToModbusAddress` 离线实证固化为 6 守护用例；另用本机模拟器全链路实证
- **教训**：协议类的地址格式必须离线实证（TranslateToModbusAddress 一行即可验证）；「连接成功」不等于「通讯正常」；SDK 行为测试桩无法守护的路径，用 SDK 自身离线 API 写守护测试

### ERR-023：后台事件现取 SynchronizationContext 导致 UI 永远显示初始状态 + OnFormClosing 退出死锁

- **错误现象**：① 用户反馈「plc 还是显示未连接」——程序实际已连接但 Status 面板永远停留初始状态；② 点退出后窗体关闭但进程残留
- **根本原因**：① PLC 状态事件来自后台线程，现取 `SynchronizationContext.Current ?? new`——后台线程 Current 为 null，新建上下文无消息泵，Post 回调永不执行；② OnFormClosing 在 UI 线程直接 Wait 异步停止任务 → 死锁
- **解决方式**：① MainViewModel 构造时捕获 UI 上下文存 `_uiContext` 字段（4 处统一改用）；② OnFormClosing 改 `Task.Run(() => ShutdownAsync()).Wait(3s)`；③ 守护测试（new Thread 无上下文触发）
- **教训**：SynchronizationContext 必须在 UI 线程构造时捕获存字段，严禁后台事件里现取（`Current ?? new` 模式静默丢失无异常无日志）；同类 UI 更新点要成批排查；UI 假死/进程残留先查 UI 线程 Wait 异步任务的死锁；测试须还原生产的线程环境

### ERR-024：控件未加入容器时设置 Anchor=Right，按钮被推出窗口外

- **错误现象**：右上角窗口控制按钮 UIA 树可见、点击有效，但屏幕上看不到（实际按钮 bounds 被排到 x=3026，窗口右缘 1870，超出 1156px）
- **根本原因**：`Anchor = Top|Right` 在控件 Location 仍为 (0,0)、尚未加入容器时设置——WinForms 冻结「控件右缘到容器右缘的距离」为负值（基于默认 200×100 容器计算）
- **解决方式**：① 去掉 Anchor=Right；② 按钮位置由 `_topBar.Resize` 事件统一重算（LayoutWindowButtons）；③ 位置正确后原生 Button 仍不渲染，最终改用 **AntdUI.Button**（同窗体长期渲染正常的组件），先加入容器再设置 Anchor
- **教训**：Anchor=Right/Bottom 必须在控件加入容器且容器尺寸定型之后再设置（或用 Resize 事件重算）；「UIA 树里有 + 点击有效 + 屏幕上看不见」= 控件在窗口外的典型组合，先查 bounds 绝对坐标；原生 Button 渲染异常时换已实证的组件体系是最快收敛路径

### ERR-025：共享协议 BuildResponse 头部缺 `\n\n` 结束标记

- **错误现象**：VmBridgeProtocolTests「响应往返_成功含PNG」用例失败——ParseResponse 返回的 PngBytes 为 null，PNG 结果图字节被静默丢弃
- **根本原因**：BuildResponse 生成头部文本后直接拼 PNG 字节，头部末尾没有 `\n\n` 结束标记；ParseResponse 依赖「头部结束 = \n\n」切分，找不到分隔符时把整个 payload 当 headerText 解析
- **解决方式**：BuildResponse 在含 PNG 的响应头部末尾补 `sb.Append('\n')` 形成 `\n\n`；构建/解析两端往返由测试锁死
- **教训**：自定义二进制/文本混合协议的「分隔符约定」必须构建端与解析端同步实现；这类跨端契约 Bug 首选「构建→解析往返测试」当场暴露

### ERR-026：桥接 exe 相对路径回溯级数错误（4 级应为 3 级）

- **错误现象**：图像页加载方案报「桥接进程不存在：D:\tools\VmVisionBridge\...」——实际文件存在于 D:\GitRepo\tools\ 下
- **根本原因**：`Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ...)` 从 `bin\Debug\net10.0-windows\` 回溯到仓库根只需 3 级，误写 4 级解析到 D:\tools\
- **解决方式**：回溯级数改 3 级并注释推导链；修复后桥接 --probe 端到端实证 PROBE_OK
- **教训**：相对路径回溯级数必须以 AppContext.BaseDirectory 的实际值逐级推导（bin\Debug\<TFM>\ 是 3 级不是 4 级）；此类错误构建/测试全绿，只有运行时才暴露——错误信息中打印完整解析路径是必备的排障手段

### ERR-027：桥接进程加密狗授权失败后被复用（SDK 授权每进程仅一次）

- **错误现象**：图像页加载方案报 `VmException 0xE0000700: IMVS_EC_ENCRYPT_DONGLE_OUTDATE:Dongle not detected!` 且重试永远失败；但加密狗实际正常（VM 客户端可识别、全新桥接进程 --probe 一次成功）
- **根本原因**：三层叠加——① VM SDK 授权登录每进程只执行一次，进程内失败后无法自愈；② 桥接 HandleCommand 捕获 VmException 回 ERR 帧后进程继续存活；③ 服务层收到 ERR 业务失败响应不清理桥接进程 → 带病进程被无限复用
- **破案线索**：桥接进程 `log/SDK/PlatformSDK.log` 的 `Dongle check fail` 与 `log/Server/Monitor.log` 的 `MV_LoginLicense ret[5]`——主程序日志只有上层错误，SDK 底层日志才分清「狗真不在」vs「进程带病」
- **解决方式**：服务层 LoadCore 收到 Load 失败响应即 CleanupBridgeNoLock 清理桥接进程（下次自动全新进程重试）；VmBridgeProtocol 新增 IsDongleLicenseError + DongleLicenseHint 用户指引文案
- **教训**：子进程内「一次性初始化」失败是永久性的，调用方收到业务失败响应也必须当进程已污染处理；排除此类问题必须看子进程自己的底层日志；加密狗类环境故障重启后高发，服务层必须自带「失败换新进程」的自愈路径

### ERR-028：流程执行成功但输出图间歇为空（图像源帧率低于轮询频率）

- **错误现象**：连续检测约 1/3~1/2 运行报「流程无输出图」，结果图黑屏间歇出现
- **根本原因**：诊断日志实证——失败运行输出清单就是 ImageData 但值为空、ErrorCode=0、重试仍空 → 方案内图像源帧率低于 1s 轮询频率，无新帧的运行图像输出值为空属方案常态，上层把它当检测错误是语义错位
- **解决方式**：三层降级——① 桥接 HandleRun 取图为空且 ErrorCode=0 → 返回成功无图；② 服务层收到成功无图响应 → 返回 Image=null 的 OK 结果；③ VM 对 Image=null 仅记 Info 跳过显示；另 TryGetOutputImage 枚举 GetAllOutputNameInfo() 回退其余图像输出键
- **诊断手段沉淀**：桥接进程加 DiagLog（log/VmBridge-diag.log 文件日志）——服务启动方式 stderr 无重定向会丢失
- **教训**：「流程执行成功但无输出」≠「检测失败」——低速图像源下无新帧是常态，必须按跳过处理；间歇性为空先看子进程诊断日志分清「键不存在/值未发布/值真空」三种情形

### ERR-029：图像页 INPC 绑定静默失效（属性更新但界面黑屏——改 View 轮询定时器）

- **错误现象**：桥接取图链路正常、日志持续输出，但图像区持续黑屏、结论角标与统计文本从不显示；「方案已加载」标签（同为 INPC 绑定）却正常
- **排查过程**：① SyncRun() 替换异步 Run()——仍间歇无图；② KeepModuleLastResult(true)——无效；③ 桥接加 --grab 调试模式连续落盘 PNG 实证图像正常且尺寸各异；④ VM 临时代码证实 INPC 在 UI 线程正常触发——但绑定控件纹丝不动
- **根本原因**：WinForms DataBinding 在本环境下对部分绑定静默失效（INPC 触发而控件不刷新，无异常无日志）——与 AntdUI/自绘控件混合的复杂窗体环境相关；个别绑定正常无法归纳规律
- **解决方式**：放弃三个失效属性的 INPC 绑定，View 端 500ms Windows.Forms.Timer 轮询同步（SyncDisplayFromViewModel）——定时器 Tick 固定 UI 线程，对线程/编组/绑定全部免疫；图像所有权移交 View（VM 不再 Dispose 旧图，View 替换引用时释放）；保留 AttachUiMarshaller；桥接 Run() → SyncRun()
- **附带发现**：本环境 dotnet build 增量构建不可靠（编译陈旧源码、报成功但产物未更新）——验证产物必须 clean 构建 + 检查产物内新符号；net48 无 Math.Clamp（用 Min/Max）
- **教训**：WinForms 绑定失效无任何报错，排障必须逐层实证（INPC 触发了吗？在哪个线程？绑定收到了吗？）而非反复猜测；显示类需求可用 UI 定时器轮询兜底——简单可靠对线程模型免疫；「参考程序怎么写就怎么对齐」往往比自创路径更快
---

## 附：activeContext 搬移内容（2026-09-10，v1.32 交付时按 §3.3 替换式写入归档）

### 上一焦点（v1.30，2026-09-09）

**VM 方案文件加密防外泄 ✅** —— SolutionProtector（AES-256 + PBKDF2，VMENC1 壳）+ 桥接加载时解密到临时明文用完即删 + SolutionEncryptor 工具；真实方案已加密替换 `D:\Printer\VisionTesting.dll`。202/202 全绿。

### 上一焦点（v1.29，2026-09-09）

**界面整体调整 ✅** —— ① 进料抽屉配方输入框加宽（220→300px、字体 12f）；② 无边框窗口可拉伸（`WndProc` WM_NCHITTEST 八方向边缘命中，8px Padding 外沿，ERR-033）；③ 全局字体放大 +1~2pt。196/196 测试全绿。

### 上一焦点（v1.28/28b，2026-09-09）

**图像页「保存图片」应用层实现 + 保存对话框失控修复（ERR-032/32a）✅** —— 右键菜单保存崩 OpenCvSharp（原生库未分发）→ GDI+ 自实现 SaveImageCommand（Clone 后台落盘）+ 成功/失败/无图弹窗；SaveFileDialog 误放 Bind 参数提供器致进页即弹 → 改 `SavePathRequestEventArgs` 请求回填模式 + 命令无参化。详 errorlog.md ERR-032/32a。
