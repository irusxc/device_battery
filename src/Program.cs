using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PeripheralPower
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--probe") {
                try { Json.Write(args[1], Scanner.Scan(s => Json.Write(args[1], s))); return 0; }
                catch (Exception ex) { Json.Write(args[1], new Snapshot { ScannedUtc = DateTime.UtcNow.ToString("o"), Errors = { "设备扫描失败：" + ex.GetType().Name + " · " + ex.Message } }); return 1; }
            }
            if (args.Length >= 2 && args[0] == "--self-test") return SelfTests.Run(args[1]);
            if (args.Length >= 2 && args[0] == "--ui-test") return UiTests.Run(args[1]);
            if (args.Length >= 2 && args[0] == "--monitor-test") return MonitorTests.Run(args[1]);
            if (args.Length >= 2 && args[0] == "--capture-demo") return Capture(args[1], null);
            if (args.Length >= 2 && args[0] == "--capture-settings") return Capture(args[1], null, "settings");
            if (args.Length >= 2 && args[0] == "--capture-narrow") return Capture(args[1], null, "narrow");
            if (args.Length >= 3 && args[0] == "--capture-real") return Capture(args[2], Json.Read<Snapshot>(args[1]));
            bool first;
            string suffix = DeviceState.Identity(Environment.UserName) + (args.Contains("--runtime-test-instance") ? "-test-v02" : "");
            using (var mutex = new Mutex(true, "Local\\PeripheralPower-" + suffix, out first))
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PeripheralPowerWake-" + suffix)) {
                if (!first) { wake.Set(); return 0; }
                try {
                    // This static panel does not need a persistent Direct3D device or GPU surfaces.
                    System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    using (var desktop = new DesktopApp(args.Contains("--demo"), false)) {
                        new WindowInteropHelper(desktop.Window).EnsureHandle();
                        var activation = ThreadPool.RegisterWaitForSingleObject(wake, delegate {
                            if (!application.Dispatcher.HasShutdownStarted) application.Dispatcher.BeginInvoke(new Action(desktop.Show));
                        }, null, Timeout.Infinite, false);
                        if (!args.Contains("--background")) desktop.Show();
                        application.Dispatcher.BeginInvoke(new Action(desktop.Refresh));
                        application.Run(); activation.Unregister(null);
                    }
                    return 0;
                } catch (Exception ex) {
                    Directory.CreateDirectory(Preferences.Folder);
                    File.WriteAllText(Path.Combine(Preferences.Folder, "error.log"), ex.ToString());
                    MessageBox.Show("程序未能启动：" + ex.Message + "\n\n详细信息保存在 " + Preferences.Folder, "栖电");
                    return 1;
                } finally { mutex.ReleaseMutex(); }
            }
        }
        private static int Capture(string path, Snapshot snapshot, string view = null)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using (var desktop = new DesktopApp(snapshot == null, true)) {
                if (snapshot != null) desktop.SetSnapshot(snapshot);
                if (view == "settings") ((System.Windows.Controls.Button)desktop.Window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (view == "narrow") { desktop.Window.Width = 640; desktop.Window.Height = 440; }
                desktop.Window.WindowStartupLocation = WindowStartupLocation.Manual;
                desktop.Window.Left = -20000; desktop.Window.Top = -20000;
                desktop.Window.ShowInTaskbar = false; desktop.Window.ShowActivated = false;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                timer.Tick += delegate {
                    timer.Stop(); desktop.Window.UpdateLayout();
                    var visual = (FrameworkElement)desktop.Window.Content;
                    var bitmap = new RenderTargetBitmap((int)(visual.ActualWidth * 1.5), (int)(visual.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var output = File.Create(path)) encoder.Save(output);
                    desktop.Quit();
                };
                desktop.Window.Show(); timer.Start(); app.Run();
            }
            return 0;
        }
    }
}
