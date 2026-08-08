using FWLauncherV2.Dialogs;
using FWLauncherV2.Models;
using FWLauncherV2.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FWLauncherV2.Views
{
    public partial class ReklamlarView : UserControl
    {
        // Sayfa açıkken arka planda yeni duyuru yayınlanırsa otomatik gelsin diye periyodik yoklama.
        private readonly DispatcherTimer _poll;

        public ReklamlarView()
        {
            InitializeComponent();

            _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _poll.Tick += async (s, e) => await LoadAsync();

            // Sayfaya her girişte (navigasyon) yeniden yükle — yayınlanan duyuru anında görünsün.
            Loaded += async (s, e) => { _poll.Start(); await LoadAsync(); };
            Unloaded += (s, e) => _poll.Stop();
        }

        private async Task LoadAsync()
        {
            try
            {
                AdsList.ItemsSource = await ShopService.GetAdsAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Reklamlar yüklenemedi: {ex.Message}");
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not AdItem ad)
                return;

            if (!string.IsNullOrWhiteSpace(ad.Url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(ad.Url) { UseShellExecute = true });
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Reklam bağlantısı açılamadı: {ex.Message}");
                }
            }

            FWDialog.Info($"{ad.Title}\n\n{ad.Description}", ad.Badge ?? "Duyuru");
        }
    }
}
