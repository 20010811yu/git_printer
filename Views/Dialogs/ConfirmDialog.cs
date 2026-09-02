using System;
using System.Drawing;
using System.Windows.Forms;

namespace UiTopMachine.Views.Dialogs
{
    /// <summary>
    /// 通用确认弹框（纯 View）：提示文字 + 确定/取消按钮，
    /// 仅负责向用户确认危险操作（如删除行/列，零业务逻辑），
    /// 用户选择结果由调用方 ViewModel 读取并决定后续流程
    /// </summary>
    internal class ConfirmDialog : Form
    {
        // ══════════════ 布局控件 ══════════════
        private readonly AntdUI.Button _okButton;
        private readonly AntdUI.Button _cancelButton;

        // ══════════════ 构造 ══════════════

        /// <summary>
        /// 构造确认弹框
        /// </summary>
        /// <param name="title">弹框标题</param>
        /// <param name="message">确认提示内容（说明将执行的操作与后果）</param>
        public ConfirmDialog(string title, string message)
        {
            // ── 窗体基本设置：固定对话框边框、居中父窗体、不在任务栏显示 ──
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 170);
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

            // ── 警示图标 + 提示文字 ──
            var iconLabel = new Label
            {
                Text = "⚠",
                Location = new Point(24, 24),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(250, 173, 20)
            };

            var messageLabel = new Label
            {
                Text = message,
                Location = new Point(64, 30),
                MaximumSize = new Size(440 - 64 - 24, 80),
                AutoSize = true,
                ForeColor = Color.FromArgb(80, 92, 108)
            };

            // ── 确定 / 取消按钮（确定用 Error 红色警示危险操作语义）──
            _okButton = new AntdUI.Button
            {
                Text = "确定删除",
                Type = AntdUI.TTypeMini.Error,
                Size = new Size(96, 36),
                Location = new Point(ClientSize.Width - 96 - 24 - 8, 116),
                Radius = 8
            };
            _okButton.Click += (_, _) => DialogResult = DialogResult.OK;

            _cancelButton = new AntdUI.Button
            {
                Text = "取消",
                Type = AntdUI.TTypeMini.Default,
                Size = new Size(96, 36),
                Location = new Point(ClientSize.Width - 96 - 24, 116),
                Radius = 8
            };
            _cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

            // 回车 = 确认删除，Esc = 取消（键盘操作习惯）
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            Controls.AddRange(new Control[] { iconLabel, messageLabel, _okButton, _cancelButton });
        }
    }
}