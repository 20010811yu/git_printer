# 项目进度 (Progress)

## ✅ 已完成功能

### 仿参考程序重构 PLC 连接/心跳（2026-09-04）⭐ 最新

- [x] **需求（v1.15）**：仿照旧程序（D:\OneDrive\桌面\Form.txt）的 连接/重连/开启心跳/心跳检测/取消心跳 方式重构
- [x] **心跳与物料合一（核心）**：删除 写 D100 心跳 / 读 D101 心跳 / 独立物料轮询 三个循环 → **一个心跳循环**周期读 M1000×19：读成功=通讯正常（失败计数清零）+ 推送物料变化（首读即推送）；对应参考的 CheckHeartbeatAsync（其读 MX9002×19 顺便更新抽屉）
- [x] **连续失败阈值重连（仿参考 _maxRetryCount=3）**：连续 3 次读失败判"心跳检测连续 3 次失败"→ 断开 → 10 秒重连；读成功清零；重连后心跳新循环计数/基线天然重置（仿 ReconnectPLCAsync）
- [x] **参数仿参考**：连接超时 10s / 收发 5s / 重连等待 10s；移除 D100/D101 写读与 monitorPlcAlive
- [x] **保留差异（已实证约定不搬）**：M1000×19 + H5U + 默认偏移（参考 MX9002+AM+1 基+CDAB 会地址错位）；全异步用 HSL ReceiveTimeOut 兜底（参考 WhenAny+ConnectClose 是同步 API 补丁）
- [x] **测试重构**：删 3 旧用例 + 新增 2 用例（连续失败达阈值重连 / 未达阈值恢复不断连）
- [x] 验证：dotnet test **127/127 PASS** + dotnet build **0 警告 0 错误**；运行 25 秒+ 连接稳定零错误、物料联动正常
- [x] Memory Bank 同步更新（activeContext/systemPatterns/progress/projectbrief）

### 取消双向心跳（2026-09-04）

- [x] **需求（v1.14）**：用户要求「取消双向连接」——背景为本地模拟器不改变 D101，双向心跳的读监测反复判丢失断连
- [x] **实现**：`PlcCommunicationService` 加 `monitorPlcAlive` 开关（**默认 false 单向心跳**：只周期写 D100 递增；true 恢复读 D101 停滞监测判 HeartbeatLost，通路保留）；Program.cs 显式 false
- [x] **测试**：停滞用例改显式开启监测 + 新增单向用例（PLC 停滞不判丢失/不读 D101/连接保持）
- [x] **运行验证**：连接保持 2 分钟+ 零重连（对比 v1.13 每 10 秒一轮断连）；物料轮询首读推送实证生效（模拟器 M1000 区 12 位 true → 对应抽屉状态灯联动变黄）
- [x] 验证：dotnet test **129/129 PASS** + dotnet build **0 警告 0 错误**
- [x] Memory Bank 同步更新（activeContext/systemPatterns/progress/projectbrief）

### Status 面板常驻展示 PLC 连接状态（2026-09-04）

- [x] **需求（v1.13）**：在 listbox（Status 列表面板）展示 PLC 的连接状态——面板顶部常驻状态行，一眼可见当前连接状态
- [x] **实现**：LogPanelControl 顶部自绘状态行（颜色圆点 + 粗体文字：绿=已连接 {Target} / 橙=连接中 / 红=心跳丢失与未连接）+ `UpdatePlcStatus` 方法；消息流保持 v1.11 语义（只存 PLC 成功提示与对接错误）
- [x] **链路**：`IPlcCommunicationService` 加 `Target` 描述（"IP:端口 站号1"，Program.cs 显式传）→ MainViewModel `PlcStatusText`/`PlcStatusLevel`（事件内 SynchronizationContext.Post 刷新）→ MainForm 订阅 PropertyChanged 转发 → 控件刷新
- [x] **测试**：新增状态行流转用例（未连接→连接中→已连接→心跳丢失→未连接 + 级别断言）
- [x] 验证：dotnet test **128/128 PASS** + dotnet build **0 警告 0 错误**；运行截图验证状态行渲染正常
- [x] Memory Bank 同步更新（activeContext/systemPatterns/progress/projectbrief）

