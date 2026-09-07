using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Miashot.Tests
{
    internal static class UiSmokeTest
    {
        private const uint KeyUp = 0x0002;
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(
            uint flags, uint dx, uint dy, uint data, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern int GetGuiResources(IntPtr process, int flags);

        [STAThread]
        private static int Main(string[] args)
        {
            var executable = args.Length > 0 ? args[0] : "快截-标准版.exe";
            var output = args.Length > 1 ? args[1] : "ui-smoke-result.txt";
            Process app = null;
            try
            {
                EnsureNoKuaijieProcessIsRunning();
                app = Process.Start(executable);
                Thread.Sleep(1200);
                AssertRunning(app);

                DragCapture(0x12, 0x41, new Point(180, 180), new Point(540, 400));
                AssertClipboardSize(360, 220, "Alt+A");

                DragCapture(0x12, 0x53, new Point(240, 220), new Point(660, 480));
                PressKey(0x0D);
                Thread.Sleep(900);
                AssertClipboardSize(420, 260, "Alt+S + Enter");

                SendHotkey(0x12, 0x53);
                Thread.Sleep(350);
                Drag(new Point(220, 200), new Point(1120, 540));
                Thread.Sleep(300);
                Click(new Point(242, 568));
                Drag(new Point(340, 280), new Point(520, 390));
                PressKey(0x0D);
                Thread.Sleep(900);
                AssertClipboardSize(900, 340, "Alt+S rectangle annotation");
                AssertRedAnnotation();

                SendHotkey(0x12, 0x53);
                Thread.Sleep(300);
                Drag(new Point(300, 260), new Point(700, 500));
                PressKey(0x1B);
                Thread.Sleep(500);
                AssertRunning(app);

                var screen = Screen.PrimaryScreen;
                SetCursorPos(screen.Bounds.Left + screen.Bounds.Width / 2,
                    screen.Bounds.Top + screen.Bounds.Height / 2);
                SendHotkey(0x12, 0x44);
                Thread.Sleep(1200);
                AssertClipboardSize(screen.Bounds.Width, screen.Bounds.Height, "Alt+D");

                var gdiBefore = GetGuiResources(app.Handle, 0);
                for (var index = 0; index < 20; index++)
                {
                    SendHotkey(0x12, 0x41);
                    Thread.Sleep(220);
                    Drag(new Point(120, 120), new Point(300, 240));
                    Thread.Sleep(350);
                }
                AssertClipboardSize(180, 120, "repeated quick capture");
                var gdiAfter = GetGuiResources(app.Handle, 0);
                if (gdiAfter > gdiBefore + 12)
                    throw new InvalidOperationException("GDI resource growth: " +
                        gdiBefore + " -> " + gdiAfter);

                File.WriteAllText(output,
                    "UI_TEST_OK\r\nAlt+A=360x220\r\nAlt+S=420x260\r\n" +
                    "annotation=900x340\r\ncancel=ok\r\nAlt+D=" +
                    screen.Bounds.Width + "x" + screen.Bounds.Height +
                    "\r\nrepeated captures=20, GDI " + gdiBefore + " -> " + gdiAfter);
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(output, "UI_TEST_FAILED\r\n" + ex);
                return 1;
            }
            finally
            {
                if (app != null && !app.HasExited)
                {
                    app.Kill();
                    app.WaitForExit(3000);
                }
                if (app != null) app.Dispose();
            }
        }

        private static void DragCapture(byte modifier, byte key, Point start, Point end)
        {
            SendHotkey(modifier, key);
            Thread.Sleep(350);
            Drag(start, end);
            Thread.Sleep(900);
        }

        private static void Drag(Point start, Point end)
        {
            SetCursorPos(start.X, start.Y);
            Thread.Sleep(80);
            mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(120);
            SetCursorPos(end.X, end.Y);
            Thread.Sleep(120);
            mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        }

        private static void SendHotkey(byte modifier, byte key)
        {
            keybd_event(modifier, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KeyUp, UIntPtr.Zero);
            keybd_event(modifier, 0, KeyUp, UIntPtr.Zero);
        }

        private static void Click(Point point)
        {
            SetCursorPos(point.X, point.Y);
            Thread.Sleep(80);
            mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(100);
        }

        private static void PressKey(byte key)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KeyUp, UIntPtr.Zero);
        }

        private static void AssertClipboardSize(int width, int height, string step)
        {
            using (var image = Clipboard.GetImage())
            {
                if (image == null)
                    throw new InvalidOperationException(step + ": clipboard has no image.");
                if (image.Width != width || image.Height != height)
                    throw new InvalidOperationException(step + ": expected " + width + "x" +
                        height + ", actual " + image.Width + "x" + image.Height + ".");
            }
        }

        private static void AssertRedAnnotation()
        {
            using (var image = Clipboard.GetImage())
            using (var bitmap = image == null ? null : new Bitmap(image))
            {
                if (bitmap == null)
                    throw new InvalidOperationException("Annotation did not reach clipboard.");
                var found = false;
                for (var x = 116; x <= 125 && !found; x++)
                    for (var y = 76; y <= 194; y++)
                    {
                        var color = bitmap.GetPixel(x, y);
                        if (color.R > 210 && color.G < 105 && color.B < 120)
                        {
                            found = true;
                            break;
                        }
                    }
                if (!found)
                    throw new InvalidOperationException("Expected red rectangle was not found.");
            }
        }

        private static void AssertRunning(Process process)
        {
            if (process == null || process.HasExited)
                throw new InvalidOperationException("Kuaijie exited unexpectedly.");
        }

        private static void EnsureNoKuaijieProcessIsRunning()
        {
            var processNames = new[]
            {
                "快截", "快截-标准版", "快截-Lite", "Miashot",
                "Miashot-Standard", "Miashot-Lite", "Miashot-ProOCR"
            };
            foreach (var processName in processNames)
            {
                var processes = Process.GetProcessesByName(processName);
                try
                {
                    if (processes.Length > 0)
                        throw new InvalidOperationException(
                            "A Kuaijie process is already running: " + processName);
                }
                finally
                {
                    foreach (var process in processes) process.Dispose();
                }
            }
        }
    }
}
