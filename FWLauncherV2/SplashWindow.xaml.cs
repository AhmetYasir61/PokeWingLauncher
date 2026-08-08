using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace FWLauncherV2
{
    /// <summary>
    /// 🎬 LoL-tarzı animasyonlu açılış splash'ı. Transparan pencere, animasyonlu logo (parlama +
    /// yanıp sönme + yansıma). Gösterilirken arka planda bütünlük taraması + auto-login çalışır.
    /// İş bitince FadeOutAndClose ile kapanır, gerçek pencere (Login/Main) açılır.
    /// </summary>
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => PlayIntro();
        }

        /// <summary>Durum metnini günceller (dışarıdan: "Bütünlük...", "Oturum kontrol ediliyor...").</summary>
        public void SetStatus(string text)
        {
            Dispatcher.Invoke(() => StatusText.Text = text);
        }

        /// <summary>İlerleme çubuğunu (0..1) ilerletir.</summary>
        public void SetProgress(double p)
        {
            Dispatcher.Invoke(() =>
            {
                double target = Math.Max(0, Math.Min(1, p)) * 220;
                var a = new DoubleAnimation(target, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase() };
                BarFill.BeginAnimation(WidthProperty, a);
            });
        }

        private void PlayIntro()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 1) Logo belirir + büyür (0.7 → 1.0) — yavaş, zarif
            Logo.BeginAnimation(OpacityProperty, Fade(0, 1, 1200, 0));
            LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, Scale(0.72, 1.0, 1400, 0, ease));
            LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, Scale(0.72, 1.0, 1400, 0, ease));

            // 2) Parlama halesi nabız gibi (sürekli yanıp sönme)
            Glow.BeginAnimation(OpacityProperty, Pulse(0.15, 0.75, 1600, 300));

            // 3) Logo kenar parlaması (glow blur nabzı)
            LogoGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, Pulse(6, 34, 1400, 400));

            // 4) Marka adı + durum + bar (gecikmeli belirir)
            BrandText.BeginAnimation(OpacityProperty, Fade(0, 1, 600, 700));
            StatusText.BeginAnimation(OpacityProperty, Fade(0, 0.9, 500, 1000));
            BarTrack.BeginAnimation(OpacityProperty, Fade(0, 1, 500, 1000));
        }

        /// <summary>Solup kapanır (iş bitince çağrılır); tamamlanınca onDone çalışır.</summary>
        public async Task FadeOutAndClose(Action onDone)
        {
            SetProgress(1);
            await Task.Delay(700);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(850));
            fade.Completed += (_, _) =>
            {
                onDone?.Invoke();
                Close();
            };
            BeginAnimation(OpacityProperty, fade);
        }

        // ---- animasyon yardımcıları ----
        private static DoubleAnimation Fade(double from, double to, int ms, int delayMs)
            => new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delayMs) };

        private static DoubleAnimation Scale(double from, double to, int ms, int delayMs, IEasingFunction ease)
            => new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = ease };

        private static DoubleAnimation Pulse(double from, double to, int ms, int delayMs)
            => new(from, to, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
    }
}