### PLC 地址格式修复 + 连接实证（2026-09-04）

- [x] **问题（v1.12b/ERR-022）**：运行程序检查连接状态发现无限循环——TCP 连上 → 0.5 秒心跳丢失 → 断开 → 5 秒重连；日志报「写入寄存器 100 失败：输入的地址解析失败」
- [x] **根因**：InovanceTcpNet 要求汇川软元件格式地址（位 "M1000"/字 "D100"，纯数字解析失败）；默认构造（AM 系列）不支持 D 字地址，必须显式 `InovanceSeries.H5U`；v1.12 的 ResolveBitAddress 剥 M 前缀方向相反
- [x] **修复**：删除 ResolveBitAddress（地址原样透传）；心跳默认地址 "D100"/"D101"；transport 显式 `InovanceSeries.H5U`
- [x] **实证**：`TranslateToModbusAddress` 离线地址翻译固化为 HslModbusAddressTests 6 守护用例；本机模拟器（127.0.0.1:502）全链路通过——连接/写 D101=567 回读 567/读 M1000×19 返回 19 位；修复后程序日志地址解析错误消失
- [x] **确认行为**：双向心跳第二向要求 PLC 侧周期变化 D101，被动模拟器不动 → 5 周期按设计判 HeartbeatLost 重连（真机需 PLC 程序员配合约定）
- [x] 验证：dotnet test **127/127 PASS** + dotnet build **0 警告 0 错误**
- [x] Memory Bank 同步更新（errorlog ERR-022 + 防回归清单 #15 + activeContext/systemPatterns/progress/projectbrief）

### PLC 连续读取 M1000 物料数组驱动 18 抽屉（2026-09-04）

- [x] **需求（v1.12）**：从 M1000 起连续读 19 个 bool，数组下标 1~18 对应抽屉 1~18（下标 0 不使用），true=有料 / false=无料，连续一直读取
- [x] **传输层**：`IPlcTransport`/`HslModbusTransport` 加 `ReadBoolsAsync(address, length)`——HSL `ReadBoolAsync(address, length)` 批量线圈读；`ResolveBitAddress` 剥离 "M" 前缀（Modbus 不识别软元件名，汇川 H5U M 区与线圈同址；真机若有偏移调整传入地址）
- [x] **服务层**：`DrawerMaterialsChanged` 事件（`Values[i]`=抽屉 i；仅抽屉位变化时触发，首读即推送覆盖初始状态）+ 构造参数 `materialAddress="M1000"`/`materialLength=19`/`materialPollPeriodMs=1000`；连接成功后与心跳并列自动轮询（SemaphoreSlim 串行化）；读失败报"抽屉物料读取失败（M1000）"并断开重连（面板一条对接错误，不刷屏）
- [x] **隐患修复**：断开重连前 `StopCyclesAsync()` 显式取消并等待心跳/物料任务退出（v1.10 起旧任务重连后报失败会误触发断开的竞态）
- [x] **UI**：MainViewModel 订阅物料事件批量更新 18 抽屉（`UpdateFromModel` 只同步物料、配方保留用户输入）+ 统一刷新三态汇总；**InitializeAsync 移除 Mock `StartMonitoring()`**——PLC 物料轮询为有料状态唯一真值源（原 Mock 每 2 秒随机翻转与真值打架）
- [x] **测试**：FakePlcTransport 加位读桩（调用记录/可编程返回/失败注入）；新增 5 用例（自动连续读取 M1000×19 断言 / 变化触发且下标对应+无变化不重发 / 下标 0 变化不触发 / 读失败断开重连 / VM 物料映射配方保留汇总正确）
- [x] 验证：dotnet test **121/121 PASS** + dotnet build **0 警告 0 错误**
- [x] Memory Bank 同步更新（activeContext/systemPatterns/projectbrief/progress；techContext/errorlog 无需更新）

### Status 列表面板改为 PLC 专用（2026-09-04）

