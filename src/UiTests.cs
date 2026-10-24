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
                        Check(Find<TextBlock>(desktop,"DeviceCount").Text == "0", "Empty scan has no fake devices");
                        var snapshot = new Snapshot { ScannedUtc = DateTime.UtcNow.ToString("o"), Devices = {
                            new DeviceState { Id="a", Name="A mouse", Kind="mouse", Status="ok", Battery=18 },
                            new DeviceState { Id="b", Name="B keyboard", Kind="keyboard", Status="unsupported" },
                            new DeviceState { Id="c", Name="C headset", Kind="headset", Status="ok", Battery=5, Charging=true }
                        }};
                        desktop.SetSnapshot(snapshot);
                        Check(Find<StackPanel>(desktop,"DeviceList").Children.Count == 3, "All device cards render");
                        Check(Find<TextBlock>(desktop,"SummaryDetail").Text == "2 台电量可读 · 1 台低电量", "Charging battery excluded from low count");
                        Check(Find<StackPanel>(desktop,"DeviceList").Orientation == Orientation.Horizontal, "Devices form a single horizontal shelf");
                        Check(((Border)Find<StackPanel>(desktop,"DeviceList").Children[0]).BorderThickness == new Thickness(0), "Device tiles are borderless");
                        desktop.Window.UpdateLayout();
                        var first = (FrameworkElement)Find<StackPanel>(desktop,"DeviceList").Children[0];
                        var second = (FrameworkElement)Find<StackPanel>(desktop,"DeviceList").Children[1];
                        Check(second.TranslatePoint(new Point(0,0),desktop.Window).X > first.TranslatePoint(new Point(0,0),desktop.Window).X + first.ActualWidth, "Horizontal layout renders side by side");
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
                        desktop.Window.Width = 640; desktop.Window.Height = 440; desktop.Window.UpdateLayout();
                        Check(((FrameworkElement)desktop.Window.Content).ActualWidth > 0, "Minimum window size lays out");
                        Check(Find<ScrollViewer>(desktop,"DeviceScroll").ScrollableWidth > 0, "Narrow window allows horizontal scrolling");
                        var beforeScroll = Find<ScrollViewer>(desktop,"DeviceScroll").HorizontalOffset;
                        Find<ScrollViewer>(desktop,"DeviceScroll").ScrollToRightEnd(); desktop.Window.UpdateLayout();
                        Check(Find<ScrollViewer>(desktop,"DeviceScroll").HorizontalOffset > beforeScroll, "Horizontal scroll reaches clipped devices");
                        var retainedTile = Find<StackPanel>(desktop,"DeviceList").Children[0];
                        desktop.SetSnapshot(snapshot);
                        Check(Object.ReferenceEquals(retainedTile, Find<StackPanel>(desktop,"DeviceList").Children[0]), "Unchanged inventory does not rebuild focused tiles");
                        Click(desktop,"DemoButton"); desktop.Window.UpdateLayout();
                        Check(((FrameworkElement)Find<StackPanel>(desktop,"DeviceList").Children[0]).ActualHeight <= Find<ScrollViewer>(desktop,"DeviceScroll").ActualHeight - 12, "Short window keeps device battery fully visible");
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
