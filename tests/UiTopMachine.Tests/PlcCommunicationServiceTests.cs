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
    /// PLC 通讯服务测试：自动连接、双向心跳启停、心跳丢失告警与重连、幂等
    /// </summary>
    public class PlcCommunicationServiceTests
    {
        /// <summary>缩短等待的心跳参数（覆盖构造默认值；地址格式与生产一致为汇川软元件格式）</summary>
        private const string WriteAddress = "D100";
        private const string ReadAddress = "D101";
        private const int HeartbeatPeriodMs = 20;
        private const int MaxMissedCycles = 2;
        private const int ReconnectDelayMs = 20;
        private const string MaterialAddress = "M1000";
        private const ushort MaterialLength = 19;
        private const int MaterialPollPeriodMs = 20;

        private static PlcCommunicationService CreateService(FakePlcTransport transport, bool monitorPlcAlive = false) =>
            new(transport,
                target: "127.0.0.1:502 站号1",
                writeAddress: WriteAddress,
                readAddress: ReadAddress,
                heartbeatPeriodMs: HeartbeatPeriodMs,
                maxMissedCycles: MaxMissedCycles,
                reconnectDelayMs: ReconnectDelayMs,
                materialAddress: MaterialAddress,
                materialLength: MaterialLength,
                materialPollPeriodMs: MaterialPollPeriodMs,
                monitorPlcAlive: monitorPlcAlive);

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

            await service.StopAsync();
        }

        [Fact]
        public async Task 连接后_自动连续读取物料位区_地址与长度正确()
        {
            var transport = new FakePlcTransport();
            var service = CreateService(transport);

            await service.StartAsync();

            // 连接成功后应自动开始连续读取（多轮轮询）
            await WaitUntilAsync(() =>
            {
                lock (transport.BitReads)
                {
                    return transport.BitReads.Count >= 3;
                }
            });

            List<(string Address, ushort Length)> bitReads;
            lock (transport.BitReads)
            {
                bitReads = new List<(string, ushort)>(transport.BitReads);
            }

            Assert.All(bitReads, r =>
            {
                Assert.Equal(MaterialAddress, r.Address);
                Assert.Equal(MaterialLength, r.Length);
            });

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

            // 再等若干轮轮询，确认无变化时不重复触发
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
            await WaitUntilAsync(() =>
            {
                lock (transport.BitReads)
                {
                    return transport.BitReads.Count >= 5;
                }
            });
            await Task.Delay(100);

            Assert.Equal(1, eventCount);

            await service.StopAsync();
        }

        [Fact]
        public async Task 物料读取失败_报错误状态并断开重连()
        {
            var transport = new FakePlcTransport { BitReadShouldFail = true };
            var service = CreateService(transport);

            var disconnected = new TaskCompletionSource<PlcConnectionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.ConnectionStateChanged += (_, e) =>
            {
                if (e.State == PlcConnectionState.Disconnected && e.Message.Contains("物料读取失败"))
                {
                    disconnected.TrySetResult(e);
                }
            };

            await service.StartAsync();

            // 首轮位读取即失败：应报"物料读取失败"错误并自动重连
            var failure = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(MaterialAddress, failure.Message);

            await WaitUntilAsync(() => transport.ConnectCount >= 2);
            await WaitUntilAsync(() => service.State == PlcConnectionState.Connected);

            await service.StopAsync();
        }

        [Fact]
        public async Task PlcHeartbeatStall_RaisesHeartbeatLost_AndReconnects()
        {
            var transport = new FakePlcTransport { PlcEchoesHeartbeat = false, StaleHeartbeatValue = 0 };
            var service = CreateService(transport, monitorPlcAlive: true); // 双向心跳：开启 PLC 侧存活监测

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

            // 双向模式确实在读寄存器上监测
            lock (transport.Reads)
            {
                Assert.Contains(ReadAddress, transport.Reads);
            }

            await WaitUntilAsync(() => transport.ConnectCount >= 2);
            await WaitUntilAsync(() => service.State == PlcConnectionState.Connected);

            await service.StopAsync();
        }

        [Fact]
        public async Task 单向心跳_只写不读_PLC停滞不判丢失()
        {
            var transport = new FakePlcTransport { PlcEchoesHeartbeat = false, StaleHeartbeatValue = 0 };
            var service = CreateService(transport, monitorPlcAlive: false); // 单向：默认，取消双向

            var heartbeatLost = new TaskCompletionSource<PlcConnectionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.ConnectionStateChanged += (_, e) =>
            {
                if (e.State == PlcConnectionState.HeartbeatLost)
                {
                    heartbeatLost.TrySetResult(e);
                }
            };

            await service.StartAsync();

            // PLC 侧寄存器停滞也不应触发 HeartbeatLost：连接持续保持
            await WaitUntilAsync(() => WriteCount(transport) >= 8);
            Assert.False(heartbeatLost.Task.IsCompleted, "单向心跳不应判 PLC 心跳丢失");
            Assert.Equal(PlcConnectionState.Connected, service.State);
            Assert.Equal(1, transport.ConnectCount);

            // 不读读心跳寄存器（只写 D100）
            lock (transport.Reads)
            {
                Assert.DoesNotContain(ReadAddress, transport.Reads);
            }

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
