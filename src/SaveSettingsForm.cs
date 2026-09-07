using System;
using System.Drawing;
using System.Windows.Forms;

namespace Miashot
{
    internal sealed class SaveSettingsForm : Form
    {
        private readonly AppSettings original;
        private readonly TextBox saveFolderBox;
        private readonly TextBox fileNameTemplateBox;
        private readonly Label fileNamePreview;
        private bool useDefaultSaveFolder;

        internal SaveSettingsForm(AppSettings settings, Icon icon)
        {
            original = settings.Clone();
            Text = "快截截图保存设置";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 242);
            Font = new Font("Microsoft YaHei UI", 9f);

            var group = new GroupBox
            {
                Text = "文件保存",
                Location = new Point(18, 14),
                Size = new Size(564, 174)
            };
            Controls.Add(group);
            group.Controls.Add(new Label
            {
                Text = "保存位置",
                AutoSize = true,
                Location = new Point(16, 34)
            });

            useDefaultSaveFolder = string.IsNullOrWhiteSpace(settings.SaveFolder);
            saveFolderBox = new TextBox
            {
                ReadOnly = true,
                BackColor = Color.White,
                Location = new Point(100, 29),
                Size = new Size(286, 27),
                Text = ScreenshotStorage.ResolveSaveFolder(settings.SaveFolder)
            };
            group.Controls.Add(saveFolderBox);

            var browseButton = new Button
            {
                Text = "浏览...",
                Location = new Point(394, 28),
                Size = new Size(68, 29)
            };
            browseButton.Click += OnBrowseSaveFolder;
            group.Controls.Add(browseButton);

            var restoreButton = new Button
            {
                Text = "恢复默认",
                Location = new Point(470, 28),
                Size = new Size(78, 29)
            };
            restoreButton.Click += OnRestoreDefaults;
            group.Controls.Add(restoreButton);

            group.Controls.Add(new Label
            {
                Text = "文件名格式",
                AutoSize = true,
                Location = new Point(16, 76)
            });
            fileNameTemplateBox = new TextBox
            {
                Location = new Point(100, 71),
                Size = new Size(448, 27),
                Text = string.IsNullOrWhiteSpace(settings.FileNameTemplate)
                    ? ScreenshotStorage.DefaultFileNameTemplate : settings.FileNameTemplate
            };
            group.Controls.Add(fileNameTemplateBox);
            group.Controls.Add(new Label
            {
                Text = "可用变量：{日期}、{时间}、{序号}；图片格式固定为 PNG",
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 95, 105),
                Location = new Point(100, 106)
            });
            fileNamePreview = new Label
            {
                AutoSize = true,
                Location = new Point(100, 136)
            };
            group.Controls.Add(fileNamePreview);
            fileNameTemplateBox.TextChanged += delegate { UpdatePreview(); };
            UpdatePreview();

            var cancelButton = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(408, 202),
                Size = new Size(78, 30)
            };
            Controls.Add(cancelButton);
            var saveButton = new Button
            {
                Text = "保存",
                DialogResult = DialogResult.OK,
                Location = new Point(496, 202),
                Size = new Size(78, 30),
                BackColor = Color.FromArgb(25, 112, 226),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            saveButton.FlatAppearance.BorderSize = 0;
            Controls.Add(saveButton);
            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        private void OnBrowseSaveFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择截图保存位置",
                SelectedPath = saveFolderBox.Text,
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                useDefaultSaveFolder = false;
                saveFolderBox.Text = dialog.SelectedPath;
            }
        }

        private void OnRestoreDefaults(object sender, EventArgs e)
        {
            useDefaultSaveFolder = true;
            saveFolderBox.Text = ScreenshotStorage.PicturesFolder;
            fileNameTemplateBox.Text = ScreenshotStorage.DefaultFileNameTemplate;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var preview = ScreenshotStorage.PreviewFileName(fileNameTemplateBox.Text);
            fileNamePreview.Text = "预览：" + preview;
            fileNamePreview.ForeColor = preview.EndsWith(".png",
                StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(20, 62, 128) : Color.FromArgb(190, 55, 55);
        }

        internal bool TryGetSettings(out AppSettings settings, out string error)
        {
            settings = original.Clone();
            settings.SaveFolder = useDefaultSaveFolder
                ? string.Empty : saveFolderBox.Text.Trim();
            settings.FileNameTemplate = fileNameTemplateBox.Text.Trim();
            return ScreenshotStorage.TryValidateSettings(settings.SaveFolder,
                settings.FileNameTemplate, out error);
        }
    }
}
