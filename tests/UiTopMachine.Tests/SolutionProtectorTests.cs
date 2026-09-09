using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UiTopMachine.Common.VmBridge;
using UiTopMachine.Services;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// VM 方案加密壳测试（防方案外泄）：加密往返、魔数识别、口令校验失败、
    /// 桥接服务加载加密方案时的解密与临时明文清理（加密方案功能 v1.30）
    /// </summary>
    public class SolutionProtectorTests : IDisposable
    {
        private readonly string _dir;

        public SolutionProtectorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"UiTopMachine_tests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }

        /// <summary>构造一段可辨认的伪方案内容（含中文与二进制字节）</summary>
        private static byte[] SamplePlain()
        {
            var text = "VmServer 测试方案内容 Solution #1";
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var result = new byte[bytes.Length + 4];
            bytes.CopyTo(result, 0);
            result[bytes.Length] = 0x00;
            result[bytes.Length + 1] = 0xFF;
            result[bytes.Length + 2] = 0x10;
            result[bytes.Length + 3] = 0x7F;
            return result;
        }

        private string WritePlain(string name, byte[] content)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        // ──────────── 加密壳本体 ────────────

        [Fact]
        public void 加密后往返解密_内容一致且带魔数()
        {
            var plainPath = WritePlain("方案.sol", SamplePlain());
            var encryptedPath = Path.Combine(_dir, "方案.dll");

            SolutionProtector.EncryptFile(plainPath, encryptedPath);

            Assert.True(SolutionProtector.IsEncrypted(encryptedPath));
            Assert.False(SolutionProtector.IsEncrypted(plainPath)); // 明文不识别为加密

            var temp = SolutionProtector.DecryptToTempFile(encryptedPath);
            try
            {
                Assert.Equal(File.ReadAllBytes(plainPath), File.ReadAllBytes(temp));
            }
            finally
            {
                File.Delete(temp);
            }
        }

        [Fact]
        public void 错误口令_解密失败抛异常_明文不外泄()
        {
            var plainPath = WritePlain("方案.sol", SamplePlain());
            var encryptedPath = Path.Combine(_dir, "方案.dll");

            SolutionProtector.EncryptFile(plainPath, encryptedPath, "口令甲");

            Assert.ThrowsAny<CryptographicException>(() =>
                SolutionProtector.DecryptToTempFile(encryptedPath, "口令乙"));
        }

        [Fact]
        public void 非加密文件_解密报魔数不符()
        {
            var plainPath = WritePlain("普通.dll", new byte[] { 0x4D, 0x5A, 0x00, 0x01 });

            Assert.ThrowsAny<InvalidDataException>(() => SolutionProtector.DecryptToTempFile(plainPath));
        }

        [Fact]
        public void 二次加密_工具侧拒绝_魔数识别兜底()
        {
            var plainPath = WritePlain("方案.sol", SamplePlain());
            var encryptedPath = Path.Combine(_dir, "方案.dll");

            SolutionProtector.EncryptFile(plainPath, encryptedPath);

            Assert.True(SolutionProtector.IsEncrypted(encryptedPath)); // 工具 encrypt 前置检查依赖此判定
        }

        // ──────────── 桥接服务集成（解密时机 + 临时明文清理） ────────────

        private VisionMasterBridgeInspectionService CreateService()
        {
            // bridgeExePath 指向不存在的路径：合法加载会在"桥接进程不存在"处失败——
            // 恰好验证解密发生在启动桥接之前/之后与临时文件清理时机
            return new VisionMasterBridgeInspectionService(
                bridgeExePath: Path.Combine(_dir, "不存在的桥接.exe"),
                solutionPath: Path.Combine(_dir, "方案.dll"),
                procedureName: "流程1",
                logService: new StubLogService());
        }

        [Fact]
        public async Task 服务加载口令不符的加密方案_解密失败且不启动桥接()
        {
            var plainPath = WritePlain("方案.sol", SamplePlain());
            var encryptedPath = Path.Combine(_dir, "方案.dll");
            SolutionProtector.EncryptFile(plainPath, encryptedPath, "现场口令");

            var service = CreateService();
            try
            {
                var result = await service.LoadSolutionAsync(null);

                Assert.False(result.Success);
                Assert.Contains("解密失败", result.ErrorMessage);
                Assert.False(service.IsSolutionLoaded);
            }
            finally
            {
                service.Shutdown();
            }

            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "UiTopMachine_vmsol_*")); // 无明文残留
        }

        [Fact]
        public async Task 服务加载有效加密方案_桥接缺失失败时临时明文已清理()
        {
            var plainPath = WritePlain("方案.sol", SamplePlain());
            var encryptedPath = Path.Combine(_dir, "方案.dll");
            SolutionProtector.EncryptFile(plainPath, encryptedPath);

            var service = CreateService();
            try
            {
                var result = await service.LoadSolutionAsync(null);

                Assert.False(result.Success);
                Assert.Contains("桥接进程不存在", result.ErrorMessage);
                Assert.False(service.IsSolutionLoaded);
            }
            finally
            {
                service.Shutdown();
            }

            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "UiTopMachine_vmsol_*")); // 桥接失败清理时明文已删除
        }
    }
}
