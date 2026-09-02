using System;
using System.Drawing;
using System.Windows.Forms;

namespace UiTopMachine.Views.Dialogs
{
    /// <summary>
    /// 通用输入弹框（纯 View）：说明文字 + 输入框 + 确定/取消按钮，
    /// 仅负责收集用户输入（零业务逻辑），输入内容的校验由调用方 ViewModel 完成
    /// </summary>
    internal class InputDialog : Form
    {
        // ══════════════ 布局控件 ══════════════
        private readonly TextBox _inputTextBox;
        private readonly AntdUI.Button _okButton;
        private readonly AntdUI.Button _cancelButton;

        // ══════════════ 属性 ══════════════

        /// <summary>用户输入内容（弹框关闭后由调用方读取，原文回传不加工）</summary>
        public string InputText => _inputTextBox.Text;

        // ══════════════ 构造 ══════════════

        /// <summary>
        /// 构造输入弹框
        /// </summary>
        /// <param name="title">弹框标题</param>
        /// <param name="prompt">输入说明文字</param>
        public InputDialog(string title, string prompt)
        {
            // ── 窗体基本设置：固定对话框边框、居中父窗体、不在任务栏显示 ──
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 170);
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

            // ── 说明文字 ──
            var promptLabel = new Label
            {
                Text = prompt,
                Location = new Point(24, 22),
                AutoSize = true,
                ForeColor = Color.FromArgb(80, 92, 108)
            };

            // ── 输入框 ──
            _inputTextBox = new TextBox
            {
                Location = new Point(24, 54),
                Size = new Size(372, 32),
                Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point)
            };

            // ── 确定 / 取消按钮（AntdUI 与工具栏风格统一）──
            _okButton = new AntdUI.Button
            {
                Text = "确定",
                Type = AntdUI.TTypeMini.Primary,
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

            // 回车 = 确定，Esc = 取消（键盘操作习惯）
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            Controls.AddRange(new Control[] { promptLabel, _inputTextBox, _okButton, _cancelButton });
        }
    }
}