- [x] **面板语义变更（v1.11）**：主窗体右侧 Status 列表（LogPanelControl）**不再存入系统操作信息**（初始化/抽屉/配方/打印等一般日志），**只存 PLC 对接信息**——连接成功提示（成功绿条）+ 对接错误（连接失败/心跳丢失，错误红条）
- [x] **实现**：MainViewModel 取消订阅 `LogService.LogEmitted`（删除 OnLogEmitted），`Logs` 集合仅由 `PlcConnectionStateChanged` 事件驱动（`AddPlcPanelEntry` 直插，Connecting 过程信息只写文件）；全部 PLC 状态仍经 LogService 落文件（面板=过滤视图，文件=完整留痕）
- [x] **测试**：新增 MainViewModelPlcPanelTests 6 用例（StubDrawerService/StubPlcCommunicationService 桩 + ImmediateSynchronizationContext 无消息泵环境断言面板）
- [x] 验证：dotnet test **116/116 PASS** + dotnet build **0 警告 0 错误**
- [x] Memory Bank 同步更新（activeContext/systemPatterns/projectbrief/progress）

### PLC Modbus TCP 连接 + 双向心跳（2026-09-04）

- [x] **依赖接入（v1.10）**：HslCommunication 12.9.2——客户端类 **InovanceTcpNet**（汇川协议，继承 ModbusTcpNet，用户指定保留，命名空间 `HslCommunication.Profinet.Inovance`），构造参数可切标准 ModbusTcpNet；V12 默认长连接，`SetPersistentConnection` 过时不调（ERR-021）
- [x] **通信抽象层**：`Communications/Plc/IPlcTransport`（Connect/ReadShort/WriteShort/Close）+ `HslModbusTransport`（3s 连接/收发超时，OperateResult 在此层转异常，SDK 对象不外泄）
- [x] **Service 层**：`IPlcCommunicationService` + `PlcCommunicationService`——① 后台自动连接循环（启动即连、失败 5s 重试、断线自动重连、幂等启动）；② **双向心跳**（连接成功自动启动：周期写递增值到写心跳寄存器 100 证 PC 在线 + 读读心跳寄存器 101 监测变化证 PLC 在线，PLC 侧停滞 5 周期判 HeartbeatLost 触发重连）；③ 手动 StartHeartbeatAsync/StopHeartbeatAsync（幂等）；④ ReadRegisterAsync/WriteRegisterAsync 基础读写；SemaphoreSlim 串行化 IO、CancellationTokenSource 驱动循环、Result<T> 统一返回
- [x] **UI 接线**：不加页面——MainViewModel.InitializeAsync 自动启动 PLC 服务，ConnectionStateChanged 事件按级别写 ILogService（主窗体右侧 Status 列表面板显示，用户确认决策）；MainForm.OnFormClosing → ShutdownAsync 关心跳断连（3s 超时兜底）；Program.cs DI 单例注册
- [x] **测试**：新增 `PlcCommunicationServiceTests` 7 用例（FakePlcTransport 桩模拟 PLC 回写/停滞：自动连接+心跳递增断言 / PLC 停滞触发 HeartbeatLost+重连 / 手动停止心跳连接保持+手动重启 / StopAsync 断连冻结 / StartAsync 幂等 / 未连接启心跳拒绝 / 未连接读写拒绝）；顺手修 1 个既有 xUnit2013 警告
- [x] 验证：dotnet test **110/110 PASS** + dotnet build **0 警告 0 错误**
- [x] Memory Bank 同步更新（errorlog 新增 ERR-021 + 防回归清单 #14 + activeContext/systemPatterns/techContext/projectbrief）

### 打印页自定义打印内容 + 通道切换 Spooler（2026-09-03）

