# 技术上下文 (Tech Context)

## 技术栈（已落地）

| 类别 | 技术 | 说明 |
|------|------|------|
| 语言 | C# (.NET 10) | SDK 10.0.400（用户指定） |
| UI 框架 | WinForms | `net10.0-windows`，高 DPI SystemAware |
| 架构模式 | MVVM | WinForms 手写基础设施（非 CommunityToolkit） |
| DI 容器 | Microsoft.Extensions.DependencyInjection 10.0.11 | NuGet，唯一外部依赖 |
| 目标平台 | Windows | 工业上位机（工业 PC / 工控机） |
| 版本控制 | Git | 仓库位于 d:\GitRepo |

## 开发环境

- **操作系统**：Windows 10
- **IDE**：Visual Studio Code（兼容 Visual Studio）
- **.NET SDK**：10.0.400（`dotnet --version` 已验证）

## 项目构建

```powershell
dotnet build UiTopMachine.csproj        # 构建（当前无 .sln，直接用 csproj）
.\bin\Debug\net10.0-windows\UiTopMachine.exe   # 运行
```

## 已实现的技术要点

| 要点 | 实现 |
|------|------|
| MVVM 基础设施 | `Common/Commands/`：ObservableObject（INPC + SetProperty）、RelayCommand/AsyncRelayCommand（IsBusy 防重复）、CommandManagerHelper（命令↔按钮绑定 + 动态参数提供器，替代 WPF CommandManager） |
| 统一返回 | `Result<T>`（定义在 IDrawerService.cs）：Success / ErrorMessage / Data |
| UI 线程调度 | VM 后台事件经 `SynchronizationContext.Post` 回 UI 线程 |
| VM↔View 弹框请求 | 输入请求（`InputRequestEventArgs` + InputDialog）/ 确认请求（`ConfirmRequestEventArgs` + ConfirmDialog）事件模式，VM 零 UI 依赖 |
| 自绘控件 | DrawerIndicatorControl / FlatButton / LogPanelControl（双缓冲 + AntiAlias） |
| 日志 | ILogService 事件推送 + 落盘 `logs/yyyyMMdd.log` |
| 异常处理 | Program.cs 全局异常捕获；VM 捕获业务异常记日志；Service 返回 Result 不抛 UI |

## 技术约束与注意点

1. **ImplicitUsings + UseWindowsForms**：`Timer` 与 `System.Threading.Timer` 歧义，Service 层需完全限定
2. **WinForms 绑定弱于 WPF**：枚举属性绑定需自绘控件暴露同类型属性（如 `DrawerIndicatorControl.Status`）
3. **自绘控件**：需标注 `[DesignerSerializationVisibility(Hidden)]` 消除设计器序列化警告
4. **无 WPF CommandManager**：命令状态刷新需手动调 `RaiseCanExecuteChanged()`
5. **UI 线程**：硬件 IO 全 async/await；后台事件禁止直接操作绑定控件
6. **AntdUI Table 事件语义**：`CellFocused` 鼠标单击不触发（键盘焦点用），跟踪鼠标选中必须订阅 `CellClick`（详 ERR-012）；第三方事件勿望文生义，先反射实证。**索引基准（二轮运行时实证，ERR-017）**：`CellEndEdit`/`CellClick`/`CellFocused` 的 **RowIndex 均为含表头的 1 基内部 INDEX**（内部 rows[0]=表头，首条数据行=1），**ColumnIndex 为 0 基**；`SelectedIndex` 亦为 1 基——传 0 基数据源（DataTable）前行必须减 1，恢复高亮反向 +1；编辑与删除链路共用此换算
7. **ClosedXML 空行语义**：`RowsUsed()` 只返回有内容的行（空行被跳过）；空字符串单元格不落盘。Excel 往返必须「写端整行全空时首列空格占位 + 读端 `LastRowUsed().RowNumber()` 行号循环逐行装载」，不要依赖 RowsUsed 枚举（详 ERR-014）
8. **HslCommunication 大版本 API**：引入/升级前以包内 XML 文档核对签名与命名空间（`.nuget/packages/hslcommunication/<ver>/lib/*/HslCommunication.xml`）；过时 API 查注释中的替代方案（详 ERR-021）。**协议地址格式必须离线实证**：`TranslateToModbusAddress(address, functionCode)` 一行验证（InovanceTcpNet 需软元件格式 + 显式系列，详 ERR-022）

