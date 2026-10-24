using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PeripheralPower
{
    internal sealed class HidEndpoint
    {
        internal string Path, Group, Product, Manufacturer;
        internal ushort Vendor, ProductId;
        internal Native.Caps Caps;
        internal string HardwareId { get { return String.Format("VID_{0:X4}&PID_{1:X4}", Vendor, ProductId); } }
    }
    internal static class Native
    {
        internal const int Success = 0x110000;
        [StructLayout(LayoutKind.Sequential)] internal struct InterfaceData { internal int Size; internal Guid ClassGuid; internal int Flags; internal IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceInfo { internal int Size; internal Guid ClassGuid; internal uint DevInst; internal IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct Attributes { internal int Size; internal ushort Vendor, Product, Version; }
        [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { internal Guid Id; internal uint Pid; }
        [StructLayout(LayoutKind.Sequential)] internal struct Caps
        {
            internal ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] internal ushort[] Reserved;
            internal ushort LinkNodes, InputButtons, InputValues, InputData, OutputButtons, OutputValues, OutputData, FeatureButtons, FeatureValues, FeatureData;
        }
        [StructLayout(LayoutKind.Explicit, Size = 72)] internal struct ValueCaps
        {
            [FieldOffset(0)] internal ushort UsagePage;
            [FieldOffset(2)] internal byte ReportId;
            [FieldOffset(6)] internal ushort LinkCollection;
            [FieldOffset(12)] internal byte IsRange;
            [FieldOffset(16)] internal byte HasNull;
            [FieldOffset(18)] internal ushort BitSize;
            [FieldOffset(20)] internal ushort ReportCount;
            [FieldOffset(40)] internal int LogicalMin;
            [FieldOffset(44)] internal int LogicalMax;
            [FieldOffset(56)] internal ushort UsageMin;
            [FieldOffset(58)] internal ushort UsageMax;
        }
        [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] buffer, int length);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetManufacturerString(SafeFileHandle handle, byte[] buffer, int length);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data, out Caps caps);
        [DllImport("hid.dll")] internal static extern int HidP_GetValueCaps(int reportType, [Out] ValueCaps[] caps, ref ushort count, IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetUsageValue(int reportType, ushort page, ushort link, ushort usage, out uint value, IntPtr data, byte[] report, uint length);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int length);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetInputReport(SafeFileHandle handle, byte[] buffer, int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, ref DeviceInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DeviceInfo info, StringBuilder id, int size, out int required);
        [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        internal static SafeFileHandle Open(string path, bool write)
        {
            return CreateFile(path, write ? 0xC0000000u : 0, 3, IntPtr.Zero, 3, write ? 0x40000000u : 0, IntPtr.Zero);
        }
        internal static List<HidEndpoint> Enumerate(List<string> errors)
        {
            var result = new List<HidEndpoint>();
            Guid guid; HidD_GetHidGuid(out guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            try {
                for (uint i = 0; ; i++) {
                    var iface = new InterfaceData { Size = Marshal.SizeOf(typeof(InterfaceData)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface)) {
                        if (Marshal.GetLastWin32Error() != 259) errors.Add("部分 HID 接口未能枚举。");
                        break;
                    }
                    var info = new DeviceInfo { Size = Marshal.SizeOf(typeof(DeviceInfo)) };
                    uint size;
                    SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out size, ref info);
                    if (size < 8 || size > 65536) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, size, out size, ref info)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        string physical = UsbInventory.PhysicalInstance(info.DevInst);
                        if (path == null || physical == null) continue;
                        var e = new HidEndpoint { Path = path, Group = physical, Product = "", Manufacturer = "" };
                        using (var handle = Open(path, false)) {
                            if (handle.IsInvalid) { errors.Add("一个 HID 接口无法访问，可能正被驱动独占。"); continue; }
                            var attrs = new Attributes { Size = Marshal.SizeOf(typeof(Attributes)) };
                            if (!HidD_GetAttributes(handle, ref attrs)) continue;
                            e.Vendor = attrs.Vendor; e.ProductId = attrs.Product;
                            byte[] buffer = new byte[512];
                            if (HidD_GetProductString(handle, buffer, buffer.Length)) e.Product = Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
                            Array.Clear(buffer, 0, buffer.Length);
                            if (HidD_GetManufacturerString(handle, buffer, buffer.Length)) e.Manufacturer = Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
                            IntPtr data;
                            if (!HidD_GetPreparsedData(handle, out data)) continue;
                            try { if (HidP_GetCaps(data, out e.Caps) != Success) continue; }
                            finally { HidD_FreePreparsedData(data); }
                        }
                        result.Add(e);
                    } finally { Marshal.FreeHGlobal(detail); }
                }
            } finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }
    }
    internal interface IBatteryAdapter
    {
        string Name { get; }
        bool CanHandle(IList<HidEndpoint> endpoints);
        List<DeviceState> Read(IList<HidEndpoint> endpoints, DeviceState receiver);
    }
    internal sealed class StandardBatteryAdapter : IBatteryAdapter
    {
        public string Name { get { return "标准 HID Battery Strength"; } }
        public bool CanHandle(IList<HidEndpoint> endpoints) { return true; }
        internal static int? Decode(uint raw, int minimum, int maximum)
        {
            // HID Battery Strength is a percent. Do not turn unknown vendor scales into fabricated percentages.
            if (minimum != 0 || maximum != 100 || raw > 100) return null;
            return (int)raw;
        }
        public List<DeviceState> Read(IList<HidEndpoint> endpoints, DeviceState receiver)
        {
            foreach (var e in endpoints) using (var handle = Native.Open(e.Path, false)) {
                if (handle.IsInvalid) continue;
                IntPtr data;
                if (!Native.HidD_GetPreparsedData(handle, out data)) continue;
                try {
                    foreach (int reportType in new[] { 2, 0 }) {
                        ushort count = reportType == 2 ? e.Caps.FeatureValues : e.Caps.InputValues;
                        int length = reportType == 2 ? e.Caps.FeatureLength : e.Caps.InputLength;
                        if (count == 0 || count > 1024 || length < 2 || length > 4096) continue;
                        var caps = new Native.ValueCaps[count];
                        if (Native.HidP_GetValueCaps(reportType, caps, ref count, data) != Native.Success) continue;
                        foreach (var c in caps.Take(count)) {
                            if (c.UsagePage != 6 || !(c.IsRange != 0 ? c.UsageMin <= 0x20 && c.UsageMax >= 0x20 : c.UsageMin == 0x20)) continue;
                            receiver.Adapter = Name;
                            receiver.Status = "unavailable";
                            receiver.Detail = "设备提供标准电量字段，但本次未返回有效百分比；可能休眠或不支持主动查询。";
                            var report = new byte[length]; report[0] = c.ReportId;
                            bool ok = reportType == 2 ? Native.HidD_GetFeature(handle, report, length) : Native.HidD_GetInputReport(handle, report, length);
                            uint raw;
                            if (!ok || Native.HidP_GetUsageValue(reportType, 6, c.LinkCollection, 0x20, out raw, data, report, (uint)length) != Native.Success) continue;
                            int? percent = Decode(raw, c.LogicalMin, c.LogicalMax);
                            if (!percent.HasValue) continue;
                            receiver.Battery = percent; receiver.Status = "ok";
                            receiver.UpdatedUtc = DateTime.UtcNow.ToString("o");
                            receiver.Detail = "由设备的标准 HID 电量字段读取。充电状态未公开。";
                            return new List<DeviceState> { receiver };
                        }
                    }
                } finally { Native.HidD_FreePreparsedData(data); }
            }
            return new List<DeviceState>();
        }
    }
}
