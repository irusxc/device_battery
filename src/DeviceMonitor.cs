using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeripheralPower
{
    // Runs on the UI context. Discovery is independent from potentially slow battery I/O.
    internal sealed class DeviceMonitor : IDisposable
    {
        private readonly Func<Task<Snapshot>> discover, readBattery;
        private readonly Action cancelBattery;
        private bool inventoryBusy, inventoryPending, forcePending, batteryPending, awaitingInventory, disposed;
        private int epoch;
        internal bool BatteryBusy { get; private set; }
        internal Snapshot Current { get; private set; }
        internal string Error { get; private set; }
        internal event Action Changed;
        internal DeviceMonitor(Func<Task<Snapshot>> discoverDevices, Func<Task<Snapshot>> read, Action cancel)
        {
            discover = discoverDevices; readBattery = read; cancelBattery = cancel; Current = new Snapshot();
        }
        internal void DevicesChanged()
        {
            if (disposed) return;
            epoch++; // A response started before this notification must never resurrect a removed device.
            awaitingInventory = true;
            batteryPending = true;
            if (BatteryBusy) cancelBattery();
        }
        internal async Task RefreshDevices(bool forceBattery)
        {
            if (disposed) return;
            inventoryPending = true; forcePending |= forceBattery;
            if (inventoryBusy) return;
            inventoryBusy = true;
            try {
                do {
                    inventoryPending = false;
                    bool force = forcePending; forcePending = false;
                    int version = epoch;
                    try {
                        Snapshot next = await discover();
                        if (disposed) return;
                        if (version != epoch) { inventoryPending = true; forcePending |= force; continue; }
                        if (!next.InventoryComplete) throw new InvalidOperationException("设备列表读取不完整");
                        bool changed = !PhysicalIds(Current).SetEquals(PhysicalIds(next));
                        if (changed) { epoch++; batteryPending = true; if (BatteryBusy) cancelBattery(); }
                        Current = MergeInventory(Current, next);
                        awaitingInventory = false;
                        Error = null;
                        if (force || changed) batteryPending = true;
                        RaiseChanged(); // New/removed devices are published before any battery call.
                    } catch (Exception ex) { Error = "设备列表读取失败 · " + ex.GetType().Name; RaiseChanged(); }
                } while (inventoryPending && !disposed);
            } finally { inventoryBusy = false; }
            if (!disposed && batteryPending) await PumpBattery();
        }
        internal async Task RefreshBattery()
        {
            if (disposed) return;
            batteryPending = true;
            if (!Current.InventoryComplete) { await RefreshDevices(true); return; }
            await PumpBattery();
        }
        private async Task PumpBattery()
        {
            if (BatteryBusy || inventoryBusy || awaitingInventory || disposed) return;
            BatteryBusy = true;
            try {
                do {
                    batteryPending = false;
                    int version = epoch;
                    RaiseChanged();
                    try {
                        Snapshot result = await readBattery();
                        if (disposed) return;
                        if (version != epoch) continue;
                        Current = ApplyBattery(Current, result);
                        Error = result.BatteryComplete ? null : "部分电量读取未完成";
                        RaiseChanged();
                    } catch (Exception ex) {
                        if (version != epoch || disposed) continue;
                        Error = "电量读取失败 · " + ex.GetType().Name;
                        foreach (var d in Current.Devices) if (d.Status == "ok") d.Status = "stale";
                        RaiseChanged();
                    }
                } while (batteryPending && !inventoryBusy && !awaitingInventory && !disposed);
            } finally { BatteryBusy = false; RaiseChanged(); }
        }
        internal static string Key(DeviceState d) { return d.PhysicalId ?? d.Id; }
        private static HashSet<string> PhysicalIds(Snapshot s) { return new HashSet<string>(s.Devices.Select(Key), StringComparer.Ordinal); }
        internal static Snapshot MergeInventory(Snapshot previous, Snapshot next)
        {
            var merged = new Snapshot { InventoryComplete = true, ScannedUtc = next.ScannedUtc, BatteryComplete = previous.BatteryComplete, Errors = next.Errors };
            foreach (var physical in next.Devices) {
                var cached = previous.Devices.Where(d => Key(d) == Key(physical)).ToList();
                if (cached.Count > 0) merged.Devices.AddRange(cached);
                else merged.Devices.Add(physical);
            }
            return merged;
        }
        internal static Snapshot ApplyBattery(Snapshot inventory, Snapshot result)
        {
            var merged = new Snapshot { InventoryComplete = inventory.InventoryComplete, ScannedUtc = inventory.ScannedUtc, BatteryComplete = result.BatteryComplete, Errors = result.Errors };
            foreach (var group in inventory.Devices.GroupBy(Key)) {
                // Ignore devices not in the current inventory, even in a late successful reply.
                var read = result.Devices.Where(d => Key(d) == group.Key).ToList();
                if (read.Count > 0) merged.Devices.AddRange(read);
                else foreach (var d in group) { if (d.Status == "ok") d.Status = "stale"; merged.Devices.Add(d); }
            }
            return merged;
        }
        private void RaiseChanged() { if (!disposed && Changed != null) Changed(); }
        public void Dispose() { disposed = true; epoch++; cancelBattery(); }
    }
}
