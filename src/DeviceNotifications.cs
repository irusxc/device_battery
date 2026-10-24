using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PeripheralPower
{
    internal sealed class DeviceNotifications : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Filter { internal uint Size, Type, Reserved; internal Guid Class; internal short Name; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr RegisterDeviceNotification(IntPtr window, ref Filter filter, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterDeviceNotification(IntPtr handle);
        private IntPtr registration;
        internal DeviceNotifications(IntPtr window)
        {
            // DEVICE_NOTIFY_ALL_INTERFACE_CLASSES delivers arrival/removal to hidden tray windows too.
            var filter = new Filter { Size = (uint)Marshal.SizeOf(typeof(Filter)), Type = 5 };
            registration = RegisterDeviceNotification(window, ref filter, 4);
            if (registration == IntPtr.Zero) throw new Win32Exception();
        }
        internal static bool IsDeviceChange(int message, IntPtr value)
        {
            int code = value.ToInt32();
            return message == 0x219 && (code == 0x8000 || code == 0x8004 || code == 7);
        }
        public void Dispose() { if (registration != IntPtr.Zero) { UnregisterDeviceNotification(registration); registration = IntPtr.Zero; } }
    }
}
