# 技能：WinForms MVVM 开发规范（winforms-mvvm）

> **适用场景**：本项目（WinForms + C# 工业上位机）新增页面/功能时的标准开发流程与避坑指南。
> 所有内容均在本项目实测验证（进料抽屉监控页面，2026-08-31）。

## 一、架构约定（强制）

严格 MVVM 四层，单向依赖：**View → ViewModel → Service → Model**

```
Models\        纯 POCO 数据实体 + 枚举（无业务逻辑）
ViewModels\    界面状态/命令/业务逻辑（继承 ObservableObject，禁止引用 UI 控件）
Views\         Form + Controls（只布局与绑定，零业务逻辑）
Services\      硬件/数据服务（接口 IxxxService + 实现，统一 Result<T> 返回）
Common\        MVVM 基础设施（ObservableObject / RelayCommand / CommandManagerHelper）
```

## 二、新增一个页面的标准流程

1. **Model**：`Models/` 下建数据实体与枚举
2. **Service**：`Services/Interfaces/` 定义接口（返回 `Result<T>`）；`Services/` 写实现（Mock 或真实设备）
3. **ViewModel**：继承 `ObservableObject`；属性用 `SetProperty` 触发通知；命令用 `RelayCommand`/`AsyncRelayCommand`；集合用 `ObservableCollection<T>`
4. **View**：布局控件；`DataBindings.Add(...)` 绑定；按钮用 `CommandManagerHelper.Bind(control, command)` 绑定命令
5. **DI 注册**：`Program.cs` 的 `ConfigureServices` 中注册（Service 单例、VM/View 瞬态）

## 三、WinForms MVVM 关键技巧（实测）

### 数据绑定
- VM 属性变更 → UI 刷新：`DataBindings.Add(nameof(Control.Text), vm, nameof(Vm.Prop), false, DataSourceUpdateMode.Never)`
- 用户输入 → VM 回写（双向）：`DataSourceUpdateMode.OnPropertyChanged`（TextBox 的 Text 绑定 formatEnabled 传 `true`）
- **枚举属性绑定**：WinForms 绑定不支持枚举→颜色自动转换；让自绘控件暴露**同类型枚举属性**（如 `DrawerIndicatorControl.Status`），setter 中 `Invalidate()` 重绘

### 命令
- WinForms 无 WPF CommandManager：`CommandManagerHelper.Bind(control, cmd)` 负责「点击转发 + Enabled 联动」
- 异步命令必须 `AsyncRelayCommand`（内置 IsBusy 防重复点击）
- VM 中命令状态变化后手动调 `RaiseCanExecuteChanged()`

### 后台线程 → UI 更新
```csharp
var context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
context.Post(_ => { /* 更新可观察集合/属性 */ }, null);
```

### 自绘控件模板
```csharp
SetStyle(ControlStyles.AllPaintingInWmPaint
         | ControlStyles.UserPaint
         | ControlStyles.OptimizedDoubleBuffer
         | ControlStyles.ResizeRedraw, true);   // 双缓冲防闪烁
```
- 属性需标注 `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]` 消除设计器序列化警告
- 绘制开启 `g.SmoothingMode = SmoothingMode.AntiAlias`

## 四、🚫 踩坑黑名单（本项目实测记录）

| 坑 | 原因 | 正确做法 |
|:---|:-----|:---------|
| `BackColor = Color.Transparent` 抛 ArgumentException | 普通 Control 未启用 SupportsTransparentBackColor | 背景用与父容器一致的具体色；半透明效果用 GDI+ `FromArgb` 画刷 |
| `Timer` 歧义 CS0104 | ImplicitUsings + UseWindowsForms 同时引入两个 Timer | Service 层完全限定 `System.Threading.Timer` |
| PowerShell `mkdir a b c` 失败 | mkdir 别名不支持多参数 | `New-Item -ItemType Directory -Force -Path "a","b"` |
| `Set-Content` 写中文乱码 | PowerShell 5.1 默认 ANSI 编码 | 用 editor 工具写文件，或 .NET File API（UTF-8） |
| ViewModel 直接 new 硬件客户端 | 违反分层，无法替换 Mock | 构造注入接口，DI 容器组装 |
| `.Wait()` / `.Result` 阻塞 | 卡死 UI 线程 | 全 async/await |

## 五、参考实现（本项目的范例代码）

| 学习点 | 文件 |
|--------|------|
| 完整页面绑定 | `Views/MainForm.cs` |
| 三态业务判定（VM 层） | `ViewModels/DrawerItemViewModel.cs` |
| 命令 + IsBusy + 统一 Result | `ViewModels/MainViewModel.cs` + `Services/Interfaces/IDrawerService.cs` |
| 自绘控件 | `Views/Controls/DrawerIndicatorControl.cs`、`FlatButton.cs`、`LogPanelControl.cs` |
| DI 组装 + 全局异常 | `Program.cs` |