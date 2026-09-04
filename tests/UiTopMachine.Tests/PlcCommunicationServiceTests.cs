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
    /// 连接成功/失败可编程；记录全部读写；位读（心跳+物料合一）可编程返回值与失败注入
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

        /// <summary>位读取记录（地址, 长度）</summary>
        public List<(string Address, ushort Length)> BitReads { get; } = new();

        /// <summary>置 true 时 ReadBoolsAsync 抛异常（模拟位读取失败）</summary>
        public bool BitReadShouldFail { get; set; }

        /// <summary>位读取可编程返回（参数：地址, 长度；默认返回全 false 数组）</summary>
        public Func<string, ushort, bool[]>? BitReadHandler { get; set; }

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

        public Task<bool[]> ReadBoolsAsync(string address, ushort length)
        {
            lock (_lock)
            {
                BitReads.Add((address, length));
                if (BitReadShouldFail)
                {
                    throw new InvalidOperationException("模拟位读取失败");
                }

                return Task.FromResult(BitReadHandler is not null
                    ? BitReadHandler(address, length)
                    : new bool[length]);
            }
        }

        public Task CloseAsync()
        {
            Interlocked.Increment(ref _closeCount);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// PLC 通讯服务测试（v1.15 仿参考程序重构：心跳=读物料位区合一，连续失败计数重连）：
    /// 自动连接、心跳轮询、连续失败阈值重连、心跳启停、幂等
    /// </summary>
    public class PlcCommunicationServiceTests
    {
        /// <summary>缩短等待的心跳参数（覆盖构造默认值；地址格式与生产一致为汇川软元件格式）</summary>
        private const string MaterialAddress = "M1000";
        private const ushort MaterialLength = 19;
        private const int HeartbeatPeriodMs = 20;
        private const int MaxRetryCount = 3;
        private const int ReconnectDelayMs = 20;

        private static PlcCommunicationService CreateService(FakePlcTransport transport) =>
            new(transport,
                target: "127.0.0.1:502 站号1",
                heartbeatPeriodMs: HeartbeatPeriodMs,
                maxRetryCount: MaxRetryCount,
                reconnectDelayMs: ReconnectDelayMs,
                materialAddress: MaterialAddress,
                materialLength: MaterialLength);

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

        private static int BitReadCount(FakePlcTransport transport)
        {
            lock (transport.BitReads)
            {
                return transport.BitReads.Count;
            }
        }

        private static List<(string Address, ushort Length)> SnapshotBitReads(FakePlcTransport transport)
        {
            lock (transport.BitReads)
            {
                return new List<(string, ushort)>(transport.BitReads);
            }
        }

        [Fact]
        public async Task StartAsync_AutoConnects_AndStartsHeartbeatPolling()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            await service.StartAsync();

            // 等待连接成功并产生多轮心跳（读物料位区）
            await WaitUntilAsync(() => BitReadCount(transport) >= 3);

            Assert.Equal(PlcConnectionState.Connected, service.State);
            Assert.Equal(1, transport.ConnectCount);

            // 心跳与物料合一：只读物料位区，不再有心跳写
            Assert.All(SnapshotBitReads(transport), r =>
            {
                Assert.Equal(MaterialAddress, r.Address);
                Assert.Equal(MaterialLength, r.Length);
            });
            Assert.Empty(transport.Writes);

            await service.StopAsync();
        }

        [Fact]
        public async Task 心跳读失败_连续达到阈值_判丢失并重连()
        {
            var transport = new FakePlcTransport { BitReadShouldFail = true };
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

            // 连续 3 次读失败：应触发 HeartbeatLost（消息含连续次数）并自动断开重连
            var lost = await heartbeatLost.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains($"连续 {MaxRetryCount} 次失败", lost.Message);

            await WaitUntilAsync(() => transport.ConnectCount >= 2);
            await WaitUntilAsync(() => service.State == PlcConnectionState.Connected);

            await service.StopAsync();
        }

        [Fact]
        public async Task 心跳读失败_未达阈值_恢复后不断连()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            // 前 2 次（< 阈值 3）失败，之后恢复成功
            int readCount = 0;
            transport.BitReadHandler = (_, length) =>
            {
                var value = Interlocked.Increment(ref readCount);
                if (value <= 2)
                {
                    throw new InvalidOperationException($"前 2 次模拟失败（第 {value} 次）");
                }

                var values = new bool[length];
                values[5] = true;
                return values;
            };

            var heartbeatLost = new TaskCompletionSource<PlcConnectionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.ConnectionStateChanged += (_, e) =>
            {
                if (e.State == PlcConnectionState.HeartbeatLost)
                {
                    heartbeatLost.TrySetResult(e);
                }
            };

            await service.StartAsync();

            // 恢复成功且推送物料（下标 5 = 抽屉 5 有料），期间不应触发心跳丢失
            var events = new List<DrawerMaterialsChangedEventArgs>();
            var eventsLock = new object();
            service.DrawerMaterialsChanged += (_, e) =>
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            };

            await WaitUntilAsync(() =>
            {
                lock (eventsLock)
                {
                    return events.Count > 0 && events[^1].Values[5];
                }
            });

            Assert.False(heartbeatLost.Task.IsCompleted, "连续失败未达阈值且已恢复，不应判心跳丢失");
            Assert.Equal(PlcConnectionState.Connected, service.State);
            Assert.Equal(1, transport.ConnectCount);

            await service.StopAsync();
        }

        [Fact]
        public async Task 物料数组变化_触发事件且下标对应抽屉_无变化不重复触发()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            // 编程位读取：第 1 轮全 false；第 2 轮起下标 3 = true；之后恒定
            int readCount = 0;
            transport.BitReadHandler = (_, length) =>
            {
                var value = Interlocked.Increment(ref readCount);
                var values = new bool[length];
                if (value >= 2)
                {
                    values[3] = true;
                }

                return values;
            };

            var events = new List<DrawerMaterialsChangedEventArgs>();
            var eventsLock = new object();
            service.DrawerMaterialsChanged += (_, e) =>
            {
                lock (eventsLock)
                {
                    events.Add(e);
                }
            };

            await service.StartAsync();

            // 期望：轮 1 首读推送基线；轮 2 下标 3 变化推送；之后无变化不再推送
            await WaitUntilAsync(() =>
            {
                lock (eventsLock)
                {
                    return events.Count >= 2;
                }
            });

            lock (eventsLock)
            {
                Assert.False(events[0].Values[3]); // 首读基线：下标 3 为 false
                Assert.True(events[1].Values[3]);  // 变化后：下标 3 为 true（对应抽屉 3 有料）
                Assert.Equal(MaterialLength, events[1].Values.Count);
            }

            // 再等若干轮，确认无变化时不重复触发
            await Task.Delay(200);
            lock (eventsLock)
            {
                Assert.Equal(2, events.Count);
            }

            await service.StopAsync();
        }

        [Fact]
        public async Task 物料下标0变化_不触发事件()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            // 下标 0 非抽屉位：每轮强制翻转，抽屉位恒 false
            int bitReadCount = 0;
            transport.BitReadHandler = (_, length) =>
            {
                var value = Interlocked.Increment(ref bitReadCount);
                var values = new bool[length];
                values[0] = value % 2 == 0;
                return values;
            };

            var eventCount = 0;
            service.DrawerMaterialsChanged += (_, _) => Interlocked.Increment(ref eventCount);

            await service.StartAsync();

            // 跑若干轮后：仅首读推送 1 次，后续下标 0 变化不应触发
            await WaitUntilAsync(() => BitReadCount(transport) >= 5);
            await Task.Delay(100);

            Assert.Equal(1, eventCount);

            await service.StopAsync();
        }

        [Fact]
        public async Task StopHeartbeatAsync_StopsReads_ButKeepsConnection()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            await service.StartAsync();
            await WaitUntilAsync(() => BitReadCount(transport) >= 3);

            var stopResult = await service.StopHeartbeatAsync();
            Assert.True(stopResult.Success);
            Assert.Equal(PlcConnectionState.Connected, service.State);

            // 心跳关闭后不再读寄存器（StopHeartbeatAsync 已等待心跳任务退出，此处计数应冻结）
            var frozenCount = BitReadCount(transport);
            await Task.Delay(300);
            Assert.Equal(frozenCount, BitReadCount(transport));

            // 手动重启心跳恢复读取
            var restartResult = await service.StartHeartbeatAsync();
            Assert.True(restartResult.Success);
            await WaitUntilAsync(() => BitReadCount(transport) > frozenCount);

            await service.StopAsync();
        }

        [Fact]
        public async Task StopAsync_ClosesConnection_AndStopsHeartbeat()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            await service.StartAsync();
            await WaitUntilAsync(() => BitReadCount(transport) >= 3);

            await service.StopAsync();

            Assert.Equal(1, transport.CloseCount);
            Assert.Equal(PlcConnectionState.Disconnected, service.State);

            // 停止后心跳读取冻结
            var frozenCount = BitReadCount(transport);
            await Task.Delay(300);
            Assert.Equal(frozenCount, BitReadCount(transport));
        }

        [Fact]
        public async Task StartAsync_IsIdempotent_OnlyOneConnectLoop()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            await service.StartAsync();
            await service.StartAsync(); // 重复调用应被忽略

            await WaitUntilAsync(() => BitReadCount(transport) >= 3);

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

            var readResult = await service.ReadRegisterAsync("D200");
            var writeResult = await service.WriteRegisterAsync("D200", 5);

            Assert.False(readResult.Success);
            Assert.False(writeResult.Success);
            Assert.Equal(0, transport.ConnectCount);
        }
    }
}
