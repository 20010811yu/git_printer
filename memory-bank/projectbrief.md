# 项目简报 (Project Brief)

## 项目定义

**项目名称**：工业上位机项目（WinForms + C#）

**项目类型**：Windows 桌面应用程序 —— 工业自动化上位机软件

## 核心要求与目标

1. **技术栈**：C# + WinForms（.NET 10，SDK 10.0.400）
2. **架构模式**：严格采用 MVVM（Model-View-ViewModel）模式
3. **应用领域**：工业自动化控制与监控（上位机）

## 上位机核心职责

- 与下位设备（PLC、串口设备、网络设备）进行数据通信
- 数据采集、显示与监控
- 设备控制指令下发
- 报警管理与历史数据记录

## 项目范围

- ✅ MVVM 分层架构（Models / ViewModels / Views）
- ✅ 通信层（PLC / 串口 / TCP / 协议解析）
- ✅ 数据访问层（数据库、历史存储）
- ✅ 业务服务层（采集、报警）
- ✅ 公共基础设施（命令、工具类、扩展方法）
- ✅ 配置管理与资源管理

## 项目状态

- 📅 初始化日期：2026-08-31
- 📁 当前阶段：**多页面应用 + 单元测试基础设施已开发完成并可运行**
  - ✅ MVVM 基础设施（ObservableObject / RelayCommand / CommandManagerHelper 含参数化与动态参数绑定）
  - ✅ 浅色现代风界面：18 抽屉三态监控（绿=有料有配方/灰=无料无配方/黄=其余）、配方双向绑定、全局 Status 日志
  - ✅ 底部 Tab 页面导航：打印/图像/进料抽屉/配方四页面切换（NavigationViewModel 驱动）
  - ✅ 配方管理页（AntdUI Table）：单元格双击编辑（编号唯一校验）、增删行列、新建空白配方（备份轮转沿用原名）、保存/打开文件夹
  - ✅ 打印管理页（ZPL 真实打印）：Spooler RAW 主通道（TCP 直连备用）、5 种码型、流水号自动递增持久化、自定义打印内容（非空打印输入内容，留空走流水号）
  - ✅ 弹框交互：新增列经 InputDialog 收集列名；删除行/列经 ConfirmDialog 二次确认（VM↔View 事件模式，零 UI 耦合）
  - ✅ 配方服务：ClosedXML 多配方接口（带路径加载/保存 + CreateBlankAsync 备份轮转）
  - ✅ 打印服务：IPrintService/ZplPrinterService（Socket/句柄私有封装，全 async）
  - ✅ PLC 通讯基础设施（v1.10）：IPlcTransport 抽象 + HslModbusTransport（HslCommunication，InovanceTcpNet 192.168.1.88:502 站号1）+ PlcCommunicationService（后台自动连接/断线重连 + 双向心跳自动启停 + 手动心跳控制，状态入 Status 列表；待真机联调）
  - ✅ DI 组装（Microsoft.Extensions.DependencyInjection）+ Mock 模拟服务 + errorlog.md 错误归档机制
   - ✅ 单元测试（xUnit，tests/UiTopMachine.Tests + .slnx，129 用例全绿；2026-09-03 固化「每次任务修改功能必须配套测试并全绿」工作流）
  - ✅ PLC 抽屉物料接入（v1.12）：连续读取 M1000 起 19 个 bool（下标 1~18 对应抽屉，true=有料），变化推送驱动抽屉状态灯；PLC 为物料唯一真值源（Mock 随机监控停用）
  - ⏳ 待办：IDrawerService 真实 PLC 版替换 Mock（配方下发等）、图像页真实服务接入
  