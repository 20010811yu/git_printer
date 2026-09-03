using System;
using System.Threading.Tasks;

namespace UiTopMachine.Services.Interfaces
{
    /// <summary>码型枚举（打印页选择用）</summary>
    public enum ZplCodeType
    {
        /// <summary>二维码（QR Code，30mm×30mm 标签）</summary>
        QRCode,
        /// <summary>Code 39 条码</summary>
        Code39,
        /// <summary>Code 128 条码</summary>
        Code128,
        /// <summary>PDF417 二维条码</summary>
        Pdf417,
        /// <summary>纯数字文本</summary>
        Number
    }

    /// <summary>
    /// 打印服务接口：隔离 ZPL 打印机（TCP 直连 / Windows Spooler RAW）与 ZPL 指令生成，
    /// ViewModel 不接触 Socket/Spooler 句柄等 SDK 原始对象
    /// </summary>
    public interface IPrintService
    {
        /// <summary>
        /// 通过 TCP 直连打印机发送 ZPL（默认 192.168.1.200:9100，可配置）
        /// </summary>
        Task<Result<bool>> PrintByIpAsync(string zpl);

        /// <summary>
        /// 通过 Windows Spooler 发送 RAW ZPL 到指定名称的打印机（默认 "zpl"，可配置）
        /// </summary>
        Task<Result<bool>> PrintBySpoolerAsync(string zpl);

        /// <summary>
        /// 生成 ZPL 指令（按码型分发；serialNumber 为标签数据）
        /// </summary>
        string GenerateZpl(ZplCodeType codeType, string serialNumber);

        /// <summary>
        /// 生成二维码 ZPL（30mm×30mm 标签，二维码约 20mm）
        /// </summary>
        string CreateQrCodeZpl(string serialNumber);

        /// <summary>
        /// 生成 Code 39 条码 ZPL
        /// </summary>
        string CreateCode39Zpl(string serialNumber);

        /// <summary>
        /// 生成 Code 128 条码 ZPL
        /// </summary>
        string CreateCode128Zpl(string serialNumber);

        /// <summary>
        /// 生成 PDF417 条码 ZPL
        /// </summary>
        string CreatePdf417Zpl(string serialNumber);

        /// <summary>
        /// 生成纯数字文本 ZPL
        /// </summary>
        string CreateNumberZpl(string serialNumber);

        /// <summary>
        /// 校验流水号：非空且为纯数字
        /// </summary>
        bool IsValidSerial(string serialNumber);

        /// <summary>
        /// 加载流水号（持久化文件不存在时返回默认 000001）
        /// </summary>
        Task<Result<string>> LoadSerialAsync();

        /// <summary>
        /// 保存流水号（6 位补零；文件不存在自动创建）
        /// </summary>
        Task<Result<bool>> SaveSerialAsync(string serialNumber);
    }
}