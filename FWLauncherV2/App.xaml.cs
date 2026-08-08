using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FWLauncherV2.Dialogs;
using FWLauncherV2.Services;

namespace FWLauncherV2
{
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            // Launcher KAPANIRSA açık oyun da kapanır (tek hesap kuralı; logout'la aynı yol).
            // Mod tarafı ayrıca launcher PID'ini izler — launcher çökse bile oyun kendini kapatır.
            try { Views.MainView.KillGameAndClearAuth(); } catch { }
            base.OnExit(e);
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global hata yakalama: çökme yerine günlüğe yaz ve kullanıcıya bilgi ver.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            Logger.Info("Launcher başlatılıyor.");

            // 🖥️ Donanım kapısı: en az 6 GB sistem RAM'i şart — altındaki makinede launcher hiç açılmaz.
            double totalGb = GetTotalRamGb();
            if (totalGb > 0 && totalGb < 5.5)   // 6 GB modüller ~5.9 GB raporlanır; 5.5 eşiği güvenli
            {
                Logger.Warn($"Yetersiz RAM: {totalGb:0.0} GB — launcher kapatılıyor.");
                MessageBox.Show(
                    $"Bu bilgisayar PokeWing için uygun değil veya yeterince güçlü değil.\n\n" +
                    $"Gereken: en az 6 GB RAM\nBu bilgisayar: {totalGb:0.0} GB RAM",
                    "PokeWing — Sistem Yetersiz", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // 🎬 LoL-tarzı animasyonlu splash — gösterilirken arka planda bütünlük + auto-login çalışır.
            var splash = new SplashWindow();
            splash.Show();

            // Marka dosyalarını (logo + ikon) launcher klasörüne kopyala.
            EnsureBrandingFiles();

            // 🛡️ Bütünlük kontrolü (splash animasyonu oynarken)
            splash.SetStatus("Bütünlük doğrulanıyor...");
            splash.SetProgress(0.3);
            await Task.Delay(1400);   // animasyon nefes alsın (logo belirme + parlama)
            bool tampered = false;
            try { tampered = await IntegrityGuard.CheckAsync(); }
            catch { /* kontrol hatası kullanıcıyı engellemesin */ }

            if (tampered)
            {
                // ❌ Doğrulanamadı — uyarı sesi + splash kapanır + uygulama biter
                SplashSound.PlayFail();
                splash.SetStatus("§ Bütünlük doğrulanamadı!");
                await Task.Delay(1500);
                splash.Close();
                Dialogs.FWDialog.Error("Launcher bütünlüğü doğrulanamadı. Güvenlik nedeniyle kapatılıyor.\n" +
                                       "Orijinal launcher'ı tekrar indir.", "Güvenlik");
                Shutdown();
                return;
            }

            // ✅ Doğrulandı — başarı sesi (rüzgâr/başarı efekti)
            SplashSound.PlaySuccess();

            // Oturum kontrolü
            splash.SetStatus("Oturum kontrol ediliyor...");
            splash.SetProgress(0.7);
            await Task.Delay(1000);   // durum değişimi görünsün
            string? username = null;
            try { username = await SessionManager.TryAutoLogin(); }
            catch (Exception ex) { Logger.Error("Otomatik giriş hatası.", ex); }

            splash.SetStatus("Hoş geldin!");
            splash.SetProgress(0.95);

            // Minimum splash süresi (animasyon tamamlansın — LoL hissi, yavaş geçiş)
            await Task.Delay(1600);

            // Splash'ı kapat + gerçek pencereyi aç
            string? finalUser = username;
            await splash.FadeOutAndClose(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(finalUser))
                    {
                        Logger.Info($"Otomatik giriş başarılı: {finalUser}");
                        var main = new MainWindow();
                        main.NavigateToMainView(finalUser);
                        main.Show();
                    }
                    else
                    {
                        new LoginWindow().Show();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Pencere açılışı hatası.", ex);
                    new LoginWindow().Show();
                }
            });
        }

        /// <summary>Gömülü logo (.png) ve ikon (.ico) dosyalarını launcher klasörüne kopyalar.</summary>
        // ---- Toplam fiziksel RAM (GB) — GlobalMemoryStatusEx; okunamazsa 0 döner (engelleme yapılmaz) ----
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                         ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        private static double GetTotalRamGb()
        {
            try
            {
                var st = new MemoryStatusEx { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatusEx>() };
                if (GlobalMemoryStatusEx(ref st)) return st.ullTotalPhys / 1073741824.0;
            }
            catch { }
            return 0;
        }

        private static void EnsureBrandingFiles()
        {
            try
            {
                var dir = SettingsService.LauncherDirectory;
                Directory.CreateDirectory(dir);
                CopyResource("pack://application:,,,/PokeWingLauncher.png", Path.Combine(dir, "PokeWingLauncher.png"));
                CopyResource("pack://application:,,,/icon/FWLauncherV2.ico", Path.Combine(dir, "PokeWingLauncher.ico"));
            }
            catch (Exception ex)
            {
                Logger.Warn($"Marka dosyaları kopyalanamadı: {ex.Message}");
            }
        }

        private static void CopyResource(string packUri, string destPath)
        {
            if (File.Exists(destPath))
                return;

            var info = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
            if (info?.Stream == null)
                return;

            using var stream = info.Stream;
            using var fs = File.Create(destPath);
            stream.CopyTo(fs);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("İşlenmeyen arayüz hatası.", e.Exception);
            FWDialog.Error($"Beklenmeyen bir hata oluştu:\n{e.Exception.Message}", "PokeWingLauncher");
            e.Handled = true; // uygulamanın çökmesini engelle
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
            => Logger.Error("İşlenmeyen uygulama hatası.", e.ExceptionObject as Exception);

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error("Gözlenmeyen görev (Task) hatası.", e.Exception);
            e.SetObserved();
        }
    }
}