- [x] **打印通道切换（v1.8b）**：`PrintPageViewModel.PrintAsync` 由 `PrintByIpAsync`（TCP 直连）改为 `PrintBySpoolerAsync`（Windows Spooler RAW，打印机名 "zpl"）——TCP 192.168.1.200:9100 不可达持续超时，Spooler 实测打印成功；TCP 通道保留为备用
- [x] **自定义打印内容（v1.9）**：`CustomContent` 属性——Trim 后非空 → 每张打印用户输入内容（批量每张相同），**流水号不递增不持久化**；留空 → 走流水号自动递增原路径；自定义路径跳过流水号校验，两条路径互不干扰
- [x] **View 输入框**：打印页新增「打印内容（留空则打印流水号）」TextBox（PlaceholderText 提示）+ `TextChanged` → VM 绑定
- [x] **视图布局缺陷修复**：三个说明标题（当前流水号/码型/打印张数）原为局部变量、未参与 `CenterLayout` 布局叠在左上角——提升为字段统一排布（每行标题位于控件上方）
- [x] **ERR-020 修复**：测试桩 `SentZpl` 记录/`FailFromIndex` 失败注入从 `PrintByIpAsync` 迁至 `PrintBySpoolerAsync`（对齐生产通道，VM 打印用例恢复真实守护）；另修 1 个历史 xUnit2013 警告
- [x] **测试**：新增 3 用例（自定义内容每张打印流水号不变 / 纯空白回退流水号递增 / 自定义优先时非法流水号不拦截不弹窗）
- [x] 验证：dotnet test **103/103 PASS** + dotnet build 0 警告 0 错误
- [x] Memory Bank 同步更新（errorlog 新增 ERR-020 + 防回归清单 #13 + activeContext/progress/projectbrief）

### ZPL 打印机集成（2026-09-03）

- [x] **Service 层**：`IPrintService` + `ZplPrinterService`——整合用户 ZplPrinter 源码：TCP 直连（默认 192.168.1.200:9100，可配置，3s 超时）+ Windows Spooler RAW（默认打印机名 "zpl"，winspool 句柄私有封装）+ ZPL 生成 5 码型（二维码 ^BQ / Code39 ^B3 / Code128 ^BC / PDF417 ^B7 / 数字文本 ^AO）+ 流水号校验；全 async + Result 返回；剔除非相关 using；修正源码 Code128 笔误（`"LL300"` → `"^LL300"`）
- [x] **流水号自动递增**：打印成功 +1 持久化 `D:\Printer\Data\SerialNumber.txt`（6 位补零、保留位数、自然进位）；重开不断号；**批量中途失败流水号不前进**（防跳号）+ 持久化失败弹窗
- [x] **打印页启用**：当前流水号显示 + 码型选择 + 张数（1~999）+ 批量打印命令 + 失败弹窗
- [x] **DI**：`IPrintService → ZplPrinterService` 单例注册
- [x] **测试**：新增 `ZplPrinterServiceTests` 21 用例（ZPL 5 码型断言含笔误修正回归 / 流水号校验 Theory 7 组合 / 持久化往返补零 / VM 5 用例打印桩模拟）；**流水号补零位数保留修复**（打印内容 000001 而非 1）
- [x] 验证：dotnet test **100/100 PASS** + dotnet build 0 错误
- [x] Memory Bank 同步更新（activeContext/progress/systemPatterns/projectbrief/techContext）

### 备份轮转 + 行序整理 + 自动补空白行（2026-09-03）

- [x] **需求语义修正（ERR-019）**：v1.7「另存副本」返工 → **备份轮转**：原配方 `File.Move` 改名（原名+时间戳）备份（同秒递增 `_2/_3` 防覆盖）→ 新空白配方**沿用原文件名**（Recipe.xlsx）→ 页面立即显示新配方；接口 `CreateBlankAsync(headers, blankRowCount)` 移除 recipeName
- [x] **用户确认**：新建前经通用 `ConfirmationRequested` 事件（替换 DeletionConfirmRequested，删除行/列迁移共用，View 单点订阅）弹确认框「当前配方将自动备份（原文件名+时间戳），新配方沿用当前文件名」；取消则一切不变
- [x] **行序整理 `CompactRows`**：加载完成/单元格修改后/删除行后自动将中间空行稳定移到末尾——数据连续排列、空白垫底；有实际移动时 TableVersion++ 重建显示 + 自动保存（文件与显示一致）；实现采用「新表整体替换」杜绝半删中间态
- [x] **自动补空白行 `EnsureMinRows`**：总行数不足时末尾补**真实可编辑空白行**（双击录入、过查重、自动保存、空格占位持久化）；minRows 由 View 按表格可见高度计算（`(Height-40)/36`），Resize 时重算，页面始终填满
- [x] **行高调整**：`RowHeight=36` / `RowHeightHeader=40`（AntdUI 2.4.7 实证 API）
- [x] **测试**：改写 CreateBlank 7 个 Service 用例（备份轮转+数据完整断言/连续两次不覆盖/无原文件直接建/空表头回退/ERR014 联动）+ VM 3 个（确认轮转沿用路径/取消一切不变/保存往返）+ 行序整理/补行 4 个（中间空行整理+重载一致/不足补足可编辑/已够不补/编辑后数据连续）
- [x] 验证：dotnet test **79/79 PASS** + dotnet build 0 错误
- [x] Memory Bank 同步更新（errorlog 新增 ERR-019 + activeContext/progress/systemPatterns/projectbrief）

