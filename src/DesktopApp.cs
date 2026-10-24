using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PeripheralPower
{
    internal sealed class DesktopApp : IDisposable
    {
        internal readonly Window Window;
        private readonly Preferences preferences;
        private readonly DispatcherTimer timer, debounce, inventoryTimer;
        private readonly DeviceMonitor monitor;
        private DeviceNotifications notifications;
        private bool notificationFallback;
        private readonly NotificationPolicy notificationPolicy = new NotificationPolicy();
        private readonly List<CatalogEntry> catalog = Catalog.Load();
        private readonly bool capture;
        private Forms.NotifyIcon tray;
        private System.Drawing.Icon trayIcon;
        private Process probe;
        private bool demo, lowOnly, quitting, loadingSettings;
        private bool busy { get { return monitor != null && monitor.BatteryBusy; } }
        private Snapshot real = new Snapshot();
        private string refreshError;
        private string deviceRenderKey;
        internal DesktopApp(bool demoMode, bool captureMode)
        {
            demo = demoMode; capture = captureMode; preferences = capture ? new Preferences() : Preferences.Load();
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                Window = (Window)XamlReader.Load(resource);
            Window.Height = Math.Min(Window.Height, Math.Max(Window.MinHeight, SystemParameters.WorkArea.Height - 24));
            Window.SourceInitialized += delegate {
                IntPtr handle = new WindowInteropHelper(Window).Handle;
                HwndSource.FromHwnd(handle).AddHook(WindowMessage);
                if (!capture) try { notifications = new DeviceNotifications(handle); } catch { notificationFallback = true; }
            };
            Window.Closing += delegate(object sender, CancelEventArgs e) { if (!quitting) { e.Cancel = true; Window.Hide(); } };
            Find<Grid>("TitleBar").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.OriginalSource is TextBlock || e.OriginalSource is Grid || e.OriginalSource is System.Windows.Shapes.Path) {
                    if (e.ClickCount == 2) return;
                    Window.DragMove();
                }
            };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Window.Hide(); if (e.Key == Key.F5) Refresh(); };
            Click("HideButton", delegate { Window.Hide(); }); Click("CloseButton", delegate { Window.Hide(); });
            Click("SettingsButton", delegate { ShowSettings(); }); Click("BackButton", delegate { ShowDevices(); });
            Click("RefreshButton", delegate { Refresh(); });
            Click("AllButton", delegate { lowOnly = false; Render(); }); Click("LowButton", delegate { lowOnly = true; Render(); });
            Click("DemoButton", delegate { demo = true; lowOnly = false; ShowDevices(); Render(); });
            Click("ExitDemoButton", delegate { demo = false; Render(); Refresh(); });
            Click("DiagnosticsButton", ExportDiagnostics); Click("QuitButton", Quit);
            monitor = new DeviceMonitor(() => Task.Run(() => UsbInventory.Scan()), ReadBatteryAsync, CancelBattery);
            monitor.Changed += delegate {
                real = monitor.Current; refreshError = monitor.Error;
                foreach (var d in real.Devices) if (notificationPolicy.ShouldNotify(d, preferences, demo, DateTime.UtcNow) && tray != null)
                    tray.ShowBalloonTip(6000, "低电量", d.Name + " · " + d.Battery + "%", Forms.ToolTipIcon.Warning);
                Render();
            };
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(preferences.RefreshSeconds) };
            timer.Tick += async delegate { if (!capture && !quitting) await monitor.RefreshBattery(); };
            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            debounce.Tick += async delegate { debounce.Stop(); if (!capture && !quitting) await monitor.RefreshDevices(true); };
            inventoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            inventoryTimer.Tick += async delegate { if (!capture && !quitting) await monitor.RefreshDevices(false); };
            Find<ScrollViewer>("DeviceScroll").PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) {
                var scroll = (ScrollViewer)sender; scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta); e.Handled = true;
            };
            Find<ScrollViewer>("DeviceScroll").PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                var scroll = (ScrollViewer)sender;
                if (e.Key == Key.Right) { scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + 210); e.Handled = true; }
                if (e.Key == Key.Left) { scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - 210); e.Handled = true; }
            };
            Find<ScrollViewer>("DeviceScroll").SizeChanged += delegate { FitArtwork(); };
            ConfigureSettings();
            if (!capture) { CreateTray(); timer.Start(); inventoryTimer.Start(); }
            Render();
        }
        private T Find<T>(string name) where T : class { return (T)Window.FindName(name); }
        private void Click(string name, Action action) { Find<Button>(name).Click += delegate { action(); }; }
        internal void SetSnapshot(Snapshot snapshot) { real = snapshot; Render(); }
        private static Brush B(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        private static TextBlock Text(string text, double size, string color)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = B(color), TextTrimming = TextTrimming.CharacterEllipsis };
        }
        private void ShowDevices() { Find<FrameworkElement>("MainPanel").Visibility = Visibility.Visible; Find<FrameworkElement>("SettingsPanel").Visibility = Visibility.Collapsed; }
        private void ShowSettings() { Find<FrameworkElement>("MainPanel").Visibility = Visibility.Collapsed; Find<FrameworkElement>("SettingsPanel").Visibility = Visibility.Visible; }
        internal void Show()
        {
            Window.Show();
            if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            // Return a window to the work area if a previously used monitor disappeared.
            var area = SystemParameters.WorkArea;
            if (Window.Left + Window.Width < area.Left || Window.Top + Window.Height < area.Top || Window.Left > area.Right || Window.Top > area.Bottom) {
                Window.Left = area.Right - Window.Width - 18; Window.Top = Math.Max(area.Top, area.Bottom - Window.Height - 18);
            }
            Window.Activate();
            if (!capture && monitor != null) RefreshInventoryOnOpen();
        }
        private async void RefreshInventoryOnOpen() { await monitor.RefreshDevices(false); }
        private void CreateTray()
        {
            using (var bitmap = new System.Drawing.Bitmap(32, 32)) {
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) {
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    graphics.Clear(System.Drawing.Color.Transparent);
                    using (var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(45, 45, 45))) graphics.FillEllipse(fill, 1, 1, 30, 30);
                    using (var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(225, 225, 225)))
                        graphics.FillPolygon(fill, new[] { new System.Drawing.Point(18, 5), new System.Drawing.Point(9, 18), new System.Drawing.Point(15, 18), new System.Drawing.Point(13, 27), new System.Drawing.Point(24, 13), new System.Drawing.Point(17, 13) });
                }
                IntPtr icon = bitmap.GetHicon();
                try { using (var borrowed = System.Drawing.Icon.FromHandle(icon)) trayIcon = (System.Drawing.Icon)borrowed.Clone(); }
                finally { DestroyIcon(icon); }
            }
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "栖电 · 正在发现设备", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开设备面板", null, delegate { Window.Dispatcher.Invoke(new Action(Show)); });
            menu.Items.Add("立即刷新", null, delegate { Window.Dispatcher.Invoke(new Action(Refresh)); });
            menu.Items.Add("设置", null, delegate { Window.Dispatcher.Invoke(new Action(delegate { Show(); ShowSettings(); })); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出栖电", null, delegate { Window.Dispatcher.Invoke(new Action(Quit)); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Window.Dispatcher.Invoke(new Action(delegate { if (Window.IsVisible && Window.IsActive) Window.Hide(); else Show(); })); };
            tray.BalloonTipClicked += delegate { Window.Dispatcher.Invoke(new Action(Show)); };
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!capture && (DeviceNotifications.IsDeviceChange(message, wParam) || message == 0x218 && (wParam.ToInt32() == 7 || wParam.ToInt32() == 18))) {
                monitor.DevicesChanged();
                debounce.Stop(); debounce.Start();
            }
            return IntPtr.Zero;
        }
        private void ConfigureSettings()
        {
            loadingSettings = true;
            Find<ComboBox>("IntervalCombo").SelectedIndex = preferences.RefreshSeconds == 30 ? 0 : preferences.RefreshSeconds == 120 ? 2 : 1;
            Find<CheckBox>("NotificationsCheck").IsChecked = preferences.Notifications;
            Find<CheckBox>("NotificationsCheck").Content = "电量 ≤ " + preferences.LowThreshold + "% 时提醒";
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                Find<CheckBox>("StartupCheck").IsChecked = key != null && String.Equals(key.GetValue("PeripheralPower") as string, Quote(Assembly.GetExecutingAssembly().Location) + " --background", StringComparison.OrdinalIgnoreCase);
            Find<ComboBox>("IntervalCombo").SelectionChanged += delegate {
                if (loadingSettings) return;
                preferences.RefreshSeconds = Int32.Parse(((ComboBoxItem)Find<ComboBox>("IntervalCombo").SelectedItem).Tag.ToString());
                timer.Interval = TimeSpan.FromSeconds(preferences.RefreshSeconds); SaveSettings(); Render();
            };
            Find<CheckBox>("NotificationsCheck").Click += delegate { preferences.Notifications = Find<CheckBox>("NotificationsCheck").IsChecked == true; SaveSettings(); };
            Find<CheckBox>("StartupCheck").Click += delegate {
                if (capture) return;
                try {
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                        if (Find<CheckBox>("StartupCheck").IsChecked == true) key.SetValue("PeripheralPower", Quote(Assembly.GetExecutingAssembly().Location) + " --background");
                        else key.DeleteValue("PeripheralPower", false);
                    }
                } catch (Exception ex) { Find<CheckBox>("StartupCheck").IsChecked = false; MessageBox.Show(Window, "无法保存登录启动设置：" + ex.Message, "栖电"); }
            };
            loadingSettings = false;
        }
        private void SaveSettings() { if (capture) return; try { preferences.Save(); } catch (Exception ex) { MessageBox.Show(Window, "设置未能保存：" + ex.Message, "栖电"); } }
        internal static string Quote(string value) { return "\"" + value + "\""; }
        internal async void Refresh()
        {
            if (capture || quitting) return;
            await monitor.RefreshDevices(true);
        }
        private void CancelBattery() { if (probe != null) try { if (!probe.HasExited) probe.Kill(); } catch { } }
        private async Task<Snapshot> ReadBatteryAsync()
        {
            string output = Path.Combine(Path.GetTempPath(), "peripheral-power-" + Guid.NewGuid().ToString("N") + ".json");
            Process running = null;
            try {
                running = new Process { StartInfo = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--probe " + Quote(output)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden } };
                probe = running; running.Start();
                bool finished = await Task.Run(() => running.WaitForExit(25000));
                if (quitting) throw new OperationCanceledException();
                if (!finished) { running.Kill(); await Task.Run(() => running.WaitForExit(3000)); }
                if (File.Exists(output)) {
                    var result = Json.Read<Snapshot>(output);
                    if (!finished) result.Errors.Add("电量扫描超过 25 秒，已中止。连接状态不受影响。");
                    if (!finished || running.ExitCode != 0) result.BatteryComplete = false;
                    return result;
                }
                throw new IOException("未收到电量读取结果。");
            } finally {
                if (running != null) { try { if (!running.HasExited) running.Kill(); } catch { } running.Dispose(); }
                probe = null;
                try { if (File.Exists(output)) File.Delete(output); if (File.Exists(output + ".new")) File.Delete(output + ".new"); } catch { }
            }
        }
        private void Render()
        {
            Snapshot shown = demo ? DemoSnapshot() : real;
            Find<FrameworkElement>("DemoBanner").Visibility = demo ? Visibility.Visible : Visibility.Collapsed;
            Find<Button>("RefreshButton").IsEnabled = !demo;
            Find<TextBlock>("RefreshLabel").Text = busy && !demo ? "读取中" : "刷新";
            Find<Button>("AllButton").Background = B(lowOnly ? "Transparent" : "#303030");
            Find<Button>("LowButton").Background = B(lowOnly ? "#303030" : "Transparent");
            int readable = shown.Devices.Count(d => d.Status == "ok");
            int low = shown.Devices.Count(IsLow);
            Find<TextBlock>("DeviceCount").Text = shown.Devices.Count.ToString();
            Find<TextBlock>("SummaryDetail").Text = readable + " 台电量可读" + (low > 0 ? " · " + low + " 台低电量" : "");
            var list = Find<StackPanel>("DeviceList");
            var scroll = Find<ScrollViewer>("DeviceScroll");
            // Stable order avoids moving devices under the pointer when their battery changes.
            var filtered = shown.Devices.Where(d => !lowOnly || IsLow(d)).OrderBy(d => d.Kind == "mouse" ? 0 : d.Kind == "keyboard" ? 1 : d.Kind == "headset" ? 2 : 3).ThenBy(d => d.Name).ThenBy(d => d.Id).ToList();
            string renderKey = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(filtered) + demo + lowOnly;
            if (renderKey != deviceRenderKey) {
                double offset = scroll.HorizontalOffset;
                list.Children.Clear();
                foreach (var d in filtered) list.Children.Add(DeviceCard(d));
                if (list.Children.Count > 0) ((FrameworkElement)list.Children[list.Children.Count - 1]).Margin = new Thickness(0);
                scroll.ScrollToHorizontalOffset(offset); deviceRenderKey = renderKey;
                FitArtwork();
            }
            Find<FrameworkElement>("EmptyPanel").Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("EmptyTitle").Text = lowOnly ? "无低电量设备" : busy ? "读取设备…" : "未发现 USB 设备";
            Find<TextBlock>("EmptyDetail").Text = lowOnly ? "仅统计已读取的电量" : "插入设备后会自动更新";
            DateTime time;
            string updated = DateTime.TryParse(shown.ScannedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out time) ? time.ToLocalTime().ToString("HH:mm:ss") : "尚未检测";
            Find<TextBlock>("FooterText").Text = demo ? "示例数据" : refreshError ?? (updated + " 连接检测" + (busy ? " · 读取电量…" : " · 电量 " + preferences.RefreshSeconds + " 秒刷新"));
            Find<TextBlock>("FooterText").ToolTip = demo ? null : String.Join("\n", shown.Errors) + (notificationFallback ? "\n插拔通知注册失败，使用 3 秒连接检测。" : "");
            if (tray != null) {
                string label = "栖电 · " + real.Devices.Count + " 台设备";
                var lowest = real.Devices.Where(d => d.Status == "ok" && d.Battery.HasValue).OrderBy(d => d.Battery).FirstOrDefault();
                if (lowest != null) label += " · 最低 " + lowest.Battery + "%";
                tray.Text = label.Length > 63 ? label.Substring(0, 63) : label;
            }
        }
        private bool IsLow(DeviceState d) { return d.Status == "ok" && !d.Charging && (d.Battery.HasValue ? d.Battery.Value <= preferences.LowThreshold : d.Level == "偏低" || d.Level == "极低"); }
        private void FitArtwork()
        {
            double available = Find<ScrollViewer>("DeviceScroll").ActualHeight;
            if (available <= 0) return;
            double height = Math.Max(80, Math.Min(148, available - 145));
            foreach (Border tile in Find<StackPanel>("DeviceList").Children) {
                var art = (Grid)((StackPanel)tile.Child).Children[0];
                art.Height = height;
                ((FrameworkElement)art.Children[0]).Height = height;
            }
        }
        private UIElement DeviceCard(DeviceState d)
        {
            var tile = new Border { Width = 194, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 26, 0) };
            var stack = new StackPanel();
            CatalogEntry entry = Catalog.Match(catalog, d);
            string imagePath = null; preferences.Images.TryGetValue(d.Id, out imagePath);
            if (imagePath == null && entry != null && !String.IsNullOrEmpty(entry.Image)) imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, entry.Image);
            ImageSource source = LoadImage(imagePath);
            var artPanel = new Grid { Height = 148, Margin = new Thickness(0, 0, 0, 14), ToolTip = source == null ? "通用设备示意图" : "型号图片" };
            if (source != null) artPanel.Children.Add(new Image { Source = source, Width = 176, Height = 148, Stretch = Stretch.Uniform });
            else artPanel.Children.Add(new Viewbox { Width = 176, Height = 148, Stretch = Stretch.Uniform, Child = new DeviceArt(entry != null && !String.IsNullOrEmpty(entry.Kind) ? entry.Kind : d.Kind) });
            stack.Children.Add(artPanel);
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string display = entry == null || String.IsNullOrEmpty(entry.DisplayName) ? d.Name : entry.DisplayName;
            var name = Text(display, 13, "#E5E5E5"); name.FontWeight = FontWeights.Medium; name.VerticalAlignment = VerticalAlignment.Center; name.ToolTip = display; heading.Children.Add(name);
            var more = new Button { Content = "···", Padding = new Thickness(7, 0, 2, 0), FontSize = 17, Foreground = B("#888888"), ToolTip = "设备详情与图片" }; Grid.SetColumn(more, 1);
            var menu = new ContextMenu();
            var info = new MenuItem { Header = "设备详情" }; info.Click += delegate { MessageBox.Show(Window, d.Name + "\n\n" + d.HardwareId + "\n" + d.Connection + "\n适配器：" + d.Adapter + "\n\n" + d.Detail + "\n\n上次电量读取：" + LocalTime(d.UpdatedUtc) + "\n\n" + String.Join("\n", d.Interfaces), "设备详情", MessageBoxButton.OK, MessageBoxImage.Information); }; menu.Items.Add(info);
            var changeImage = new MenuItem { Header = "更换型号图片…", IsEnabled = !demo }; changeImage.Click += delegate { ChooseImage(d); }; menu.Items.Add(changeImage);
            var resetImage = new MenuItem { Header = "恢复默认图片", IsEnabled = !demo && preferences.Images.ContainsKey(d.Id) }; resetImage.Click += delegate { preferences.Images.Remove(d.Id); SaveSettings(); deviceRenderKey = null; Render(); }; menu.Items.Add(resetImage);
            more.Click += delegate { menu.PlacementTarget = more; menu.IsOpen = true; }; heading.Children.Add(more); stack.Children.Add(heading);
            var connection = Text(d.Connection, 10, "#777777"); connection.Margin = new Thickness(0, 5, 0, 0); stack.Children.Add(connection);
            bool stale = d.Status == "stale";
            string color = stale ? "#888888" : IsLow(d) ? "#D6AD77" : "#DDDDDD";
            var valueRow = new Grid { Margin = new Thickness(0, 11, 0, 9) };
            valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); valueRow.ColumnDefinitions.Add(new ColumnDefinition());
            var value = Text(d.Battery.HasValue ? d.Battery.Value + "%" : d.Level ?? "—", 22, color); value.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); valueRow.Children.Add(value);
            string state = stale ? "上次读取" : d.Charging ? "充电中" : IsLow(d) ? "低电量" : d.Status == "ok" ? "" : d.Status == "pending" ? "等待电量" : d.Status == "unavailable" ? "电量未响应" : "电量未公开";
            var status = Text(state, 10, stale || d.Status != "ok" ? "#7D7D7D" : color); status.VerticalAlignment = VerticalAlignment.Center; status.HorizontalAlignment = HorizontalAlignment.Right; status.ToolTip = d.Detail; Grid.SetColumn(status, 1); valueRow.Children.Add(status); stack.Children.Add(valueRow);
            var bar = new Grid { Height = 2, ClipToBounds = true };
            bar.Children.Add(new Border { Background = B("#303030") });
            if (d.Battery.HasValue) {
                var fill = new Border { Background = B(IsLow(d) ? "#C19C6B" : stale ? "#666666" : "#BBBBBB"), HorizontalAlignment = HorizontalAlignment.Left };
                bar.SizeChanged += delegate { fill.Width = bar.ActualWidth * Math.Max(0, Math.Min(100, d.Battery.Value)) / 100.0; }; bar.Children.Add(fill);
            }
            stack.Children.Add(bar); tile.Child = stack;
            System.Windows.Automation.AutomationProperties.SetName(tile, display + "，" + (d.Battery.HasValue ? d.Battery + "%" : d.Level ?? "电量未知") + "，" + state);
            return tile;
        }
        private static string LocalTime(string value) { DateTime d; return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out d) ? d.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "暂无"; }
        private static ImageSource LoadImage(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; bitmap.DecodePixelWidth = 240; bitmap.UriSource = new Uri(Path.GetFullPath(path)); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            } catch { return null; }
        }
        private void ChooseImage(DeviceState d)
        {
            var dialog = new OpenFileDialog { Title = "选择设备型号图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dialog.ShowDialog(Window) != true) return;
            try {
                if (new FileInfo(dialog.FileName).Length > 10 * 1024 * 1024 || LoadImage(dialog.FileName) == null) { MessageBox.Show(Window, "请选择 10 MB 以内的有效 PNG、JPEG 或 BMP 图片。", "栖电"); return; }
                string folder = Path.Combine(Preferences.Folder, "images"); Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, d.Id + Path.GetExtension(dialog.FileName).ToLowerInvariant());
                if (!String.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(dialog.FileName, target, true);
                preferences.Images[d.Id] = target; SaveSettings(); deviceRenderKey = null; Render();
            } catch (Exception ex) { MessageBox.Show(Window, "图片未能保存：" + ex.Message, "栖电"); }
        }
        private void ExportDiagnostics()
        {
            var dialog = new SaveFileDialog { Title = "保存设备诊断信息", Filter = "JSON 文件|*.json", FileName = "栖电-设备诊断-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json" };
            if (dialog.ShowDialog(Window) != true) return;
            try { Json.Write(dialog.FileName, new { Application = "Peripheral Power 0.2.0", ExportedUtc = DateTime.UtcNow.ToString("o"), LastScanError = refreshError, Snapshot = real }); MessageBox.Show(Window, "诊断信息已保存。此文件使用真实设备扫描结果。", "栖电"); }
            catch (Exception ex) { MessageBox.Show(Window, "导出失败：" + ex.Message, "栖电"); }
        }
        internal static Snapshot DemoSnapshot()
        {
            string now = DateTime.UtcNow.ToString("o");
            return new Snapshot { ScannedUtc = now, InventoryComplete = true, BatteryComplete = true, Devices = new List<DeviceState> {
                new DeviceState { Id="demo-mouse", Name="无线鼠标", Kind="mouse", Battery=86, Status="ok", Connection="2.4 GHz", UpdatedUtc=now, Detail="此卡片是界面演示，不代表实际设备。", Adapter="演示" },
                new DeviceState { Id="demo-keyboard", Name="机械键盘 · 75%", Kind="keyboard", Battery=18, Status="ok", Connection="2.4 GHz", UpdatedUtc=now, Detail="此卡片是界面演示，不代表实际设备。", Adapter="演示" },
                new DeviceState { Id="demo-headset", Name="无线耳机", Kind="headset", Battery=64, Charging=true, Status="ok", Connection="2.4 GHz", UpdatedUtc=now, Detail="此卡片是界面演示，不代表实际设备。", Adapter="演示" },
                new DeviceState { Id="demo-usb", Name="USB 接收器", Kind="receiver", Connection="USB", Status="unsupported", Detail="示例设备", Adapter="演示" }
            }};
        }
        internal void Quit() { if (quitting) return; quitting = true; Dispose(); Window.Close(); Application.Current.Shutdown(); }
        public void Dispose()
        {
            timer.Stop(); debounce.Stop(); inventoryTimer.Stop(); monitor.Dispose();
            if (notifications != null) { notifications.Dispose(); notifications = null; }
            if (probe != null) try { if (!probe.HasExited) probe.Kill(); } catch { }
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            if (trayIcon != null) { trayIcon.Dispose(); trayIcon = null; }
        }
    }
}
