using System;
using System.Text;
using UiTopMachine.Common.VmBridge;
using UiTopMachine.Services;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 视觉桥接协议编解码测试（v1.24）：
    /// 帧编解码往返、响应构建/解析往返（含中文/空数据/PNG 边界），
    /// 主程序与桥接进程共享同一份源码，协议一致性由这里锁死
    /// </summary>
    public class VmBridgeProtocolTests
    {
        // ══════════════ 帧编解码 ══════════════

        [Fact]
        public void 帧编码_头部结构与payload一致()
        {
            var payload = VmBridgeProtocol.EncodeRequestText("D:\\test\\方案.sol");

            var frame = VmBridgeProtocol.EncodeFrame(VmBridgeProtocol.Command.Load, payload);

            Assert.Equal(VmBridgeProtocol.Command.Load, frame[0]);
            int length = frame[1] | (frame[2] << 8) | (frame[3] << 16) | (frame[4] << 24);
            Assert.Equal(payload.Length, length);
            Assert.Equal(VmBridgeProtocol.HeaderLength + payload.Length, frame.Length);
        }

        [Fact]
        public void 帧编解码往返_payload完整还原()
        {
            var original = Encoding.UTF8.GetBytes("流程1");

            var frame = VmBridgeProtocol.EncodeFrame(VmBridgeProtocol.Command.Run, original);
            int length = VmBridgeProtocol.TryGetPayloadLength(frame, frame.Length);

            Assert.Equal(original.Length, length);
            var restored = new byte[length];
            Array.Copy(frame, VmBridgeProtocol.HeaderLength, restored, 0, length);
            Assert.Equal(original, restored);
        }

        [Fact]
        public void 帧头长度不足_返回负数待续读()
        {
            var buffer = new byte[] { 1, 2, 3 };

            Assert.Equal(-1, VmBridgeProtocol.TryGetPayloadLength(buffer, buffer.Length));
        }

        [Fact]
        public void 帧长度超上限_抛出防御异常()
        {
            // 构造长度字段 = int.MaxValue（超过 512MB 上限）
            var buffer = new byte[] { 1, 0xFF, 0xFF, 0xFF, 0x7F };

            Assert.Throws<InvalidOperationException>(
                () => VmBridgeProtocol.TryGetPayloadLength(buffer, buffer.Length));
        }

        // ══════════════ 响应构建/解析 ══════════════

        [Fact]
        public void 响应往返_成功含PNG_字段与字节完整还原()
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

            var payload = VmBridgeProtocol.BuildResponse(true, null!, null!, true, 123.4, 640, 480, png);
            var response = VmBridgeProtocol.ParseResponse(payload);

            Assert.True(response.Ok);
            Assert.True(response.IsOk);
            Assert.Equal(123.4, response.ElapsedMs, 1);
            Assert.Equal(640, response.Width);
            Assert.Equal(480, response.Height);
            Assert.NotNull(response.PngBytes);
            Assert.Equal(png, response.PngBytes);
        }

        [Fact]
        public void 响应往返_失败含中文错误信息_Base64正确还原()
        {
            var payload = VmBridgeProtocol.BuildResponse(false, "方案文件不存在", null!, false, 0, 0, 0, null!);
            var response = VmBridgeProtocol.ParseResponse(payload);

            Assert.False(response.Ok);
            Assert.Equal("方案文件不存在", response.Error);
        }

        [Fact]
        public void 响应往返_流程名列表_中文逗号分隔还原()
        {
            var payload = VmBridgeProtocol.BuildResponse(true, null!, new[] { "流程1", "流程2" }, false, 0, 0, 0, null!);
            var response = VmBridgeProtocol.ParseResponse(payload);

            Assert.True(response.Ok);
            Assert.NotNull(response.Procedures);
            Assert.Equal(new[] { "流程1", "流程2" }, response.Procedures);
        }

        [Fact]
        public void 响应解析_空payload_返回失败带原因()
        {
            var response = VmBridgeProtocol.ParseResponse(new byte[0]);

            Assert.False(response.Ok);
            Assert.False(string.IsNullOrEmpty(response.Error));
        }

        [Fact]
        public void 响应解析_PNG长度与声明不符_仍返回字节由上层校验()
        {
            // pnglen 声明 3，实际尾部 2 字节——解析侧不做硬校验（保持字节），上层判空兜底
            var headerText = "status=OK\npnglen=3\n\n";
            var png = new byte[] { 0xAA, 0xBB };
            var payload = new byte[Encoding.UTF8.GetByteCount(headerText) + png.Length];
            Encoding.UTF8.GetBytes(headerText, 0, headerText.Length, payload, 0);
            png.CopyTo(payload, payload.Length - png.Length);

            var response = VmBridgeProtocol.ParseResponse(payload);

            Assert.True(response.Ok);
            Assert.NotNull(response.PngBytes);
            Assert.Equal(png, response.PngBytes);
        }

        // ══════════════ 加密狗授权错误识别（ERR-027） ══════════════

        [Theory]
        [InlineData("VmException 0xE0000700: IMVS_EC_ENCRYPT_DONGLE_OUTDATE:Dongle not detected!")]
        [InlineData("VmException 0xE0000700: 授权登录失败")]
        [InlineData("MV_LoginLicense fail, msg[There are no available local locks.] ret[Dongle not detected]")]
        [InlineData("dongle not detected")]
        public void 加密狗错误识别_授权类错误文本_识别为真(string error)
        {
            Assert.True(VmBridgeProtocol.IsDongleLicenseError(error));
        }

        [Theory]
        [InlineData("方案文件不存在：D:\\x.sol")]
        [InlineData("方案内不存在流程「流程1」；可用流程：流程2")]
        [InlineData("流程无输出图（GetOutputImageV2 为空）")]
        [InlineData("")]
        public void 加密狗错误识别_普通错误与空文本_识别为假(string error)
        {
            Assert.False(VmBridgeProtocol.IsDongleLicenseError(error));
        }

        [Fact]
        public void 加密狗错误识别_null_识别为假()
        {
            Assert.False(VmBridgeProtocol.IsDongleLicenseError(null));
        }

        [Fact]
        public void 加密狗指引文案_非空且含关键指引()
        {
            Assert.False(string.IsNullOrWhiteSpace(VmBridgeProtocol.DongleLicenseHint));
            Assert.Contains("加密狗", VmBridgeProtocol.DongleLicenseHint);
            Assert.Contains("VisionMaster", VmBridgeProtocol.DongleLicenseHint);
        }

        // ══════════════ 服务失败路径（不启动真实桥接进程） ══════════════

        [Fact]
        public async Task 服务加载_方案文件不存在_返回失败不抛异常()
        {
            var service = new VisionMasterBridgeInspectionService(
                @"C:\不存在的路径\VmVisionBridge.exe",
                @"C:\不存在的方案.sol",
                "流程1",
                new StubLogService());

            var result = await service.LoadSolutionAsync(null!);

            Assert.False(result.Success);
            Assert.Contains("不存在", result.ErrorMessage);
            Assert.False(service.IsSolutionLoaded);
        }

        [Fact]
        public async Task 服务加载_空路径_返回失败()
        {
            var service = new VisionMasterBridgeInspectionService(
                @"C:\x\VmVisionBridge.exe", string.Empty, "流程1", new StubLogService());

            var result = await service.LoadSolutionAsync(" ");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task 服务检测_未加载方案_直接拒绝()
        {
            var service = new VisionMasterBridgeInspectionService(
                @"C:\x\VmVisionBridge.exe", @"C:\x\Test.sol", "流程1", new StubLogService());

            var result = await service.RunInspectionAsync();

            Assert.False(result.Success);
            Assert.Contains("未加载", result.ErrorMessage);
        }

        [Fact]
        public void 服务停止_幂等不抛异常()
        {
            var service = new VisionMasterBridgeInspectionService(
                @"C:\x\VmVisionBridge.exe", @"C:\x\Test.sol", "流程1", new StubLogService());

            service.Shutdown();
            service.Shutdown();

            Assert.False(service.IsSolutionLoaded);
        }

        [Fact]
        public void 服务构造_null依赖_抛参数异常()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new VisionMasterBridgeInspectionService(null!, @"C:\x.sol", "流程1", new StubLogService()));
            Assert.Throws<ArgumentNullException>(() =>
                new VisionMasterBridgeInspectionService(@"C:\x.exe", null!, "流程1", new StubLogService()));
            Assert.Throws<ArgumentNullException>(() =>
                new VisionMasterBridgeInspectionService(@"C:\x.exe", @"C:\x.sol", null!, new StubLogService()));
            Assert.Throws<ArgumentNullException>(() =>
                new VisionMasterBridgeInspectionService(@"C:\x.exe", @"C:\x.sol", "流程1", null!));
        }

        [Fact]
        public void 服务流程名_构造值透传()
        {
            var service = new VisionMasterBridgeInspectionService(
                @"C:\x\VmVisionBridge.exe", @"C:\x\Test.sol", "流程1", new StubLogService());

            Assert.Equal("流程1", service.ProcedureName);
        }
    }
}