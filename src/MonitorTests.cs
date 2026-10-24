using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace PeripheralPower
{
    internal static class MonitorTests
    {
        private static int passed;
        private static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
        private static Snapshot Inventory(params string[] ids)
        {
            return new Snapshot { InventoryComplete = true, ScannedUtc = DateTime.UtcNow.ToString("o"), Devices = ids.Select(id => new DeviceState { Id = id, PhysicalId = id, Name = id, Status = "pending" }).ToList() };
        }
        private static Snapshot Battery(params string[] ids)
        {
            var s = Inventory(ids); s.BatteryComplete = true;
            foreach (var d in s.Devices) { d.Status = "ok"; d.Battery = 80; d.UpdatedUtc = DateTime.UtcNow.ToString("o"); }
            return s;
        }
        private static async Task Exercise()
        {
            string[] attached = { "A" };
            int canceled = 0, reads = 0, discoveries = 0;
            var firstRead = new TaskCompletionSource<Snapshot>();
            var nextRead = new TaskCompletionSource<Snapshot>();
            using (var monitor = new DeviceMonitor(() => { discoveries++; return Task.FromResult(Inventory(attached)); }, () => { reads++; return reads == 1 ? firstRead.Task : nextRead.Task; }, () => canceled++)) {
                var pending = monitor.RefreshDevices(true);
                Check(monitor.Current.Devices.Count == 1 && !pending.IsCompleted && monitor.BatteryBusy, "Inventory is visible while battery request is blocked");
                attached = new[] { "B" }; monitor.DevicesChanged();
                await monitor.RefreshDevices(true);
                Check(monitor.Current.Devices.Count == 1 && monitor.Current.Devices[0].Id == "B", "Unplug A and plug B updates immediately during blocked battery read");
                Check(canceled > 0, "Device change cancels obsolete worker");
                firstRead.SetResult(Battery("A"));
                Check(!monitor.Current.Devices.Any(d => d.Id == "A"), "Late battery result never resurrects removed device");
                nextRead.SetResult(Battery("B")); await pending;
                Check(reads == 2 && monitor.Current.Devices[0].Battery == 80, "Queued battery read runs after a busy scan");
                int before = reads;
                await monitor.RefreshDevices(false); await monitor.RefreshDevices(false);
                Check(reads == before, "Periodic inventory check does not repeatedly query batteries");
                attached = new string[0];
                await monitor.RefreshDevices(false);
                Check(monitor.Current.Devices.Count == 0, "Polling fallback removes device when notification is missed");
            }
            // Multiple arrival/removal events during a delayed inventory call coalesce into a fresh inventory.
            var inventory1 = new TaskCompletionSource<Snapshot>(); var inventory2 = new TaskCompletionSource<Snapshot>();
            int inv = 0;
            using (var monitor = new DeviceMonitor(() => ++inv == 1 ? inventory1.Task : inventory2.Task, () => Task.FromResult(Battery("C")), () => { })) {
                var pending = monitor.RefreshDevices(true);
                monitor.DevicesChanged(); await monitor.RefreshDevices(true);
                monitor.DevicesChanged(); await monitor.RefreshDevices(true);
                inventory1.SetResult(Inventory("A", "B"));
                Check(!monitor.Current.Devices.Any(), "Superseded inventory is not published");
                inventory2.SetResult(Inventory("C")); await pending;
                Check(inv == 2 && monitor.Current.Devices.Single().Id == "C", "Rapid hotplug events coalesce without losing the final state");
            }
            var cached = Battery("A"); var partial = new Snapshot();
            Check(DeviceMonitor.ApplyBattery(cached, partial).Devices.Single().Status == "stale", "Incomplete battery result retains explicitly stale connected device");
            Check(DeviceMonitor.ApplyBattery(Inventory("B"), Battery("A")).Devices.Single().Id == "B", "Only current physical inventory can receive battery updates");
            var shared = Battery("one", "two"); foreach (var d in shared.Devices) d.PhysicalId = "receiver";
            Check(DeviceMonitor.MergeInventory(shared, Inventory("receiver")).Devices.Count == 2, "Paired children survive receiver inventory refresh");
            Check(DeviceMonitor.MergeInventory(shared, Inventory()).Devices.Count == 0, "Unplugging receiver removes all paired children");
            Check(DeviceNotifications.IsDeviceChange(0x219, new IntPtr(0x8000)), "Arrival notification handled");
            Check(DeviceNotifications.IsDeviceChange(0x219, new IntPtr(0x8004)), "Removal notification handled");
            Check(DeviceNotifications.IsDeviceChange(0x219, new IntPtr(7)), "Device-node notification handled");
            Check(!DeviceNotifications.IsDeviceChange(0x219, new IntPtr(0x8001)), "Query-remove request is not confused with completed unplug");
            // Verify real registration works with an invisible HWND, as used by tray startup.
            using (var source = new HwndSource(new HwndSourceParameters("PeripheralPower notification test") { Width = 0, Height = 0, WindowStyle = 0 }))
            using (var notifications = new DeviceNotifications(source.Handle)) Check(source.Handle != IntPtr.Zero, "Native notifications register for hidden window");
            var real = UsbInventory.Scan();
            Check(real.InventoryComplete && real.Devices.All(d => d.Id == d.PhysicalId), "Real PnP inventory is complete with physical grouping");
            Check(real.Devices.Select(d => d.Id).Distinct().Count() == real.Devices.Count, "Real inventory has no duplicated physical devices");
            Check(real.Devices.All(d => d.HardwareId.StartsWith("VID_")), "USB hardware identity retained");
        }
        internal static int Run(string output)
        {
            int code = 0; var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Dispatcher.BeginInvoke(new Action(async delegate {
                try { var time = Stopwatch.StartNew(); await Exercise(); File.WriteAllText(output, "PASS: " + passed + " monitor checks in " + time.ElapsedMilliseconds + " ms.\r\nHotplug during blocked I/O, stale replies, coalescing, missed notifications, receiver removal, hidden HWND registration, real PnP inventory.\r\n"); }
                catch (Exception ex) { code = 1; File.WriteAllText(output, "FAIL after " + passed + " monitor checks: " + ex); }
                finally { app.Shutdown(); }
            }));
            app.Run(); return code;
        }
    }
}
