using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PeripheralPower
{
    internal static class UiTests
    {
        private static int passed;
        private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; }
        private static T Find<T>(DesktopApp d, string name) where T : class { return (T)d.Window.FindName(name); }
        private static void Click(DesktopApp d, string name) { Find<Button>(d, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        internal static int Run(string output)
        {
            int code = 0;
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using (var desktop = new DesktopApp(false, true)) {
                desktop.Window.WindowStartupLocation = WindowStartupLocation.Manual;
                desktop.Window.Left = -20000; desktop.Window.Top = -20000; desktop.Window.ShowInTaskbar = false; desktop.Window.ShowActivated = false;
                desktop.Window.Show();
                application.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate {
                    try {
                        Check(Find<FrameworkElement>(desktop,"DemoBanner").Visibility == Visibility.Collapsed, "Default mode is real data");
                        Check(Find<TextBlock>(desktop,"DeviceCount").Text == "00", "Empty scan has no fake devices");
                        var snapshot = new Snapshot { ScannedUtc = DateTime.UtcNow.ToString("o"), Devices = {
                            new DeviceState { Id="a", Name="A mouse", Kind="mouse", Status="ok", Battery=18 },
                            new DeviceState { Id="b", Name="B keyboard", Kind="keyboard", Status="unsupported" },
                            new DeviceState { Id="c", Name="C headset", Kind="headset", Status="ok", Battery=5, Charging=true }
                        }};
                        desktop.SetSnapshot(snapshot);
                        Check(Find<StackPanel>(desktop,"DeviceList").Children.Count == 3, "All device cards render");
                        Check(Find<TextBlock>(desktop,"SummaryTitle").Text == "1 台设备需要充电", "Charging battery excluded from low count");
                        Click(desktop,"LowButton");
                        Check(Find<StackPanel>(desktop,"DeviceList").Children.Count == 1, "Low filter excludes unknown and charging devices");
                        Click(desktop,"AllButton");
                        Check(Find<StackPanel>(desktop,"DeviceList").Children.Count == 3, "All filter restores devices");
                        Click(desktop,"SettingsButton");
                        Check(Find<FrameworkElement>(desktop,"SettingsPanel").Visibility == Visibility.Visible, "Settings navigation");
                        Click(desktop,"BackButton");
                        Check(Find<FrameworkElement>(desktop,"SettingsPanel").Visibility == Visibility.Collapsed, "Back navigation");
                        Click(desktop,"DemoButton");
                        Check(Find<FrameworkElement>(desktop,"DemoBanner").Visibility == Visibility.Visible, "Demo explicitly labelled");
                        Check(!Find<Button>(desktop,"RefreshButton").IsEnabled, "Demo does not query hardware");
                        Click(desktop,"ExitDemoButton");
                        Check(Find<FrameworkElement>(desktop,"DemoBanner").Visibility == Visibility.Collapsed, "Exit demo");
                        Check(Find<StackPanel>(desktop,"DeviceList").Children.Count == 3 && snapshot.Devices[0].Name == "A mouse", "Real results survive demo");
                        desktop.Window.Close();
                        Check(!desktop.Window.IsVisible, "Close hides the panel");
                        desktop.Window.Show();
                        Check(desktop.Window.IsVisible, "Panel can reopen after close");
                        desktop.Window.Width = 450; desktop.Window.Height = 540; desktop.Window.UpdateLayout();
                        Check(((FrameworkElement)desktop.Window.Content).ActualWidth > 0, "Minimum window size lays out");
                        File.WriteAllText(output, "PASS: " + passed + " UI checks.\r\nReal/demo separation, filters, settings, close/reopen, minimum size.\r\nWorking set: " + (Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024) + " MiB (UI test process).\r\n");
                    } catch (Exception ex) { code = 1; File.WriteAllText(output, "FAIL after " + passed + " UI checks: " + ex); }
                    finally { desktop.Quit(); }
                }));
                application.Run();
            }
            return code;
        }
    }
}
