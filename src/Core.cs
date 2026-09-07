using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Miashot
{
    internal enum CaptureMode
    {
        Quick,
        Advanced
    }

    internal enum HotkeyCommand
    {
        QuickCapture,
        AdvancedCapture,
        FullscreenCapture,
        SaveRecent,
        DelayedCapture
    }

    internal sealed class HotkeyBinding
    {
        internal Keys Key;
        internal uint Modifiers;

        internal HotkeyBinding(uint modifiers, Keys key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        internal HotkeyBinding Clone()
        {
            return new HotkeyBinding(Modifiers, Key);
        }

        internal string DisplayText
        {
            get
            {
                if (Key == Keys.None || Modifiers == 0) return "未设置";
                var parts = new List<string>();
                if ((Modifiers & NativeMethods.ModControl) != 0) parts.Add("Ctrl");
                if ((Modifiers & NativeMethods.ModAlt) != 0) parts.Add("Alt");
                if ((Modifiers & NativeMethods.ModShift) != 0) parts.Add("Shift");
                if ((Modifiers & NativeMethods.ModWin) != 0) parts.Add("Win");
                parts.Add(Key.ToString());
                return string.Join("+", parts.ToArray());
            }
        }

        internal string Serialize()
        {
            return Modifiers.ToString(CultureInfo.InvariantCulture) + "," +
                ((int)Key).ToString(CultureInfo.InvariantCulture);
        }

        internal static HotkeyBinding Parse(string value, HotkeyBinding fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback.Clone();
            var parts = value.Split(',');
            uint modifiers;
            int key;
            if (parts.Length != 2 ||
                !uint.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out modifiers) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out key))
                return fallback.Clone();

            return new HotkeyBinding(modifiers, (Keys)key);
        }
    }

    internal sealed class AppSettings
    {
        internal bool AutoSave;
        internal string SaveFolder;
        internal string FileNameTemplate;
        internal HotkeyBinding QuickCapture;
        internal HotkeyBinding AdvancedCapture;
        internal HotkeyBinding FullscreenCapture;
        internal HotkeyBinding SaveRecent;
        internal HotkeyBinding DelayedCapture;

        internal static AppSettings Defaults()
        {
            return new AppSettings
            {
                AutoSave = false,
                SaveFolder = string.Empty,
                FileNameTemplate = ScreenshotStorage.DefaultFileNameTemplate,
                QuickCapture = new HotkeyBinding(NativeMethods.ModAlt, Keys.A),
                AdvancedCapture = new HotkeyBinding(NativeMethods.ModAlt, Keys.S),
                FullscreenCapture = new HotkeyBinding(NativeMethods.ModAlt, Keys.D),
                SaveRecent = new HotkeyBinding(NativeMethods.ModAlt, Keys.W),
                DelayedCapture = new HotkeyBinding(NativeMethods.ModAlt, Keys.E)
            };
        }

        internal AppSettings Clone()
        {
            return new AppSettings
            {
                AutoSave = AutoSave,
                SaveFolder = SaveFolder,
                FileNameTemplate = FileNameTemplate,
                QuickCapture = QuickCapture.Clone(),
                AdvancedCapture = AdvancedCapture.Clone(),
                FullscreenCapture = FullscreenCapture.Clone(),
                SaveRecent = SaveRecent.Clone(),
                DelayedCapture = DelayedCapture.Clone()
            };
        }

        internal static string SettingsPath
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(root, ProductInfo.SettingsFolderName, "settings.ini");
            }
        }

        internal static AppSettings Load()
        {
            var defaults = Defaults();
            try
            {
                var settingsPath = ResolveSettingsPathForLoad();
                if (!File.Exists(settingsPath)) return defaults;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rawLine in File.ReadAllLines(settingsPath, Encoding.UTF8))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    var separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                }

                string value;
                bool autoSave;
                if (values.TryGetValue("AutoSave", out value) && bool.TryParse(value, out autoSave))
                    defaults.AutoSave = autoSave;
                if (values.TryGetValue("SaveFolder", out value))
                    defaults.SaveFolder = value;
                if (values.TryGetValue("FileNameTemplate", out value))
                    defaults.FileNameTemplate = value;
                if (values.TryGetValue("QuickCapture", out value))
                    defaults.QuickCapture = HotkeyBinding.Parse(value, defaults.QuickCapture);
                if (values.TryGetValue("AdvancedCapture", out value))
                    defaults.AdvancedCapture = HotkeyBinding.Parse(value, defaults.AdvancedCapture);
                if (values.TryGetValue("FullscreenCapture", out value))
                    defaults.FullscreenCapture = HotkeyBinding.Parse(value, defaults.FullscreenCapture);
                if (values.TryGetValue("SaveRecent", out value))
                    defaults.SaveRecent = HotkeyBinding.Parse(value, defaults.SaveRecent);
                if (values.TryGetValue("DelayedCapture", out value))
                    defaults.DelayedCapture = HotkeyBinding.Parse(value, defaults.DelayedCapture);

                string saveError;
                if (!ScreenshotStorage.TryValidateSettings(defaults.SaveFolder,
                    defaults.FileNameTemplate, out saveError))
                {
                    defaults.SaveFolder = string.Empty;
                    defaults.FileNameTemplate = ScreenshotStorage.DefaultFileNameTemplate;
                }
            }
            catch
            {
                return Defaults();
            }

            return defaults;
        }

        private static string ResolveSettingsPathForLoad()
        {
            if (File.Exists(SettingsPath)) return SettingsPath;
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var legacyFolder in ProductInfo.LegacySettingsFolderNames)
            {
                var legacyPath = Path.Combine(root, legacyFolder, "settings.ini");
                if (File.Exists(legacyPath)) return legacyPath;
            }
            return SettingsPath;
        }

        internal void Save()
        {
            var folder = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            var lines = new[]
            {
                "# Kuaijie settings",
                "AutoSave=" + AutoSave,
                "SaveFolder=" + (SaveFolder ?? string.Empty),
                "FileNameTemplate=" + (FileNameTemplate ?? string.Empty),
                "QuickCapture=" + QuickCapture.Serialize(),
                "AdvancedCapture=" + AdvancedCapture.Serialize(),
                "FullscreenCapture=" + FullscreenCapture.Serialize(),
                "SaveRecent=" + SaveRecent.Serialize(),
                "DelayedCapture=" + DelayedCapture.Serialize()
            };
            File.WriteAllLines(SettingsPath, lines, new UTF8Encoding(false));
        }

        internal IDictionary<HotkeyCommand, HotkeyBinding> GetBindings()
        {
            return new Dictionary<HotkeyCommand, HotkeyBinding>
            {
                { HotkeyCommand.QuickCapture, QuickCapture },
                { HotkeyCommand.AdvancedCapture, AdvancedCapture },
                { HotkeyCommand.FullscreenCapture, FullscreenCapture },
                { HotkeyCommand.SaveRecent, SaveRecent },
                { HotkeyCommand.DelayedCapture, DelayedCapture }
            };
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private readonly Dictionary<int, HotkeyCommand> registered = new Dictionary<int, HotkeyCommand>();
        private IDictionary<HotkeyCommand, HotkeyBinding> currentBindings;

        internal event EventHandler<HotkeyPressedEventArgs> HotkeyPressed;

        internal HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        internal IList<HotkeyCommand> Apply(IDictionary<HotkeyCommand, HotkeyBinding> bindings)
        {
            UnregisterAll();
            currentBindings = CloneBindings(bindings);
            var failed = new List<HotkeyCommand>();
            foreach (var pair in bindings)
            {
                if (pair.Value == null || pair.Value.Key == Keys.None ||
                    pair.Value.Modifiers == 0) continue;
                var id = 0x4D00 + (int)pair.Key;
                if (NativeMethods.RegisterHotKey(Handle, id,
                    pair.Value.Modifiers | NativeMethods.ModNoRepeat, (uint)pair.Value.Key))
                    registered[id] = pair.Key;
                else
                    failed.Add(pair.Key);
            }
            return failed;
        }

        internal bool TryApplyAll(IDictionary<HotkeyCommand, HotkeyBinding> bindings, out HotkeyCommand failedCommand)
        {
            var oldBindings = currentBindings == null ? null : CloneBindings(currentBindings);
            var failed = Apply(bindings);
            if (failed.Count == 0)
            {
                failedCommand = HotkeyCommand.QuickCapture;
                return true;
            }

            failedCommand = failed[0];
            if (oldBindings != null) Apply(oldBindings);
            return false;
        }

        internal void SuspendRegistrations()
        {
            UnregisterAll();
        }

        internal IList<HotkeyCommand> RestoreRegistrations()
        {
            if (currentBindings == null)
                return new List<HotkeyCommand>();
            return Apply(currentBindings);
        }

        private static IDictionary<HotkeyCommand, HotkeyBinding> CloneBindings(
            IDictionary<HotkeyCommand, HotkeyBinding> source)
        {
            var result = new Dictionary<HotkeyCommand, HotkeyBinding>();
            foreach (var pair in source) result[pair.Key] = pair.Value.Clone();
            return result;
        }

        protected override void WndProc(ref Message m)
        {
            HotkeyCommand command;
            if (m.Msg == WmHotkey && registered.TryGetValue(m.WParam.ToInt32(), out command))
            {
                var handler = HotkeyPressed;
                if (handler != null) handler(this, new HotkeyPressedEventArgs(command));
            }
            base.WndProc(ref m);
        }

        private void UnregisterAll()
        {
            foreach (var id in new List<int>(registered.Keys))
                NativeMethods.UnregisterHotKey(Handle, id);
            registered.Clear();
        }

        public void Dispose()
        {
            UnregisterAll();
            DestroyHandle();
        }
    }

    internal sealed class HotkeyPressedEventArgs : EventArgs
    {
        internal readonly HotkeyCommand Command;
        internal HotkeyPressedEventArgs(HotkeyCommand command) { Command = command; }
    }

    internal static class ScreenshotStorage
    {
        private static readonly object SaveLock = new object();

        internal static string DefaultFileNameTemplate
        {
            get
            {
                return ProductInfo.ScreenshotPrefix.TrimEnd('_') + "_{日期}_{序号}";
            }
        }

        internal static string PicturesFolder
        {
            get
            {
                var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrWhiteSpace(folder))
                    folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return folder;
            }
        }

        internal static string SavePng(Image image)
        {
            lock (SaveLock)
            {
                var settings = AppSettings.Load();
                var folder = string.IsNullOrWhiteSpace(settings.SaveFolder)
                    ? PicturesFolder : settings.SaveFolder.Trim();
                var template = string.IsNullOrWhiteSpace(settings.FileNameTemplate)
                    ? DefaultFileNameTemplate : settings.FileNameTemplate.Trim();
                string validationError;
                if (!TryValidateSettings(folder, template, out validationError))
                    throw new InvalidOperationException(validationError);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var now = DateTime.Now;
                var hasSequence = template.IndexOf("{序号}",
                    StringComparison.Ordinal) >= 0;
                var attempt = 0;
                while (true)
                {
                    attempt++;
                    var fileName = RenderFileName(template, now, attempt);
                    if (!hasSequence && attempt > 1)
                        fileName += "_" + (attempt - 1).ToString("000",
                            CultureInfo.InvariantCulture);
                    var path = Path.Combine(folder, fileName + ".png");
                    if (File.Exists(path)) continue;
                    try
                    {
                        using (var stream = new FileStream(path, FileMode.CreateNew,
                            FileAccess.Write, FileShare.Read))
                            image.Save(stream, ImageFormat.Png);
                        return path;
                    }
                    catch (IOException)
                    {
                        if (!File.Exists(path)) throw;
                    }
                }
            }
        }

        internal static bool TryValidateSettings(string configuredFolder,
            string template, out string error)
        {
            var folder = string.IsNullOrWhiteSpace(configuredFolder)
                ? PicturesFolder : configuredFolder.Trim();
            try
            {
                if (!Path.IsPathRooted(folder))
                {
                    error = "保存位置必须是完整的文件夹路径。";
                    return false;
                }
                Path.GetFullPath(folder);
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is NotSupportedException) &&
                    !(ex is PathTooLongException)) throw;
                error = "保存位置无效，请重新选择文件夹。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(template))
            {
                error = "文件名格式不能为空。";
                return false;
            }
            template = template.Trim();
            if (template.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                error = "文件名格式无需填写 .png 扩展名。";
                return false;
            }
            foreach (var invalid in Path.GetInvalidFileNameChars())
                if (template.IndexOf(invalid) >= 0)
                {
                    error = "文件名格式包含 Windows 不允许的字符：“" + invalid + "”。";
                    return false;
                }

            var remaining = template.Replace("{日期}", string.Empty)
                .Replace("{时间}", string.Empty)
                .Replace("{序号}", string.Empty);
            if (remaining.IndexOf('{') >= 0 || remaining.IndexOf('}') >= 0)
            {
                error = "文件名格式中存在不支持的变量。";
                return false;
            }
            var preview = RenderFileName(template,
                new DateTime(2026, 9, 7, 15, 30, 45), 1);
            if (string.IsNullOrWhiteSpace(preview) || preview == "." || preview == "..")
            {
                error = "文件名格式无法生成有效文件名。";
                return false;
            }

            error = null;
            return true;
        }

        internal static string PreviewFileName(string template)
        {
            string error;
            if (!TryValidateSettings(string.Empty, template, out error)) return error;
            return RenderFileName(template.Trim(),
                new DateTime(2026, 9, 7, 15, 30, 45), 1) + ".png";
        }

        private static string RenderFileName(string template, DateTime now, int sequence)
        {
            var number = sequence < 1000
                ? sequence.ToString("000", CultureInfo.InvariantCulture)
                : sequence.ToString(CultureInfo.InvariantCulture);
            return template.Replace("{日期}",
                    now.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
                .Replace("{时间}", now.ToString("HHmmss", CultureInfo.InvariantCulture))
                .Replace("{序号}", number);
        }
    }

    internal static class ClipboardHelper
    {
        internal static void SetImage(Image image)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetImage(image);
                    return;
                }
                catch (ExternalException ex)
                {
                    lastError = ex;
                    Thread.Sleep(70);
                }
            }
            if (lastError != null) throw lastError;
        }

        internal static void SetText(string text)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetText(text ?? string.Empty, TextDataFormat.UnicodeText);
                    return;
                }
                catch (ExternalException ex)
                {
                    lastError = ex;
                    Thread.Sleep(70);
                }
            }
            if (lastError != null) throw lastError;
        }
    }

    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        internal static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                var value = key == null ? null :
                    key.GetValue(ProductInfo.StartupValueName) as string;
                var expected = "\"" + Application.ExecutablePath + "\" --startup";
                if (string.Equals(value == null ? null : value.Trim(), expected,
                    StringComparison.OrdinalIgnoreCase)) return true;

                if (key != null)
                    foreach (var legacyName in ProductInfo.LegacyStartupValueNames)
                        if (!string.IsNullOrWhiteSpace(key.GetValue(legacyName) as string))
                        {
                            try { SetEnabled(true); return true; }
                            catch { return false; }
                        }
                return false;
            }
        }

        internal static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new InvalidOperationException("无法打开 Windows 启动设置。");
                foreach (var legacyName in ProductInfo.LegacyStartupValueNames)
                    key.DeleteValue(legacyName, false);
                if (enabled)
                    key.SetValue(ProductInfo.StartupValueName,
                        "\"" + Application.ExecutablePath + "\" --startup",
                        RegistryValueKind.String);
                else
                    key.DeleteValue(ProductInfo.StartupValueName, false);
            }
        }
    }

    internal static class NativeMethods
    {
        internal const uint ModAlt = 0x0001;
        internal const uint ModControl = 0x0002;
        internal const uint ModShift = 0x0004;
        internal const uint ModWin = 0x0008;
        internal const uint ModNoRepeat = 0x4000;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
            internal NativePoint(int x, int y) { X = x; Y = y; }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(
            NativePoint point, uint flags);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        internal static void EnableBestDpiAwareness()
        {
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch (EntryPointNotFoundException)
            {
                SetProcessDPIAware();
            }
        }

        internal static float GetDpiForPoint(Point point)
        {
            try
            {
                var monitor = MonitorFromPoint(new NativePoint(point.X, point.Y), 2);
                uint dpiX;
                uint dpiY;
                if (monitor != IntPtr.Zero &&
                    GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0 && dpiX > 0)
                    return dpiX;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                return graphics.DpiX;
        }
    }
}
