using FWLauncherV2.Services;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace FWLauncherV2.Views
{
    public partial class HomeView : UserControl
    {
        private const string DefaultDiscordUrl = "https://discord.gg/RrBTdgrUM2";
        private string _discordUrl = DefaultDiscordUrl;

        public HomeView()
        {
            InitializeComponent();
            // Discord adresi admin panelden (sunucudan) gelir; her girişte tazele, gelmezse varsayılan.
            Loaded += async (s, e) =>
            {
                var cfg = await ShopService.GetSettingsAsync();
                if (!string.IsNullOrWhiteSpace(cfg.DiscordUrl)) _discordUrl = cfg.DiscordUrl!;
            };
        }

        public void SetUser(string username)
        {
            TxtHello.Text = string.IsNullOrWhiteSpace(username) ? "Hoş geldin!" : $"Hoş geldin, {username}!";
        }

        private void GoIngameMarketInfo_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show(
                "Market artık tamamen oyun içinde!\n\n" +
                "Sunucuya bağlandığında Rotom Telefon veya Rotom PC ile market, depo ve kataloğa ulaşırsın. " +
                "Satın aldıkların depona düşer; envanterinde yer açtığında tıklayıp teslim alırsın.",
                "Oyun İçi Market", MessageBoxButton.OK, MessageBoxImage.Information);

        private void GoReklam_Click(object sender, RoutedEventArgs e)
            => (Window.GetWindow(this) as MainWindow)?.NavigateToReklamlar();

        private void Discord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_discordUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Warn($"Discord bağlantısı açılamadı: {ex.Message}");
            }
        }
    }
}
