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
        private readonly DispatcherTimer timer, debounce;
        private readonly NotificationPolicy notificationPolicy = new NotificationPolicy();
        private readonly List<CatalogEntry> catalog = Catalog.Load();
        private readonly bool capture;
        private Forms.NotifyIcon tray;
        private System.Drawing.Icon trayIcon;
        private Process probe;
        private bool busy, demo, lowOnly, quitting, loadingSettings;
        private Snapshot real = new Snapshot();
        private string refreshError;
        internal DesktopApp(bool demoMode, bool captureMode)
        {
            demo = demoMode; capture = captureMode; preferences = capture ? new Preferences() : Preferences.Load();
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                Window = (Window)XamlReader.Load(resource);
            Window.Height = Math.Min(Window.Height, Math.Max(Window.MinHeight, SystemParameters.WorkArea.Height - 24));
            Window.SourceInitialized += delegate {
                HwndSource.FromHwnd(new WindowInteropHelper(Window).Handle).AddHook(WindowMessage);
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
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(preferences.RefreshSeconds) };
            timer.Tick += delegate { Refresh(); };
            debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            debounce.Tick += delegate { debounce.Stop(); Refresh(); };
            ConfigureSettings();
            if (!capture) { CreateTray(); timer.Start(); }
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
        }
        private void CreateTray()
        {
            using (var bitmap = new System.Drawing.Bitmap(32, 32)) {
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) {
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    graphics.Clear(System.Drawing.Color.Transparent);
                    using (var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(37, 75, 60))) graphics.FillEllipse(fill, 1, 1, 30, 30);
                    using (var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(219, 234, 185)))
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
            if (message == 0x219 || message == 0x218 && (wParam.ToInt32() == 7 || wParam.ToInt32() == 18)) {
                debounce.Stop(); debounce.Start();
            }
            return IntPtr.Zero;
        }
        private void ConfigureSettings()
        {
            loadingSettings = true;
            Find<ComboBox>("IntervalCombo").SelectedIndex = preferences.RefreshSeconds == 30 ? 0 : preferences.RefreshSeconds == 120 ? 2 : 1;
            Find<CheckBox>("NotificationsCheck").IsChecked = preferences.Notifications;
            Find<CheckBox>("NotificationsCheck").Content = "电量低于或等于 " + preferences.LowThreshold + "% 时提醒";
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                Find<CheckBox>("StartupCheck").IsChecked = key != null && key.GetValue("PeripheralPower") != null;
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
            if (busy || capture || quitting) return;
            if (demo) { Render(); return; }
            busy = true; refreshError = null; Render();
            string output = Path.Combine(Path.GetTempPath(), "peripheral-power-" + Guid.NewGuid().ToString("N") + ".json");
            Process running = null;
            try {
                running = new Process { StartInfo = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--probe " + Quote(output)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden } };
                probe = running; running.Start();
                bool finished = await Task.Run(() => running.WaitForExit(25000));
                if (quitting) return;
                if (!finished) { running.Kill(); await Task.Run(() => running.WaitForExit(3000)); refreshError = "部分设备响应超时"; }
                if (File.Exists(output)) {
                    real = Json.Read<Snapshot>(output);
                    if (!finished) real.Errors.Add("扫描超过 25 秒，已中止；当前仅展示已完成的结果。");
                    else if (running.ExitCode != 0) refreshError = "扫描未完整完成";
                    foreach (var d in real.Devices) {
                        if (notificationPolicy.ShouldNotify(d, preferences, demo, DateTime.UtcNow) && tray != null)
                            tray.ShowBalloonTip(6000, "该给设备充电了", d.Name + " 剩余 " + d.Battery + "% 电量。", Forms.ToolTipIcon.Warning);
                    }
                } else {
                    refreshError = "本次读取失败，请稍后重试";
                    foreach (var d in real.Devices) { d.Status = "stale"; d.Detail = "本次扫描未完成。显示的是上次成功读取的数据。"; }
                }
            } catch (Exception ex) {
                refreshError = "本次读取失败 · " + ex.GetType().Name;
                foreach (var d in real.Devices) { d.Status = "stale"; d.Detail = "本次扫描失败，数据尚未更新。"; }
            } finally {
                if (running != null) { try { if (!running.HasExited) running.Kill(); } catch { } running.Dispose(); }
                probe = null; busy = false;
                try { if (File.Exists(output)) File.Delete(output); if (File.Exists(output + ".new")) File.Delete(output + ".new"); } catch { }
                if (!quitting) Render();
            }
        }
        private void Render()
        {
            Snapshot shown = demo ? DemoSnapshot() : real;
            Find<FrameworkElement>("DemoBanner").Visibility = demo ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("MonitorBadge").Text = demo ? "界面演示" : "自动监测";
            Find<Button>("RefreshButton").IsEnabled = !busy && !demo;
            Find<TextBlock>("RefreshLabel").Text = busy ? "读取中" : "刷新";
            Find<Button>("AllButton").Background = B(lowOnly ? "Transparent" : "#E2E8DB");
            Find<Button>("LowButton").Background = B(lowOnly ? "#E2E8DB" : "Transparent");
            int readable = shown.Devices.Count(d => d.Status == "ok");
            int low = shown.Devices.Count(IsLow);
            Find<TextBlock>("DeviceCount").Text = shown.Devices.Count.ToString("00");
            Find<TextBlock>("SummaryTitle").Text = busy && shown.Devices.Count == 0 ? "正在发现你的设备" : low > 0 ? low + " 台设备需要充电" : readable > 0 ? "电量尽在掌握" : shown.Devices.Count > 0 ? "设备已发现，等待适配" : "等待设备接入";
            Find<TextBlock>("SummaryDetail").Text = shown.Devices.Count > 0 ? readable + " 台电量可读  /  " + (shown.Devices.Count - readable) + " 台暂无电量" : "插入无线接收器后，设备会自动出现在这里";
            var list = Find<StackPanel>("DeviceList"); list.Children.Clear();
            var filtered = shown.Devices.Where(d => !lowOnly || IsLow(d)).OrderByDescending(IsLow).ThenByDescending(d => d.Status == "ok").ThenBy(d => d.Kind == "mouse" ? 0 : d.Kind == "keyboard" ? 1 : 2).ThenBy(d => d.Name).ToList();
            foreach (var d in filtered) list.Children.Add(DeviceCard(d));
            if (filtered.Count == 0) {
                var empty = new StackPanel { Margin = new Thickness(12, 22, 12, 20), HorizontalAlignment = HorizontalAlignment.Center };
                empty.Children.Add(new DeviceArt("receiver") { HorizontalAlignment = HorizontalAlignment.Center });
                var title = Text(lowOnly ? "暂没有低电量设备" : busy ? "正在寻找设备…" : "还没有发现设备", 16, "#52664D"); title.Margin = new Thickness(0, 16, 0, 8); title.TextAlignment = TextAlignment.Center; empty.Children.Add(title);
                var detail = Text(lowOnly ? "仅统计本次成功读取的电量" : "连接接收器，唤醒设备，再点一次刷新。", 11, "#8B9584"); detail.TextAlignment = TextAlignment.Center; empty.Children.Add(detail);
                if (!lowOnly && !busy) {
                    var button = new Button { Content = "先看看界面演示", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.White };
                    button.Click += delegate { demo = true; Render(); }; empty.Children.Add(button);
                }
                list.Children.Add(empty);
            }
            DateTime time;
            string updated = DateTime.TryParse(shown.ScannedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out time) ? time.ToLocalTime().ToString("HH:mm:ss") : "尚未读取";
            Find<TextBlock>("FooterText").Text = demo ? "示例数据 · 不代表实际连接设备" : busy ? "正在读取设备电量…" : refreshError ?? (shown.Errors.Count > 0 ? "部分接口无法读取 · 可导出诊断" : updated + " 更新 · " + preferences.RefreshSeconds + " 秒刷新");
            Find<TextBlock>("FooterText").ToolTip = demo ? null : String.Join("\n", shown.Errors);
            if (tray != null) {
                string label = demo ? "栖电 · 演示模式（监测已暂停）" : "栖电 · " + readable + " 台电量可读";
                var lowest = real.Devices.Where(d => d.Status == "ok" && d.Battery.HasValue).OrderBy(d => d.Battery).FirstOrDefault();
                if (!demo && lowest != null) label += " · 最低 " + lowest.Battery + "%";
                tray.Text = label.Length > 63 ? label.Substring(0, 63) : label;
            }
        }
        private bool IsLow(DeviceState d) { return d.Status == "ok" && !d.Charging && (d.Battery.HasValue ? d.Battery.Value <= preferences.LowThreshold : d.Level == "偏低" || d.Level == "极低"); }
        private UIElement DeviceCard(DeviceState d)
        {
            var border = new Border { Background = Brushes.White, BorderBrush = B(IsLow(d) ? "#E8D7B6" : "#E3E7DD"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 11) };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(121) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            CatalogEntry entry = Catalog.Match(catalog, d);
            string imagePath = null; preferences.Images.TryGetValue(d.Id, out imagePath);
            if (imagePath == null && entry != null && !String.IsNullOrEmpty(entry.Image)) imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, entry.Image);
            var artPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            ImageSource source = LoadImage(imagePath);
            if (source != null) artPanel.Children.Add(new Image { Source = source, Width = 106, Height = 100, Stretch = Stretch.Uniform });
            else artPanel.Children.Add(new DeviceArt(entry != null && !String.IsNullOrEmpty(entry.Kind) ? entry.Kind : d.Kind));
            var caption = Text(source == null ? "设备示意" : "型号图片", 9, "#ABB1A3"); caption.TextAlignment = TextAlignment.Center; caption.Margin = new Thickness(0, 6, 0, 0); artPanel.Children.Add(caption); grid.Children.Add(artPanel);
            var content = new Grid(); Grid.SetColumn(content, 1);
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string display = entry == null || String.IsNullOrEmpty(entry.DisplayName) ? d.Name : entry.DisplayName;
            var name = Text(display, 13, "#293E30"); name.FontWeight = FontWeights.SemiBold; name.VerticalAlignment = VerticalAlignment.Center; name.ToolTip = display; heading.Children.Add(name);
            var more = new Button { Content = "···", Padding = new Thickness(7, 0, 2, 0), FontSize = 17, Foreground = B("#87917D"), ToolTip = "设备详情与图片" }; Grid.SetColumn(more, 1);
            var menu = new ContextMenu();
            var info = new MenuItem { Header = "设备详情" }; info.Click += delegate { MessageBox.Show(Window, d.Name + "\n\n" + d.HardwareId + "\n" + d.Connection + "\n适配器：" + d.Adapter + "\n\n" + d.Detail + "\n\n上次电量读取：" + LocalTime(d.UpdatedUtc) + "\n\n" + String.Join("\n", d.Interfaces), "设备详情", MessageBoxButton.OK, MessageBoxImage.Information); }; menu.Items.Add(info);
            var changeImage = new MenuItem { Header = "更换型号图片…", IsEnabled = !demo }; changeImage.Click += delegate { ChooseImage(d); }; menu.Items.Add(changeImage);
            var resetImage = new MenuItem { Header = "恢复默认图片", IsEnabled = !demo && preferences.Images.ContainsKey(d.Id) }; resetImage.Click += delegate { preferences.Images.Remove(d.Id); SaveSettings(); Render(); }; menu.Items.Add(resetImage);
            more.Click += delegate { menu.PlacementTarget = more; menu.IsOpen = true; }; heading.Children.Add(more); content.Children.Add(heading);
            var connection = Text(d.Connection, 10, "#8F9888"); connection.Margin = new Thickness(0, 5, 0, 0); Grid.SetRow(connection, 1); content.Children.Add(connection);
            var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 5) };
            bool stale = d.Status == "stale";
            string color = stale ? "#8C9486" : IsLow(d) ? "#B58B3A" : "#3F5D32";
            var value = Text(d.Battery.HasValue ? d.Battery.Value.ToString() : d.Level ?? "—", d.Battery.HasValue || d.Level == null ? 36 : 24, color); value.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); value.FontWeight = FontWeights.Light; valueRow.Children.Add(value);
            if (d.Battery.HasValue) { var percent = Text("%", 14, color); percent.VerticalAlignment = VerticalAlignment.Bottom; percent.Margin = new Thickness(3, 0, 7, 6); valueRow.Children.Add(percent); }
            string state = stale ? "上次读取" : d.Charging ? "充电中" : IsLow(d) ? "建议充电" : d.Status == "ok" ? "电量可读" : d.Status == "unavailable" ? "未响应" : "待适配";
            var tag = Text(state, 10, d.Status == "ok" ? color : "#979D8F"); tag.VerticalAlignment = VerticalAlignment.Bottom; tag.Margin = new Thickness(5, 0, 0, 7); tag.ToolTip = d.Detail; valueRow.Children.Add(tag); Grid.SetRow(valueRow, 2); content.Children.Add(valueRow);
            var bar = new Grid { Height = 4, Margin = new Thickness(0, 0, 2, 4), ClipToBounds = true };
            bar.Children.Add(new Border { Background = B("#EDF0E8"), CornerRadius = new CornerRadius(2) });
            if (d.Battery.HasValue) {
                var fill = new Border { Background = B(IsLow(d) ? "#C5A05A" : stale ? "#B8BDB0" : "#8CA66C"), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
                bar.SizeChanged += delegate { fill.Width = bar.ActualWidth * Math.Max(0, Math.Min(100, d.Battery.Value)) / 100.0; }; bar.Children.Add(fill);
            }
            Grid.SetRow(bar, 3); content.Children.Add(bar); grid.Children.Add(content); border.Child = grid;
            System.Windows.Automation.AutomationProperties.SetName(border, display + "，" + (d.Battery.HasValue ? d.Battery + "%" : d.Level ?? "电量未知") + "，" + state);
            return border;
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
                preferences.Images[d.Id] = target; SaveSettings(); Render();
            } catch (Exception ex) { MessageBox.Show(Window, "图片未能保存：" + ex.Message, "栖电"); }
        }
        private void ExportDiagnostics()
        {
            var dialog = new SaveFileDialog { Title = "保存设备诊断信息", Filter = "JSON 文件|*.json", FileName = "栖电-设备诊断-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json" };
            if (dialog.ShowDialog(Window) != true) return;
            try { Json.Write(dialog.FileName, new { Application = "Peripheral Power 0.1.0", ExportedUtc = DateTime.UtcNow.ToString("o"), LastScanError = refreshError, Snapshot = real }); MessageBox.Show(Window, "诊断信息已保存。此文件使用真实设备扫描结果。", "栖电"); }
            catch (Exception ex) { MessageBox.Show(Window, "导出失败：" + ex.Message, "栖电"); }
        }
        internal static Snapshot DemoSnapshot()
        {
            string now = DateTime.UtcNow.ToString("o");
            return new Snapshot { ScannedUtc = now, Devices = new List<DeviceState> {
                new DeviceState { Id="demo-mouse", Name="无线游戏鼠标", Kind="mouse", Battery=86, Status="ok", Connection="2.4 GHz · 示例设备", UpdatedUtc=now, Detail="此卡片是界面演示，不代表实际设备。", Adapter="演示" },
                new DeviceState { Id="demo-keyboard", Name="机械键盘 · 75%", Kind="keyboard", Battery=18, Status="ok", Connection="2.4 GHz · 示例设备", UpdatedUtc=now, Detail="此卡片是界面演示，不代表实际设备。", Adapter="演示" },
                new DeviceState { Id="demo-headset", Name="无线游戏耳机", Kind="headset", Battery=64, Charging=true, Status="ok", Connection="2.4 GHz · 示例设备", UpdatedUtc=now, Detail="此卡片是界面演示，不代表实际设备。", Adapter="演示" }
            }};
        }
        internal void Quit() { if (quitting) return; quitting = true; Dispose(); Window.Close(); Application.Current.Shutdown(); }
        public void Dispose()
        {
            timer.Stop(); debounce.Stop();
            if (probe != null) try { if (!probe.HasExited) probe.Kill(); } catch { }
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            if (trayIcon != null) { trayIcon.Dispose(); trayIcon = null; }
        }
    }
}
