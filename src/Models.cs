using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PeripheralPower
{
    public sealed class DeviceState
    {
        public string Id { get; set; }
        public string PhysicalId { get; set; }
        public string Name { get; set; }
        public string Manufacturer { get; set; }
        public string Kind { get; set; }
        public string Connection { get; set; }
        public string HardwareId { get; set; }
        public string Adapter { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public string Level { get; set; }
        public int? Battery { get; set; }
        public bool Charging { get; set; }
        public string UpdatedUtc { get; set; }
        public List<string> Interfaces { get; set; }
        public DeviceState() { Interfaces = new List<string>(); Kind = "receiver"; Status = "unsupported"; Connection = "USB"; Adapter = "未匹配"; }
        public static string Identity(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").Substring(0, 20);
        }
    }
    public sealed class Snapshot
    {
        public string ScannedUtc { get; set; }
        public bool InventoryComplete { get; set; }
        public bool BatteryComplete { get; set; }
        public List<DeviceState> Devices { get; set; }
        public List<string> Errors { get; set; }
        public Snapshot() { Devices = new List<DeviceState>(); Errors = new List<string>(); }
    }
    public sealed class Preferences
    {
        public int RefreshSeconds { get; set; }
        public bool Notifications { get; set; }
        public int LowThreshold { get; set; }
        public Dictionary<string, string> Images { get; set; }
        public Preferences() { RefreshSeconds = 60; Notifications = true; LowThreshold = 20; Images = new Dictionary<string, string>(); }
        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeripheralPower"); } }
        public static Preferences Load()
        {
            try {
                var p = Json.Read<Preferences>(Path.Combine(Folder, "settings.json"));
                if (p == null) return new Preferences();
                if (p.RefreshSeconds != 30 && p.RefreshSeconds != 60 && p.RefreshSeconds != 120) p.RefreshSeconds = 60;
                p.LowThreshold = Math.Max(5, Math.Min(50, p.LowThreshold));
                if (p.Images == null) p.Images = new Dictionary<string, string>();
                return p;
            } catch { return new Preferences(); }
        }
        public void Save() { Directory.CreateDirectory(Folder); Json.Write(Path.Combine(Folder, "settings.json"), this); }
    }
    public static class Json
    {
        public static T Read<T>(string path) { return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        public static void Write(string path, object data)
        {
            string temporary = path + ".new";
            try {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public sealed class CatalogEntry
    {
        public string HardwareId { get; set; }
        public string ReportedName { get; set; }
        public string DisplayName { get; set; }
        public string Kind { get; set; }
        public string Image { get; set; }
    }
    public static class Catalog
    {
        // A shared receiver PID alone does not identify the paired model.
        public static CatalogEntry Match(IEnumerable<CatalogEntry> entries, DeviceState device)
        {
            return entries.FirstOrDefault(e => e != null && !String.IsNullOrEmpty(e.ReportedName)
                && String.Equals(e.ReportedName, device.Name, StringComparison.OrdinalIgnoreCase)
                && (String.IsNullOrEmpty(e.HardwareId) || String.Equals(e.HardwareId, device.HardwareId, StringComparison.OrdinalIgnoreCase)));
        }
        public static List<CatalogEntry> Load()
        {
            try { return Json.Read<List<CatalogEntry>>(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device-catalog.json")) ?? new List<CatalogEntry>(); }
            catch { return new List<CatalogEntry>(); }
        }
    }
    public sealed class NotificationPolicy
    {
        private readonly Dictionary<string, DateTime> sent = new Dictionary<string, DateTime>();
        public bool ShouldNotify(DeviceState d, Preferences settings, bool demo, DateTime now)
        {
            if (demo || !settings.Notifications || d.Status != "ok" || !d.Battery.HasValue) return false;
            if (d.Charging || d.Battery.Value > settings.LowThreshold + 5) { sent.Remove(d.Id); return false; }
            if (d.Battery.Value > settings.LowThreshold) return false;
            DateTime last;
            if (sent.TryGetValue(d.Id, out last) && now - last < TimeSpan.FromHours(4)) return false;
            sent[d.Id] = now;
            return true;
        }
    }
}