### 新建空白配方改造（2026-09-03）

- [x] **接口变更**：`IRecipeFileService.CreateBlankAsync(recipeName, headers, blankRowCount=10)`——表头由调用方传入（沿用当前配方表列结构，与已有数据表一致），不再写死默认 5 列；不再写首行编号（数据全空等待录入）
- [x] **Service 实现**：构造模板表（传入表头 + N 空白行）复用 `SaveCoreAsync` 落盘——首列空格占位持久化，空白行刷新/重开程序后不消失（ERR-014 机制自动生效）；空表头回退默认表头兜底
- [x] **VM 流程改造**：表头取自当前表 + 内存直接构造 10 空白行立即显示（无需文件重载，界面即时切换）；配方编号 R00x（GenerateUniqueRecipeId 跳过已占用）仅作新文件名，表内编号由用户录入并受三道查重防线保护；原配方文件保留不删（既有决策），新建即切换工作区
- [x] **测试**：新增 4 用例（表头一致+数据全空+指定行数 / 空白行占位保存重载不消失 ERR014 联动 / 空表头回退默认 / VM 端到端表头沿用+10 空行+保存往返）；既有 5 处 CreateBlankAsync 调用同步新签名（1 个旧语义用例「首行含编号」改写为「表头一致+数据全空」）
- [x] 验证：dotnet test **74/74 PASS** + dotnet build 0 警告 0 错误
- [x] Memory Bank 同步更新（activeContext/progress/systemPatterns/projectbrief/techContext）

### 编号查重真实表头生效 + 失败弹窗（2026-09-03）

- [x] **根因确证（ERR-018）**：查重列名硬编码「配方编号」，用户真实配方表表头为「编号」→ 编辑校验（`columnName == RecipeIdColumn` 恒 false）/保存兜底（`IndexOf` 返回 -1）/自动编号三道防线静默失效，重复编号照常写盘
- [x] **编号列识别宽松化**：候选表头 `{ "配方编号", "编号" }` + Trim + 忽略大小写，`FindRecipeIdColumnIndex()` 统一入口替换 6 处硬编码调用（TryCommitCellEdit/IsDuplicateRecipeId/ValidateRecipeIdUnique/GenerateUniqueRecipeId/AddRow/DeleteRow 文案）；识别不到编号列记 Warn 明确告知
- [x] **失败弹窗反馈**：新增 `Common/MessageRequestEventArgs.cs` + VM `MessageRequested` 事件（VM→View 弹框请求模式）；编辑重复拒绝 → 弹「编号已存在，修改失败」+ 拒绝也 TableVersion++ 强制还原显示；手动保存兜底失败弹窗（`SaveCoreAsync` 增 `userInitiated` 参数，自动保存静默）；View 经 `BeginInvoke` 封送 UI 线程弹 MessageBox
- [x] **Service 表头 Trim**：`LoadCoreAsync` 读表头 Trim 规范化（消除「编号␣」空格陷阱）
- [x] **新增 6 个测试用例**：「编号」表头编辑重复拒绝+弹窗断言 / 唯一通过不弹窗 / 新增行自动编号跳过占用 / 新增行手改重复拒绝 / 手动保存兜底拒绝+弹窗 /「配方编号」候选兼容
- [x] 验证：dotnet test **71/71 PASS**（65 + 6 新用例）+ dotnet build 0 警告 0 错误
- [x] Memory Bank 同步更新（errorlog 新增 ERR-018 + 防回归清单 11a + activeContext/progress/systemPatterns/projectbrief）

