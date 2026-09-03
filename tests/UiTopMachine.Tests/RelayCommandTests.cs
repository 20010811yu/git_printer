using System;
using System.Threading.Tasks;
using UiTopMachine.Common.Commands;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// MVVM 命令基础设施测试：RelayCommand / AsyncRelayCommand
    /// 覆盖：CanExecute 判定、防重复执行（IsBusy）、CanExecuteChanged 通知
    /// </summary>
    public class RelayCommandTests
    {
        // ══════════════ RelayCommand（同步命令） ══════════════

        [Fact]
        public void 无CanExecute约束_默认可执行()
        {
            var command = new RelayCommand(_ => { });
            Assert.True(command.CanExecute(null));
        }

        [Fact]
        public void CanExecute返回false_命令禁用()
        {
            var command = new RelayCommand(_ => { }, _ => false);
            Assert.False(command.CanExecute(null));
        }

        [Fact]
        public void Execute_执行委托被调用且参数原样传递()
        {
            object? received = null;
            var command = new RelayCommand(p => received = p);

            var token = new object();
            command.Execute(token);

            Assert.Same(token, received);
        }

        [Fact]
        public void Execute执行期间_CanExecute为false_执行后恢复()
        {
            bool canExecuteDuringExecution = true;
            RelayCommand? command = null;
            command = new RelayCommand(
                _ => canExecuteDuringExecution = command!.CanExecute(null));

            command.Execute(null);

            // 执行期间防重复生效（_isExecuting = true）
            Assert.False(canExecuteDuringExecution);
            // 执行完毕恢复正常
            Assert.True(command.CanExecute(null));
        }

        [Fact]
        public void RaiseCanExecuteChanged_触发CanExecuteChanged事件()
        {
            var command = new RelayCommand(_ => { });
            int fired = 0;
            command.CanExecuteChanged += (_, _) => fired++;

            command.RaiseCanExecuteChanged();

            Assert.Equal(1, fired);
        }

        [Fact]
        public void 构造传入null执行委托_抛ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
        }

        // ══════════════ AsyncRelayCommand（异步命令） ══════════════

        [Fact]
        public async Task 异步命令执行前IsBusy为false_执行中为true_完成后恢复false()
        {
            var tcs = new TaskCompletionSource();
            var command = new AsyncRelayCommand(_ => tcs.Task);

            Assert.False(command.IsBusy);

            var run = Task.Run(() => command.Execute(null));
            // 等待命令进入执行态（IsBusy 翻转为 true）
            while (!command.IsBusy)
            {
                await Task.Delay(10);
            }
            Assert.True(command.IsBusy);
            Assert.False(command.CanExecute(null)); // IsBusy 期间禁止再次执行

            tcs.SetResult();
            await run;
            Assert.False(command.IsBusy);
            Assert.True(command.CanExecute(null));
        }

        [Fact]
        public void 异步命令构造传入null_抛ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AsyncRelayCommand(null!));
        }

        [Fact]
        public void 异步命令CanExecute约束_与IsBusy叠加生效()
        {
            var command = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
            Assert.False(command.CanExecute(null));
        }
    }
}