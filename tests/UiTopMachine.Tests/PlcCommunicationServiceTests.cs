using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Communications.Plc;
using UiTopMachine.Services;
using UiTopMachine.Services.Interfaces;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 测试桩：IPlcTransport 假实现 —— 模拟 PLC 行为供服务层测试：
    /// 连接成功/失败可编程；记录全部读写；读心跳寄存器可编程返回
    /// （回显最近写入值 = PLC 活着；固定不变 = PLC 心跳停滞）
    /// </summary>
    public class FakePlcTransport : IPlcTransport
    {
        private readonly object _lock = new();
        private int _connectCount;
        private int _closeCount;

        /// <summary>置 true 时 ConnectAsync 抛异常（模拟连接失败）</summary>
        public bool ConnectShouldFail { get; set; }

        public int ConnectCount { get => Volatile.Read(ref _connectCount); }

        public int CloseCount { get => Volatile.Read(ref _closeCount); }

        /// <summary>true=读寄存器回显最近写入值（模拟 PLC 心跳活着）；false=读固定值（模拟 PLC 停滞）</summary>
        public bool PlcEchoesHeartbeat { get; set; } = true;

        /// <summary>PLC 停滞时读心跳寄存器的固定返回值</summary>
        public short StaleHeartbeatValue { get; set; }

        /// <summary>写入记录（地址, 值）</summary>
        public List<(string Address, short Value)> Writes { get; } = new();

        /// <summary>读取记录（地址）</summary>
        public List<string> Reads { get; } = new();

        public Task ConnectAsync()
        {
            if (ConnectShouldFail)
            {
                throw new InvalidOperationException("模拟连接失败");
            }

            Interlocked.Increment(ref _connectCount);
            return Task.CompletedTask;
        }

        public Task<short> ReadShortAsync(string address)
        {
            lock (_lock)
            {
                Reads.Add(address);
                return Task.FromResult(PlcEchoesHeartbeat
                    ? (Writes.Count > 0 ? Writes[^1].Value : (short)0)
                    : StaleHeartbeatValue);
            }
        }

        public Task WriteShortAsync(string address, short value)
        {
            lock (_lock)
            {
                Writes.Add((address, value));
            }

            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            Interlocked.Increment(ref _closeCount);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// PLC 通讯服务测试：自动连接、双向心跳启停、心跳丢失告警与重连、幂等
    /// </summary>
    public class PlcCommunicationServiceTests
    {
        /// <summary>缩短等待的心跳参数（覆盖构造默认值）</summary>
        private const string WriteAddress = "100";
        private const string ReadAddress = "101";
        private const int HeartbeatPeriodMs = 20;
        private const int MaxMissedCycles = 2;
        private const int ReconnectDelayMs = 20;

        private static PlcCommunicationService CreateService(FakePlcTransport transport) =>
            new(transport, WriteAddress, ReadAddress, HeartbeatPeriodMs, MaxMissedCycles, ReconnectDelayMs);

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(20);
            }

            Assert.True(condition(), "等待条件超时");
        }

        private static int WriteCount(FakePlcTransport transport)
        {
            lock (transport.Writes)
            {
                return transport.Writes.Count;
            }
        }

        [Fact]
        public async Task StartAsync_AutoConnects_AndStartsIncrementalHeartbeat()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            await service.StartAsync();

            // 等待连接成功并产生多次心跳写
            await WaitUntilAsync(() => WriteCount(transport) >= 3);

            Assert.Equal(PlcConnectionState.Connected, service.State);
            Assert.Equal(1, transport.ConnectCount);

            List<(string Address, short Value)> writes;
            lock (transport.Writes)
            {
                writes = new List<(string, short)>(transport.Writes);
            }

            // 写心跳：全部写到写寄存器，且计数严格递增（证明 PC 在线）
            Assert.All(writes, w => Assert.Equal(WriteAddress, w.Address));
            for (int i = 1; i < writes.Count; i++)
            {
                Assert.True(writes[i].Value > writes[i - 1].Value, $"心跳计数值应递增：{writes[i - 1].Value} -> {writes[i].Value}");
            }

            // 读心跳：确实在读寄存器上监测（证明 PLC 在线的监听通道存在）
            lock (transport.Reads)
            {
                Assert.Contains(ReadAddress, transport.Reads);
            }

            await service.StopAsync();
        }

        [Fact]
        public async Task PlcHeartbeatStall_RaisesHeartbeatLost_AndReconnects()
        {
            var transport = new FakePlcTransport { PlcEchoesHeartbeat = false, StaleHeartbeatValue = 0 };
            var service = CreateService(transport);

            var heartbeatLost = new TaskCompletionSource<PlcConnectionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.ConnectionStateChanged += (_, e) =>
            {
                if (e.State == PlcConnectionState.HeartbeatLost)
                {
                    heartbeatLost.TrySetResult(e);
                }
            };

            await service.StartAsync();

            // PLC 侧寄存器停滞：应触发 HeartbeatLost 事件并自动断开重连
            var lost = await heartbeatLost.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(ReadAddress, lost.Message);

            await WaitUntilAsync(() => transport.ConnectCount >= 2);
            await WaitUntilAsync(() => service.State == PlcConnectionState.Connected);

            await service.StopAsync();
        }

        [Fact]
        public async Task StopHeartbeatAsync_StopsWrites_ButKeepsConnection()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            await service.StartAsync();
            await WaitUntilAsync(() => WriteCount(transport) >= 3);

            var stopResult = await service.StopHeartbeatAsync();
            Assert.True(stopResult.Success);
            Assert.Equal(PlcConnectionState.Connected, service.State);

            // 心跳关闭后不再写寄存器（StopHeartbeatAsync 已等待心跳任务退出，此处计数应冻结）
            var frozenCount = WriteCount(transport);
            await Task.Delay(300);
            Assert.Equal(frozenCount, WriteCount(transport));

            // 手动重启心跳恢复写入
            var restartResult = await service.StartHeartbeatAsync();
            Assert.True(restartResult.Success);
            await WaitUntilAsync(() => WriteCount(transport) > frozenCount);

            await service.StopAsync();
        }

        [Fact]
        public async Task StopAsync_ClosesConnection_AndStopsHeartbeat()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            await service.StartAsync();
            await WaitUntilAsync(() => WriteCount(transport) >= 3);

            await service.StopAsync();

            Assert.Equal(1, transport.CloseCount);
            Assert.Equal(PlcConnectionState.Disconnected, service.State);

            // 停止后心跳写入冻结
            var frozenCount = WriteCount(transport);
            await Task.Delay(300);
            Assert.Equal(frozenCount, WriteCount(transport));
        }

        [Fact]
        public async Task StartAsync_IsIdempotent_OnlyOneConnectLoop()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            await service.StartAsync();
            await service.StartAsync(); // 重复调用应被忽略

            await WaitUntilAsync(() => WriteCount(transport) >= 3);

            // 若存在两个连接循环，会各自发起连接，ConnectCount 将大于 1
            Assert.Equal(1, transport.ConnectCount);

            await service.StopAsync();
        }

        [Fact]
        public async Task StartHeartbeatAsync_Fails_WhenNotConnected()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            var result = await service.StartHeartbeatAsync();

            Assert.False(result.Success);
            Assert.Contains("未连接", result.ErrorMessage);
        }

        [Fact]
        public async Task ReadWriteRegister_Fails_WhenNotConnected()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            var readResult = await service.ReadRegisterAsync("200");
            var writeResult = await service.WriteRegisterAsync("200", 5);

            Assert.False(readResult.Success);
            Assert.False(writeResult.Success);
            Assert.Equal(0, transport.ConnectCount);
        }
    }
}
