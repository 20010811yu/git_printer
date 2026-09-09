using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UiTopMachine.Common.VmBridge
{
    /// <summary>
    /// VM 方案文件加密壳（防方案外泄）：
    /// 明文 .sol 经 AES-256-CBC + PBKDF2(SHA256) 派生密钥加密为 .dll 交付文件（魔数头 VMENC1），
    /// 主程序加载前解密到临时目录，桥接进程按普通 .sol 加载，用完即删。
    /// 加密与解密共用本文件（主程序与 tools/SolutionEncryptor 双侧 link 编译，同 VmBridgeProtocol 模式）。
    /// 注意：本方案为「提高外泄门槛」的对称加密，密钥随程序分发，不能对抗逆向工程。
    /// </summary>
    public static class SolutionProtector
    {
        // ══════════════ 常量 ══════════════

        /// <summary>加密文件魔数（5 字节 ASCII）+ 1 字节格式版本</summary>
        private static readonly byte[] Magic = { (byte)'V', (byte)'M', (byte)'E', (byte)'N', (byte)'C', 0x01 };

        /// <summary>默认口令（现场交付固定使用；工具可用第四参数覆盖，主程序与工具两侧须一致）</summary>
        public const string DefaultPassphrase = "UiTopMachine.VmSol.2026";

        /// <summary>PBKDF2 迭代次数</summary>
        private const int Iterations = 100_000;

        /// <summary>随机 IV 长度（AES 块大小 16B）</summary>
        private const int IvLength = 16;

        // ══════════════ 公开接口 ══════════════

        /// <summary>判断文件是否为本工具加密的方案（按魔数识别，与扩展名无关）</summary>
        public static bool IsEncrypted(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < Magic.Length)
                {
                    return false;
                }

                var header = new byte[Magic.Length];
                return fs.Read(header, 0, header.Length) == header.Length
                    && header.AsSpan().SequenceEqual(Magic);
            }
            catch
            {
                return false; // 不可读一律按未加密处理（后续按明文路径加载）
            }
        }

        /// <summary>
        /// 加密方案文件：明文 .sol → 加密 .dll（覆盖已存在目标文件）
        /// </summary>
        public static void EncryptFile(string plainPath, string encryptedPath, string? passphrase = null)
        {
            var plain = File.ReadAllBytes(plainPath);
            var encrypted = Encrypt(plain, passphrase ?? DefaultPassphrase);
            File.WriteAllBytes(encryptedPath, encrypted);
        }

        /// <summary>
        /// 解密到临时文件（调用方负责删除返回的临时文件；临时文件固定 .sol 扩展名供桥接进程加载）
        /// </summary>
        public static string DecryptToTempFile(string encryptedPath, string? passphrase = null)
        {
            var plain = Decrypt(File.ReadAllBytes(encryptedPath), passphrase ?? DefaultPassphrase);
            var tempPath = Path.Combine(Path.GetTempPath(),
                $"UiTopMachine_vmsol_{Guid.NewGuid():N}.sol");
            File.WriteAllBytes(tempPath, plain);
            return tempPath;
        }

        // ══════════════ 加解密核心 ══════════════

        /// <summary>加密：输出 = 魔数 6B + 盐 16B + IV 16B + 密文</summary>
        private static byte[] Encrypt(byte[] plain, string passphrase)
        {
            var salt = RandomNumberGenerator.GetBytes(IvLength);
            using var aes = Aes.Create();
            aes.Key = DeriveKey(passphrase, salt);
            aes.GenerateIV();

            using var output = new MemoryStream();
            output.Write(Magic, 0, Magic.Length);
            output.Write(salt, 0, salt.Length);
            output.Write(aes.IV, 0, aes.IV.Length);
            using (var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                crypto.Write(plain, 0, plain.Length);
                crypto.FlushFinalBlock();
            }

            return output.ToArray();
        }

        /// <summary>解密：校验魔数 → 读盐/IV → AES 解密（口令错误表现为填充校验失败）</summary>
        private static byte[] Decrypt(byte[] encrypted, string passphrase)
        {
            int headerLength = Magic.Length + IvLength + IvLength;
            if (encrypted.Length < headerLength || !encrypted.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                throw new InvalidDataException("不是有效的加密方案文件（魔数不符）");
            }

            var salt = encrypted.AsSpan(Magic.Length, IvLength).ToArray();
            using var aes = Aes.Create();
            aes.Key = DeriveKey(passphrase, salt);
            aes.IV = encrypted.AsSpan(Magic.Length + IvLength, IvLength).ToArray();

            using var input = new MemoryStream(encrypted, headerLength, encrypted.Length - headerLength);
            using var crypto = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var output = new MemoryStream();
            crypto.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>PBKDF2(SHA256) 从口令 + 盐派生 256 位密钥</summary>
        private static byte[] DeriveKey(string passphrase, byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(passphrase, salt, Iterations, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32);
        }
    }
}
