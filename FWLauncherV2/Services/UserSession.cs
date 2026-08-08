using System;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FWLauncherV2.Models;

namespace FWLauncherV2.Services
{
    /// <summary>
    /// Giriş yapan kullanıcının sunucudaki profilini (rol + coin) tutar.
    /// me.php'den yüklenir; satın alma/transfer sonrası bakiye güncellenir.
    /// </summary>
    public static class UserSession
    {
        public const string ApiBase = "https://pokewing.com/pwapi/";

        public static string Username { get; private set; } = "Oyuncu";
        public static string Role { get; private set; } = "player";
        public static long Coins { get; private set; }
        public static bool IsAdmin => Role is "owner" or "admin";
        /// <summary>Ticket'ları görebilen personel: owner/admin/moderator/rehber.</summary>
        public static bool IsStaff => Role is "owner" or "admin" or "moderator" or "rehber";

        /// <summary>Coin/rol değiştiğinde tetiklenir (UI dinler).</summary>
        public static event Action? Changed;

        public static (string username, string token) Credentials()
        {
            var s = SettingsService.Load();
            return (s.LastUsername, s.SessionToken ?? "");
        }

        public static async Task LoadAsync()
        {
            var (u, t) = Credentials();
            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(t))
                return;

            try
            {
                var resp = await HttpClientProvider.Api.PostAsJsonAsync(ApiBase + "me.php",
                    new { username = u, token = t });

                if (!resp.IsSuccessStatusCode) return;

                var me = await resp.Content.ReadFromJsonAsync<MeResponse>();
                if (me?.Success == true)
                {
                    if (!string.IsNullOrWhiteSpace(me.Username)) Username = me.Username;
                    Role = string.IsNullOrWhiteSpace(me.Role) ? "player" : me.Role;
                    Coins = me.Coins;
                    Raise();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Profil (me.php) yüklenemedi: {ex.Message}");
            }
        }

        public static void SetCoins(long coins)
        {
            Coins = coins;
            Raise();
        }

        private static void Raise()
        {
            try { Changed?.Invoke(); } catch { /* dinleyici hatası yutulur */ }
        }
    }
}
