using HslCommunication.Profinet.Inovance;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// HslModbusTransport 地址格式守护测试（ERR-022 回归）：
    /// 实证结论（2026-09-04，HslCommunication 12.9.2）——
    /// ① InovanceTcpNet 要求汇川软元件格式地址，纯数字解析失败；
    /// ② 默认构造（AM 系列）不支持 D 字地址，必须显式指定系列；
    /// ③ H5U 系列：位地址 "M1000"→线圈 1000，字地址 "D100"→保持寄存器 100。
    /// 生产代码不得对地址做任何前缀剥离/改写，直接透传
    /// </summary>
    public class HslModbusAddressTests
    {
        [Theory]
        [InlineData(InovanceSeriesCode.H5U, "M1000", 1, "1000")]
        [InlineData(InovanceSeriesCode.H5U, "D100", 3, "100")]
        [InlineData(InovanceSeriesCode.H5U, "D101", 6, "101")]
        public void H5U系列_软元件地址_正确翻译为Modbus地址(InovanceSeriesCode series, string address, byte functionCode, string expected)
        {
            var plc = new InovanceTcpNet(ToSeries(series), "127.0.0.1", 502, 1);

            var result = plc.TranslateToModbusAddress(address, functionCode);

            Assert.True(result.IsSuccess, $"地址 {address} 翻译失败：{result.Message}");
            Assert.Equal(expected, result.Content);
        }

        [Theory]
        [InlineData("100")]
        [InlineData("1000")]
        public void 纯数字地址_InovanceTcpNet_解析失败(string address)
        {
            var plc = new InovanceTcpNet(InovanceSeries.H5U, "127.0.0.1", 502, 1);

            var result = plc.TranslateToModbusAddress(address, 3);

            Assert.False(result.IsSuccess, "纯数字地址在 InovanceTcpNet 上应解析失败（要求汇川软元件格式）");
        }

        [Fact]
        public void 默认系列_D字地址_解析失败_必须显式指定系列()
        {
            // 默认构造（AM 系列）：D 字地址不支持——生产代码必须显式指定 InovanceSeries.H5U
            var plc = new InovanceTcpNet("127.0.0.1", 502, 1);

            var result = plc.TranslateToModbusAddress("D100", 3);

            Assert.False(result.IsSuccess, "默认系列（AM）不支持 D 字地址——transport 必须显式指定 H5U 系列");
        }

        /// <summary>测试内使用的系列标识（避免测试文件直接依赖枚举字面量引起 Theory 泛化歧义）</summary>
        public enum InovanceSeriesCode
        {
            H5U = 0
        }

        private static InovanceSeries ToSeries(InovanceSeriesCode code) =>
            code switch
            {
                InovanceSeriesCode.H5U => InovanceSeries.H5U,
                _ => InovanceSeries.H5U
            };
    }
}
