# 上位机开发约束规则

---

## 技术栈

 C# .net10/ WinForms/WPF,MVVM,PLC通讯(ModBusTcpNet,InovanceTcpNet),海康VisionMaster SDK,工业视觉项目。

---

## 强制架构规则

1. **严格 MVVM 分层，禁止业务逻辑写进 UI 后台代码**
    - View层：WinForm / WPF Window / UserControl，只负责界面渲染，**不能包含任何业务逻辑、PLC读写、算法逻辑**。
    - ViewModel层：业务逻辑、状态、命令，不引用任何UI控件（Button,TextBox,DataGridView、PictureBox等），禁止直接操作控件。
    - Model层：纯数据实体、POCO，只存属性，无业务代码。
    - Service服务层：独立封装 PLC通讯、相机SDK、数据库、文件读写、打印服务；ViewModel调用服务，ViewModel不直接操作SDK原始对象。

2. 分层引用方向（单向依赖，禁止反向引用）
    View → ViewModel → Service → Model
    ❌禁止：ViewModel 引用 View；Service 引用 ViewModel；Model引用任何上层

3. 数据绑定规范
    - ViewModel 属性必须实现 `INotifyPropertyChanged`；属性变更通知不能遗漏。
    - 集合使用 `ObservableCollection<T>`，禁止普通List绑定UI。
    - UI交互全部使用 `ICommand`（RelayCommand / CommunityToolkit.Mvvm），按钮点击绝对不能写在Form.Click事件里面。

4. 异步&工业IO强制规范
    - PLC读写、相机取图、网络通讯、文件操作**全部使用 async/await**；禁止阻塞UI线程，严禁 .Wait() / .Result 卡死界面。
    - 耗时任务必须放到后台Task，UI刷新通过主线程调度器更新。
    - 所有网络通讯代码必须写超时、重试机制、异常捕获。

5. 异常处理规范
    - Service层捕获硬件通讯异常（PLC断开、相机掉线、读写超时），向上抛出自定义业务异常；
    - ViewModel捕获异常，设置错误提示属性；**UI层仅展示弹窗消息，不处理业务异常逻辑**。

6. 禁止生成老旧耦合代码
    ❌禁止生成 this.textBox1.Text = vm.Value; 这种双向赋值代码（WinForm后台直接读写控件）
    ❌禁止在Form按钮事件里直接写PLC读写逻辑
    ❌禁止静态全局变量存设备、相机、PLC连接实例，使用依赖注入。

## 🟡代码编写规范

1. 命名规范
    - ViewModel后缀：xxxViewModel
    - Service后缀：xxxService，如 PlcCommunicationService、VisionCameraService
    - Model: xxxDto / xxxModel
    - 异步方法命名必须 Async 结尾：ReadPlcDataAsync()

2. CommunityToolkit.Mvvm优先使用
    允许使用 [ObservableProperty] 源生成器，手写INPC也可以，优先使用官方MVVM工具包，禁止自己手写复杂RelayCommand。

3. PLC/视觉SDK隔离
    SDK原始对象（VisionMaster、Modbus客户端实例）**必须封装在Service内部私有字段，绝对不能暴露给ViewModel**。
    Service返回干净的数据，不要把SDK对象一路传到UI层。

4. 日志
    所有硬件交互、异常必须预留日志回调接口；ViewModel不直接写日志文件，调用日志服务。

## 🟢输出交付规则（AI返回代码强制要求）

1. 生成完整可运行代码，给出所有引用的Nuget包；
2. 代码附带注释，划分区域：属性、命令、业务方法；
3. 如果生成UI和ViewModel，**同时给出绑定示例代码**；
4. 生成完代码后，输出架构说明，解释分层职责、数据流走向；
5. 如果代码违反以上MVVM规则，请自我检查并修正后再输出。

## ⚫禁止输出内容清单

- 不要给我一堆零散的代码片段，优先给出完整类；
- 不要生成任何后台代码直接操作控件的耦合写法；
- 不要使用单例模式滥用设备连接；
- 不要省略异常处理、超时判断；
- 不要写同步阻塞的工业读写代码。

## UI 线程安全规则（C# WinForms）

### 强制要求

- 所有对 UI 控件（如 `ListBox`、`TextBox`、`Label`、`vmRenderControl1` 等）的属性修改或方法调用，**必须**在 UI 线程上执行。
- 如果当前代码在后台线程（如 `Timer.Elapsed`、`Task.Run`、`BackgroundWorker`）中，必须使用 `Control.BeginInvoke` 或 `Control.Invoke` 封送调用。

### 正确示例

```csharp
// 在后台线程中更新 ListBox
this.BeginInvoke(new Action(() =>
{
    listBox1.Items.Insert(0, "状态更新");
}));