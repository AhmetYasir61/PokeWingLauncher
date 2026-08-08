using System;
using System.Management;

namespace FWLauncherV2.Services
{
    /// <summary>
    /// Cihaz kimliğini üretir (işlemci kimliği). WMI başarısız olursa makine adına düşer.
    /// Sonuç önbelleğe alınır; WMI sorgusu maliyetlidir.
    /// </summary>
    public static class HardwareId
    {
        private static string? _cached;

        public static string Get()
        {
            if (!string.IsNullOrEmpty(_cached))
                return _cached;

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var id = obj["ProcessorId"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        _cached = id;
                        return _cached;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Cihaz kimliği alınamadı: {ex.Message}");
            }

            _cached = Environment.MachineName;
            return _cached;
        }
    }
}
