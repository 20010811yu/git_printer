using System;
using System.Drawing;
using System.Windows.Forms;
using UiTopMachine.Common.Commands;
using UiTopMachine.ViewModels;

namespace UiTopMachine.Views.Pages
{
    /// <summary>
    /// 打印管理页（纯 View）：标题 + 说明 + 打印按钮 + 计数展示，零业务逻辑
    /// </summary>
    public class PrintPage : UserControl
    {
        private readonly PrintPageViewModel _viewModel;

        // ══════════════ 布局控件 ══════════════
        private Label _titleLabel = null!;
        private Label _descriptionLabel = null!;
        private AntdUI.Button _printButton = null!;
        private Label _countLabel = null!;

        /// <summary>
        /// 构造：注入 ViewModel，初始化 UI 与绑定
        /// </summary>
        public PrintPage(PrintPageViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeUi();
            BindViewModel();
        }

        /// <summary>
        /// 构建页面布局（居中卡片式占位内容）
        /// </summary>
        private void InitializeUi()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(244, 247, 250);
            Padding = new Padding(20, 8, 20, 16);

            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            card.Paint += (s, e) =>
            {
                var rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using var pen = new Pen(Color.FromArgb(224, 230, 238));
                e.Graphics.DrawRectangle(pen, rect);
            };

            _titleLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(38, 50, 66)
            };

            _descriptionLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(120, 132, 148)
            };

            _printButton = new AntdUI.Button
            {
                Text = "打印标签",
                Type = AntdUI.TTypeMini.Primary,
                Size = new Size(140, 46),
                Radius = 8
            };

            _countLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(56, 70, 88)
            };

            card.Controls.Add(_titleLabel);
            card.Controls.Add(_descriptionLabel);
            card.Controls.Add(_printButton);
            card.Controls.Add(_countLabel);
            Controls.Add(card);

            // 布局完成后居中排布
            card.Resize += (_, _) => CenterLayout(card);
            PerformLayout();
            CenterLayout(card);
        }

        /// <summary>
        /// 控件居中纵向排布（纯布局，无业务）
        /// </summary>
        private void CenterLayout(Control card)
        {
            _titleLabel.Location = new Point((card.Width - _titleLabel.PreferredWidth) / 2, card.Height / 2 - 130);
            _descriptionLabel.Location = new Point((card.Width - _descriptionLabel.PreferredWidth) / 2, card.Height / 2 - 70);
            _printButton.Location = new Point((card.Width - _printButton.Width) / 2, card.Height / 2 - 10);
            _countLabel.Location = new Point((card.Width - _countLabel.PreferredWidth) / 2, card.Height / 2 + 60);
        }

        /// <summary>
        /// 绑定 ViewModel（View ↔ VM）
        /// </summary>
        private void BindViewModel()
        {
            _titleLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(PrintPageViewModel.Title),
                false, DataSourceUpdateMode.Never);
            _descriptionLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(PrintPageViewModel.Description),
                false, DataSourceUpdateMode.Never);
            _countLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(PrintPageViewModel.PrintCountText),
                false, DataSourceUpdateMode.Never);

            // 打印按钮绑定命令（AsyncRelayCommand 内置 IsBusy 防重复）
            CommandManagerHelper.Bind(_printButton, _viewModel.PrintCommand);
        }
    }
}