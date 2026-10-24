using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace PeripheralPower
{
    internal static class SelfTests
    {
        private static int passed;
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed++;
        }
        internal static int Run(string output)
        {
            try {
                Check(Marshal.SizeOf(typeof(Native.Caps)) == 64, "HIDP_CAPS ABI must be 64 bytes");
                Check(Marshal.SizeOf(typeof(Native.ValueCaps)) == 72, "HIDP_VALUE_CAPS ABI must be 72 bytes");
                Check(Marshal.OffsetOf(typeof(Native.ValueCaps), "LogicalMin").ToInt32() == 40, "Logical minimum ABI offset");
                Check(StandardBatteryAdapter.Decode(0, 0, 100) == 0, "Standard HID zero is a valid empty battery");
                Check(StandardBatteryAdapter.Decode(100, 0, 100) == 100, "Full battery");
                Check(StandardBatteryAdapter.Decode(101, 0, 100) == null, "Reject values over 100");
                Check(StandardBatteryAdapter.Decode(255, 0, 255) == null, "Do not fabricate percent from vendor scales");
                var d = new DeviceState();
                LogitechBatteryAdapter.ApplyUnified(d, new byte[] { 86, 4, 0, 0 });
                Check(d.Battery == 86 && !d.Charging && d.Status == "ok", "Unified battery percentage");
                d = new DeviceState(); LogitechBatteryAdapter.ApplyUnified(d, new byte[] { 0, 2, 0, 0 });
                Check(d.Battery == null && d.Level == "偏低" && d.Status == "ok", "Coarse levels remain textual, not fake percentages");
                d = new DeviceState(); LogitechBatteryAdapter.ApplyUnified(d, new byte[] { 44, 4, 1, 0 });
                Check(d.Battery == 44 && d.Charging, "Charging flag");
                d = new DeviceState(); LogitechBatteryAdapter.ApplyUnified(d, new byte[] { 255, 8, 0, 0 });
                Check(d.Status != "ok" && !d.Battery.HasValue, "Reject corrupt unified battery");
                d = new DeviceState(); LogitechBatteryAdapter.ApplyUnified(d, new byte[] { 86, 4 });
                Check(d.Status != "ok", "Reject truncated reports");
                d = new DeviceState(); LogitechBatteryAdapter.ApplyLegacy(d, new byte[] { 0, 0, 0 });
                Check(!d.Battery.HasValue && d.Status != "ok", "Legacy zero means unknown");
                d = new DeviceState(); LogitechBatteryAdapter.ApplyLegacy(d, new byte[] { 18, 10, 0 });
                Check(d.Battery == 18 && d.Status == "ok", "Legacy battery report");
                var reply = new byte[] { 0x11, 1, 4, 0x12, 86, 4, 0, 0 };
                Check(HidppTransport.Matches(reply, 1, 4, 0x12), "Correct reply accepted");
                Check(!HidppTransport.Matches(reply, 2, 4, 0x12), "Other paired device reply rejected");
                Check(!HidppTransport.Matches(reply, 1, 4, 0x1D), "Other software reply rejected");
                Check(!HidppTransport.Matches(new byte[] { 0x11, 1, 4 }, 1, 4, 0x12), "Truncated reply rejected");
                Check(HidppTransport.IsError(new byte[] { 0x11, 1, 0xFF, 4, 0x12, 1, 0 }, 1, 4, 0x12), "Feature errors recognized");
                var settings = new Preferences(); var policy = new NotificationPolicy(); var now = DateTime.UtcNow;
                d = new DeviceState { Id = "mouse", Battery = 18, Status = "ok" };
                Check(!policy.ShouldNotify(d, settings, true, now), "Demo never emits alerts");
                Check(policy.ShouldNotify(d, settings, false, now), "Low battery emits one alert");
                Check(!policy.ShouldNotify(d, settings, false, now.AddMinutes(1)), "Duplicate alert suppressed");
                Check(policy.ShouldNotify(d, settings, false, now.AddHours(4)), "Four hour reminder allowed");
                d.Charging = true;
                Check(!policy.ShouldNotify(d, settings, false, now.AddHours(5)), "Charging devices do not alert");
                d.Charging = false; d.Status = "stale";
                Check(!policy.ShouldNotify(d, settings, false, now.AddHours(6)), "Stale data cannot alert");
                d.Status = "ok"; d.Battery = null;
                Check(!policy.ShouldNotify(d, settings, false, now.AddHours(6)), "Unknown battery cannot alert");
                settings.Notifications = false; d.Battery = 1;
                Check(!policy.ShouldNotify(d, settings, false, now.AddHours(6)), "Notifications opt out respected");
                var entries = new List<CatalogEntry> { new CatalogEntry { HardwareId = "VID_046D&PID_C539", ReportedName = "Model A", DisplayName = "A" } };
                d = new DeviceState { HardwareId = "VID_046D&PID_C539", Name = "Model B" };
                Check(Catalog.Match(entries, d) == null, "Shared receiver PID must not select wrong model thumbnail");
                d.Name = "Model A"; Check(Catalog.Match(entries, d) != null, "Exact model match accepted");
                string temp = Path.Combine(Path.GetTempPath(), "peripheral-power-test-" + Guid.NewGuid().ToString("N") + ".json");
                try {
                    Json.Write(temp, new Snapshot { Devices = { new DeviceState { Name = "中文鼠标", Battery = null, Status = "unsupported" } } });
                    var roundTrip = Json.Read<Snapshot>(temp);
                    Check(roundTrip.Devices[0].Name == "中文鼠标" && !roundTrip.Devices[0].Battery.HasValue, "JSON preserves Unicode and unknown battery");
                    Json.Write(temp, new Snapshot { Devices = { new DeviceState { Name = "更新", Battery = 0 } } });
                    Check(Json.Read<Snapshot>(temp).Devices[0].Battery == 0, "JSON replacement preserves valid zero");
                } finally { if (File.Exists(temp)) File.Delete(temp); }
                File.WriteAllText(output, "PASS: " + passed + " checks.\r\nProtocol parsing, native ABI, response correlation, alert policy, thumbnail matching, JSON persistence.\r\n");
                return 0;
            } catch (Exception ex) { File.WriteAllText(output, "FAIL after " + passed + " checks: " + ex); return 1; }
        }
    }
}
