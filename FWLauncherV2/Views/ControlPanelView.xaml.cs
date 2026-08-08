using FWLauncherV2.Dialogs;
using FWLauncherV2.Models;
using FWLauncherV2.Services;
using Microsoft.Win32;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FWLauncherV2.Views
{
    public partial class ControlPanelView : UserControl
    {
        private string _editId = "0";     // "0" = yeni ürün
        private string _adEditId = "0";   // "0" = yeni duyuru
        private bool _loaded;
        private bool _adsLoaded;
        private bool _linksLoaded;
        private bool _usersLoaded;
        private AdminUser? _modTarget;   // rol/uyarı/ban için seçili kullanıcı
        private int _warnUnit = 1;       // dakika çarpanı

        public ControlPanelView()
        {
            InitializeComponent();
            Loaded += async (s, e) =>
            {
                if (_loaded) return;
                _loaded = true;
                bool owner = UserSession.Role == "owner";
                // admin market/ürün paneline dokunamaz — sadece owner yönetir.
                BtnTabProducts.Visibility = owner ? Visibility.Visible : Visibility.Collapsed;
                // Yetkiler (dev/owner ver-al) hassas — yalnız owner görür.
                BtnTabDevs.Visibility = owner ? Visibility.Visible : Visibility.Collapsed;
                if (owner) await ReloadAsync();
                else { ShowPanel(AdsPanel, BtnTabAds); _adsLoaded = true; await ReloadAdsAsync(); }
            };
        }

        // ===================== SEKME GEÇİŞİ =====================
        private void ShowPanel(UIElement panel, Button tab)
        {
            ProductsPanel.Visibility = AdsPanel.Visibility = LinksPanel.Visibility = UsersPanel.Visibility = DevsPanel.Visibility = Visibility.Collapsed;
            BtnTabProducts.Tag = BtnTabAds.Tag = BtnTabLinks.Tag = BtnTabUsers.Tag = BtnTabDevs.Tag = null;
            panel.Visibility = Visibility.Visible;
            tab.Tag = "active";
        }

        private void TabProducts_Click(object sender, RoutedEventArgs e) => ShowPanel(ProductsPanel, BtnTabProducts);

        private async void TabAds_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(AdsPanel, BtnTabAds);
            if (!_adsLoaded) { _adsLoaded = true; await ReloadAdsAsync(); }
        }

        private async void TabLinks_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(LinksPanel, BtnTabLinks);
            if (!_linksLoaded) { _linksLoaded = true; await ReloadLinksAsync(); }
        }

        private async void TabUsers_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(UsersPanel, BtnTabUsers);
            if (!_usersLoaded) { _usersLoaded = true; await ReloadUsersAsync(); }
        }

        // ===================== YETKİLER (dev/owner) =====================
        private bool _devsLoaded;

        private async void TabDevs_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(DevsPanel, BtnTabDevs);
            if (!_devsLoaded) { _devsLoaded = true; await ReloadDevsAsync(); }
        }

        private async Task ReloadDevsAsync()
        {
            var devs = await ShopService.GetDevsAsync();
            DevList.ItemsSource = devs;
            EmptyDevHint.Visibility = devs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private string _selectedRole = "dev";
        private void RoleDev_Click(object sender, RoutedEventArgs e)
        {
            _selectedRole = "dev";
            RoleDevBtn.Tag = "active"; RoleOwnerBtn.Tag = null;
        }
        private void RoleOwner_Click(object sender, RoutedEventArgs e)
        {
            _selectedRole = "owner";
            RoleOwnerBtn.Tag = "active"; RoleDevBtn.Tag = null;
        }

        private async void DevAdd_Click(object sender, RoutedEventArgs e)
        {
            string user = DevUserBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(user)) { FWDialog.Warn("Kullanıcı adı gir."); return; }
            string role = _selectedRole;
            var res = await ShopService.SetDevAsync(user, role);
            if (res.Success)
            {
                DevUserBox.Text = "";
                await ReloadDevsAsync();
                FWDialog.Success(res.Message ?? $"{user} artık {role}.");
            }
            else FWDialog.Error(res.Message ?? "İşlem başarısız.");
        }

        private async void DevRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string user) return;
            if (!FWDialog.Confirm($"{user} yetkisi kaldırılsın mı?")) return;
            var res = await ShopService.RemoveDevAsync(user);
            if (res.Success) { await ReloadDevsAsync(); }
            else FWDialog.Error(res.Message ?? "İşlem başarısız.");
        }

        private async Task ReloadAsync()
        {
            // Admin gerçek veritabanını görmeli — örnek veriye düşme.
            var market = await ShopService.GetMarketAsync(allowSample: false);
            ProductList.ItemsSource = market.Products;
            EmptyHint.Visibility = market.Products.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ReloadAsync();

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not MarketProduct p) return;

            _editId = p.Id;
            TxtFormTitle.Text = "Ürünü Düzenle";
            TxtName.Text = p.Name;
            TxtCategory.Text = p.Category;
            SelectType(p.Type);
            TxtPrice.Text = p.Price.ToString();
            TxtDesc.Text = p.Description ?? "";
            TxtLongDesc.Text = p.LongDesc ?? "";
            TxtBadge.Text = p.Badge ?? "";
            TxtImage.Text = p.ImageUrl ?? "";
            TxtCommand.Text = p.Command ?? "";
            TxtKitCommands.Text = p.KitCommandsText;
            TxtGallery.Text = p.GalleryText;
            TxtSlots.Text = p.Slots.ToString();
            TxtStock.Text = p.Stock.ToString();
            UpdatePreview(p.DisplayImage);
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not MarketProduct p) return;
            if (!FWDialog.Confirm($"'{p.Name}' ürününü silmek istediğine emin misin?", "Ürünü Sil", "Sil", "Vazgeç"))
                return;

            var r = await ShopService.DeleteProductAsync(p.Id);
            if (r.Success)
            {
                FWDialog.Success(r.Message ?? "Ürün silindi.");
                if (_editId == p.Id) ClearForm();
                await ReloadAsync();
            }
            else FWDialog.Error(r.Message ?? "Silinemedi.");
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                FWDialog.Warn("Ürün adı boş olamaz.");
                return;
            }
            long.TryParse(TxtPrice.Text.Trim(), out long price);
            if (!int.TryParse(TxtSlots.Text.Trim(), out int slots) || slots < 1) slots = 1;
            if (!int.TryParse(TxtStock.Text.Trim(), out int stock)) stock = -1;

            var p = new MarketProduct
            {
                Id = _editId,
                Name = TxtName.Text.Trim(),
                Category = string.IsNullOrWhiteSpace(TxtCategory.Text) ? "Genel" : TxtCategory.Text.Trim(),
                Type = SelectedType(),
                Price = price,
                Description = TxtDesc.Text.Trim(),
                LongDesc = TxtLongDesc.Text.Trim(),
                Badge = TxtBadge.Text.Trim(),
                ImageUrl = TxtImage.Text.Trim(),
                Command = TxtCommand.Text.Trim(),
                KitCommandsText = TxtKitCommands.Text,
                GalleryText = TxtGallery.Text,
                Slots = slots,
                Stock = stock
            };

            BtnSave.IsEnabled = false;
            BtnSave.Content = "Kaydediliyor...";
            var r = await ShopService.SaveProductAsync(p);
            BtnSave.IsEnabled = true;
            BtnSave.Content = "Kaydet";

            if (r.Success)
            {
                FWDialog.Success(r.Message ?? "Ürün kaydedildi.");
                ClearForm();
                await ReloadAsync();
            }
            else FWDialog.Error(r.Message ?? "Kaydedilemedi.");
        }

        private async void Upload_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Görseller|*.png;*.jpg;*.jpeg;*.webp;*.gif" };
            if (dlg.ShowDialog() != true) return;

            BtnUpload.IsEnabled = false;
            BtnUpload.Content = "...";
            var r = await ShopService.UploadImageAsync(dlg.FileName);
            BtnUpload.IsEnabled = true;
            BtnUpload.Content = "Yükle";

            if (r.Success && !string.IsNullOrWhiteSpace(r.Url))
            {
                TxtImage.Text = r.Url;
                UpdatePreview(r.Url);
                FWDialog.Success("Görsel yüklendi.");
            }
            else FWDialog.Error(r.Message ?? "Görsel yüklenemedi.");
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => ClearForm();

        private void ClearForm()
        {
            _editId = "0";
            TxtFormTitle.Text = "Yeni Ürün";
            TxtName.Text = "";
            TxtCategory.Text = "Genel";
            SelectType("item");
            TxtPrice.Text = "0";
            TxtDesc.Text = "";
            TxtLongDesc.Text = "";
            TxtBadge.Text = "";
            TxtImage.Text = "";
            TxtCommand.Text = "";
            TxtKitCommands.Text = "";
            TxtGallery.Text = "";
            TxtSlots.Text = "1";
            TxtStock.Text = "-1";
            ImgPreview.Source = null;
        }

        // --- Tür ComboBox yardımcıları ---
        private string SelectedType() =>
            (CmbType.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "item";

        private void SelectType(string? type)
        {
            type = string.IsNullOrWhiteSpace(type) ? "item" : type;
            foreach (var it in CmbType.Items)
                if (it is System.Windows.Controls.ComboBoxItem ci && (ci.Tag as string) == type)
                { CmbType.SelectedItem = ci; return; }
            CmbType.SelectedIndex = 0;
        }

        private void UpdatePreview(string uri)
        {
            try
            {
                ImgPreview.Source = string.IsNullOrWhiteSpace(uri) ? null : new BitmapImage(new Uri(uri, UriKind.Absolute));
            }
            catch (Exception ex)
            {
                Logger.Warn($"Önizleme yüklenemedi: {ex.Message}");
                ImgPreview.Source = null;
            }
        }

        // ===================== DUYURU YÖNETİMİ =====================
        private async Task ReloadAdsAsync()
        {
            var ads = await ShopService.GetAdsAsync(allowSample: false);
            AdList.ItemsSource = ads;
            EmptyAdHint.Visibility = ads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshAds_Click(object sender, RoutedEventArgs e) => _ = ReloadAdsAsync();

        private void EditAd_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not AdItem a) return;

            _adEditId = a.Id;
            TxtAdFormTitle.Text = "Duyuruyu Düzenle";
            TxtAdTitle.Text = a.Title;
            TxtAdDesc.Text = a.Description ?? "";
            TxtAdBadge.Text = a.Badge ?? "";
            TxtAdImage.Text = a.ImageUrl ?? "";
            TxtAdUrl.Text = a.Url ?? "";
            UpdateAdPreview(a.DisplayImage);
        }

        private async void DeleteAd_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not AdItem a) return;
            if (!FWDialog.Confirm($"'{a.Title}' duyurusunu silmek istediğine emin misin?", "Duyuruyu Sil", "Sil", "Vazgeç"))
                return;

            var r = await ShopService.DeleteAdAsync(a.Id);
            if (r.Success)
            {
                FWDialog.Success(r.Message ?? "Duyuru silindi.");
                if (_adEditId == a.Id) ClearAdForm();
                await ReloadAdsAsync();
            }
            else FWDialog.Error(r.Message ?? "Silinemedi.");
        }

        private async void SaveAd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtAdTitle.Text))
            {
                FWDialog.Warn("Başlık boş olamaz.");
                return;
            }

            var a = new AdItem
            {
                Id = _adEditId,
                Title = TxtAdTitle.Text.Trim(),
                Description = TxtAdDesc.Text.Trim(),
                Badge = TxtAdBadge.Text.Trim(),
                ImageUrl = TxtAdImage.Text.Trim(),
                Url = TxtAdUrl.Text.Trim()
            };

            BtnAdSave.IsEnabled = false;
            BtnAdSave.Content = "Yayınlanıyor...";
            var r = await ShopService.SaveAdAsync(a);
            BtnAdSave.IsEnabled = true;
            BtnAdSave.Content = "Yayınla";

            if (r.Success)
            {
                FWDialog.Success(r.Message ?? "Duyuru yayınlandı.");
                ClearAdForm();
                await ReloadAdsAsync();
            }
            else FWDialog.Error(r.Message ?? "Kaydedilemedi.");
        }

        private async void UploadAd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Görseller|*.png;*.jpg;*.jpeg;*.webp;*.gif" };
            if (dlg.ShowDialog() != true) return;

            BtnAdUpload.IsEnabled = false;
            BtnAdUpload.Content = "...";
            var r = await ShopService.UploadImageAsync(dlg.FileName);
            BtnAdUpload.IsEnabled = true;
            BtnAdUpload.Content = "Yükle";

            if (r.Success && !string.IsNullOrWhiteSpace(r.Url))
            {
                TxtAdImage.Text = r.Url;
                UpdateAdPreview(r.Url);
                FWDialog.Success("Görsel yüklendi.");
            }
            else FWDialog.Error(r.Message ?? "Görsel yüklenemedi.");
        }

        private void ClearAd_Click(object sender, RoutedEventArgs e) => ClearAdForm();

        private void ClearAdForm()
        {
            _adEditId = "0";
            TxtAdFormTitle.Text = "Yeni Duyuru";
            TxtAdTitle.Text = "";
            TxtAdDesc.Text = "";
            TxtAdBadge.Text = "";
            TxtAdImage.Text = "";
            TxtAdUrl.Text = "";
            ImgAdPreview.Source = null;
        }

        private void UpdateAdPreview(string uri)
        {
            try
            {
                ImgAdPreview.Source = string.IsNullOrWhiteSpace(uri) ? null : new BitmapImage(new Uri(uri, UriKind.Absolute));
            }
            catch (Exception ex)
            {
                Logger.Warn($"Önizleme yüklenemedi: {ex.Message}");
                ImgAdPreview.Source = null;
            }
        }

        // ===================== BAĞLANTILAR =====================
        private async Task ReloadLinksAsync()
        {
            var cfg = await ShopService.GetSettingsAsync();
            TxtDiscord.Text = cfg.DiscordUrl ?? "";
            TxtWebsite.Text = cfg.WebsiteUrl ?? "";
        }

        private async void SaveLinks_Click(object sender, RoutedEventArgs e)
        {
            BtnSaveLinks.IsEnabled = false;
            BtnSaveLinks.Content = "Kaydediliyor...";

            var r1 = await ShopService.SaveSettingAsync("discord_url", TxtDiscord.Text.Trim());
            var r2 = await ShopService.SaveSettingAsync("website_url", TxtWebsite.Text.Trim());

            BtnSaveLinks.IsEnabled = true;
            BtnSaveLinks.Content = "Kaydet";

            if (r1.Success && r2.Success)
                FWDialog.Success("Bağlantılar kaydedildi. Ana sayfadaki Discord butonu artık yeni adresi açar.");
            else
                FWDialog.Error(r1.Message ?? r2.Message ?? "Kaydedilemedi.");
        }

        // ===================== KULLANICILAR / YETKİ =====================
        private async Task ReloadUsersAsync()
        {
            var res = await ShopService.GetUsersAsync(TxtUserSearch.Text.Trim());
            UserList.ItemsSource = res.Users;
            EmptyUserHint.Visibility = res.Users.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UserSearch_Click(object sender, RoutedEventArgs e) => _ = ReloadUsersAsync();

        private void UserSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; _ = ReloadUsersAsync(); }
        }

        // --- Rol seç ---
        private void RolePick_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not AdminUser usr) return;
            _modTarget = usr;
            TxtRoleUser.Text = usr.Name;
            RolePanel.Visibility = Visibility.Visible;
        }

        private async void RoleSet_Click(object sender, RoutedEventArgs e)
        {
            if (_modTarget == null || (sender as Button)?.Tag is not string role) return;
            RolePanel.Visibility = Visibility.Collapsed;
            var r = await ShopService.SetRoleAsync(_modTarget.Username, role);
            if (r.Success) { FWDialog.Success(r.Message ?? "Rol güncellendi."); await ReloadUsersAsync(); }
            else FWDialog.Error(r.Message ?? "Güncellenemedi.");
        }

        // --- Uyar ---
        private void WarnPick_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not AdminUser usr) return;
            _modTarget = usr;
            TxtWarnUser.Text = $"{usr.Name} — Uyar";
            TxtWarnAmount.Text = "30"; TxtWarnReason.Text = "";
            SetWarnUnit(ChipDk, 1);
            WarnPanel.Visibility = Visibility.Visible;
        }

        private void WarnUnit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || !int.TryParse(b.CommandParameter?.ToString(), out int mult)) return;
            SetWarnUnit(b, mult);
        }

        private void SetWarnUnit(Button active, int mult)
        {
            _warnUnit = mult;
            ChipDk.Tag = ChipSaat.Tag = ChipGun.Tag = ChipAy.Tag = null;
            active.Tag = "active";
        }

        private async void WarnSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (_modTarget == null) return;
            if (!int.TryParse(TxtWarnAmount.Text.Trim(), out int amount) || amount <= 0)
            {
                FWDialog.Warn("Geçerli bir süre gir."); return;
            }
            int minutes = amount * _warnUnit;
            WarnPanel.Visibility = Visibility.Collapsed;
            var r = await ShopService.WarnAsync(_modTarget.Username, minutes, TxtWarnReason.Text.Trim());
            if (r.Success) { FWDialog.Success(r.Message ?? "Kullanıcı uyarıldı."); await ReloadUsersAsync(); }
            else FWDialog.Error(r.Message ?? "Uyarılamadı.");
        }

        // --- Ban / Unban ---
        private async void Ban_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not AdminUser usr) return;
            bool ban = !usr.Banned;
            string q = ban
                ? $"{usr.Name} kalıcı olarak yasaklansın mı? (Yasak kalkana dek launcher'da hiçbir şeye erişemez.)"
                : $"{usr.Name} kullanıcısının yasağı kaldırılsın mı?";
            if (!FWDialog.Confirm(q, ban ? "Yasakla" : "Yasağı Kaldır", ban ? "Yasakla" : "Kaldır", "Vazgeç")) return;

            var r = await ShopService.BanAsync(usr.Username, ban, "");
            if (r.Success) { FWDialog.Success(r.Message ?? "İşlem tamam."); await ReloadUsersAsync(); }
            else FWDialog.Error(r.Message ?? "İşlem başarısız.");
        }

        private void ModCancel_Click(object sender, RoutedEventArgs e)
        {
            RolePanel.Visibility = Visibility.Collapsed;
            WarnPanel.Visibility = Visibility.Collapsed;
        }
    }
}
