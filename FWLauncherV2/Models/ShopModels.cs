using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows;

namespace FWLauncherV2.Models
{
    internal static class ShopDefaults
    {
        public const string LogoUri = "pack://application:,,,/PokeWingLauncher.png";
    }

    // ================= MARKET =================
    public class MarketCategory
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    public class MarketProduct
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("price")] public long Price { get; set; }
        [JsonPropertyName("badge")] public string? Badge { get; set; }   // örn. "İNDİRİM", "YENİ"

        [JsonIgnore] public string PriceText => Price > 0 ? $"{Price:N0} Coin" : "Ücretsiz";
        [JsonPropertyName("imageUrl")] public string? ImageUrl { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }       // tıklayınca açılacak web adresi
        [JsonPropertyName("command")] public string? Command { get; set; } // satın alınca çalışacak komut ({player})

        // --- kit / katalog ---
        [JsonPropertyName("type")] public string? Type { get; set; }       // item|kit|key|rank|coin|cosmetic
        [JsonPropertyName("longDesc")] public string? LongDesc { get; set; } // katalog uzun açıklaması
        [JsonPropertyName("kitCommands")] public List<string>? KitCommands { get; set; } // kit içeriği (çoklu komut)
        [JsonPropertyName("gallery")] public List<string>? Gallery { get; set; }          // ek görsel URL'leri
        [JsonPropertyName("slots")] public int Slots { get; set; } = 1;
        [JsonPropertyName("stock")] public int Stock { get; set; } = -1;    // -1 = sınırsız

        // çok satırlı TextBox bağlamak için yardımcılar
        [JsonIgnore] public string KitCommandsText
        {
            get => KitCommands == null ? "" : string.Join("\n", KitCommands);
            set => KitCommands = string.IsNullOrWhiteSpace(value) ? new List<string>()
                : new List<string>(value.Replace("\r\n", "\n").Split('\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        [JsonIgnore] public string GalleryText
        {
            get => Gallery == null ? "" : string.Join("\n", Gallery);
            set => Gallery = string.IsNullOrWhiteSpace(value) ? new List<string>()
                : new List<string>(value.Replace("\r\n", "\n").Split('\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        [JsonIgnore] public string DisplayImage =>
            string.IsNullOrWhiteSpace(ImageUrl) ? ShopDefaults.LogoUri : ImageUrl!;
        [JsonIgnore] public Visibility BadgeVisibility =>
            string.IsNullOrWhiteSpace(Badge) ? Visibility.Collapsed : Visibility.Visible;
    }

    public class MarketData
    {
        [JsonPropertyName("categories")] public List<MarketCategory> Categories { get; set; } = new();
        [JsonPropertyName("products")] public List<MarketProduct> Products { get; set; } = new();
    }

    // ================= REKLAM / DUYURU =================
    public class AdItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("badge")] public string? Badge { get; set; }
        [JsonPropertyName("imageUrl")] public string? ImageUrl { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }

        [JsonIgnore] public string DisplayImage =>
            string.IsNullOrWhiteSpace(ImageUrl) ? ShopDefaults.LogoUri : ImageUrl!;
        [JsonIgnore] public Visibility BadgeVisibility =>
            string.IsNullOrWhiteSpace(Badge) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ================= AYARLAR =================
    public class AppSettings
    {
        [JsonPropertyName("discord_url")] public string? DiscordUrl { get; set; }
        [JsonPropertyName("website_url")] public string? WebsiteUrl { get; set; }
    }

    // ================= KULLANICI / ROL YÖNETİMİ =================
    public class AdminUser
    {
        [JsonPropertyName("username")] public string Username { get; set; } = "";  // küçük harf (hedef)
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("role")] public string Role { get; set; } = "player";
        [JsonPropertyName("coins")] public long Coins { get; set; }
        [JsonPropertyName("banned")] public bool Banned { get; set; }
        [JsonPropertyName("warnRemaining")] public double WarnRemaining { get; set; }

        [JsonIgnore] public string RoleText => Role switch
        {
            "owner" => "Owner", "admin" => "Admin", "moderator" => "Moderatör", "rehber" => "Rehber", _ => "Oyuncu"
        };
        [JsonIgnore] public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : char.ToUpperInvariant(Name[0]).ToString();
        [JsonIgnore] public string CoinsText => $"{Coins:N0} Coin";
        [JsonIgnore] public bool IsWarned => WarnRemaining > 0;
        [JsonIgnore] public string ModText => Banned ? "⛔ Yasaklı" : (IsWarned ? "⚠ Uyarılı" : "");
        [JsonIgnore] public Visibility ModVisibility => (Banned || IsWarned) ? Visibility.Visible : Visibility.Collapsed;
        [JsonIgnore] public string BanButtonText => Banned ? "Yasağı Kaldır" : "Ban";
    }

    public class ModerationStatus
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("banned")] public bool Banned { get; set; }
        [JsonPropertyName("remainingMs")] public double RemainingMs { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }

    public class AdminUsersResult
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("meRole")] public string MeRole { get; set; } = "player";
        [JsonPropertyName("users")] public List<AdminUser> Users { get; set; } = new();
    }

    // ================= API YANITLARI =================
    public class MeResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("role")] public string Role { get; set; } = "player";
        [JsonPropertyName("coins")] public long Coins { get; set; }
        [JsonPropertyName("isAdmin")] public bool IsAdmin { get; set; }
    }

    public class ShopResult
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("coins")] public long? Coins { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("id")] public int? Id { get; set; }
    }

    /// <summary>Dev/owner yetki kaydı (Item Creator vb. erişimi).</summary>
    public class DevEntry
    {
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("role")] public string Role { get; set; } = "dev";
    }

    public class DevListResult
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("devs")] public List<DevEntry> Devs { get; set; } = new();
    }
}
