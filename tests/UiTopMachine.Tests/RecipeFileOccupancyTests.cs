using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using UiTopMachine.Common;
using UiTopMachine.Models;
using UiTopMachine.Services;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 配方文件占用探测守护测试（v1.35）：使用（保存/加载/备份轮转）前以 FileShare.None
    /// 试探性独占打开判断文件是否被外部程序（Excel 等）锁定——
    /// 守护不变量：① 被占用时三个操作均返回含「占用」的失败且不破坏文件；
    /// ② 释放锁后重试成功；③ VM 侧占用失败发布 Status 面板条目、非占用失败不进面板。
    /// </summary>
    public class RecipeFileOccupancyTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _recipePath;

        public RecipeFileOccupancyTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"UiTopMachineOccTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _recipePath = Path.Combine(_tempDir, "Recipe.xlsx");
        }

        public void Dispose()
        {
            // 锁定句柄未释放等竞态：带短暂重试清理；失败忽略残留（独立临时目录）
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(_tempDir))
                    {
                        Directory.Delete(_tempDir, recursive: true);
                    }
                    return;
                }
                catch (IOException)
                {
                    Task.Delay(50).Wait();
                }
            }
        }

        /// <summary>建立真实配方文件（先落一个合法表，再交给调用方加锁）</summary>
        private async Task<RecipeFileService> CreateRecipeFileAsync()
        {
            var service = new RecipeFileService(_recipePath);
            var table = new DataTable("配方");
            table.Columns.Add("配方编号");
            table.Columns.Add("配方名称");
            table.Rows.Add("R001", "甲");
            var save = await service.SaveAsync(table, _recipePath);
            Assert.True(save.Success, save.ErrorMessage ?? "预置配方文件失败");
            return service;
        }

        [Fact]
        public async Task 文件被独占锁定时_保存返回占用失败且文件内容不变()
        {
            var service = await CreateRecipeFileAsync();
            var before = File.ReadAllBytes(_recipePath);

            using (var hold = new FileStream(_recipePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = await service.SaveAsync(new DataTable("配方"));
                Assert.False(result.Success);
                Assert.Contains("占用", result.ErrorMessage);
            }

            Assert.Equal(before, File.ReadAllBytes(_recipePath)); // 占用期间不产生半写破坏
        }

        [Fact]
        public async Task 文件被独占锁定时_加载返回占用失败()
        {
            var service = await CreateRecipeFileAsync();

            using (var hold = new FileStream(_recipePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = await service.LoadAsync();
                Assert.False(result.Success);
                Assert.Contains("占用", result.ErrorMessage);
            }
        }

        [Fact]
        public async Task 文件被独占锁定时_新建空白配方拒绝轮转且原文件保留()
        {
            var service = await CreateRecipeFileAsync();

            using (var hold = new FileStream(_recipePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = await service.CreateBlankAsync(new[] { "配方编号", "配方名称" }, 3);
                Assert.False(result.Success);
                Assert.Contains("占用", result.ErrorMessage);
            }

            Assert.True(File.Exists(_recipePath), "占用期间拒绝轮转，原配方文件必须原样保留");
        }

        [Fact]
        public async Task 释放锁后重试_保存与加载均恢复成功()
        {
            var service = await CreateRecipeFileAsync();

            using (var hold = new FileStream(_recipePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.False((await service.SaveAsync(new DataTable("配方"))).Success);
            }

            // 锁释放后重试：保存成功、加载成功（占用探测不得误伤正常流程）
            var table = new DataTable("配方");
            table.Columns.Add("配方编号");
            table.Rows.Add("R002");
            Assert.True((await service.SaveAsync(table)).Success);
            var load = await service.LoadAsync();
            Assert.True(load.Success);
            Assert.Equal("R002", load.Data!.Rows[0]["配方编号"]);
        }

        /// <summary>保存结果可配置的配方服务桩（供 VM 面板发布用例）</summary>
        private sealed class SwitchableSaveStub : IRecipeFileService
        {
            public Result<bool> SaveResult { get; set; } = Result<bool>.OK(true);
            public DataTable Table { get; set; } = new DataTable("配方");
            public string FilePath { get; set; } = string.Empty;
            public string FolderPath { get; set; } = string.Empty;

            public Task<Result<DataTable>> LoadAsync() => Task.FromResult(Result<DataTable>.OK(Table));
            public Task<Result<DataTable>> LoadAsync(string filePath) => LoadAsync();
            public Task<Result<bool>> SaveAsync(DataTable table) => Task.FromResult(SaveResult);
            public Task<Result<bool>> SaveAsync(DataTable table, string filePath) => SaveAsync(table);
            public Task<Result<string>> CreateBlankAsync(IEnumerable<string> headers, int blankRowCount = 10) =>
                Task.FromResult(Result<string>.OK(string.Empty));
            public Result<bool> OpenFolder() => Result<bool>.OK(true);
        }

        [Fact]
        public async Task VM保存失败为占用时_发布Status面板条目_非占用失败不进面板()
        {
            var stub = new SwitchableSaveStub();
            var log = new StubLogService();
            var panel = new StubPanelPublisher();
            var vm = new RecipePageViewModel(stub, log, panel);

            // 占用失败 → 进面板
            stub.SaveResult = Result<bool>.Fail("配方文件正被其他程序占用（可能已在 Excel 中打开），请关闭该文件后重试");
            vm.SaveCommand.Execute(null);
            while (vm.IsSaving)
            {
                await Task.Delay(10);
            }

            Assert.Single(panel.Entries);
            Assert.Equal(LogLevel.Error, panel.Entries[0].Level);
            Assert.Contains("占用", panel.Entries[0].Message);

            // 非占用失败 → 不进面板（维持既有行为：仅日志）
            stub.SaveResult = Result<bool>.Fail("磁盘已满");
            vm.SaveCommand.Execute(null);
            while (vm.IsSaving)
            {
                await Task.Delay(10);
            }

            Assert.Single(panel.Entries);
        }
    }
}
