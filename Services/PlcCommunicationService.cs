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

        /// <summary>Modbus IO 串行化锁：心跳与业务读写共用一条长连接，必须排队（防回归清单 #3 精神）</summary>
        private readonly SemaphoreSlim _ioSemaphore = new(1, 1);

        private CancellationTokenSource? _serviceCts;
        private Task? _runTask;
        private Task? _heartbeatTask;
        private CancellationTokenSource? _heartbeatCts;

        /// <summary>心跳期望状态（手动关闭后置 false，连接循环不再拉起）</summary>
        private volatile bool _heartbeatRequested;

        /// <summary>心跳异常结束标志（PLC 停滞或通讯异常，由连接循环消费并触发重连）</summary>
        private volatile bool _heartbeatFailed;

        private string _heartbeatFailureMessage = string.Empty;

        /// <inheritdoc />
        public event EventHandler<PlcConnectionEventArgs>? ConnectionStateChanged;

        /// <inheritdoc />
        public PlcConnectionState State { get; private set; } = PlcConnectionState.Disconnected;

        /// <summary>
        /// 构造：注入传输层与心跳参数（参数默认值可被测试覆盖以缩短等待）
        /// </summary>
        public PlcCommunicationService(
            IPlcTransport transport,
            string writeAddress = "100",
            string readAddress = "101",
            int heartbeatPeriodMs = 1000,
            int maxMissedCycles = 5,
            int reconnectDelayMs = 5000)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _writeAddress = writeAddress;
            _readAddress = readAddress;
            _heartbeatPeriodMs = heartbeatPeriodMs;
            _maxMissedCycles = maxMissedCycles;
            _reconnectDelayMs = reconnectDelayMs;
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
            _heartbeatCts?.Cancel();

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
            _heartbeatTask = null;

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
                    // 心跳异常结束（PLC 停滞/通讯异常）→ 断开重连
                    if (_heartbeatFailed)
                    {
                        _heartbeatFailed = false;
                        SetState(PlcConnectionState.HeartbeatLost, $"PLC 心跳丢失：{_heartbeatFailureMessage}，即将断开重连");
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
