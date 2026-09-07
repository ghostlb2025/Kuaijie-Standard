using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Miashot
{
    internal sealed class HotkeyBox : TextBox
    {
        private HotkeyBinding binding;
        internal event EventHandler BindingChanged;

        internal HotkeyBox(HotkeyBinding initial)
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            TextAlign = HorizontalAlignment.Center;
            BackColor = Color.White;
            Binding = initial;
        }

        internal HotkeyBinding Binding
        {
            get { return binding.Clone(); }
            set
            {
                binding = value.Clone();
                Text = binding.DisplayText;
                ForeColor = binding.Key == Keys.None || binding.Modifiers == 0
                    ? Color.FromArgb(125, 132, 139) : Color.FromArgb(30, 36, 42);
                var handler = BindingChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                Binding = new HotkeyBinding(0, Keys.None);
                return;
            }

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey ||
                e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                return;

            uint modifiers = 0;
            if (e.Control) modifiers |= NativeMethods.ModControl;
            if (e.Alt) modifiers |= NativeMethods.ModAlt;
            if (e.Shift) modifiers |= NativeMethods.ModShift;
            if (modifiers == 0) return;

            Binding = new HotkeyBinding(modifiers, e.KeyCode);
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            SelectAll();
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly HotkeyBox quickBox;
        private readonly HotkeyBox advancedBox;
        private readonly HotkeyBox fullscreenBox;
        private readonly HotkeyBox saveBox;
        private readonly HotkeyBox delayedBox;
        private readonly TextBox saveFolderBox;
        private readonly TextBox fileNameTemplateBox;
        private readonly Label fileNamePreview;
        private readonly AppSettings original;
        private bool useDefaultSaveFolder;
        private bool transferringBinding;

        internal SettingsForm(AppSettings settings, Icon icon)
        {
            original = settings.Clone();
            Text = "快截设置";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 528);
            Font = new Font("Microsoft YaHei UI", 9f);

            var fileGroup = new GroupBox
            {
                Text = "文件保存",
                Location = new Point(18, 14),
                Size = new Size(564, 180)
            };
            Controls.Add(fileGroup);

            fileGroup.Controls.Add(new Label
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
                Text = useDefaultSaveFolder ? ScreenshotStorage.PicturesFolder : settings.SaveFolder
            };
            fileGroup.Controls.Add(saveFolderBox);

            var browseButton = new Button
            {
                Text = "浏览...",
                Location = new Point(394, 28),
                Size = new Size(68, 29)
            };
            browseButton.Click += OnBrowseSaveFolder;
            fileGroup.Controls.Add(browseButton);

            var restoreSaveButton = new Button
            {
                Text = "恢复默认",
                Location = new Point(470, 28),
                Size = new Size(78, 29)
            };
            restoreSaveButton.Click += OnRestoreSaveDefaults;
            fileGroup.Controls.Add(restoreSaveButton);

            fileGroup.Controls.Add(new Label
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
            fileGroup.Controls.Add(fileNameTemplateBox);
            fileGroup.Controls.Add(new Label
            {
                Text = "可用变量：{日期}、{时间}、{序号}；图片格式固定为 PNG",
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 95, 105),
                Location = new Point(100, 106)
            });
            fileNamePreview = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(20, 62, 128),
                Location = new Point(100, 136)
            };
            fileGroup.Controls.Add(fileNamePreview);
            fileNameTemplateBox.TextChanged += delegate { UpdateFileNamePreview(); };
            UpdateFileNamePreview();

            var title = new Label
            {
                Text = "快捷键",
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 62, 128),
                AutoSize = true,
                Location = new Point(24, 208)
            };
            Controls.Add(title);

            var note = new Label
            {
                Text = "设置窗口打开期间快捷键暂时停用；按 Delete 可取消绑定，重复组合会自动转移。",
                ForeColor = Color.FromArgb(90, 95, 105),
                AutoSize = false,
                Size = new Size(552, 34),
                Location = new Point(24, 234)
            };
            Controls.Add(note);

            quickBox = AddRow("快速区域截图", settings.QuickCapture, 272);
            advancedBox = AddRow("高级区域截图", settings.AdvancedCapture, 312);
            fullscreenBox = AddRow("当前显示器全屏", settings.FullscreenCapture, 352);
            saveBox = AddRow("保存最近截图", settings.SaveRecent, 392);
            delayedBox = AddRow("3 秒后区域截图", settings.DelayedCapture, 432);

            quickBox.BindingChanged += OnBindingChanged;
            advancedBox.BindingChanged += OnBindingChanged;
            fullscreenBox.BindingChanged += OnBindingChanged;
            saveBox.BindingChanged += OnBindingChanged;
            delayedBox.BindingChanged += OnBindingChanged;

            var defaultsButton = new Button
            {
                Text = "恢复默认",
                Location = new Point(24, 482),
                Size = new Size(90, 30)
            };
            defaultsButton.Click += OnRestoreDefaults;
            Controls.Add(defaultsButton);

            var cancelButton = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(408, 482),
                Size = new Size(78, 30)
            };
            Controls.Add(cancelButton);

            var saveButton = new Button
            {
                Text = "保存",
                DialogResult = DialogResult.OK,
                Location = new Point(496, 482),
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

        private HotkeyBox AddRow(string labelText, HotkeyBinding binding, int top)
        {
            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                Location = new Point(28, top + 6)
            };
            Controls.Add(label);

            var box = new HotkeyBox(binding)
            {
                Location = new Point(300, top),
                Size = new Size(276, 27)
            };
            Controls.Add(box);
            return box;
        }

        private void OnRestoreDefaults(object sender, EventArgs e)
        {
            var defaults = AppSettings.Defaults();
            quickBox.Binding = defaults.QuickCapture;
            advancedBox.Binding = defaults.AdvancedCapture;
            fullscreenBox.Binding = defaults.FullscreenCapture;
            saveBox.Binding = defaults.SaveRecent;
            delayedBox.Binding = defaults.DelayedCapture;
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

        private void OnRestoreSaveDefaults(object sender, EventArgs e)
        {
            useDefaultSaveFolder = true;
            saveFolderBox.Text = ScreenshotStorage.PicturesFolder;
            fileNameTemplateBox.Text = ScreenshotStorage.DefaultFileNameTemplate;
            UpdateFileNamePreview();
        }

        private void UpdateFileNamePreview()
        {
            var preview = ScreenshotStorage.PreviewFileName(fileNameTemplateBox.Text);
            fileNamePreview.Text = "预览：" + preview;
        }

        private void OnBindingChanged(object sender, EventArgs e)
        {
            if (transferringBinding) return;
            var changed = sender as HotkeyBox;
            if (changed == null) return;
            var binding = changed.Binding;
            if (binding.Key == Keys.None || binding.Modifiers == 0) return;

            transferringBinding = true;
            try
            {
                var boxes = new[] { quickBox, advancedBox, fullscreenBox, saveBox, delayedBox };
                foreach (var box in boxes)
                {
                    if (box == changed) continue;
                    var existing = box.Binding;
                    if (existing.Key == binding.Key && existing.Modifiers == binding.Modifiers)
                        box.Binding = new HotkeyBinding(0, Keys.None);
                }
            }
            finally
            {
                transferringBinding = false;
            }
        }

        internal bool TryGetSettings(out AppSettings settings, out string error)
        {
            settings = original.Clone();
            settings.SaveFolder = useDefaultSaveFolder
                ? string.Empty : saveFolderBox.Text.Trim();
            settings.FileNameTemplate = fileNameTemplateBox.Text.Trim();
            if (!ScreenshotStorage.TryValidateSettings(settings.SaveFolder,
                settings.FileNameTemplate, out error)) return false;
            settings.QuickCapture = quickBox.Binding;
            settings.AdvancedCapture = advancedBox.Binding;
            settings.FullscreenCapture = fullscreenBox.Binding;
            settings.SaveRecent = saveBox.Binding;
            settings.DelayedCapture = delayedBox.Binding;

            var bindings = new[]
            {
                settings.QuickCapture, settings.AdvancedCapture,
                settings.FullscreenCapture, settings.SaveRecent, settings.DelayedCapture
            };
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < bindings.Length; index++)
            {
                if (bindings[index].Key == Keys.None || bindings[index].Modifiers == 0)
                    continue;

                var serialized = bindings[index].Serialize();
                if (!used.Add(serialized))
                {
                    error = "不同功能不能使用相同的快捷键。";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }
}
