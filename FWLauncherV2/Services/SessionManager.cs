using System;
using System.IO;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FWLauncherV2.Models;

namespace FWLauncherV2.Services
{
    public static class SessionManager
    {
        private const string ValidateSessionUrl = "https://pokewing.com/pwapi/validate_session.php";

        /// <summary>
        /// Kayıtlı oturum belirtecini sunucuda doğrular. Geçerliyse kullanıcı adını,
        /// aksi halde null döner. Ağ hatası da null ile sonuçlanır.
        /// </summary>
        public static async Task<string?> TryAutoLogin()
        {
            var settings = SettingsService.Load();

            if (string.IsNullOrEmpty(settings.LastUsername) ||
                string.IsNullOrEmpty(settings.SessionToken))
                return null;

            try
            {
                var response = await HttpClientProvider.Api.PostAsJsonAsync(
                    ValidateSessionUrl,
                    new { username = settings.LastUsername, token = settings.SessionToken });

                if (!response.IsSuccessStatusCode)
                    return null;

                var result = await response.Content.ReadFromJsonAsync<SessionCheckResponse>();
                if (result?.Success != true)
                    return null;

                // Sunucu kanonik (kayıtlı) kullanıcı adını döndürdüyse onu kullan ve
                // kaydı düzelt (örn. "ametio" -> "Ametio"). Yoksa kayıtlı isme düş.
                string canonical = !string.IsNullOrWhiteSpace(result.Username)
                    ? result.Username
                    : settings.LastUsername;

                if (!string.Equals(canonical, settings.LastUsername, StringComparison.Ordinal))
                {
                    settings.LastUsername = canonical;
                    try { SettingsService.Save(settings); } catch { /* düzeltme kaydı başarısız olsa da devam */ }
                }

                return canonical;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Otomatik giriş doğrulanamadı: {ex.Message}");
                return null;
            }
        }

        /// <summary>Oturumu temizler (çıkış). Ayar dosyası varsa siler.</summary>
        public static void ClearSession()
        {
            try
            {
                if (File.Exists(SettingsService.SettingsPath))
                    File.Delete(SettingsService.SettingsPath);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Oturum temizlenemedi: {ex.Message}");
            }
        }
    }
}
