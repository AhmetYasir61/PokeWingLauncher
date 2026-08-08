using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FWLauncherV2.Models;

namespace FWLauncherV2.Services
{
    /// <summary>
    /// Market/coin sunucu uçlarıyla konuşur. Sunucuya erişilemezse market için yerleşik örnek veriye düşer.
    /// </summary>
    public static class ShopService
    {
        private static HttpClient Api => HttpClientProvider.Api;

        // ================= MARKET =================
        /// <param name="allowSample">false ise sunucu boş/erişilemez olduğunda örnek veri DÖNMEZ
        /// (admin panelinin gerçek veritabanını göstermesi için).</param>
        public static async Task<MarketData> GetMarketAsync(bool allowSample = true)
        {
            try
            {
                var data = await Api.GetFromJsonAsync<MarketData>(UserSession.ApiBase + "products.php");
                if (data != null)
                {
                    if (data.Products.Count > 0) return data;
                    if (!allowSample) return data;   // boş ama gerçek veritabanı
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Market verisi alınamadı: {ex.Message}");
                if (!allowSample) return new MarketData();
            }
            return allowSample ? SampleMarket() : new MarketData();
        }

        public static Task<ShopResult> BuyAsync(string productId)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("buy.php", new { username = u, token = t, productId = ParseInt(productId) });
        }

        // ================= COIN TRANSFER =================
        public static Task<ShopResult> TransferAsync(string toUsername, long amount)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("transfer.php", new { username = u, token = t, toUsername, amount });
        }

