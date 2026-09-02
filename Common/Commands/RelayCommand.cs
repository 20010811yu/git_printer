using System;
using System.Windows.Forms;

namespace UiTopMachine.Common.Commands
{
    /// <summary>
    /// WinForms MVVM 命令基础设施：命令接口
    /// </summary>
    public interface ICommand
    {
        /// <summary>命令是否可执行</summary>
        bool CanExecute(object? parameter);

        /// <summary>执行命令</summary>
        void Execute(object? parameter);

        /// <summary>可执行状态变更事件</summary>
        event EventHandler? CanExecuteChanged;
    }

    /// <summary>
    /// 同步命令实现（命令模式）
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        /// <inheritdoc />
        public event EventHandler? CanExecuteChanged;


        /// <summary>
        /// 创建同步命令
        /// </summary>
        /// <param name="execute">执行逻辑</param>
        /// <param name="canExecute">可执行判定（可选）</param>
        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <inheritdoc />
        public bool CanExecute(object? parameter)
        {
            // 防重复执行：正在执行时禁止再次触发
            return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
        }

        /// <inheritdoc />
        public void Execute(object? parameter)
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                _execute(parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// 通知命令状态变化（触发事件 + 刷新绑定控件启用态）
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 异步命令实现：IsBusy 防重复点击（工业 IO 操作必须异步）
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, System.Threading.Tasks.Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        /// <inheritdoc />
        public event EventHandler? CanExecuteChanged;


        /// <summary>
        /// 是否正在执行（可绑定到 UI 提示忙碌状态）
        /// </summary>
        public bool IsBusy => _isExecuting;

        /// <summary>
        /// 创建异步命令
        /// </summary>
        public AsyncRelayCommand(Func<object?, System.Threading.Tasks.Task> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <inheritdoc />
        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
        }

        /// <inheritdoc />
        public async void Execute(object? parameter)
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute(parameter);
            }
            catch (Exception ex)
            {
                // 异步命令兜底异常捕获：交由全局异常处理
                MessageBox.Show($"操作失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// 通知命令状态变化（触发事件 + 刷新绑定控件启用态）
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
