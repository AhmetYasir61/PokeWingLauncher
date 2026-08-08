using System;
using System.IO;

namespace FWLauncherV2.Services
{
    /// <summary>
    /// Basit, thread-safe dosya günlükleyici. Hatalar %AppData%\FWLauncherV2\logs altında tutulur.
    /// Günlükleme hiçbir zaman istisna fırlatmaz; sorun olursa sessizce yutulur.
    /// </summary>
    public static class Logger
    {
        private static readonly object Gate = new();

        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PokeWingLauncher", "logs");

        private static string LogFile => Path.Combine(LogDirectory, $"launcher-{DateTime.Now:yyyy-MM-dd}.log");

        public static void Info(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception? ex = null) =>
            Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(LogFile,
                        $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Günlükleme asla uygulamayı çökertmemeli.
            }
        }
    }
}