        // ================= ADMIN: SUNUCU YÖNETİM KÖPRÜSÜ (/pwadmin) =================
        /// <summary>
        /// Launcher ↔ sunucu köprüsü: bir komutu pw_deliveries kuyruğuna yazar; mod delivery_pull ile
        /// çeker + çalıştırır (RCON YOK). player = ONLINE bir oyuncu (genel komutlar için online bir admin adı).
        /// </summary>
        public static Task<ShopResult> RunServerCommandAsync(string onlinePlayer, string command)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_run_command.php", new { username = u, token = t, player = onlinePlayer, command });
        }

        /// <summary>Tüm sunucuya duyuru gönder (mod toast + oyun-içi Duyurular).</summary>
        public static Task<ShopResult> AnnounceAsync(string onlinePlayer, string message)
            => RunServerCommandAsync(onlinePlayer, "pwadmin announce " + message);

        /// <summary>Mod sistemini aç/kapat (treefeller|combat|choptrees|voiceonly).</summary>
        public static Task<ShopResult> ToggleSystemAsync(string onlinePlayer, string system)
            => RunServerCommandAsync(onlinePlayer, "pwadmin toggle " + system);

        /// <summary>AI bot başlat.</summary>
        public static Task<ShopResult> SpawnBotsAsync(string onlinePlayer, int count)
            => RunServerCommandAsync(onlinePlayer, "aiplay " + count);

        /// <summary>Bir oyuncuya kasa aç.</summary>
        public static Task<ShopResult> GiveCrateAsync(string targetPlayer, string crate, int amount)
            => RunServerCommandAsync(targetPlayer, $"crate give {targetPlayer} {crate} {amount}");

        // ================= ADMIN: DEV/OWNER YETKİ YÖNETİMİ =================
        /// <summary>Tüm dev/owner listesini çeker (Item Creator vb. yetkileri).</summary>
        public static async Task<List<DevEntry>> GetDevsAsync()
        {
            var (u, t) = UserSession.Credentials();
            try
            {
                var resp = await Api.PostAsJsonAsync(UserSession.ApiBase + "admin_manage_dev.php",
                    new { username = u, token = t, action = "list" });
                var data = await resp.Content.ReadFromJsonAsync<DevListResult>();
                return data?.Devs ?? new List<DevEntry>();
            }
            catch (Exception ex) { Logger.Warn($"Dev listesi alınamadı: {ex.Message}"); return new List<DevEntry>(); }
        }

        /// <summary>Bir oyuncuya dev/owner rütbesi ver (role = "dev" | "owner").</summary>
        public static Task<ShopResult> SetDevAsync(string targetUser, string role)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_manage_dev.php", new { username = u, token = t, action = "set", targetUser, role });
        }

        /// <summary>Bir oyuncunun dev/owner yetkisini kaldır.</summary>
        public static Task<ShopResult> RemoveDevAsync(string targetUser)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_manage_dev.php", new { username = u, token = t, action = "remove", targetUser });
        }

        // ================= ADMIN: ÜRÜN YÖNETİMİ =================
        public static Task<ShopResult> SaveProductAsync(MarketProduct p)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_save_product.php", new
            {
                username = u,
                token = t,
                id = ParseInt(p.Id),
                name = p.Name,
                category = p.Category,
                type = string.IsNullOrWhiteSpace(p.Type) ? "item" : p.Type,
                price = p.Price,
                description = p.Description ?? "",
                longDesc = p.LongDesc ?? "",
                badge = p.Badge ?? "",
                imageUrl = p.ImageUrl ?? "",
                command = p.Command ?? "",
                kitCommands = p.KitCommands ?? new List<string>(),
                gallery = p.Gallery ?? new List<string>(),
                slots = p.Slots,
                stock = p.Stock
            });
        }

        public static Task<ShopResult> DeleteProductAsync(string id)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_delete_product.php", new { username = u, token = t, id = ParseInt(id) });
        }

        public static async Task<ShopResult> UploadImageAsync(string filePath)
        {
            var (u, t) = UserSession.Credentials();
            try
            {
                using var form = new MultipartFormDataContent
                {
                    { new StringContent(u), "username" },
                    { new StringContent(t), "token" }
                };
                var bytes = await File.ReadAllBytesAsync(filePath);
                form.Add(new ByteArrayContent(bytes), "file", Path.GetFileName(filePath));

                var resp = await Api.PostAsync(UserSession.ApiBase + "upload_image.php", form);
                var r = await resp.Content.ReadFromJsonAsync<ShopResult>();
                return r ?? Fail("Sunucu yanıtı okunamadı.");
            }
            catch (Exception ex)
            {
                Logger.Error("Resim yüklenemedi.", ex);
                return Fail("Resim yüklenemedi: " + ex.Message);
            }
        }

        // ================= REKLAM / DUYURU =================
        /// <param name="allowSample">false ise (admin paneli) sunucu boş/erişilemez olduğunda örnek veri DÖNMEZ.</param>
        public static async Task<List<AdItem>> GetAdsAsync(bool allowSample = true)
        {
            try
            {
                var data = await Api.GetFromJsonAsync<List<AdItem>>(UserSession.ApiBase + "ads.php");
                if (data != null)
                {
                    if (data.Count > 0) return data;
                    if (!allowSample) return data;   // boş ama gerçek veritabanı
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Reklam verisi alınamadı: {ex.Message}");
                if (!allowSample) return new List<AdItem>();
            }
            return allowSample ? SampleAds() : new List<AdItem>();
        }

        // ================= ADMIN: DUYURU YÖNETİMİ =================
        public static Task<ShopResult> SaveAdAsync(AdItem a)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_save_ad.php", new
            {
                username = u,
                token = t,
                id = ParseInt(a.Id),
                title = a.Title,
                description = a.Description ?? "",
                badge = a.Badge ?? "",
                imageUrl = a.ImageUrl ?? "",
                url = a.Url ?? ""
            });
        }

        public static Task<ShopResult> DeleteAdAsync(string id)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_delete_ad.php", new { username = u, token = t, id = ParseInt(id) });
        }

        // ================= AYARLAR (Discord, web vb.) =================
        public static async Task<AppSettings> GetSettingsAsync()
        {
            try
            {
                var s = await Api.GetFromJsonAsync<AppSettings>(UserSession.ApiBase + "settings.php");
                if (s != null) return s;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Ayarlar alınamadı: {ex.Message}");
            }
            return new AppSettings();
        }

        public static Task<ShopResult> SaveSettingAsync(string key, string value)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_save_setting.php", new { username = u, token = t, key, value });
        }

        // ================= ADMIN: KULLANICI / ROL =================
        public static async Task<AdminUsersResult> GetUsersAsync(string q = "")
        {
            var (u, t) = UserSession.Credentials();
            try
            {
                var resp = await Api.PostAsJsonAsync(UserSession.ApiBase + "admin_users.php",
                    new { username = u, token = t, q });
                var r = await resp.Content.ReadFromJsonAsync<AdminUsersResult>();
                return r ?? new AdminUsersResult();
            }
            catch (Exception ex) { Logger.Warn($"admin_users: {ex.Message}"); return new AdminUsersResult(); }
        }

        public static Task<ShopResult> SetRoleAsync(string target, string role)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_set_role.php", new { username = u, token = t, target, role });
        }

        public static Task<ShopResult> WarnAsync(string target, int minutes, string reason)
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_warn.php", new { username = u, token = t, target, minutes, reason });
        }

        public static Task<ShopResult> BanAsync(string target, bool ban, string reason = "")
        {
            var (u, t) = UserSession.Credentials();
            return PostAsync("admin_ban.php", new { username = u, token = t, target, ban = ban ? 1 : 0, reason });
        }

        public static async Task<ModerationStatus> ModerationStatusAsync()
        {
            var (u, t) = UserSession.Credentials();
            try
            {
                var resp = await Api.PostAsJsonAsync(UserSession.ApiBase + "moderation_status.php", new { username = u, token = t });
                var r = await resp.Content.ReadFromJsonAsync<ModerationStatus>();
                return r ?? new ModerationStatus();
            }
            catch (Exception ex) { Logger.Warn($"moderation_status: {ex.Message}"); return new ModerationStatus(); }
        }

        // ================= YARDIMCILAR =================
        private static async Task<ShopResult> PostAsync(string endpoint, object body)
        {
            try
            {
                var resp = await Api.PostAsJsonAsync(UserSession.ApiBase + endpoint, body);
                var r = await resp.Content.ReadFromJsonAsync<ShopResult>();
                return r ?? Fail("Sunucu yanıtı okunamadı.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"{endpoint} hatası: {ex.Message}");
                return Fail("Sunucuya bağlanılamadı.");
            }
        }

        private static ShopResult Fail(string msg) => new() { Success = false, Message = msg };
        private static int ParseInt(string? s) => int.TryParse(s, out var n) ? n : 0;

        // ================= ÖRNEK VERİ (sunucu erişilemezse) =================
        private static MarketData SampleMarket() => new()
        {
            Categories = new()
            {
                new() { Id = "all", Name = "Tümü" },
                new() { Id = "Rütbeler", Name = "Rütbeler" },
                new() { Id = "Coin", Name = "Coin" },
                new() { Id = "Kitler", Name = "Kitler" },
                new() { Id = "Kozmetik", Name = "Kozmetik" },
            },
            Products = new()
            {
                new() { Id="1", Name="VIP Rütbesi",   Category="Rütbeler", Price=5000, Badge="POPÜLER", Description="Renkli isim, /fly, özel kit ve daha fazlası." },
                new() { Id="2", Name="MVP Rütbesi",   Category="Rütbeler", Price=9000, Badge="İNDİRİM", Description="VIP'nin tüm özellikleri + ek avantajlar." },
                new() { Id="3", Name="ELITE Rütbesi", Category="Rütbeler", Price=14000, Description="En üst seviye oyuncu rütbesi." },
                new() { Id="4", Name="Başlangıç Kiti",Category="Kitler",   Price=1500, Description="Yeni başlayanlar için ekipman seti." },
                new() { Id="5", Name="PvP Kiti",      Category="Kitler",   Price=4000, Badge="YENİ", Description="Savaş için tam donanım." },
                new() { Id="6", Name="Efsanevi Pelerin",Category="Kozmetik",Price=2500, Description="Karakterine özel görünüm." },
                new() { Id="7", Name="Zapdos Petty",  Category="Kozmetik", Price=3500, Badge="YENİ", Description="Seni takip eden sevimli evcil hayvan." },
            }
        };

        private static List<AdItem> SampleAds() => new()
        {
            new() { Title="Sezon 1 Başladı!", Badge="ETKİNLİK", Description="Yeni sezon ödülleri, görevler ve sınırlı süreli içerikler seni bekliyor. Hemen katıl!" },
            new() { Title="Hafta Sonu %50 İndirim", Badge="KAMPANYA", Description="Tüm rütbelerde ve coin paketlerinde hafta sonuna özel indirim." },
            new() { Title="Discord'a Katıl", Badge="TOPLULUK", Description="Topluluğumuza katıl, duyuruları kaçırma ve çekilişlere katıl.", Url="https://discord.gg" },
            new() { Title="Yeni Modlar Eklendi", Badge="GÜNCELLEME", Description="Paket bu hafta yeni içerikle güncellendi. 'Güncelle' butonuyla en sona geç." },
        };
    }
}
