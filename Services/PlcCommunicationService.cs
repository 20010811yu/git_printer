using System;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Communications.Plc;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Services
{
    /// <summary>
    /// PLC 通讯服务实现（Modbus TCP，经 IPlcTransport 屏蔽协议细节）：
    /// ① 后台自动连接循环——启动即连，失败按间隔重试，断线自动重连
    /// ② 双向心跳——连接成功后自动启动：周期向写心跳寄存器递增写数 + 读读心跳寄存器监测变化，
    ///    PLC 侧连续停滞达到阈值判定心跳丢失，触发断开重连；支持手动 启动/关闭 心跳
    /// ③ 所有 Modbus IO 经 SemaphoreSlim 串行化，异常统一转 Result，事件在后台线程触发
    /// </summary>
    public class PlcCommunicationService : IPlcCommunicationService
    {
        /// <summary>连接循环的状态轮询间隔（检查心跳启停请求与心跳异常标志）</summary>
        private const int LoopPollIntervalMs = 500;

        /// <summary>心跳写寄存器计数值上限（递增到此后回到 1，避免溢出为负数）</summary>
        private const ushort HeartbeatCounterMax = (ushort)short.MaxValue;

        private readonly IPlcTransport _transport;
        private readonly string _writeAddress;
        private readonly string _readAddress;
        private readonly int _heartbeatPeriodMs;
        private readonly int _maxMissedCycles;
        private readonly int _reconnectDelayMs;
        private readonly string _materialAddress;
        private readonly ushort _materialLength;
        private readonly int _materialPollPeriodMs;
        private readonly string _target;

        /// <summary>Modbus IO 串行化锁：心跳与业务读写共用一条长连接，必须排队（防回归清单 #3 精神）</summary>
        private readonly SemaphoreSlim _ioSemaphore = new(1, 1);

        private CancellationTokenSource? _serviceCts;
        private Task? _runTask;
        private Task? _heartbeatTask;
        private CancellationTokenSource? _heartbeatCts;
        private Task? _materialTask;
        private CancellationTokenSource? _materialCts;

        /// <summary>心跳期望状态（手动关闭后置 false，连接循环不再拉起）</summary>
        private volatile bool _heartbeatRequested;

        /// <summary>心跳异常结束标志（PLC 停滞或通讯异常，由连接循环消费并触发重连）</summary>
        private volatile bool _heartbeatFailed;

        private string _heartbeatFailureMessage = string.Empty;

        /// <summary>物料轮询异常结束标志（读失败，由连接循环消费并触发重连）</summary>
        private volatile bool _materialFailed;

        private string _materialFailureMessage = string.Empty;

        /// <inheritdoc />
        public event EventHandler<PlcConnectionEventArgs>? ConnectionStateChanged;

        /// <inheritdoc />
        public event EventHandler<DrawerMaterialsChangedEventArgs>? DrawerMaterialsChanged;

        /// <inheritdoc />
        public string Target => _target;

        /// <inheritdoc />
        public PlcConnectionState State { get; private set; } = PlcConnectionState.Disconnected;

        /// <summary>
        /// 构造：注入传输层与心跳/物料轮询参数（参数默认值可被测试覆盖以缩短等待）。
        /// 地址默认值按生产所用 InovanceTcpNet 的汇川软元件格式（字地址 D 区、位地址 M 区，ERR-022）
        /// </summary>
        public PlcCommunicationService(
            IPlcTransport transport,
            string target = "127.0.0.1:502 站号1",
            string writeAddress = "D100",
            string readAddress = "D101",
            int heartbeatPeriodMs = 1000,
            int maxMissedCycles = 5,
            int reconnectDelayMs = 5000,
            string materialAddress = "M1000",
            ushort materialLength = 19,
            int materialPollPeriodMs = 1000)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _target = target;
            _writeAddress = writeAddress;
            _readAddress = readAddress;
            _heartbeatPeriodMs = heartbeatPeriodMs;
            _maxMissedCycles = maxMissedCycles;
            _reconnectDelayMs = reconnectDelayMs;
            _materialAddress = materialAddress;
            _materialLength = materialLength;
            _materialPollPeriodMs = materialPollPeriodMs;
        }

        /// <inheritdoc />
        public Task StartAsync()
        {
            if (_runTask is not null && !_runTask.IsCompleted)
            {
                return Task.CompletedTask; // 幂等：已在运行
            }

            _serviceCts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunLoopAsync(_serviceCts.Token));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StopAsync()
        {
            _heartbeatRequested = false;
            _serviceCts?.Cancel();
            await StopCyclesAsync();

            if (_runTask is not null)
            {
                try
                {
                    await _runTask.WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch
                {
                    // 超时/取消：循环内有 3 秒收发超时兜底，退出路径已尽力
                }
            }

            _runTask = null;

            await _transport.CloseAsync();
            SetState(PlcConnectionState.Disconnected, "PLC 服务已停止：心跳已关闭，连接已断开");
        }

        /// <inheritdoc />
        public Task<Result<bool>> StartHeartbeatAsync()
        {
            if (_heartbeatTask is not null && !_heartbeatTask.IsCompleted)
            {
                return Task.FromResult(Result<bool>.OK(true)); // 幂等：心跳已在运行
            }

            if (State != PlcConnectionState.Connected)
            {
                return Task.FromResult(Result<bool>.Fail("PLC 未连接，无法启动心跳"));
            }

            _heartbeatRequested = true;
            StartHeartbeatCore();
            return Task.FromResult(Result<bool>.OK(true));
        }

        /// <inheritdoc />
        public async Task<Result<bool>> StopHeartbeatAsync()
        {
            _heartbeatRequested = false;

            if (_heartbeatTask is null || _heartbeatTask.IsCompleted)
            {
                return Result<bool>.OK(true); // 幂等：心跳未在运行
            }

            _heartbeatCts?.Cancel();
            try
            {
                await _heartbeatTask;
            }
            catch
            {
                // 取消过程中的收发超时异常不影响关闭结果
            }

            return Result<bool>.OK(true);
        }

        /// <inheritdoc />
        public async Task<Result<short>> ReadRegisterAsync(string address)
        {
            if (State != PlcConnectionState.Connected)
            {
                return Result<short>.Fail("PLC 未连接，无法读取寄存器");
            }

            await _ioSemaphore.WaitAsync();
            try
            {
                var value = await _transport.ReadShortAsync(address);
                return Result<short>.OK(value);
            }
            catch (Exception ex)
            {
                return Result<short>.Fail($"读取寄存器 {address} 失败：{ex.Message}");
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool>> WriteRegisterAsync(string address, short value)
        {
            if (State != PlcConnectionState.Connected)
            {
                return Result<bool>.Fail("PLC 未连接，无法写入寄存器");
            }

            await _ioSemaphore.WaitAsync();
            try
            {
                await _transport.WriteShortAsync(address, value);
                return Result<bool>.OK(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"写入寄存器 {address} 失败：{ex.Message}");
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }

        /// <summary>
        /// 自动连接主循环：连接成功后进入连接保持段（管理心跳启停），心跳丢失则断开重连
        /// </summary>
        private async Task RunLoopAsync(CancellationToken token)
        {
            int attempt = 0;
            while (!token.IsCancellationRequested)
            {
                SetState(PlcConnectionState.Connecting, "PLC 连接中…");

                try
                {
                    await _transport.ConnectAsync();
                }
                catch (Exception ex)
                {
                    attempt++;
                    SetState(PlcConnectionState.Disconnected,
                        $"PLC 连接失败（第 {attempt} 次）：{ex.Message}，{_reconnectDelayMs / 1000} 秒后自动重试");
                    if (!await SafeDelayAsync(_reconnectDelayMs, token))
                    {
                        return;
                    }

                    continue;
                }

                attempt = 0;
                SetState(PlcConnectionState.Connected, "PLC 已连接，心跳已启动");
                _heartbeatRequested = true;

                while (!token.IsCancellationRequested)
                {
                    // 心跳/物料轮询异常结束（PLC 停滞/通讯异常）→ 断开重连
                    if (_heartbeatFailed || _materialFailed)
                    {
                        var isHeartbeatLost = _heartbeatFailed;
                        var reason = isHeartbeatLost ? _heartbeatFailureMessage : _materialFailureMessage;

                        // 先取消并等待心跳/物料任务退出，防止旧任务在重连后报失败造成误断开
                        await StopCyclesAsync();
                        _heartbeatFailed = false;
                        _materialFailed = false;

                        if (isHeartbeatLost)
                        {
                            SetState(PlcConnectionState.HeartbeatLost, $"PLC 心跳丢失：{reason}，即将断开重连");
                        }
                        else
                        {
                            SetState(PlcConnectionState.Disconnected, $"PLC 抽屉物料读取失败（{_materialAddress}）：{reason}，即将断开重连");
                        }

                        await _transport.CloseAsync();
                        if (!await SafeDelayAsync(_reconnectDelayMs, token))
                        {
                            return;
                        }

                        break;
                    }

                    // 心跳期望开启且未运行 → 拉起
                    if (_heartbeatRequested && (_heartbeatTask is null || _heartbeatTask.IsCompleted))
                    {
                        StartHeartbeatCore();
                    }

                    // 物料轮询未运行 → 拉起（连续一直读取）
                    if (_materialTask is null || _materialTask.IsCompleted)
                    {
                        StartMaterialPollCore();
                    }

                    await SafeDelayAsync(LoopPollIntervalMs, token);
                }
            }
        }

        /// <summary>
        /// 拉起心跳循环（连接保持段内调用，保证同一时刻至多一个心跳任务）
        /// </summary>
        private void StartHeartbeatCore()
        {
            _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(_serviceCts?.Token ?? CancellationToken.None);
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
        }

        /// <summary>
        /// 拉起物料轮询循环（连接保持段内调用，保证同一时刻至多一个轮询任务）
        /// </summary>
        private void StartMaterialPollCore()
        {
            _materialCts = CancellationTokenSource.CreateLinkedTokenSource(_serviceCts?.Token ?? CancellationToken.None);
            _materialTask = Task.Run(() => MaterialPollLoopAsync(_materialCts.Token));
        }

        /// <summary>
        /// 取消并等待心跳/物料循环退出（断开重连与服务停止时调用），清空任务引用防旧任务误报
        /// </summary>
        private async Task StopCyclesAsync()
        {
            _heartbeatCts?.Cancel();
            _materialCts?.Cancel();

            foreach (var task in new[] { _heartbeatTask, _materialTask })
            {
                if (task is null)
                {
                    continue;
                }

                try
                {
                    await task;
                }
                catch
                {
                    // 取消过程中的收发超时异常不影响关闭
                }
            }

            _heartbeatTask = null;
            _materialTask = null;
            _heartbeatCts = null;
            _materialCts = null;
        }

        /// <summary>
        /// 抽屉物料轮询循环：连续读取物料位区（M1000 起 19 个 bool，下标 0 不使用，1~18 对应抽屉 1~18），
        /// 与上次快照按抽屉位比对，有变化才触发事件；首读即推送（用 PLC 真值覆盖初始状态）；
        /// 读取异常时置失败标志退出（由连接循环处理断开重连）
        /// </summary>
        private async Task MaterialPollLoopAsync(CancellationToken token)
        {
            bool[]? last = null;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    bool[] values;
                    await _ioSemaphore.WaitAsync(token);
                    try
                    {
                        values = await _transport.ReadBoolsAsync(_materialAddress, _materialLength);
                    }
                    finally
                    {
                        _ioSemaphore.Release();
                    }

                    if (last is null || HasDrawerChange(last, values))
                    {
                        RaiseMaterialsChanged(values);
                    }

                    last = values;

                    if (!await SafeDelayAsync(_materialPollPeriodMs, token))
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 断开重连或服务停止，正常退出
            }
            catch (Exception ex)
            {
                _materialFailureMessage = ex.Message;
                _materialFailed = true;
            }
        }

        /// <summary>
        /// 比较两轮物料快照是否在抽屉位（下标 1 起）上有差异；下标 0 非抽屉位，不参与判定
        /// </summary>
        private static bool HasDrawerChange(bool[] previous, bool[] current)
        {
            if (previous.Length != current.Length)
            {
                return true;
            }

            for (int i = 1; i < current.Length; i++)
            {
                if (previous[i] != current[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 触发抽屉物料变化事件（后台线程触发，订阅方负责调度 UI 线程；订阅方异常不影响轮询）
        /// </summary>
        private void RaiseMaterialsChanged(bool[] values)
        {
            try
            {
                DrawerMaterialsChanged?.Invoke(this, new DrawerMaterialsChangedEventArgs { Values = values });
            }
            catch
            {
                // 事件订阅方异常不中断轮询循环
            }
        }

        /// <summary>
        /// 双向心跳循环：写递增值到写心跳寄存器 + 读读心跳寄存器监测变化，
        /// PLC 侧连续停滞达到阈值或通讯异常时置 _heartbeatFailed 退出（由连接循环处理重连）
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            ushort counter = 0;
            short? lastPlcValue = null;
            int missed = 0;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await _ioSemaphore.WaitAsync(token);
                    try
                    {
                        // 写心跳：PC 侧计数递增，证明 PC 在线
                        counter = counter >= HeartbeatCounterMax ? (ushort)1 : (ushort)(counter + 1);
                        await _transport.WriteShortAsync(_writeAddress, (short)counter);

                        // 读心跳：监测 PLC 侧寄存器变化，证明 PLC 在线
                        var plcValue = await _transport.ReadShortAsync(_readAddress);
                        if (lastPlcValue.HasValue && plcValue == lastPlcValue.Value)
                        {
                            missed++;
                        }
                        else
                        {
                            missed = 0;
                        }

                        lastPlcValue = plcValue;

                        if (missed >= _maxMissedCycles)
                        {
                            _heartbeatFailureMessage = $"读心跳寄存器 {_readAddress} 连续 {_maxMissedCycles} 个周期无变化";
                            _heartbeatFailed = true;
                            return;
                        }
                    }
                    finally
                    {
                        _ioSemaphore.Release();
                    }

                    if (!await SafeDelayAsync(_heartbeatPeriodMs, token))
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 手动关闭心跳或服务停止，正常退出
            }
            catch (Exception ex)
            {
                _heartbeatFailureMessage = ex.Message;
                _heartbeatFailed = true;
            }
        }

        /// <summary>
        /// 状态变更并触发事件（后台线程触发，订阅方负责调度 UI 线程；订阅方异常不影响服务运行）
        /// </summary>
        private void SetState(PlcConnectionState state, string message)
        {
            State = state;
            try
            {
                ConnectionStateChanged?.Invoke(this, new PlcConnectionEventArgs { State = state, Message = message });
            }
            catch
            {
                // 事件订阅方异常不中断连接循环
            }
        }

        /// <summary>
        /// 可取消延时：被取消返回 false（调用方据此退出循环），不抛异常
        /// </summary>
        private static async Task<bool> SafeDelayAsync(int milliseconds, CancellationToken token)
        {
            try
            {
                await Task.Delay(milliseconds, token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
