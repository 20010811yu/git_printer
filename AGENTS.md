# UiTopMachine 项目规则（ZCode Workspace 指令）

> 本文件是 ZCode 自动加载的 Workspace 指令入口。**规则正文以以下两个文件为唯一事实源**，
> 本文件不复制其内容（ZCode 不支持 @import），避免双源失同步；两者冲突时以源文件为准。

## 1. 强制规则文件（任务开始必须先通读，未读完禁止进行任何代码修改）

| 顺序 | 文件 | 内容 |
|---|---|---|
| 1 | `.clinerules/code-rules.md` | 编码硬约束：MVVM 分层、异步规范、异常处理、UI 线程安全、命名规范 |
| 2 | `.clinerules/memory-bank.md` | 记忆库工作流：P0~P5 任务生命周期、写入触发条件、Git 同步规范 |

- 执行 `memory-bank.md` 的完整生命周期：P0 按其 §1.2 固定顺序通读根目录 `memory-bank/` 下 7 个核心文件
  （projectbrief → productContext → activeContext → systemPatterns → techContext → progress → errorlog）；
  交付前按其 §5 执行记忆库同步检查、§6 执行 Git 同步。
- 修改规则时只改 `.clinerules/` 下源文件与本文件中的引用关系，不在此复制正文。

## 2. 项目速览

- **项目**：工业上位机 —— 进料抽屉监控系统（WinForms + MVVM，`net10.0-windows`，.NET 10 SDK）
- **构建**：`dotnet build UiTopMachine.csproj`（仓库根目录执行）
- **运行**：`bin\Debug\net10.0-windows\UiTopMachine.exe`；日志写入该目录 `logs\`
- **分层**：View → ViewModel → Service → Model 单向依赖；依赖注入组装在 `Program.cs`
- **tests/**：独立测试代码，已在主项目 csproj 中排除 glob，不随主程序编译
- **打印通道**：Windows Spooler RAW（打印机名 `zpl`）为主，TCP 直连（192.168.1.200:9100）为备用；
  流水号持久化于 `D:\Printer\Data\SerialNumber.txt`

## 3. 核心约束速记（完整版见 `.clinerules/code-rules.md`，冲突时以源文件为准）

- 业务逻辑只进 ViewModel / Service；Form 后台代码只做界面渲染与绑定转发，禁止操作业务控件逻辑
- 工业读写（PLC/相机/网络/文件）全部 `async/await`，严禁 `.Wait()` / `.Result` 阻塞 UI 线程；通讯代码必须带超时与异常捕获
- Service 层捕获硬件通讯异常并返回统一 `Result`；ViewModel 设置错误提示；UI 层只弹窗展示，不处理业务异常
- SDK 原始对象（Socket、Spooler 句柄、相机 SDK 等）封装在 Service 私有字段内，不外泄到 ViewModel
- 禁止静态全局变量存设备实例，统一依赖注入；ViewModel 属性必须实现 `INotifyPropertyChanged`，集合用 `ObservableCollection<T>`
