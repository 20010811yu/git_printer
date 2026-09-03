using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UiTopMachine.Common;
using UiTopMachine.Services;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// ZPL 打印服务测试（v1.8）：
    /// ZPL 指令生成 5 种码型（含源码 Code128 笔误修正回归）/ 流水号校验与持久化 /
    /// 打印页 VM 打印流程（打印桩模拟成功/失败，零真实网络依赖）
    /// </summary>
    public class ZplPrinterServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _serialFilePath;

        public ZplPrinterServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"UiTopMachinePrintTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _serialFilePath = Path.Combine(_tempDir, "SerialNumber.txt");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // 文件句柄释放延迟：忽略清理失败
            }
        }

        /// <summary>构造流水号文件隔离的打印服务</summary>
        private ZplPrinterService CreateService()
            => new(serialFilePath: _serialFilePath);

        // ══════════════ ZPL 指令生成（5 种码型） ══════════════

        [Fact]
        public void 二维码ZPL_标签240点_含QA前缀数据()
        {
            var service = CreateService();

            var zpl = service.CreateQrCodeZpl("000001");

            Assert.StartsWith("^XA", zpl);              // 指令以 ^XA 开始
            Assert.EndsWith("^XZ", zpl);                // 以 ^XZ 结束
            Assert.Contains("^PW240", zpl);             // 标签宽度 240 点（30mm）
            Assert.Contains("^LL240", zpl);             // 标签高度 240 点
            Assert.Contains("^BQN,2,6", zpl);           // QR Code 指令
            Assert.Contains("^FDQA,000001", zpl);       // QA 前缀 + 流水号
        }

        [Fact]
        public void Code39条码ZPL_含B3指令与流水号()
        {
            var service = CreateService();

            var zpl = service.CreateCode39Zpl("123456");

            Assert.StartsWith("^XA", zpl);
            Assert.EndsWith("^XZ", zpl);
            Assert.Contains("^B3N,N,2,Y,N", zpl);       // Code 39 指令
            Assert.Contains("^FD123456", zpl);
        }

        [Fact]
        public void Code128条码ZPL_LL指令带尖号_源码笔误修正回归()
        {
            var service = CreateService();

            var zpl = service.CreateCode128Zpl("654321");

            Assert.StartsWith("^XA", zpl);
            Assert.EndsWith("^XZ", zpl);
            Assert.Contains("^BCN,60,Y,N,N", zpl);      // Code 128 指令
            Assert.Contains("^LL300", zpl);             // ⚠️ 源码笔误 "LL300"（缺 ^）已修正
            Assert.DoesNotContain("LL300^", zpl.Replace("^LL300", "")); // 不存在残留的裸 "LL300"
            Assert.Contains("^FD654321", zpl);
        }

        [Fact]
        public void PDF417条码ZPL_含B7指令()
        {
            var service = CreateService();

            var zpl = service.CreatePdf417Zpl("111222");

            Assert.StartsWith("^XA", zpl);
            Assert.EndsWith("^XZ", zpl);
            Assert.Contains("^B7N,8,5,7,21,N", zpl);    // PDF417 指令
            Assert.Contains("^FD111222", zpl);
        }

        [Fact]
        public void 数字文本ZPL_含AO字体指令()
        {
            var service = CreateService();

            var zpl = service.CreateNumberZpl("333444");

            Assert.StartsWith("^XA", zpl);
            Assert.EndsWith("^XZ", zpl);
            Assert.Contains("^AON,60,60", zpl);         // 数字文本字体
            Assert.Contains("^FD333444", zpl);
        }

        [Fact]
        public void GenerateZpl_按码型分发到对应生成方法()
        {
            var service = CreateService();

            Assert.Equal(service.CreateQrCodeZpl("X"), service.GenerateZpl(ZplCodeType.QRCode, "X"));
            Assert.Equal(service.CreateCode39Zpl("X"), service.GenerateZpl(ZplCodeType.Code39, "X"));
            Assert.Equal(service.CreateCode128Zpl("X"), service.GenerateZpl(ZplCodeType.Code128, "X"));
            Assert.Equal(service.CreatePdf417Zpl("X"), service.GenerateZpl(ZplCodeType.Pdf417, "X"));
            Assert.Equal(service.CreateNumberZpl("X"), service.GenerateZpl(ZplCodeType.Number, "X"));
        }

        // ══════════════ 流水号校验 ══════════════

        [Theory]
        [InlineData("000001", true)]
        [InlineData("999999", true)]
        [InlineData("", false)]        // 空
        [InlineData("   ", false)]     // 空白
        [InlineData("12a456", false)]  // 含字母
        [InlineData("12 456", false)]  // 含空格
        [InlineData("12-456", false)]  // 含符号
        public void 流水号校验_非空且纯数字(string serial, bool expected)
        {
            var service = CreateService();

            Assert.Equal(expected, service.IsValidSerial(serial));
        }

        // ══════════════ 流水号持久化 ══════════════

        [Fact]
        public async Task 流水号加载_文件不存在_返回默认000001()
        {
            var service = CreateService();

            var result = await service.LoadSerialAsync();

            Assert.True(result.Success);
            Assert.Equal("000001", result.Data);
        }

        [Fact]
        public async Task 流水号保存加载往返_补零6位()
        {
            var service = CreateService();

            await service.SaveSerialAsync("42");       // 不足 6 位
            var loaded = await service.LoadSerialAsync();

            Assert.True(loaded.Success);
            Assert.Equal("000042", loaded.Data);       // 读取时补零
            Assert.True(File.Exists(_serialFilePath)); // 持久化文件真实存在
        }

        [Fact]
        public async Task 流水号保存_含非法字符_拒绝()
        {
            var service = CreateService();

            var result = await service.SaveSerialAsync("12x");

            Assert.False(result.Success);
            Assert.False(File.Exists(_serialFilePath)); // 非法值不落盘
        }

        // ══════════════ 打印页 VM 流程（打印桩模拟，零网络依赖） ══════════════

        /// <summary>打印桩：记录收到的 ZPL，成功/失败可控</summary>
        private class StubPrintService : IPrintService
        {
            public System.Collections.Generic.List<string> SentZpl { get; } = new();
            public int FailFromIndex { get; set; } = int.MaxValue; // 从第 N 张（0 基序号）起失败
            public string? StubSerial { get; set; }                // 桩返回的流水号（null = 默认 000001）

            public Task<Result<bool>> PrintByIpAsync(string zpl)
                => Task.FromResult(Result<bool>.OK(true)); // 备用通道：生产代码当前不走此路径，不做记录

            public Task<Result<bool>> PrintBySpoolerAsync(string zpl)
            {
                if (SentZpl.Count >= FailFromIndex)
                {
                    return Task.FromResult(Result<bool>.Fail("模拟打印失败"));
                }
                SentZpl.Add(zpl);
                return Task.FromResult(Result<bool>.OK(true));
            }

            public string GenerateZpl(ZplCodeType codeType, string serialNumber)
                => $"[ZPL:{codeType}:{serialNumber}]";

            public string CreateQrCodeZpl(string s) => GenerateZpl(ZplCodeType.QRCode, s);
            public string CreateCode39Zpl(string s) => GenerateZpl(ZplCodeType.Code39, s);
            public string CreateCode128Zpl(string s) => GenerateZpl(ZplCodeType.Code128, s);
            public string CreatePdf417Zpl(string s) => GenerateZpl(ZplCodeType.Pdf417, s);
            public string CreateNumberZpl(string s) => GenerateZpl(ZplCodeType.Number, s);

            public bool IsValidSerial(string serialNumber)
                => !string.IsNullOrWhiteSpace(serialNumber) && serialNumber.All(char.IsDigit);

            public Task<Result<string>> LoadSerialAsync()
                => Task.FromResult(Result<string>.OK(StubSerial ?? "000001"));

            public Task<Result<bool>> SaveSerialAsync(string serialNumber)
                => Task.FromResult(Result<bool>.OK(true));
        }

        [Fact]
        public async Task VM打印_单张_流水号自动加1()
        {
            var stub = new StubPrintService();
            var vm = new PrintPageViewModel(stub, new StubLogService());
            await vm.InitializeAsync();
            Assert.Equal("000001", vm.CurrentSerial);

            vm.PrintCommand.Execute(null);
            while (vm.IsBusy)
            {
                await Task.Delay(10);
            }

            var sent = Assert.Single(stub.SentZpl);
            Assert.Contains("000001", sent);
            Assert.Equal("000002", vm.CurrentSerial); // 自动递增
            Assert.Equal(1, vm.PrintCount);
        }

        [Fact]
        public async Task VM打印_多张_流水号连续递增()
        {
            var stub = new StubPrintService();
            var vm = new PrintPageViewModel(stub, new StubLogService());
            await vm.InitializeAsync();
            vm.Quantity = 3;

            vm.PrintCommand.Execute(null);
            while (vm.IsBusy)
            {
                await Task.Delay(10);
            }

            Assert.Equal(3, stub.SentZpl.Count);
            Assert.Contains("000001", stub.SentZpl[0]);
            Assert.Contains("000002", stub.SentZpl[1]);
            Assert.Contains("000003", stub.SentZpl[2]);
            Assert.Equal("000004", vm.CurrentSerial); // +3
        }

        [Fact]
        public async Task VM打印_中途失败_流水号不前进_弹窗提示()
        {
            var stub = new StubPrintService { FailFromIndex = 1 }; // 第 2 张失败（0 基序号 1）
            var vm = new PrintPageViewModel(stub, new StubLogService());
            await vm.InitializeAsync();
            vm.Quantity = 3;

            MessageRequestEventArgs? messageRequest = null;
            vm.MessageRequested += (_, e) => messageRequest = e;

            vm.PrintCommand.Execute(null);
            while (vm.IsBusy)
            {
                await Task.Delay(10);
            }

            Assert.Single(stub.SentZpl);                  // 只打印出 1 张
            Assert.Equal("000001", vm.CurrentSerial);     // 流水号不前进（避免跳号）
            Assert.NotNull(messageRequest);               // 弹窗告知
            Assert.Contains("失败", messageRequest!.Message);
        }

        [Fact]
        public async Task VM打印_流水号非法_拒绝打印并弹窗()
        {
            var stub = new StubPrintService { StubSerial = "12A" }; // 非法
            var vm = new PrintPageViewModel(stub, new StubLogService());
            await vm.InitializeAsync();

            MessageRequestEventArgs? messageRequest = null;
            vm.MessageRequested += (_, e) => messageRequest = e;

            vm.PrintCommand.Execute(null);
            while (vm.IsBusy)
            {
                await Task.Delay(10);
            }

            Assert.Empty(stub.SentZpl);                   // 未发送任何打印
            Assert.NotNull(messageRequest);
            Assert.Contains("非法", messageRequest!.Message);
        }

        [Fact]
        public async Task VM初始化_无持久化文件_流水号默认000001()
        {
            var stub = new StubPrintService();
            var vm = new PrintPageViewModel(stub, new StubLogService());

            await vm.InitializeAsync();

            Assert.Equal("000001", vm.CurrentSerial);
        }

        [Fact]
        public async Task VM打印_自定义内容_每张打印输入_流水号不变()
        {
            var stub = new StubPrintService();
            var vm = new PrintPageViewModel(stub, new StubLogService());
            await vm.InitializeAsync();
            vm.CustomContent = "ABC123";
            vm.Quantity = 2;

            vm.PrintCommand.Execute(null);
            while (vm.IsBusy)
            {
                await Task.Delay(10);
            }

            Assert.Equal(2, stub.SentZpl.Count);
            Assert.Contains("ABC123", stub.SentZpl[0]);   // 每张都是用户输入的内容
            Assert.Contains("ABC123", stub.SentZpl[1]);
            Assert.Equal("000001", vm.CurrentSerial);     // 流水号不递增
            Assert.Equal(2, vm.PrintCount);
        }

        [Fact]
        public async Task VM打印_自定义内容为空白_回退流水号()
        {
            var stub = new StubPrintService();
            var vm = new PrintPageViewModel(stub, new StubLogService());
            await vm.InitializeAsync();
            vm.CustomContent = "   "; // 纯空白视为未输入

            vm.PrintCommand.Execute(null);
            while (vm.IsBusy)
            {
                await Task.Delay(10);
            }

            var sent = Assert.Single(stub.SentZpl);
            Assert.Contains("000001", sent);              // 走流水号路径
            Assert.Equal("000002", vm.CurrentSerial);     // 流水号正常递增
        }

        [Fact]
        public async Task VM打印_自定义内容优先_非法流水号不拦截()
        {
            var stub = new StubPrintService { StubSerial = "12A" }; // 流水号非法
            var vm = new PrintPageViewModel(stub, new StubLogService());
            await vm.InitializeAsync();
            vm.CustomContent = "XYZ789";

            MessageRequestEventArgs? messageRequest = null;
            vm.MessageRequested += (_, e) => messageRequest = e;

            vm.PrintCommand.Execute(null);
            while (vm.IsBusy)
            {
                await Task.Delay(10);
            }

            var sent = Assert.Single(stub.SentZpl);
            Assert.Contains("XYZ789", sent);              // 自定义内容正常打印
            Assert.Null(messageRequest);                  // 不触发流水号校验弹窗
        }
    }
}