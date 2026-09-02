using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace UiTopMachine.Common.Commands
{
    /// <summary>
    /// 命令管理器：维护命令与绑定控件的关联，
    /// 当命令状态变化时刷新绑定控件的 Enabled 状态（WinForms 无 WPF 的 CommandManager，需自行实现）
    /// </summary>
    public static class CommandManagerHelper
    {
        /// <summary>
        /// 命令 → 绑定（控件 + 参数提供器）列表（一个命令可绑定多个控件）；
        /// 提供器在点击/刷新时实时取参：固定参数（PageType）或动态参数（如表格当前单元格索引）
        /// </summary>
        private static readonly Dictionary<ICommand, List<(Control Control, Func<object?>? ParameterProvider)>> _bindings = new();

        /// <summary>
        /// 将控件绑定到命令：点击触发命令，命令状态驱动控件 Enabled
        /// </summary>
        /// <param name="control">绑定的控件（如 Button）</param>
        /// <param name="command">命令</param>
        public static void Bind(Control control, ICommand command)
            => Bind(control, command, (Func<object?>?)null);

        /// <summary>
        /// 将控件绑定到命令（带固定参数）：点击时以 parameter 触发命令，
        /// 用于导航等参数化场景（如 NavigateCommand + PageType）
        /// </summary>
        /// <param name="control">绑定的控件（如 Button / 自绘 Tab）</param>
        /// <param name="command">命令</param>
        /// <param name="parameter">点击时传给命令的固定参数</param>
        public static void Bind(Control control, ICommand command, object? parameter)
            => Bind(control, command, () => parameter);

        /// <summary>
        /// 将控件绑定到命令（动态参数提供器）：点击/刷新时实时调用 provider 取参，
        /// 用于依赖运行时状态的命令（如删除表格当前行/列）
        /// </summary>
        /// <param name="control">绑定的控件（如 Button）</param>
        /// <param name="command">命令</param>
        /// <param name="parameterProvider">参数提供器（每次点击/刷新时调用）</param>
        public static void Bind(Control control, ICommand command, Func<object?>? parameterProvider)
        {
            // 支持 Click 事件的所有控件（Button、自绘 Label Tab 等）
            control.Click += (s, e) =>
            {
                var parameter = parameterProvider?.Invoke();
                if (command.CanExecute(parameter))
                {
                    command.Execute(parameter);
                }
            };

            if (!_bindings.TryGetValue(command, out var list))
            {
                list = new List<(Control, Func<object?>?)>();
                _bindings[command] = list;
                // 订阅命令的 CanExecuteChanged（首次绑定才订阅）
                command.CanExecuteChanged += (s, e) => RefreshControlState((ICommand)s!);
            }

            list.Add((control, parameterProvider));
            // 立即同步一次控件状态
            control.Enabled = command.CanExecute(parameterProvider?.Invoke());
        }

        /// <summary>
        /// 刷新指定命令绑定的所有控件状态
        /// </summary>
        public static void RefreshCanExecute(ICommand command)
        {
            if (_bindings.ContainsKey(command))
            {
                RefreshControlState(command);
            }
        }

        /// <summary>
        /// 刷新控件启用状态（在 UI 线程执行）
        /// </summary>
        private static void RefreshControlState(ICommand command)
        {
            if (!_bindings.TryGetValue(command, out var list))
            {
                return;
            }

            foreach (var (control, parameterProvider) in list)
            {
                if (control is not null && !control.IsDisposed)
                {
                    // 实时取参（动态参数如当前单元格索引）
                    var parameter = parameterProvider?.Invoke();
                    // 若控件尚未创建句柄，直接设置；否则需 Invoke 到 UI 线程
                    if (control.InvokeRequired)
                    {
                        control.BeginInvoke(new Action(() => control.Enabled = command.CanExecute(parameter)));
                    }
                    else
                    {
                        control.Enabled = command.CanExecute(parameter);
                    }
                }
            }
        }
    }
}