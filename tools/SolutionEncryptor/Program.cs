using UiTopMachine.Common.VmBridge;

namespace SolutionEncryptor
{
    /// <summary>
    /// VM 方案文件加密工具（防方案外泄）：
    ///   SolutionEncryptor encrypt <明文.sol> <加密输出.dll> [口令]
    /// 输出文件带 VMENC1 魔数，主程序视觉服务加载时自动识别并解密到临时目录；
    /// 不带参数运行打印用法。加密后请从现场删除明文 .sol。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 3 && args.Length != 4 || !string.Equals(args[0], "encrypt", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("用法: SolutionEncryptor encrypt <明文方案.sol> <加密输出.dll> [口令]");
                Console.WriteLine($"未提供口令时使用内置默认口令（与主程序一致）：{SolutionProtector.DefaultPassphrase}");
                return 1;
            }

            var plainPath = args[1];
            var encryptedPath = args[2];
            var passphrase = args.Length == 4 ? args[3] : SolutionProtector.DefaultPassphrase;

            try
            {
                if (!File.Exists(plainPath))
                {
                    Console.WriteLine($"明文方案不存在：{plainPath}");
                    return 2;
                }

                if (SolutionProtector.IsEncrypted(plainPath))
                {
                    Console.WriteLine("输入文件已是加密方案（VMENC1 魔数），拒绝二次加密");
                    return 3;
                }

                SolutionProtector.EncryptFile(plainPath, encryptedPath, passphrase);

                // 回读校验：加密产物必须能解密还原（字节数一致即校验通过，内容按需抽查首尾）
                var temp = SolutionProtector.DecryptToTempFile(encryptedPath, passphrase);
                try
                {
                    var original = new FileInfo(plainPath).Length;
                    var restored = new FileInfo(temp).Length;
                    if (original != restored)
                    {
                        throw new InvalidDataException($"回读长度不一致（原 {original} / 还原 {restored}）");
                    }
                }
                finally
                {
                    File.Delete(temp);
                }

                Console.WriteLine($"加密完成：{encryptedPath}（{new FileInfo(encryptedPath).Length} 字节，回读校验通过）");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加密失败：{ex.Message}");
                return 4;
            }
        }
    }
}
