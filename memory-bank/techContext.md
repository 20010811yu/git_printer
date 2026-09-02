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
6. **AntdUI Table 事件语义**：`CellFocused` 鼠标单击不触发（键盘焦点用），跟踪鼠标选中必须订阅 `CellClick`（详 ERR-012）；第三方事件勿望文生义，先反射实证

## 依赖清单

| 依赖 | 版本 | 状态 |
|------|------|------|
| Microsoft.Extensions.DependencyInjection | 10.0.11 | ✅ 已安装 |
| AntdUI | 2.4.7 | ✅ 已安装（配方页 Table 展示/双击编辑） |
| ClosedXML | 0.105.1 | ✅ 已安装（配方 xlsx 读写，MIT 免费） |
| PLC 通信库（建议 HslCommunication / S7NetPlus） | - | ⏳ 待接入（放 Communications/） |
| 单元测试框架（xUnit/NUnit） | - | ⏳ 待接入 |

## 工具使用模式（Cline 环境经验）

> 环境类坑的完整条目见 [errorlog.md](errorlog.md)（ERR-006/008/009/011），此处仅留操作要点：

- 终端实际为 **PowerShell**：命令用单命令或 `;` 分隔（禁 `&&`，详 ERR-008）；建目录用 `New-Item -ItemType Directory -Force`（详 ERR-011）
- `dotnet build` 输出为 GBK 乱码（凭“0 个警告 0 个错误”/“已成功生成”辨识，详 ERR-009），管道接 `| Out-String` 可读性更好
- 启动 GUI：`Start-Process "完整路径.exe"`；构建前确认 exe 未运行（详 ERR-006）
- 构建排错流程：先 `dotnet build` 拿真实错误（obj/build_result.txt 可能是过期缓存，不可信）→ 按 CS 错误码定位文件行号 → 修复后重跑构建验证

## 开发 setup

- [x] 创建 .csproj 解决方案工程（net10.0-windows）
- [x] 确定 .NET 目标框架版本（.NET 10）
- [x] 安装 NuGet 依赖包（DI 容器、AntdUI、ClosedXML）
- [x] 配置构建/调试流程（dotnet build + exe 直跑）
- [x] 建立 errorlog.md 错误归档机制（2026-09-02，编码前必查防回归清单）
- [ ] 创建 .sln（可选，多项目时再建）
