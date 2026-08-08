using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FWLauncherV2.Dialogs
{
    public enum FWDialogIcon { Info, Success, Warning, Error, Question }

    /// <summary>
    /// Temaya uygun özel uyarı/onay penceresi (Windows MessageBox yerine).
    /// Statik yardımcılar: Info / Success / Warn / Error / Confirm / Ask3.
    /// </summary>
    public partial class FWDialog : Window
    {
        // true = birincil (Tamam/Evet), false = ikincil (Hayır), null = iptal/kapat
        private bool? _result;
        private bool _closing;

        private FWDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => PlayOpen();
        }

        private void PlayOpen()
        {
            var dur = TimeSpan.FromMilliseconds(160);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
            ScaleT.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, dur) { EasingFunction = ease });
            ScaleT.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, dur) { EasingFunction = ease });
        }

        /// <summary>Kapanış animasyonunu oynatır, bitince pencereyi gerçekten kapatır.</summary>
        private void AnimateClose()
        {
            if (_closing) { return; }
            _closing = true;

            var dur = TimeSpan.FromMilliseconds(110);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            ScaleT.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.96, dur) { EasingFunction = ease });
            ScaleT.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.96, dur) { EasingFunction = ease });

            var fade = new DoubleAnimation(1, 0, dur);
            fade.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        // ================= STATİK YARDIMCILAR =================
        public static void Info(string message, string title = "Bilgi")
            => Show(message, title, FWDialogIcon.Info, "Tamam", null, null);

        public static void Success(string message, string title = "Başarılı")
            => Show(message, title, FWDialogIcon.Success, "Tamam", null, null);

        public static void Warn(string message, string title = "Uyarı")
            => Show(message, title, FWDialogIcon.Warning, "Tamam", null, null);

        public static void Error(string message, string title = "Hata")
            => Show(message, title, FWDialogIcon.Error, "Tamam", null, null);

        /// <summary>Evet/Hayır onayı. Evet ise true döner.</summary>
        public static bool Confirm(string message, string title = "Onay",
            string yesText = "Evet", string noText = "İptal")
            => Show(message, title, FWDialogIcon.Question, yesText, noText, null) == true;

        /// <summary>Üç seçenekli (Evet=true, Hayır=false, İptal=null).</summary>
        public static bool? Ask3(string message, string title,
            string yesText, string noText, string cancelText)
            => Show(message, title, FWDialogIcon.Warning, yesText, noText, cancelText);

        // ================= ÇEKİRDEK =================
        private static bool? Show(string message, string title, FWDialogIcon icon,
            string primaryText, string? secondaryText, string? cancelText)
        {
            var dlg = new FWDialog();
            dlg.Configure(message, title, icon, primaryText, secondaryText, cancelText);

            var owner = ActiveWindow();
            if (owner != null && !ReferenceEquals(owner, dlg))
            {
                dlg.Owner = owner;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dlg.ShowDialog();
            return dlg._result;
        }

        private static Window? ActiveWindow()
        {
            var app = Application.Current;
            if (app == null) return null;
            return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                   ?? app.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                   ?? app.MainWindow;
        }

        private void Configure(string message, string title, FWDialogIcon icon,
            string primaryText, string? secondaryText, string? cancelText)
        {
            TxtTitle.Text = title;
            TxtMessage.Text = message;
            BtnPrimary.Content = primaryText;

            if (!string.IsNullOrEmpty(secondaryText))
            {
                BtnSecondary.Content = secondaryText;
                BtnSecondary.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrEmpty(cancelText))
            {
                BtnCancel.Content = cancelText;
                BtnCancel.Visibility = Visibility.Visible;
            }

            ApplyIcon(icon);
        }

        private void ApplyIcon(FWDialogIcon icon)
        {
            (string glyph, Color color) = icon switch
            {
                FWDialogIcon.Success => ("✓", (Color)ColorConverter.ConvertFromString("#FF34D399")),
                FWDialogIcon.Warning => ("!", (Color)ColorConverter.ConvertFromString("#FFFBBF24")),
                FWDialogIcon.Error   => ("✕", (Color)ColorConverter.ConvertFromString("#FFEF4444")),
                FWDialogIcon.Question=> ("?", (Color)ColorConverter.ConvertFromString("#FFFFCB05")),
                _                    => ("i", (Color)ColorConverter.ConvertFromString("#FFFFCB05")),
            };

            IconGlyph.Text = glyph;
            IconGlyph.Foreground = new SolidColorBrush(color);
            IconCircle.Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B));
        }

        // ================= OLAYLAR =================
        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e) { _result = true; AnimateClose(); }
        private void BtnSecondary_Click(object sender, RoutedEventArgs e) { _result = false; AnimateClose(); }
        private void BtnCancel_Click(object sender, RoutedEventArgs e) { _result = null; AnimateClose(); }
        private void BtnClose_Click(object sender, RoutedEventArgs e) { _result = null; AnimateClose(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { _result = null; AnimateClose(); }
            else if (e.Key == Key.Enter) { _result = true; AnimateClose(); }
            base.OnKeyDown(e);
        }
    }
}
