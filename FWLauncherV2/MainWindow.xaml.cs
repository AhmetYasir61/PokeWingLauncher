using FWLauncherV2.Dialogs;
using FWLauncherV2.Services;
using FWLauncherV2.Views;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace FWLauncherV2
{
    public partial class MainWindow : Window
    {
        private HomeView? _home;
        private ReklamlarView? _reklam;
        private DestekView? _destek;
        private SettingsView? _settings;
        private ControlPanelView? _controlPanel;
        private string currentUsername = "Oyuncu";

        private readonly DispatcherTimer _modTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private double _warnRemainingMs;
        private bool _banned;
        private string _modReason = "";
        private int _modPollCounter;

        public MainWindow()
        {
            InitializeComponent();
            UserSession.Changed += OnSessionChanged;
            Closed += (s, e) => { UserSession.Changed -= OnSessionChanged; _modTimer.Stop(); };

            _modTimer.Tick += ModTimer_Tick;
            Loaded += async (s, e) => { await CheckModeration(); _modTimer.Start(); };
        }

        // ===================== MODERASYON (uyarı / ban) =====================
        private async Task CheckModeration()
        {
            var st = await ShopService.ModerationStatusAsync();
            if (!st.Success) return;
            _banned = st.Banned;
            _warnRemainingMs = st.RemainingMs;
            _modReason = st.Reason ?? "";
            ApplyModeration();
        }

        private void ModTimer_Tick(object? sender, EventArgs e)
        {
            if (_warnRemainingMs > 0)
            {
                _warnRemainingMs = Math.Max(0, _warnRemainingMs - 1000);
                ApplyModeration();
            }
            if (++_modPollCounter >= 20) { _modPollCounter = 0; _ = CheckModeration(); }
        }

        private void ApplyModeration()
        {
            if (_banned)
            {
                TxtModIcon.Text = "⛔";
                TxtModTitle.Text = "Hesabın yasaklandı";
                TxtModMsg.Text = string.IsNullOrWhiteSpace(_modReason)
                    ? "Yasağın kalkana dek launcher'da hiçbir şeye erişemezsin. Bir yetkiliyle iletişime geç."
                    : _modReason;
                TxtModTimer.Text = "";
                ModOverlay.Visibility = Visibility.Visible;
            }
            else if (_warnRemainingMs > 0)
            {
                TxtModIcon.Text = "⚠";
                TxtModTitle.Text = "Uyarıldınız";
                TxtModMsg.Text = string.IsNullOrWhiteSpace(_modReason)
                    ? "Launcher geçici olarak kilitlendi. Süre dolunca açılacak."
                    : _modReason;
                TxtModTimer.Text = "Kalan süre: " + FormatRemaining(_warnRemainingMs);
                ModOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                ModOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private static string FormatRemaining(double ms)
        {
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}g {ts.Hours}sa {ts.Minutes}dk";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}sa {ts.Minutes}dk {ts.Seconds}sn";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}dk {ts.Seconds}sn";
            return $"{ts.Seconds}sn";
        }

        private void OnSessionChanged() => Dispatcher.Invoke(UpdateSessionUi);

        private void UpdateSessionUi()
        {
            TxtCoins.Text = $"{UserSession.Coins:N0} Coin";
            NavAdmin.Visibility = UserSession.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===================== SAYFA GEÇİŞLERİ =====================
        public void NavigateToMainView(string username)
        {
            if (!EnsureCanLeaveCenter())
                return;

            if (string.IsNullOrWhiteSpace(username))
                username = SettingsService.Load().LastUsername;
            if (string.IsNullOrWhiteSpace(username))
                username = "Oyuncu";

            currentUsername = username;
            UpdateUserChip(username);
            PlayCard.SetCurrentUser(username);

            // Sunucudan rol + coin bilgisini çek (Changed olayı UI'yi günceller).
            _ = UserSession.LoadAsync();

            _home ??= new HomeView();
            _home.SetUser(username);
            ShowCenter(_home, "Ana Sayfa", NavHome);
        }

        public void NavigateToControlPanel()
        {
            if (!UserSession.IsAdmin) return;
            if (!EnsureCanLeaveCenter()) return;
            _controlPanel ??= new ControlPanelView();
            ShowCenter(_controlPanel, "Kontrol Paneli", NavAdmin);
        }

        public void NavigateToReklamlar()
        {
            if (!EnsureCanLeaveCenter()) return;
            _reklam ??= new ReklamlarView();
            ShowCenter(_reklam, "Reklamlar & Duyurular", NavReklam);
        }

        public void NavigateToDestek()
        {
            if (!EnsureCanLeaveCenter()) return;
            _destek ??= new DestekView();
            ShowCenter(_destek, "Destek", NavDestek);
        }

        public void NavigateToSettings()
        {
            if (CenterContent.Content is SettingsView)
                return;

            _settings ??= new SettingsView(currentUsername);
            ShowCenter(_settings, "Ayarlar", NavSettings);
        }

        private bool EnsureCanLeaveCenter()
            => CenterContent.Content is not SettingsView sv || sv.CanLeave();

        private void ShowCenter(UserControl view, string title, Button activeNav)
        {
            CenterContent.Content = view;
            TxtPageTitle.Text = title;
            SetActiveNav(activeNav);
        }

        private void SetActiveNav(Button active)
        {
            NavHome.Tag = ReferenceEquals(active, NavHome) ? "active" : null;
            NavReklam.Tag = ReferenceEquals(active, NavReklam) ? "active" : null;
            NavDestek.Tag = ReferenceEquals(active, NavDestek) ? "active" : null;
            NavSettings.Tag = ReferenceEquals(active, NavSettings) ? "active" : null;
            NavAdmin.Tag = ReferenceEquals(active, NavAdmin) ? "active" : null;
        }

        private void UpdateUserChip(string username)
        {
            TxtSidebarUser.Text = username;
            TxtUserInitial.Text = string.IsNullOrWhiteSpace(username)
                ? "?"
                : char.ToUpperInvariant(username[0]).ToString();
        }

        // ===================== MENÜ TIKLAMALARI =====================
        private void NavHome_Click(object sender, RoutedEventArgs e) => NavigateToMainView(currentUsername);
        private void NavReklam_Click(object sender, RoutedEventArgs e) => NavigateToReklamlar();
        private void NavDestek_Click(object sender, RoutedEventArgs e) => NavigateToDestek();
        private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateToSettings();
        private void NavAdmin_Click(object sender, RoutedEventArgs e) => NavigateToControlPanel();

        private void BtnTransfer_Click(object sender, RoutedEventArgs e)
        {
            if (new TransferDialog().ShowDialog() == true)
                UpdateSessionUi();
        }

        private void NavMods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", SettingsService.LauncherDirectory);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Dosya klasörü açılamadı: {ex.Message}");
            }
        }

        // ===================== PENCERE KONTROLLERİ =====================
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            if (e.LeftButton == MouseButtonState.Pressed && WindowState != WindowState.Maximized)
            {
                try { DragMove(); } catch { /* nadir durumlarda yok say */ }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Application.Current.Shutdown();

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => this.WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void ToggleMaximize()
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                RootBorder.Margin = new Thickness(0);
                RootBorder.CornerRadius = new CornerRadius(0);
                InnerBorder.CornerRadius = new CornerRadius(0);
                BtnMaximize.Content = "❐"; // geri al
            }
            else
            {
                RootBorder.Margin = new Thickness(16);
                RootBorder.CornerRadius = new CornerRadius(20);
                InnerBorder.CornerRadius = new CornerRadius(20);
                BtnMaximize.Content = "□"; // büyüt
            }
        }

        // ===== Borderless pencere büyütülünce görev çubuğunu kapatmasın =====
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0024) // WM_GETMINMAXINFO
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                IntPtr monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
                if (monitor != IntPtr.Zero)
                {
                    var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    GetMonitorInfo(monitor, ref info);
                    mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                    mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
                    mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                    mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                    Marshal.StructureToPtr(mmi, lParam, true);
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECTW { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECTW rcMonitor; public RECTW rcWork; public int dwFlags; }

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            // Tek hesap kuralı: çıkışta açık oyun süreci kapatılır + auth dosyası silinir
            // (mod da dosyayı izler — silinince oyun kendini kapatır, çift güvence).
            Views.MainView.KillGameAndClearAuth();
            SessionManager.ClearSession();
            new LoginWindow().Show();
            this.Close();
        }
    }
}
