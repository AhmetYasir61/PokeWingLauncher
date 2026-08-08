using System;
using System.Collections.Generic;
using System.Linq;

namespace FWLauncherV2.Services
{
    public enum ModLoaderKind { Vanilla, Forge, NeoForge }

    /// <summary>
    /// Forge ve NeoForge mod yükleyicileri için ortak yardımcı: tür tespiti, yükleyici URL'si
    /// üretimi ve kurulduktan sonra sürüm adının çözümlenmesi. Her ikisi de aynı "headless"
    /// kurulum komutuyla (java -jar installer.jar --installClient "&lt;dir&gt;") kurulur.
    /// </summary>
    public static class ModLoaderService
    {
        /// <summary>Manifest'teki loader kimliğinden ("forge-47.2.0", "neoforge-21.1.95") türü tespit eder.</summary>
        public static ModLoaderKind Detect(string? loaderId)
        {
            if (string.IsNullOrWhiteSpace(loaderId))
                return ModLoaderKind.Vanilla;

            var id = loaderId.Trim().ToLowerInvariant();
            if (id.StartsWith("neoforge")) return ModLoaderKind.NeoForge;
            if (id.StartsWith("forge")) return ModLoaderKind.Forge;
            return ModLoaderKind.Vanilla;
        }

        /// <summary>"forge-47.2.0" → "47.2.0", "neoforge-21.1.95" → "21.1.95".</summary>
        public static string ExtractVersion(string loaderId)
        {
            if (string.IsNullOrWhiteSpace(loaderId)) return "";
            int idx = loaderId.IndexOf('-');
            return idx >= 0 ? loaderId[(idx + 1)..].Trim() : loaderId.Trim();
        }

        public static string DisplayName(ModLoaderKind kind) => kind switch
        {
            ModLoaderKind.NeoForge => "NeoForge",
            ModLoaderKind.Forge => "Forge",
            _ => "Vanilla"
        };

        /// <summary>Resmî maven deposundan yükleyici jar URL'si üretir.</summary>
        public static string BuildInstallerUrl(ModLoaderKind kind, string mcVersion, string loaderVersion)
        {
            switch (kind)
            {
                case ModLoaderKind.NeoForge:
                    // NeoForge sürümü mc sürümünü içermez (örn. 21.1.95).
                    return $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar";

                case ModLoaderKind.Forge:
                    // Forge tam sürümü "{mc}-{loaderVersion}" biçimindedir (örn. 1.20.1-47.2.0).
                    string full = loaderVersion.Contains(mcVersion) ? loaderVersion : $"{mcVersion}-{loaderVersion}";
                    return $"https://maven.minecraftforge.net/net/minecraftforge/forge/{full}/forge-{full}-installer.jar";

                default:
                    return "";
            }
        }

        /// <summary>
        /// Kurulu sürümler arasından bu loader'a ait olanı bulur. NeoForge klasör adı
        /// "neoforge-21.1.95", Forge ise "1.20.1-forge-47.2.0" biçimindedir.
        /// </summary>
        /// <param name="exactOnly">
        /// true ise yalnızca tam (loader sürümü dahil) eşleşme döner; gevşek eşleşmeye düşmez.
        /// Sürüm güncellemelerinde "kurulu mu?" kararını doğru vermek için kullanılır.
        /// </param>
        public static string? FindInstalledVersion(
            ModLoaderKind kind, string mcVersion, string loaderVersion, IEnumerable<string> versionNames,
            bool exactOnly = false)
        {
            var names = versionNames.ToList();
            string ver = loaderVersion.ToLowerInvariant();

            switch (kind)
            {
                case ModLoaderKind.NeoForge:
                {
                    var exact = names.FirstOrDefault(n => n.ToLowerInvariant() is var l && l.Contains("neoforge") && l.Contains(ver));
                    if (exact != null || exactOnly) return exact;
                    return names.FirstOrDefault(n => n.ToLowerInvariant().Contains("neoforge"));
                }

                case ModLoaderKind.Forge:
                {
                    var exact = names.FirstOrDefault(n => n.ToLowerInvariant() is var l
                        && l.Contains("forge") && !l.Contains("neoforge") && l.Contains(mcVersion) && l.Contains(ver));
                    if (exact != null || exactOnly) return exact;
                    return names.FirstOrDefault(n => n.ToLowerInvariant() is var l
                        && l.Contains("forge") && !l.Contains("neoforge"));
                }

                default:
                {
                    var exact = names.FirstOrDefault(n => n == mcVersion);
                    if (exact != null || exactOnly) return exact;
                    return names.FirstOrDefault(n => n.Contains(mcVersion));
                }
            }
        }

        /// <summary>1.20 ve üzeri sürümler quickPlay argümanlarını destekler (eski --server kaldırıldı).</summary>
        public static bool SupportsQuickPlay(string mcVersion)
        {
            try
            {
                var parts = mcVersion.Split('.');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int minor))
                    return minor >= 20;
            }
            catch { /* yok say */ }
            return false;
        }
    }
}
