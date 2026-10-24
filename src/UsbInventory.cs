using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PeripheralPower
{
    internal static class UsbInventory
    {
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref Native.DeviceInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref Native.DeviceInfo info, uint property, out uint type, byte[] buffer, uint size, out uint required);
        [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint node, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_ID(uint node, StringBuilder id, int length, uint flags);
        private static readonly Regex Hardware = new Regex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        internal static string HardwareId(string value)
        {
            var m = Hardware.Match(value ?? ""); return m.Success ? m.Value.ToUpperInvariant() : null;
        }
        internal static string PhysicalInstance(uint node)
        {
            // Walk through HID collections and composite USB interfaces to their physical USB device.
            for (int depth = 0; depth < 16; depth++) {
                var id = new StringBuilder(1024);
                if (CM_Get_Device_ID(node, id, id.Capacity, 0) != 0) return null;
                string value = id.ToString();
                if (value.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase) && value.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) < 0)
                    return value.ToUpperInvariant();
                uint parent; if (CM_Get_Parent(out parent, node, 0) != 0) return null; node = parent;
            }
            return null;
        }
        internal static string Property(IntPtr set, ref Native.DeviceInfo info, uint property)
        {
            var bytes = new byte[4096]; uint type, needed;
            return SetupDiGetDeviceRegistryProperty(set, ref info, property, out type, bytes, (uint)bytes.Length, out needed)
                ? Encoding.Unicode.GetString(bytes, 0, (int)Math.Min(needed, bytes.Length)).TrimEnd('\0') : "";
        }
        private static string BusName(IntPtr set, ref Native.DeviceInfo info)
        {
            var key = new Native.PropertyKey { Id = new Guid("540B947E-8B40-45BC-A8A2-6A0B894CBDA2"), Pid = 4 };
            var bytes = new byte[2048]; uint type, needed;
            return Native.SetupDiGetDeviceProperty(set, ref info, ref key, out type, bytes, (uint)bytes.Length, out needed, 0)
                ? Encoding.Unicode.GetString(bytes, 0, (int)Math.Min(needed, bytes.Length)).TrimEnd('\0') : "";
        }
        internal static Snapshot Scan()
        {
            var snapshot = new Snapshot { ScannedUtc = DateTime.UtcNow.ToString("o") };
            var devices = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Present PnP nodes: this does not open devices or wait on battery protocols.
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 6);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            try {
                for (uint index = 0; ; index++) {
                    var info = new Native.DeviceInfo { Size = Marshal.SizeOf(typeof(Native.DeviceInfo)) };
                    if (!SetupDiEnumDeviceInfo(set, index, ref info)) {
                        if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception();
                        break;
                    }
                    var id = new StringBuilder(1024); int required;
                    if (!Native.SetupDiGetDeviceInstanceId(set, ref info, id, id.Capacity, out required)) continue;
                    string instance = id.ToString();
                    // Children provide useful device categories; do not traverse unrelated PCI/software trees.
                    if (!instance.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) && !instance.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase)) continue;
                    string physical = PhysicalInstance(info.DevInst);
                    if (physical == null) continue;
                    string hardware = HardwareId(physical);
                    if (hardware == null) continue;
                    string service = Property(set, ref info, 4);
                    bool root = String.Equals(instance, physical, StringComparison.OrdinalIgnoreCase);
                    if (root && service.StartsWith("USBHUB", StringComparison.OrdinalIgnoreCase)) { excluded.Add(physical); continue; }
                    DeviceState d;
                    if (!devices.TryGetValue(physical, out d)) {
                        string identity = DeviceState.Identity(physical);
                        d = new DeviceState { Id = identity, PhysicalId = identity, HardwareId = hardware, Name = "USB 设备", Status = "pending", Detail = "设备已连接，等待电量查询。", Connection = "USB" };
                        devices.Add(physical, d);
                    }
                    string bus = BusName(set, ref info), friendly = Property(set, ref info, 12), description = Property(set, ref info, 0);
                    string name = !String.IsNullOrWhiteSpace(bus) ? bus : !String.IsNullOrWhiteSpace(friendly) ? friendly : description;
                    if (!String.IsNullOrWhiteSpace(name) && (root || d.Name == "USB 设备")) d.Name = name;
                    if (root) d.Manufacturer = Property(set, ref info, 11);
                    string kind = Property(set, ref info, 7);
                    if (kind.Equals("Mouse", StringComparison.OrdinalIgnoreCase)) d.Kind = "mouse";
                    else if (kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) && d.Kind != "mouse") d.Kind = "keyboard";
                    else if (kind.Equals("MEDIA", StringComparison.OrdinalIgnoreCase) && d.Kind == "receiver") d.Kind = "headset";
                    else if (kind.Equals("Camera", StringComparison.OrdinalIgnoreCase) && d.Kind == "receiver") d.Kind = "camera";
                    else if (kind.Equals("Image", StringComparison.OrdinalIgnoreCase) && d.Kind == "receiver") d.Kind = "scanner";
                    else if (service.Equals("USBSTOR", StringComparison.OrdinalIgnoreCase) && d.Kind == "receiver") d.Kind = "storage";
                    if (service.Equals("USBPRINT", StringComparison.OrdinalIgnoreCase)) d.Kind = "printer";
                    string label = "PnP " + kind + (String.IsNullOrEmpty(service) ? "" : " · " + service);
                    if (!d.Interfaces.Contains(label)) d.Interfaces.Add(label);
                }
            } finally { Native.SetupDiDestroyDeviceInfoList(set); }
            snapshot.Devices = devices.Where(p => !excluded.Contains(p.Key)).Select(p => p.Value).OrderBy(d => d.Id).ToList();
            snapshot.InventoryComplete = true;
            return snapshot;
        }
    }
}
