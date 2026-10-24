using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeripheralPower
{
    internal sealed class HidppTransport : IDisposable
    {
        private readonly FileStream stream;
        private readonly int inputLength, outputLength;
        private byte softwareId = 1;
        internal HidppTransport(HidEndpoint endpoint)
        {
            var handle = Native.Open(endpoint.Path, true);
            if (handle.IsInvalid) { handle.Dispose(); throw new IOException("接收器被占用或无法访问。"); }
            try { stream = new FileStream(handle, FileAccess.ReadWrite, endpoint.Caps.InputLength, true); }
            catch { handle.Dispose(); throw; }
            inputLength = endpoint.Caps.InputLength; outputLength = endpoint.Caps.OutputLength;
        }
        internal byte[] Request(byte device, byte feature, byte function, params byte[] parameters)
        {
            // Software IDs distinguish our replies from notifications and other clients.
            softwareId = (byte)(softwareId % 6 + 1);
            byte fn = (byte)(function | softwareId);
            var packet = new byte[outputLength];
            packet[0] = 0x11; packet[1] = device; packet[2] = feature; packet[3] = fn;
            Array.Copy(parameters, 0, packet, 4, Math.Min(parameters.Length, packet.Length - 4));
            var write = stream.WriteAsync(packet, 0, packet.Length);
            if (!write.Wait(700)) throw new TimeoutException();
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 700) {
                var reply = new byte[inputLength];
                var read = stream.ReadAsync(reply, 0, reply.Length);
                if (!read.Wait(Math.Max(1, 700 - (int)clock.ElapsedMilliseconds))) throw new TimeoutException();
                if (read.Result < 7) continue;
                var actual = reply.Take(read.Result).ToArray();
                if (IsError(actual, device, feature, fn)) return null;
                if (Matches(actual, device, feature, fn)) return actual.Skip(4).ToArray();
            }
            throw new TimeoutException();
        }
        internal static bool Matches(byte[] r, byte device, byte feature, byte function)
        {
            return r.Length >= 7 && (r[0] == 0x10 || r[0] == 0x11) && r[1] == device && r[2] == feature && r[3] == function;
        }
        internal static bool IsError(byte[] r, byte device, byte feature, byte function)
        {
            return r.Length >= 7 && r[1] == device && (r[0] == 0x11 && r[2] == 0xFF || r[0] == 0x10 && r[2] == 0x8F) && r[3] == feature && r[4] == function;
        }
        internal byte Feature(byte device, ushort id)
        {
            byte[] response = Request(device, 0, 0, (byte)(id >> 8), (byte)id, 0);
            return response == null || response.Length == 0 ? (byte)0 : response[0];
        }
        public void Dispose() { stream.Dispose(); }
    }
    internal sealed class LogitechBatteryAdapter : IBatteryAdapter
    {
        public string Name { get { return "Logitech HID++ 2.0"; } }
        public bool CanHandle(IList<HidEndpoint> endpoints)
        {
            return endpoints.Any(e => e.Vendor == 0x046D && e.Caps.UsagePage == 0xFF00 && e.Caps.Usage == 2 && e.Caps.InputLength == 20 && e.Caps.OutputLength == 20);
        }
        public List<DeviceState> Read(IList<HidEndpoint> endpoints, DeviceState receiver)
        {
            var result = new List<DeviceState>();
            var endpoint = endpoints.First(e => e.Vendor == 0x046D && e.Caps.UsagePage == 0xFF00 && e.Caps.Usage == 2 && e.Caps.InputLength == 20 && e.Caps.OutputLength == 20);
            receiver.Adapter = Name;
            receiver.Status = "unavailable";
            receiver.Detail = "发现 HID++ 接口，但设备未响应或未提供支持的电量功能；请唤醒设备后刷新。";
            // Receivers use slots 1..6; directly attached devices use 0xFF.
            bool isReceiver = endpoint.ProductId >= 0xC500 && endpoint.ProductId <= 0xC5FF;
            byte[] slots = isReceiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF };
            foreach (byte slot in slots) {
                try {
                    using (var transport = new HidppTransport(endpoint)) {
                        byte marker = (byte)(0xA0 + slot % 32);
                        byte[] ping = transport.Request(slot, 0, 0x10, 0, 0, marker);
                        if (ping == null || ping.Length < 3 || ping[0] < 2 || ping[2] != marker) continue;
                        var d = new DeviceState {
                            Id = DeviceState.Identity(endpoint.Group + ":" + slot),
                            PhysicalId = receiver.PhysicalId,
                            Name = isReceiver ? "Logitech 无线设备 · " + slot : receiver.Name,
                            Manufacturer = "Logitech", HardwareId = receiver.HardwareId,
                            Adapter = Name, Connection = isReceiver ? "2.4 GHz · 无线接收器" : "USB / HID",
                            Kind = receiver.Kind, Interfaces = receiver.Interfaces,
                            Detail = "设备已响应，但未公开受支持的电量字段。"
                        };
                        result.Add(d);
                        byte battery = transport.Feature(slot, 0x1004);
                        if (battery != 0) {
                            byte[] reply = transport.Request(slot, battery, 0x10);
                            if (reply != null) ApplyUnified(d, reply);
                        }
                        if (d.Status != "ok") {
                            battery = transport.Feature(slot, 0x1000);
                            if (battery != 0) {
                                byte[] reply = transport.Request(slot, battery, 0);
                                if (reply != null) ApplyLegacy(d, reply);
                            }
                        }
                        if (d.Status == "ok") d.UpdatedUtc = DateTime.UtcNow.ToString("o");
                        // Name lookup is optional; battery results survive a name timeout.
                        try { ReadName(transport, slot, d); } catch { }
                        d.Id = DeviceState.Identity(endpoint.Group + ":" + slot + ":" + d.Name);
                    }
                } catch (Exception ex) {
                    if (!(ex is TimeoutException || ex is IOException || ex is AggregateException || ex is UnauthorizedAccessException)) throw;
                    var current = result.LastOrDefault(d => d.Id == DeviceState.Identity(endpoint.Group + ":" + slot));
                    if (current != null && current.Status != "ok") { current.Status = "unavailable"; current.Detail = "设备响应超时；可能正在休眠，请唤醒后刷新。"; }
                }
            }
            return result;
        }
        private static void ReadName(HidppTransport transport, byte slot, DeviceState d)
        {
            byte feature = transport.Feature(slot, 0x0005);
            if (feature == 0) return;
            var length = transport.Request(slot, feature, 0);
            if (length == null || length.Length == 0 || length[0] == 0 || length[0] > 100) return;
            var name = new List<byte>();
            while (name.Count < length[0]) {
                var fragment = transport.Request(slot, feature, 0x10, (byte)name.Count);
                if (fragment == null || fragment.Length == 0) return;
                name.AddRange(fragment.Take(length[0] - name.Count));
            }
            d.Name = Encoding.UTF8.GetString(name.ToArray()).TrimEnd('\0');
            var kind = transport.Request(slot, feature, 0x20);
            if (kind != null && kind.Length > 0) {
                if (kind[0] == 0) d.Kind = "keyboard";
                else if (kind[0] == 3 || kind[0] == 4 || kind[0] == 5) d.Kind = "mouse";
                else if (kind[0] == 8) d.Kind = "headset";
            }
        }
        internal static void ApplyUnified(DeviceState d, byte[] report)
        {
            if (report.Length < 4) return;
            d.Battery = report[0] > 0 && report[0] <= 100 ? (int?)report[0] : null;
            if (report[0] > 100) return;
            d.Level = report[1] == 8 ? "充足" : report[1] == 4 ? "良好" : report[1] == 2 ? "偏低" : report[1] == 1 ? "极低" : null;
            d.Charging = report[2] == 1 || report[2] == 2 || report[2] == 4;
            if (d.Battery.HasValue || d.Level != null) {
                d.Status = "ok";
                d.Detail = d.Battery.HasValue ? "读取自 HID++ Unified Battery。" : "设备仅报告电量档位，未提供精确百分比。";
            }
        }
        internal static void ApplyLegacy(DeviceState d, byte[] report)
        {
            if (report.Length < 3 || report[0] == 0 || report[0] > 100) return;
            d.Battery = report[0]; d.Charging = report[2] == 1 || report[2] == 2 || report[2] == 4;
            d.Status = "ok"; d.Detail = "读取自 HID++ Battery Status。数值精度取决于设备。";
        }
    }
    internal static class Scanner
    {
        internal static Snapshot Scan(Action<Snapshot> progress)
        {
            var snapshot = UsbInventory.Scan();
            // Publish the full inventory before opening HID handles or querying slow protocols.
            if (progress != null) progress(snapshot);
            var endpoints = Native.Enumerate(snapshot.Errors);
            var adapters = new IBatteryAdapter[] { new LogitechBatteryAdapter(), new StandardBatteryAdapter() };
            foreach (var group in endpoints.GroupBy(e => e.Group)) {
                var list = group.ToList(); var first = list.First();
                bool mouse = list.Any(e => e.Caps.UsagePage == 1 && e.Caps.Usage == 2);
                bool keyboard = list.Any(e => e.Caps.UsagePage == 1 && e.Caps.Usage == 6);
                string name = list.Select(e => e.Product).FirstOrDefault(n => !String.IsNullOrWhiteSpace(n));
                string physicalId = DeviceState.Identity(group.Key);
                var d = snapshot.Devices.FirstOrDefault(device => device.PhysicalId == physicalId) ?? new DeviceState {
                    Id = physicalId, PhysicalId = physicalId, Name = name ?? "USB HID 设备", Manufacturer = first.Manufacturer,
                    HardwareId = first.HardwareId, Kind = mouse && !keyboard ? "mouse" : keyboard && !mouse ? "keyboard" : "receiver",
                    Detail = "已识别 USB/HID 设备。尚未找到可用电量协议；可能是有线设备，或无线接收器没有公开电量。",
                    Interfaces = list.Select(e => String.Format("Usage {0:X4}:{1:X4} · Input {2} / Output {3} / Feature {4}", e.Caps.UsagePage, e.Caps.Usage, e.Caps.InputLength, e.Caps.OutputLength, e.Caps.FeatureLength)).Distinct().ToList()
                };
                if (!String.IsNullOrWhiteSpace(name)) d.Name = name;
                if (!String.IsNullOrWhiteSpace(first.Manufacturer)) d.Manufacturer = first.Manufacturer;
                d.Status = "unsupported";
                d.Detail = "设备已连接；未提供可读取的电量。";
                if (mouse && !keyboard) d.Kind = "mouse";
                else if (keyboard && !mouse) d.Kind = "keyboard";
                foreach (var e in list) d.Interfaces.Add(String.Format("HID {0:X4}:{1:X4} · Input {2} / Output {3} / Feature {4}", e.Caps.UsagePage, e.Caps.Usage, e.Caps.InputLength, e.Caps.OutputLength, e.Caps.FeatureLength));
                List<DeviceState> supported = null;
                foreach (var adapter in adapters) {
                    if (!adapter.CanHandle(list)) continue;
                    try {
                        var found = adapter.Read(list, d);
                        if (found.Count > 0) { supported = found; break; }
                    } catch (Exception ex) { snapshot.Errors.Add(first.HardwareId + " · " + adapter.Name + " · " + ex.GetType().Name); }
                }
                snapshot.Devices.RemoveAll(device => device.PhysicalId == physicalId);
                snapshot.Devices.AddRange(supported ?? new List<DeviceState> { d });
                if (progress != null) progress(snapshot);
            }
            foreach (var device in snapshot.Devices.Where(device => device.Status == "pending")) { device.Status = "unsupported"; device.Detail = "设备已连接；未提供可读取的电量。"; }
            snapshot.BatteryComplete = true;
            return snapshot;
        }
    }
}
