using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FWLauncherV2.Services
{
    /// <summary>
    /// 🛡️ Bütünlük muhafızı — DÜRÜST TAMPER CAYDIRICISI.
    ///
    /// ⚠️ Bu KESİN koruma DEĞİLDİR. Uzman bir saldırgan bu kontrolleri de patch'leyebilir/atlatabilir
    /// (kod client tarafında çalışır, kullanıcının makinesinde). Amaç: gündelik kurcalamayı ve zayıf
    /// saldırganı CAYDIRMAK + tespit edilebilen tamper durumlarını sunucuya raporlamak. Ciddi hile
    /// önleme sunucu-side (GrimAC) işidir.
    ///
    /// Ne yapar:
    ///  • Çalıştırılabilir dosyanın hash'ini bilinen değerle karşılaştırır (patch tespiti)
    ///  • Debugger takılı mı bakar (canlı analiz/decompile-debug caydırıcı)
    ///  • Sorun bulursa: sunucuya HWID raporu (kara liste) + kullanıcıya uyarı
    /// </summary>
    public static class IntegrityGuard
    {
        // Yayın exe'sinin bilinen SHA-256'sı — her RESMİ derlemede güncellenir (aşağıda açıklama).
        // Boş bırakılırsa hash kontrolü ATLANIR (geliştirme modunda). Yayında doldur.
        private const string KnownExeHash = "";

        /// <summary>Açılışta çağrılır. Tamper tespit edilirse true döner (çağıran uygulamayı kapatabilir).</summary>
        public static async Task<bool> CheckAsync()
        {
            try
            {
                bool tampered = false;
                string reason = "";

                // 1) Debugger caydırıcısı (canlı analiz)
                if (Debugger.IsAttached || IsRemoteDebuggerPresent())
                {
                    tampered = true; reason = "Debugger tespit edildi";
                }

                // 2) Exe hash kontrolü (patch/inject tespiti) — yalnız KnownExeHash doluysa
                if (!tampered && !string.IsNullOrEmpty(KnownExeHash))
                {
                    string current = SelfHash();
                    if (!string.IsNullOrEmpty(current) &&
                        !string.Equals(current, KnownExeHash, StringComparison.OrdinalIgnoreCase))
                    {
                        tampered = true; reason = "Dosya bütünlüğü bozuk (patch/inject)";
                    }
                }

                // 3) Yabancı dosya taraması (DLL-inject / bulaşma) — exe'nin yanına atılan
                //    beklenmeyen .dll/.exe temizlenir. Kararlı zararlıyı durdurmaz ama enjeksiyonu caydırır.
                int cleaned = ScanAndCleanForeignFiles();
                if (cleaned > 0)
                {
                    Logger.Warn($"[IntegrityGuard] {cleaned} şüpheli dosya temizlendi.");
                    // temizlik yapıldı ama uygulama kapatılmaz (yabancı dosya silindi, launcher temiz)
                }

                if (tampered)
                {
                    Logger.Warn($"[IntegrityGuard] TAMPER: {reason}");
                    await ReportTamperAsync(reason);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[IntegrityGuard] kontrol hatası: {ex.Message}");
                return false;   // kontrol hatası kullanıcıyı cezalandırmasın
            }
        }

        /// <summary>Tamper'ı sunucuya raporla → kara listeye HWID ekle (owner kaldırana dek).</summary>
        private static async Task ReportTamperAsync(string reason)
        {
            try
            {
                var (user, _) = UserSession.Credentials();
                await HttpClientProvider.Api.PostAsJsonAsync(
                    UserSession.ApiBase + "report_tamper.php",
                    new { username = user, hwid = HardwareId.Get(), reason });
            }
            catch (Exception ex) { Logger.Warn($"[IntegrityGuard] rapor gönderilemedi: {ex.Message}"); }
        }

        /// <summary>
        /// Yabancı/şüpheli dosya taraması. .NET uygulaması çok sayıda meşru DLL içerdiğinden GÜVENLİ yol:
        /// bilinen enjeksiyon/hile araç desenlerini hedefle (allow-list değil — meşru dosyayı silmeyelim).
        /// Bulursa siler ve sayısını döndürür. NOT: kapsamlı antivirüs DEĞİL (Windows Defender o işi yapar);
        /// yalnız launcher klasörüne atılan bilinen kurcalamа/inject araçlarını temizler.
        /// </summary>
        private static int ScanAndCleanForeignFiles()
        {
            int cleaned = 0;
            try
            {
                string? dir = Path.GetDirectoryName(Environment.ProcessPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;

                // Bilinen inject/hile/kurcalama araç dosya adı desenleri (küçük harf içerir kontrolü)
                string[] badPatterns =
                {
                    "cheatengine", "injector", "inject.dll", "hook.dll", "d3d9_hook",
                    "dinput8.dll", "winmm_hook", "bypass", "hack", "trainer", "megadumper",
                    "dnspy", "ilspy", "de4dot", "detour", "extremeinjector"
                };

                foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(file).ToLowerInvariant();
                    if (!name.EndsWith(".dll") && !name.EndsWith(".exe")) continue;
                    // kendi exe'mize dokunma
                    if (string.Equals(file, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (var bad in badPatterns)
                    {
                        if (name.Contains(bad))
                        {
                            try { File.Delete(file); cleaned++; Logger.Warn($"[IntegrityGuard] silindi: {name}"); }
                            catch (Exception ex) { Logger.Warn($"[IntegrityGuard] silinemedi ({name}): {ex.Message}"); }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Warn($"[IntegrityGuard] tarama hatası: {ex.Message}"); }
            return cleaned;
        }

        private static string SelfHash()
        {
            try
            {
                // Single-file'da Assembly.Location boştur → ProcessPath (gerçek exe) kullan.
                string path = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
                using var sha = SHA256.Create();
                using var fs = File.OpenRead(path);
                return Convert.ToHexString(sha.ComputeHash(fs));
            }
            catch { return ""; }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isPresent);

        private static bool IsRemoteDebuggerPresent()
        {
            try
            {
                bool present = false;
                CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref present);
                return present;
            }
            catch { return false; }
        }
    }
}