### 单元格错位二次修复（2026-09-03）

- [x] **二次实证**（bin/inspect/InspectRowIndex.cs 双实验对照）：AntdUI 2.4.7 `CellEndEdit`/`CellClick`/`CellFocused` 三事件 **RowIndex 均为含表头的 1 基内部 INDEX**（内部 rows[0]=表头 INDEX=0，首条数据行 INDEX=1，事件值与之一致），ColumnIndex 为 0 基，`SelectedIndex` 亦为 1 基——推翻首轮「事件 0 基」误判
- [x] **修复（ERR-017 复现，v1.5d）**：`RecipePage` 三处索引换算——① `CellEndEdit` 传 VM 前行减 1（修正「内容跑到下一行同列/末行越界静默失败/查重读错行被短路」三症状）；② `UpdateFocus`（CellClick/CellFocused 共用，删除行/列链路）同样减 1；③ `BindTable` 高亮 `SelectedIndex = 行 + 1` 反向换算；保持恒返回 false + VM 提交 + TableVersion++ 重建
- [x] **新增 2 个边界用例**：编号列重输自身原值不误报（查重错行症状回归）/ 末行可编辑且落在末行（越界静默失败症状回归）
- [x] 验证：dotnet test **65/65 PASS**（63 + 2 新用例）+ dotnet build 0 警告 0 错误
- [x] Memory Bank 同步更新（errorlog ERR-017 更新为真根因 + 防回归清单 #12 重写 + activeContext/progress/systemPatterns/techContext）

### 单元格错位写入修复（2026-09-03）

- [x] **运行时实证**（bin/inspect/InspectRowIndex.cs，临时工具）：AntdUI 2.4.7 `CellEndEdit` 事件 RowIndex/ColumnIndex 为 0 基视觉索引（与 CellClick 一致）、e.Value 为新值；但事件返回 true 时 AntdUI 内部把值提交到**含表头的 1 基 INDEX 行** → 用户编辑视觉行 N 被写到内部行 N（视觉行 N-1）→ 整体错位一行；返回 false 时内部**完全零写入**
- [x] **修复（ERR-017 首轮，VM 唯一事实源）**：`RecipePage.CellEndEdit` 恒返回 false 阻止内部错位落值；VM `TryCommitCellEdit` 提交成功后 TableVersion++ 驱动 View 重建表格同步显示（⚠️ 当时误诊「事件索引 0 基」未做行号换算 → 二次修复见上一节 v1.5d）
- [x] **新增 4 个位置正确性测试**：多单元格乱序编辑逐格断言 / 首行可改（ERR-017 症状回归）/ 编辑提交 TableVersion 自增 / 修改后保存重载各值仍在原位置
- [x] 附带：测试 Dispose 加固（后台自动保存与清理竞态重试）
- [x] 验证：dotnet test **63/63 PASS**（59 + 4 新用例）+ dotnet build 0 警告 0 错误
- [x] Memory Bank 同步更新（errorlog 新增 ERR-017 + 防回归清单 #12 + activeContext）

### 配方页修改功能测试与修复（2026-09-03）

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

