using FWLauncherV2.Dialogs;
using FWLauncherV2.Models;
using FWLauncherV2.Services;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FWLauncherV2.Views
{
    public partial class SettingsView : UserControl
    {
        private bool isDirty;
        private bool isLoading;

        private UserSettings currentUserSettings;
        private readonly string currentUsername;
        private readonly int maxRamGB;

        public SettingsView(string currentUsername)
        {
            InitializeComponent();

            this.currentUsername = string.IsNullOrWhiteSpace(currentUsername) ? "Oyuncu" : currentUsername;
            currentUserSettings = SettingsService.Load();
            maxRamGB = SettingsService.RecommendedMaxRamGB();

            LoadIntoUi();
        }

        private void LoadIntoUi()
        {
            isLoading = true;

            TxtJavaPath.Text = currentUserSettings.JavaPath ?? "";

            RamSlider.Minimum = SettingsService.MinRamGB;
            RamSlider.Maximum = maxRamGB;

            int ram = Math.Clamp(currentUserSettings.LastRamInGB, SettingsService.MinRamGB, maxRamGB);
            RamSlider.Value = ram;
            TxtRamValue.Text = $"{ram} GB";
            UpdateRAMBar(ram, maxRamGB);

            isLoading = false;
        }

        private void JavaLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Warn($"Bağlantı açılamadı: {ex.Message}");
            }
        }

        private void BtnSelectJava_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Java executable (java.exe)|java.exe" };
            if (dialog.ShowDialog() == true)
            {
                TxtJavaPath.Text = dialog.FileName;
                isDirty = true;
            }
        }

        private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtRamValue != null)
                TxtRamValue.Text = $"{(int)RamSlider.Value} GB";

            UpdateRAMBar((int)RamSlider.Value, maxRamGB);

            if (!isLoading)
                isDirty = true;
        }

        private void UpdateRAMBar(int selectedGB, int maxAllowedGB)
        {
            if (RamUsageBar == null || maxAllowedGB <= 0) return;

            double percentage = (double)selectedGB / maxAllowedGB * 100;
            RamUsageBar.Value = Math.Min(percentage, 100);

            if (selectedGB <= maxAllowedGB * 0.6)
                RamUsageBar.Foreground = new SolidColorBrush(Colors.Green);
            else if (selectedGB <= maxAllowedGB * 0.85)
                RamUsageBar.Foreground = new SolidColorBrush(Colors.Orange);
            else
                RamUsageBar.Foreground = new SolidColorBrush(Colors.Red);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySave())
                return;

            FWDialog.Success("Ayarlar başarıyla kaydedildi.");

            (Window.GetWindow(this) as MainWindow)?.NavigateToMainView(currentUserSettings.LastUsername);
        }

        private bool TrySave()
        {
            try
            {
                currentUserSettings.LastRamInGB = (int)RamSlider.Value;
                currentUserSettings.JavaPath = TxtJavaPath.Text?.Trim();
                currentUserSettings.LastUsername = currentUsername;

                SettingsService.Save(currentUserSettings);
                isDirty = false;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Ayarlar kaydedilemedi.", ex);
                FWDialog.Error($"Ayarlar kaydedilirken bir hata oluştu:\n{ex.Message}");
                return false;
            }
        }

        // ------------------------------
        // Public metotlar
        // ------------------------------
        public UserSettings GetCurrentSettings() => currentUserSettings;

        public int GetTotalRAM() => SettingsService.TotalRamMb();

        public bool CanLeave()
        {
            if (!isDirty)
                return true;

            var result = FWDialog.Ask3(
                "Ayarlar kaydedilmedi. Çıkmadan önce kaydetmek ister misiniz?",
                "Kaydedilmemiş Değişiklik", "Kaydet", "Kaydetme", "İptal");

            return result switch
            {
                true => TrySave(),   // Kaydet
                false => true,       // Kaydetmeden çık
                _ => false           // İptal — sayfada kal
            };
        }
    }
}
