using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace FWLauncherV2
{
    /// <summary>
    /// 🔊 Açılış ses efektleri — bağımlılıksız (kod-üretimli WAV). Başarı = yükselen parlak akor
    /// (rüzgâr/başarı hissi), Hata = alçalan uğursuz ton. SoundPlayer ile çalınır (harici dosya yok).
    /// </summary>
    public static class SplashSound
    {
        public static void PlaySuccess() => PlayAsync(BuildSuccess());
        public static void PlayFail() => PlayAsync(BuildFail());

        private static void PlayAsync(byte[] wav)
        {
            Task.Run(() =>
            {
                try
                {
                    using var ms = new MemoryStream(wav);
                    using var player = new SoundPlayer(ms);
                    player.PlaySync();
                }
                catch { /* ses çalınamazsa sessiz geç */ }
            });
        }

        // ---- Başarı: yükselen 3 nota (akor arpejı) + hafif "rüzgâr" gürültüsü ----
        private static byte[] BuildSuccess()
        {
            int rate = 44100;
            double dur = 1.1;
            int n = (int)(rate * dur);
            double[] buf = new double[n];
            // arpej: A4→C#5→E5→A5 (parlak major)
            double[] freqs = { 440, 554.37, 659.25, 880 };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double v = 0;
                for (int k = 0; k < freqs.Length; k++)
                {
                    double start = k * 0.14;
                    if (t >= start)
                    {
                        double lt = t - start;
                        double env = Math.Exp(-lt * 2.2);            // her nota söner
                        v += Math.Sin(2 * Math.PI * freqs[k] * lt) * env * 0.22;
                    }
                }
                // hafif rüzgâr (filtrelenmiş gürültü, yükselen)
                double wind = (Rng() * 2 - 1) * 0.05 * Math.Min(1, t * 1.5) * Math.Exp(-Math.Max(0, t - 0.6) * 3);
                buf[i] = v + wind;
            }
            return ToWav(buf, rate);
        }

        // ---- Hata: alçalan iki ton (minör, uğursuz) ----
        private static byte[] BuildFail()
        {
            int rate = 44100;
            double dur = 0.9;
            int n = (int)(rate * dur);
            double[] buf = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                // 330Hz → 155Hz kayan alçalan ton
                double f = 330 - (330 - 155) * Math.Min(1, t / 0.7);
                double env = Math.Exp(-t * 1.8);
                double v = Math.Sin(2 * Math.PI * f * t) * env * 0.28;
                // hafif titreşim (tehlike hissi)
                v *= 1 + 0.3 * Math.Sin(2 * Math.PI * 12 * t);
                buf[i] = v;
            }
            return ToWav(buf, rate);
        }

        private static uint _seed = 12345;
        private static double Rng()   // hızlı deterministik gürültü
        {
            _seed = _seed * 1664525 + 1013904223;
            return (_seed >> 8) / 16777216.0;
        }

        // ---- double örnekler → 16-bit mono WAV ----
        private static byte[] ToWav(double[] samples, int rate)
        {
            int n = samples.Length;
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int dataLen = n * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataLen);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);            // PCM
            w.Write((short)1);            // mono
            w.Write(rate);
            w.Write(rate * 2);            // byte rate
            w.Write((short)2);            // block align
            w.Write((short)16);           // bits
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataLen);
            foreach (double s in samples)
            {
                short v = (short)(Math.Max(-1, Math.Min(1, s)) * short.MaxValue);
                w.Write(v);
            }
            w.Flush();
            return ms.ToArray();
        }
    }
}
