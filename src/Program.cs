using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Miashot
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            NativeMethods.EnableBestDpiAwareness();

            if (args.Length > 0 && string.Equals(args[0], "--self-test",
                StringComparison.OrdinalIgnoreCase))
                return RunSelfTest(args.Length > 1 ? args[1] : null);
            var startupLaunch = args.Length > 0 && string.Equals(args[0], "--startup",
                StringComparison.OrdinalIgnoreCase);
            bool createdNew;
            using (var mutex = new Mutex(true, ProductInfo.MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    if (!startupLaunch)
                        MessageBox.Show(ProductInfo.DisplayName + " 已经在运行。",
                            ProductInfo.DisplayName,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 2;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MiashotContext(startupLaunch));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private static int RunSelfTest(string resultPath)
        {
            try
            {
                using (var image = new Bitmap(64, 48, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(image))
                    {
                        graphics.Clear(Color.FromArgb(22, 91, 177));
                        graphics.FillRectangle(Brushes.White, 8, 8, 48, 32);
                    }
                    ClipboardHelper.SetImage(image);
                }

                using (var clipboardImage = Clipboard.GetImage())
                {
                    if (clipboardImage == null || clipboardImage.Width != 64 || clipboardImage.Height != 48)
                        throw new InvalidOperationException("Clipboard image verification failed.");
                }

                if (!string.IsNullOrWhiteSpace(resultPath))
                    File.WriteAllText(resultPath, "SELF_TEST_OK: 快截 clipboard image 64x48");
                return 0;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(resultPath))
                    File.WriteAllText(resultPath, "SELF_TEST_FAILED: " + ex);
                return 1;
            }
        }

    }

    internal sealed class MiashotContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly Icon appIcon;
        private readonly HotkeyWindow hotkeyWindow;
        private readonly System.Windows.Forms.Timer captureDelay;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem quickItem;
        private readonly ToolStripMenuItem advancedItem;
        private readonly ToolStripMenuItem fullscreenItem;
        private readonly ToolStripMenuItem saveRecentItem;
        private readonly ToolStripMenuItem delayedItem;
        private readonly ToolStripMenuItem autoSaveItem;
        private readonly ToolStripMenuItem autoStartItem;

        private AppSettings settings;
        private CaptureOverlay overlay;
        private CaptureMode pendingMode;
        private bool pendingFullscreen;
        private IntPtr previousForeground;
        private Bitmap lastCapture;
        private string lastFingerprint;
        private string lastSavedFingerprint;
        private string lastSavedPath;
        private bool isExiting;
        private bool hotkeysSuspended;

        internal MiashotContext(bool startupLaunch)
        {
            settings = AppSettings.Load();
            appIcon = LoadApplicationIcon();
            hotkeyWindow = new HotkeyWindow();
            hotkeyWindow.HotkeyPressed += OnHotkeyPressed;

            quickItem = new ToolStripMenuItem();
            quickItem.Font = new Font(quickItem.Font, FontStyle.Bold);
            quickItem.Click += delegate { QueueRegionCapture(CaptureMode.Quick); };
            advancedItem = new ToolStripMenuItem();
            advancedItem.Click += delegate { QueueRegionCapture(CaptureMode.Advanced); };
            fullscreenItem = new ToolStripMenuItem();
            fullscreenItem.Click += delegate { QueueFullscreenCapture(); };
            saveRecentItem = new ToolStripMenuItem();
            saveRecentItem.Click += delegate { SaveRecentCapture(); };
            delayedItem = new ToolStripMenuItem();
            delayedItem.Click += delegate { QueueRegionCapture(CaptureMode.Quick, 3000); };

            autoSaveItem = new ToolStripMenuItem("截图后自动保存 PNG") { CheckOnClick = false };
            autoSaveItem.Click += OnAutoSaveClick;
            autoStartItem = new ToolStripMenuItem("开机自动启动") { CheckOnClick = false };
            autoStartItem.Click += OnAutoStartClick;

            menu = new ContextMenuStrip();
            menu.Items.Add(quickItem);
            menu.Items.Add(advancedItem);
            menu.Items.Add(fullscreenItem);
            menu.Items.Add(saveRecentItem);
            menu.Items.Add(delayedItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autoSaveItem);
            menu.Items.Add(autoStartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("打开“图片”文件夹", null, OnOpenPictures));
            menu.Items.Add(new ToolStripMenuItem("截图保存设置...", null, OnSaveSettingsClick));
            menu.Items.Add(new ToolStripMenuItem("快捷键设置...", null, OnSettingsClick));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("退出", null, OnExitClick));
            menu.Opening += delegate { RefreshMenuState(); };

            trayIcon = new NotifyIcon
            {
                Icon = appIcon,
                Text = ProductInfo.TrayDescription,
                ContextMenuStrip = menu,
                Visible = true
            };
            trayIcon.DoubleClick += delegate { QueueRegionCapture(CaptureMode.Quick); };
            trayIcon.BalloonTipClicked += delegate
            {
                if (!string.IsNullOrWhiteSpace(lastSavedPath)) OpenPicturesFolder();
            };

            captureDelay = new System.Windows.Forms.Timer { Interval = 120 };
            captureDelay.Tick += OnCaptureDelay;

            RefreshMenuLabels();
            RefreshMenuState();
            var failed = hotkeyWindow.Apply(settings.GetBindings());
            if (failed.Count > 0)
                ShowHotkeyConflictMessage(failed);
        }

        private static Icon LoadApplicationIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            return (Icon)SystemIcons.Application.Clone();
        }

        private void RefreshMenuLabels()
        {
            quickItem.Text = "快速区域截图    " + settings.QuickCapture.DisplayText;
            advancedItem.Text = "高级区域截图    " + settings.AdvancedCapture.DisplayText;
            fullscreenItem.Text = "当前显示器全屏截图    " + settings.FullscreenCapture.DisplayText;
            saveRecentItem.Text = "保存最近截图    " + settings.SaveRecent.DisplayText;
            delayedItem.Text = "3 秒后区域截图    " + settings.DelayedCapture.DisplayText;
        }

        private void RefreshMenuState()
        {
            autoSaveItem.Checked = settings.AutoSave;
            try { autoStartItem.Checked = StartupManager.IsEnabled(); }
            catch { autoStartItem.Checked = false; }
            saveRecentItem.Enabled = lastCapture != null;
        }

        private void OnHotkeyPressed(object sender, HotkeyPressedEventArgs e)
        {
            if (hotkeysSuspended) return;
            if (e.Command == HotkeyCommand.QuickCapture) QueueRegionCapture(CaptureMode.Quick);
            else if (e.Command == HotkeyCommand.AdvancedCapture) QueueRegionCapture(CaptureMode.Advanced);
            else if (e.Command == HotkeyCommand.FullscreenCapture) QueueFullscreenCapture();
            else if (e.Command == HotkeyCommand.SaveRecent) SaveRecentCapture();
            else if (e.Command == HotkeyCommand.DelayedCapture)
                QueueRegionCapture(CaptureMode.Quick, 3000);
        }

        private void QueueRegionCapture(CaptureMode mode)
        {
            QueueRegionCapture(mode, 120);
        }

        private void QueueRegionCapture(CaptureMode mode, int delayMilliseconds)
        {
            if (overlay != null || captureDelay.Enabled) return;
            previousForeground = NativeMethods.GetForegroundWindow();
            pendingMode = mode;
            pendingFullscreen = false;
            captureDelay.Interval = delayMilliseconds;
            captureDelay.Start();
        }

        private void QueueFullscreenCapture()
        {
            if (overlay != null || captureDelay.Enabled) return;
            previousForeground = NativeMethods.GetForegroundWindow();
            pendingFullscreen = true;
            captureDelay.Interval = 120;
            captureDelay.Start();
        }

        private void OnCaptureDelay(object sender, EventArgs e)
        {
            captureDelay.Stop();
            try
            {
                if (pendingFullscreen)
                {
                    CaptureCurrentScreen();
                    return;
                }

                var bounds = SystemInformation.VirtualScreen;
                Bitmap snapshot = null;
                try
                {
                    snapshot = CaptureRectangle(bounds);
                    overlay = new CaptureOverlay(snapshot, bounds, pendingMode, appIcon);
                    snapshot = null;
                }
                finally
                {
                    if (snapshot != null) snapshot.Dispose();
                }
                overlay.CaptureCompleted += OnCaptureCompleted;
                overlay.CaptureCanceled += OnCaptureCanceled;
                overlay.SaveRequested += OnAdvancedSaveRequested;
                overlay.FormClosed += OnOverlayClosed;
                overlay.Show();
                overlay.Activate();
            }
            catch (Exception ex)
            {
                ShowError("无法开始截图", ex);
                RestorePreviousFocus();
            }
        }

        private static Bitmap CaptureRectangle(Rectangle bounds)
        {
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size,
                        CopyPixelOperation.SourceCopy);
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private void CaptureCurrentScreen()
        {
            pendingFullscreen = false;
            try
            {
                var screen = Screen.FromPoint(Cursor.Position);
                using (var image = CaptureRectangle(screen.Bounds))
                {
                    UpdateLastCapture(image);
                    string saveError = null;
                    if (settings.AutoSave)
                        try { EnsureLastCaptureSaved(); }
                        catch (Exception ex) { saveError = ex.Message; }

                    try { ClipboardHelper.SetImage(image); }
                    catch (Exception ex) { ShowError("全屏截图完成，但复制失败", ex); return; }

                    if (saveError != null)
                        ShowError("全屏截图已复制，但自动保存失败", new Exception(saveError));
                    else
                        ShowInfo("已复制当前屏幕", "图片已进入剪贴板。", 1400);
                }
            }
            finally
            {
                RestorePreviousFocus();
            }
        }

        private void OnCaptureCompleted(object sender, CaptureImageEventArgs e)
        {
            using (e.Image)
            {
                UpdateLastCapture(e.Image);
                if (settings.AutoSave)
                {
                    try { EnsureLastCaptureSaved(); }
                    catch (Exception ex) { ShowError("截图已完成，但自动保存失败", ex); }
                }

                try { ClipboardHelper.SetImage(e.Image); }
                catch (Exception ex) { ShowError("截图已完成，但复制到剪贴板失败", ex); }
            }
        }

        private void OnAdvancedSaveRequested(object sender, CaptureImageEventArgs e)
        {
            try
            {
                var fingerprint = Fingerprint(e.Image);
                if (fingerprint == lastSavedFingerprint && !string.IsNullOrWhiteSpace(lastSavedPath) &&
                    File.Exists(lastSavedPath))
                {
                    e.SavedPath = lastSavedPath;
                    ShowInfo("截图已经保存", Path.GetFileName(lastSavedPath), 1700);
                    return;
                }

                var path = ScreenshotStorage.SavePng(e.Image);
                UpdateLastCapture(e.Image);
                lastSavedFingerprint = lastFingerprint;
                lastSavedPath = path;
                e.SavedPath = path;
                ShowInfo("截图已保存", Path.GetFileName(path), 2000);
            }
            catch (Exception ex)
            {
                ShowError(sender as IWin32Window, "保存截图失败", ex);
            }
        }

        private void OnCaptureCanceled(object sender, EventArgs e)
        {
            // 取消不会改变最近截图。
        }

        private void OnOverlayClosed(object sender, FormClosedEventArgs e)
        {
            var closed = overlay;
            if (closed != null)
            {
                closed.CaptureCompleted -= OnCaptureCompleted;
                closed.CaptureCanceled -= OnCaptureCanceled;
                closed.SaveRequested -= OnAdvancedSaveRequested;
                closed.FormClosed -= OnOverlayClosed;
                overlay = null;
            }
            RestorePreviousFocus();
        }

        private void RestorePreviousFocus()
        {
            if (previousForeground == IntPtr.Zero) return;
            var current = NativeMethods.GetForegroundWindow();
            uint processId;
            NativeMethods.GetWindowThreadProcessId(current, out processId);
            if (current == IntPtr.Zero || processId == (uint)Process.GetCurrentProcess().Id)
                NativeMethods.SetForegroundWindow(previousForeground);
            previousForeground = IntPtr.Zero;
        }

        private void UpdateLastCapture(Image image)
        {
            var fingerprint = Fingerprint(image);
            if (lastCapture != null) lastCapture.Dispose();
            lastCapture = new Bitmap(image);
            lastFingerprint = fingerprint;
            if (!string.Equals(lastSavedFingerprint, fingerprint, StringComparison.Ordinal))
            {
                lastSavedFingerprint = null;
                lastSavedPath = null;
            }
            saveRecentItem.Enabled = true;
        }

        private string EnsureLastCaptureSaved()
        {
            if (lastCapture == null) return null;
            if (lastFingerprint == lastSavedFingerprint && !string.IsNullOrWhiteSpace(lastSavedPath) &&
                File.Exists(lastSavedPath)) return lastSavedPath;
            lastSavedPath = ScreenshotStorage.SavePng(lastCapture);
            lastSavedFingerprint = lastFingerprint;
            return lastSavedPath;
        }

        private void SaveRecentCapture()
        {
            if (lastCapture == null)
            {
                ShowInfo("还没有可保存的截图", "请先完成一次截图。", 1800);
                return;
            }
            try
            {
                var alreadySaved = lastFingerprint == lastSavedFingerprint &&
                    !string.IsNullOrWhiteSpace(lastSavedPath) && File.Exists(lastSavedPath);
                var path = EnsureLastCaptureSaved();
                ShowInfo(alreadySaved ? "截图已经保存" : "截图已保存", Path.GetFileName(path), 2000);
            }
            catch (Exception ex)
            {
                ShowError("保存最近截图失败", ex);
            }
        }

        private static string Fingerprint(Image image)
        {
            using (var memory = new MemoryStream())
            using (var sha = SHA256.Create())
            {
                image.Save(memory, ImageFormat.Png);
                var hash = sha.ComputeHash(memory.ToArray());
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash) builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        private void OnAutoSaveClick(object sender, EventArgs e)
        {
            settings.AutoSave = !settings.AutoSave;
            autoSaveItem.Checked = settings.AutoSave;
            try { settings.Save(); }
            catch (Exception ex) { ShowError("设置已生效，但无法保存设置", ex); }
        }

        private void OnAutoStartClick(object sender, EventArgs e)
        {
            try
            {
                StartupManager.SetEnabled(!StartupManager.IsEnabled());
                autoStartItem.Checked = StartupManager.IsEnabled();
            }
            catch (Exception ex)
            {
                ShowError("无法修改开机自动启动设置", ex);
            }
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            hotkeysSuspended = true;
            var settingsApplied = false;
            hotkeyWindow.SuspendRegistrations();
            try
            {
                using (var form = new SettingsForm(settings, appIcon))
                {
                    while (form.ShowDialog() == DialogResult.OK)
                    {
                        AppSettings updated;
                        string validationError;
                        if (!form.TryGetSettings(out updated, out validationError))
                        {
                            MessageBox.Show(validationError, ProductInfo.DisplayName, MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                            continue;
                        }

                        HotkeyCommand failed;
                        if (!hotkeyWindow.TryApplyAll(updated.GetBindings(), out failed))
                        {
                            MessageBox.Show(CommandName(failed) + "快捷键已被其他程序占用，请更换。",
                                ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            hotkeyWindow.SuspendRegistrations();
                            continue;
                        }

                        settingsApplied = true;
                        updated.AutoSave = settings.AutoSave;
                        settings = updated;
                        try { settings.Save(); }
                        catch (Exception ex) { ShowError("快捷键已生效，但无法保存设置", ex); }
                        RefreshMenuLabels();
                        break;
                    }
                }
            }
            finally
            {
                if (!settingsApplied)
                {
                    var failed = hotkeyWindow.RestoreRegistrations();
                    if (failed.Count > 0) ShowHotkeyConflictMessage(failed);
                }
                hotkeysSuspended = false;
            }
        }

        private void OnSaveSettingsClick(object sender, EventArgs e)
        {
            using (var form = new SaveSettingsForm(settings, appIcon))
            {
                while (form.ShowDialog() == DialogResult.OK)
                {
                    AppSettings updated;
                    string validationError;
                    if (!form.TryGetSettings(out updated, out validationError))
                    {
                        MessageBox.Show(validationError, ProductInfo.DisplayName,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    updated.AutoSave = settings.AutoSave;
                    settings = updated;
                    try { settings.Save(); }
                    catch (Exception ex) { ShowError("保存设置已生效，但无法写入配置", ex); }
                    break;
                }
            }
        }

        private static string CommandName(HotkeyCommand command)
        {
            if (command == HotkeyCommand.QuickCapture) return "快速区域截图";
            if (command == HotkeyCommand.AdvancedCapture) return "高级区域截图";
            if (command == HotkeyCommand.FullscreenCapture) return "当前显示器全屏截图";
            if (command == HotkeyCommand.DelayedCapture) return "3 秒后区域截图";
            return "保存最近截图";
        }

        private void ShowHotkeyConflictMessage(IList<HotkeyCommand> failed)
        {
            var lines = new List<string>();
            foreach (var command in failed) lines.Add("• " + CommandName(command));
            MessageBox.Show("以下快捷键已被其他程序占用：\n\n" +
                string.Join("\n", lines.ToArray()) +
                "\n\n快截会继续运行，可通过托盘菜单使用功能或修改快捷键。",
                "快截快捷键冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnOpenPictures(object sender, EventArgs e)
        {
            OpenPicturesFolder();
        }

        private void OpenPicturesFolder()
        {
            try
            {
                var folder = ScreenshotStorage.ResolveSaveFolder(settings.SaveFolder);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", folder);
            }
            catch (Exception ex) { ShowError("无法打开“图片”文件夹", ex); }
        }

        private void ShowInfo(string title, string message, int milliseconds)
        {
            trayIcon.ShowBalloonTip(milliseconds, title, message, ToolTipIcon.Info);
        }

        private static void ShowError(string title, Exception error)
        {
            MessageBox.Show(title + "：" + error.Message, ProductInfo.DisplayName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void ShowError(IWin32Window owner, string title, Exception error)
        {
            if (owner == null)
            {
                ShowError(title, error);
                return;
            }
            MessageBox.Show(owner, title + "：" + error.Message, ProductInfo.DisplayName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            isExiting = true;
            if (overlay != null) overlay.Close();
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            if (!isExiting) isExiting = true;
            captureDelay.Stop();
            captureDelay.Dispose();
            hotkeyWindow.Dispose();
            if (lastCapture != null) lastCapture.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            menu.Dispose();
            appIcon.Dispose();
            base.ExitThreadCore();
        }
    }
}