## 依赖清单

| 依赖 | 版本 | 状态 |
|------|------|------|
| Microsoft.Extensions.DependencyInjection | 10.0.11 | ✅ 已安装 |
| AntdUI | 2.4.7 | ✅ 已安装（配方页 Table 展示/双击编辑） |
| ClosedXML | 0.105.1 | ✅ 已安装（配方 xlsx 读写，MIT 免费） |
| HslCommunication | 12.9.2 | ✅ 已安装（v1.10：PLC Modbus TCP，客户端类 InovanceTcpNet 192.168.1.88:502 站号1、构造参数可切 ModbusTcpNet；**V12 默认长连接，SetPersistentConnection 过时不调**；InovanceTcpNet 位于 `HslCommunication.Profinet.Inovance`，ERR-021；⚠️ 新版本有商业授权检查，真机运行若触发授权提示需处理） |
| xUnit | 2.9.3（Test.Sdk 17.14.1 / runner 3.1.4 / coverlet 6.0.4） | ✅ 已接入（tests/UiTopMachine.Tests） |

## 工具使用模式（Cline 环境经验）

> 环境类坑的完整条目见 [errorlog.md](errorlog.md)（ERR-006/008/009/011），此处仅留操作要点：

- 终端实际为 **PowerShell**：命令用单命令或 `;` 分隔（禁 `&&`，详 ERR-008）；建目录用 `New-Item -ItemType Directory -Force`（详 ERR-011）
- `dotnet build` 输出为 GBK 乱码（凭“0 个警告 0 个错误”/“已成功生成”辨识，详 ERR-009），管道接 `| Out-String` 可读性更好
- 启动 GUI：`Start-Process "完整路径.exe"`；构建前确认 exe 未运行（详 ERR-006）
- 构建排错流程：先 `dotnet build` 拿真实错误（obj/build_result.txt 可能是过期缓存，不可信）→ 按 CS 错误码定位文件行号 → 修复后重跑构建验证

## 测试工作流（2026-09-03 固化，每次任务强制执行）

> **约定**：每次任务修改了功能，交付前必须为修改点写/更新单元测试并全绿后才允许交付。

1. **测试项目**：`tests/UiTopMachine.Tests`（xUnit，TFM=net10.0-windows + UseWindowsForms，引用主项目）
2. **执行命令**（项目根目录）：
   ```powershell
   dotnet test                                # 全量测试
   dotnet test --logger "trx"                 # 附带 trx 报告（TestResults/ 已入 .gitignore）
   dotnet test --filter "FullyQualifiedName~类名或用例名"   # 按名过滤
   ```
3. **结果记录**：当场看控制台（失败: 0, 通过: N）→ trx 留档 → 摘要写入 activeContext「测试记录」表 → errorlog 记录返工级失败
4. **测试资产**：历史修复配套用例永不过期（如 ERR-014 空行往返、ERR-013 命令恢复），每次 dotnet test 自动回归全部历史修复
5. **结构**：测试类按被测对象分文件（RelayCommandTests / DrawerItemViewModelTests / RecipeFileServiceRoundTripTests / RecipePageViewModelTests），公共桩在 TestDoubles.cs；用例名中文自描述并关联 ERR 编号

## 开发 setup

- [x] 创建 .csproj 解决方案工程（net10.0-windows）
- [x] 确定 .NET 目标框架版本（.NET 10）
- [x] 安装 NuGet 依赖包（DI 容器、AntdUI、ClosedXML）
- [x] 配置构建/调试流程（dotnet build + exe 直跑）
- [x] 建立 errorlog.md 错误归档机制（2026-09-02，编码前必查防回归清单）
- [x] 创建解决方案 UiTopMachine.slnx（2026-09-03，.NET 10 新格式，挂载主项目 + 测试项目）
- [x] 搭建单元测试基础设施（2026-09-03，xUnit + .gitignore，55 用例全绿）