- [ ] 图像页接入真实服务（VisionCameraService）
- [x] ~~打印页接入真实服务（IPrintService）~~ ✅ 2026-09-03 完成（ZplPrinterService，ZPL TCP/Spooler 双通道 + 流水号自动递增，v1.8）
- [x] ~~PLC 通讯基础设施（HslCommunication，Communications/）~~ ✅ 2026-09-04 完成（v1.10：IPlcTransport 抽象 + HslModbusTransport(InovanceTcpNet 192.168.1.88:502 站号1) + PlcCommunicationService 自动连接/双向心跳/手动启停；剩余 = 在此基础上实现 IDrawerService 真实 PLC 版替换 MockDrawerService）
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
**单元格编辑/删除链路索引换算已修正**（AntdUI 行事件 1 基 INDEX → VM 0 基减 1，高亮反向 +1，v1.5d/ERR-017 二次修复）；
**编号查重对真实表头「编号」生效**（候选表头识别 + 重复拒绝弹窗提示 + 手动保存兜底弹窗，v1.6/ERR-018）；
**新建配方备份轮转**（原文件改名+时间戳备份、新配方沿用 Recipe.xlsx 原名、确认弹框、页面即显，v1.7b/ERR-019）；
**行序整理 + 自动补空白行**（数据连续排列空白垫底、按可见高度补真实可编辑空白行、RowHeight=36，v1.7b）；
**ZPL 打印已启用**（打印页真实可用：**Spooler RAW 为主通道**（TCP 直连备用）+ 流水号自动递增持久化 + 5 码型批量打印 + **自定义打印内容**（非空每张打印输入内容、留空走流水号），v1.8/v1.9）；
**PLC 已接入**（启动后台自动连接 **InovanceTcpNet 192.168.1.88:502 站号1** + 双向心跳自动启停：写寄存器 100 递增 / 读寄存器 101 监测，停滞自动重连；**连续读取 M1000 起 19 个 bool 驱动 18 抽屉有料状态**（下标 i=抽屉 i，变化即推送，配方保留用户输入），v1.10~v1.12；**Status 列表面板只显示 PLC 对接信息**——连接成功提示与对接错误，一般系统操作日志只落文件不进面板（v1.11）；**待真机联调**）；
配方服务已升级多配方接口（带路径加载/保存重载 + `CreateBlankAsync(headers, blankRowCount)`）；全局 Status 日志跨页面共享。
单元测试 **127 用例全绿**（dotnet test，含 HSL 地址格式守护用例）。

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
| 2026-09-03 | AntdUI CellEndEdit 恒返回 false + VM 提交后 TableVersion++ 重建（VM 唯一事实源，1.5c） | 返回 true 时 AntdUI 内部落值与业务数据源双写竞争必错位（ERR-017） |
| 2026-09-03 | AntdUI 行事件索引统一减 1 换算 + 高亮反向 +1（1.5d，ERR-017 二次修复） | 二轮实证确证 CellEndEdit/CellClick/CellFocused 的 RowIndex 均为含表头 1 基 INDEX（列 0 基），未换算导致编辑偏下一行、删除偏移、末行改不动、查重被短路（首轮误诊「事件 0 基」的修正） |
| 2026-09-03 | 编号列识别候选化（配方编号/编号 + Trim）+ 校验失败弹窗（1.6，ERR-018） | 用户真实表头「编号」≠ 硬编码「配方编号」致三道防线静默失效；校验类功能「拦截」必须配「告知」否则用户感知等于没生效 |
| 2026-09-03 | CreateBlankAsync 表头参数化 + 数据全空 + 预置空白行（1.7） | 用户需求：新表与已有表结构一致、数据待录入、页面美观；复用 SaveCoreAsync 让空格占位机制自动保证空白行持久化（ERR-014） |
| 2026-09-03 | 新建配方改备份轮转（原名+时间戳备份，新配方沿用原名）+ 行序整理 + 自动补空白行（1.7b，ERR-019） | 用户明确文件流转语义：原配方保留（改名备份）且新配方沿用 Recipe.xlsx（工作文件名不变）；涉及文件生命周期的需求必须先对齐流转语义再实施（返工教训） |
| 2026-09-03 | 打印接入 ZplPrinterService（TCP 优先）+ 流水号持久化自动递增（1.8） | 用户提供源码整合进 MVVM 分层；流水号持久化保证重开不断号、中途失败不前进防跳号；IP/端口/打印机名构造可配置便于环境变更 |
| 2026-09-03 | 打印通道切换 Spooler RAW 为主（TCP 备用）（1.8b） | 现场 TCP 192.168.1.200:9100 不可达持续超时；Spooler 实测打印成功，通道由 VM 层一行切换（Service 双通道保留） |
| 2026-09-03 | 打印内容双路径：自定义输入优先、留空走流水号（1.9） | 用户需求打印自己输入的内容；自定义路径不动流水号（避免无意义消耗编号），两条路径互不干扰 |
