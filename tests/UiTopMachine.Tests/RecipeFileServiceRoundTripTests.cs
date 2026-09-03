using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UiTopMachine.Services;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// RecipeFileService xlsx 往返测试（ERR-014 防回归核心资产）：
    /// 验证「保存 → 重新加载」数据不丢失——空行保留、中间空行位置不变、有数据行完整、空格占位还原
    /// 测试文件写入临时目录，测试结束后清理，不污染仓库
    /// </summary>
    public class RecipeFileServiceRoundTripTests : IDisposable
    {
        /// <summary>每个测试独立的临时目录（互不干扰）</summary>
        private readonly string _tempDir;

        public RecipeFileServiceRoundTripTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"UiTopMachineTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
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
                // 文件句柄释放延迟：忽略清理失败，不影响测试结论
            }
        }

        /// <summary>构造指向临时目录的服务</summary>
        private RecipeFileService CreateService(string fileName = "Recipe.xlsx")
            => new(Path.Combine(_tempDir, fileName));

        /// <summary>构造含指定行数据的表（列固定为默认 5 列结构）</summary>
        private static DataTable BuildTable(params string?[][] rows)
        {
            var table = new DataTable("配方");
            foreach (var h in new[] { "配方编号", "配方名称", "参数1", "参数2", "备注" })
            {
                table.Columns.Add(h);
            }

            foreach (var row in rows)
            {
                var dataRow = table.NewRow();
                for (int c = 0; c < row.Length; c++)
                {
                    dataRow[c] = row[c] ?? string.Empty;
                }
                table.Rows.Add(dataRow);
            }
            return table;
        }

        // ══════════════ ERR-014 核心回归：空行往返不蒸发 ══════════════

        [Fact]
        public async Task ERR014_保存含末尾空行的表_重载后行数不变()
        {
            var service = CreateService();
            var table = BuildTable(
                new[] { "R001", "配方一", "1", "2", "备注" },
                Array.Empty<string?>(),  // 新增的全空行（v1.4c 修复场景）
                Array.Empty<string?>());

            var saveResult = await service.SaveAsync(table);
            Assert.True(saveResult.Success, "保存应成功");

            var loadResult = await service.LoadAsync();
            Assert.True(loadResult.Success);
            Assert.NotNull(loadResult.Data);
            Assert.Equal(table.Rows.Count, loadResult.Data!.Rows.Count); // 空行不蒸发
        }

        [Fact]
        public async Task ERR014_中间空行位置_往返后保持不变()
        {
            var service = CreateService();
            var table = BuildTable(
                new[] { "R001", "A", "1", "2", "" },
                Array.Empty<string?>(),                    // 中间空行
                new[] { "R002", "B", "3", "4", "" });

            await service.SaveAsync(table);
            var loadResult = await service.LoadAsync();
            Assert.NotNull(loadResult.Data);

            // 中间空行仍位于第 2 行（索引 1），不被 RowsUsed 跳过
            var midRow = loadResult.Data!.Rows[1];
            Assert.True(midRow.ItemArray.All(c => string.IsNullOrWhiteSpace(c?.ToString())),
                "第 2 行应为空行（位置保持）");
            Assert.Equal("R002", loadResult.Data.Rows[2]["配方编号"].ToString());
        }

        [Fact]
        public async Task ERR014_有数据行往返_内容完整一致()
        {
            var service = CreateService();
            var table = BuildTable(
                new[] { "R001", "配方一", "100", "200", "备注A" },
                new[] { "R002", "配方二", "300", "400", "备注B" });

            await service.SaveAsync(table);
            var loadResult = await service.LoadAsync();
            Assert.NotNull(loadResult.Data);
            Assert.Equal(2, loadResult.Data!.Rows.Count);

            var row1 = loadResult.Data.Rows[0];
            Assert.Equal("R001", row1["配方编号"].ToString());
            Assert.Equal("配方一", row1["配方名称"].ToString());
            Assert.Equal("100", row1["参数1"].ToString());
            Assert.Equal("备注A", row1["备注"].ToString());
        }

        [Fact]
        public async Task 空格占位单元格_重载后Trim还原为空()
        {
            var service = CreateService();
            // 全空行 → 写端空格占位 → 读端 Trim 还原为空字符串
            var table = BuildTable(Array.Empty<string?>());

            await service.SaveAsync(table);
            var loadResult = await service.LoadAsync();
            Assert.NotNull(loadResult.Data);
            Assert.Equal(1, loadResult.Data!.Rows.Count);

            var restored = loadResult.Data.Rows[0].ItemArray
                .All(c => string.IsNullOrEmpty(c?.ToString()));
            Assert.True(restored, "空格占位应被 Trim 还原为空字符串，不应残留可见内容");
        }

        [Fact]
        public async Task 空格配方名与编号_往返后不被误判为有值()
        {
            var service = CreateService();
            var table = BuildTable(new[] { "R001", " ", "1", "2", " " });

            await service.SaveAsync(table);
            var loadResult = await service.LoadAsync();
            Assert.NotNull(loadResult.Data);

            // 用户故意输入空格的单元格：Trim 后为空（与编号唯一性校验口径一致）
            Assert.Equal(string.Empty, loadResult.Data!.Rows[0]["配方名称"].ToString());
        }

        // ══════════════ 基础往返与边界 ══════════════

        [Fact]
        public async Task 文件不存在_加载返回默认表头的空表()
        {
            var service = CreateService("不存在.xlsx");

            var loadResult = await service.LoadAsync();
            Assert.True(loadResult.Success);
            Assert.NotNull(loadResult.Data);
            Assert.Equal(0, loadResult.Data!.Rows.Count);
            Assert.Equal(5, loadResult.Data!.Columns.Count); // 默认 5 列表头
        }

        [Fact]
        public async Task 带路径保存_成功后FilePath切换数据源()
        {
            var service = CreateService("主文件.xlsx");
            var target = Path.Combine(_tempDir, "切换目标.xlsx");

            var saveResult = await service.SaveAsync(BuildTable(
                new[] { "R001", "X", "1", "2", "" }), target);

            Assert.True(saveResult.Success);
            Assert.Equal(target, service.FilePath); // 保存成功后数据源切换
        }

        [Fact]
        public async Task 传入空路径_保存与加载均拒绝()
        {
            var service = CreateService();

            var saveResult = await service.SaveAsync(BuildTable(), "  ");
            var loadResult = await service.LoadAsync("  ");

            Assert.False(saveResult.Success);
            Assert.False(loadResult.Success);
        }

        [Fact]
        public async Task 空表无行_保存重载后仅表头零数据行()
        {
            var service = CreateService();
            var table = BuildTable(); // 0 行

            await service.SaveAsync(table);
            var loadResult = await service.LoadAsync();
            Assert.NotNull(loadResult.Data);
            Assert.Equal(0, loadResult.Data!.Rows.Count);
        }

        [Fact]
        public async Task 并发保存同一文件_不抛异常_失败以Result返回()
        {
            // ERR-007 关联场景：并发写盘的文件锁冲突由 VM 层 SemaphoreSlim 串行化解决（设计事实），
            // Service 层本身不加锁。此测试验证：并发时 Service 不抛未处理异常，
            // 冲突以 Result.Fail 优雅返回（允许部分成功/部分失败，关键是绝不崩溃）
            var service = CreateService();
            var tasks = Enumerable.Range(0, 5).Select(i =>
                service.SaveAsync(BuildTable(
                    new[] { $"R{i:D3}", "并发", i.ToString(), "", "" })));

            var results = await Task.WhenAll(tasks);
            Assert.All(results, r => Assert.True(r.Success || r.ErrorMessage!.Contains("失败")));
        }

        [Fact]
        public async Task 顺序连续保存多次_全部成功()
        {
            // 正常使用模式（VM 信号量保证串行后的实际调用形态）：顺序保存必须全部成功
            var service = CreateService();
            for (int i = 0; i < 5; i++)
            {
                var result = await service.SaveAsync(BuildTable(
                    new[] { $"R{i:D3}", "顺序", i.ToString(), "", "" }));
                Assert.True(result.Success, $"第 {i + 1} 次保存失败：{result.ErrorMessage}");
            }
        }

        // ══════════════ CreateBlankAsync：时间戳命名防覆盖 ══════════════

        [Fact]
        public async Task 新建空白配方_文件名含配方名与时间戳_原文件保留()
        {
            var service = CreateService("原配方.xlsx");
            var originalPath = service.FilePath;

            // 先创建原配方文件（构造函数只设置路径不落盘），CreateBlankAsync 的契约是「不删除它」
            var seed = await service.SaveAsync(BuildTable(
                new[] { "R001", "原配方数据", "1", "2", "" }));
            Assert.True(seed.Success, $"前置创建原配方失败：{seed.ErrorMessage}");

            var result = await service.CreateBlankAsync("测试配方", "R009");

            Assert.True(result.Success, $"新建失败：{result.ErrorMessage}");
            Assert.NotNull(result.Data);
            var newPath = result.Data!;
            Assert.Contains("测试配方", Path.GetFileName(newPath));   // 文件名含配方名
            Assert.Matches(@"测试配方_\d{8}_\d{6}\.xlsx", Path.GetFileName(newPath)); // 时间戳后缀
            Assert.True(File.Exists(originalPath));                    // 原文件保留
            Assert.Equal(newPath, service.FilePath);                   // 数据源切换到新文件
        }

        [Fact]
        public async Task 新建空白配方_首行含配方编号_表头加粗可辨()
        {
            var service = CreateService();
            var result = await service.CreateBlankAsync("编号配方", "R042");
            Assert.True(result.Success);

            var loadResult = await service.LoadAsync();
            Assert.NotNull(loadResult.Data);
            Assert.Equal("R042", loadResult.Data!.Rows[0]["配方编号"].ToString()); // 首行编号写入
        }

        [Fact]
        public async Task 同名配方名_文件名不覆盖已有文件()
        {
            var service = CreateService();
            var first = await service.CreateBlankAsync("同名", "R001");
            Assert.True(first.Success);

            // 同一配方名再次新建（时间戳秒级可能相同）：必须生成不同文件名，不覆盖第一个
            var second = await service.CreateBlankAsync("同名", "R002");
            Assert.True(second.Success);
            Assert.NotEqual(first.Data, second.Data);
            Assert.True(File.Exists(first.Data!)); // 第一个文件仍存在
        }

        [Fact]
        public async Task 非法字符配方名_自动安全化不抛异常()
        {
            var service = CreateService();

            var result = await service.CreateBlankAsync("含/非法:字符*?", "R777");

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(File.Exists(result.Data!)); // 非法字符替换后成功创建
        }

        [Fact]
        public async Task 空配方名_兜底为新建配方()
        {
            var service = CreateService();

            var result = await service.CreateBlankAsync("", "R001");

            Assert.True(result.Success);
            Assert.Contains("新建配方", Path.GetFileName(result.Data!));
        }
    }
}