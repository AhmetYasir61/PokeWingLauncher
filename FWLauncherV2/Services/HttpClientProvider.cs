using System;
using System.Net.Http;

namespace FWLauncherV2.Services
{
    /// <summary>
    /// Uygulama genelinde paylaşılan HttpClient örnekleri. Her istek için yeni HttpClient
    /// oluşturmak soket tükenmesine yol açtığından tek örnek kullanılır.
    /// </summary>
    public static class HttpClientProvider
    {
        /// <summary>Hızlı API çağrıları (login, kayıt, oturum, modpack bilgisi) için kısa zaman aşımlı istemci.</summary>
        public static HttpClient Api { get; } = Create(TimeSpan.FromSeconds(30));

        /// <summary>Büyük dosya indirmeleri (modpack zip, Forge, Java, modlar) için uzun zaman aşımlı istemci.</summary>
        public static HttpClient Download { get; } = Create(TimeSpan.FromMinutes(20));

        private static HttpClient Create(TimeSpan timeout)
        {
            var client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FWLauncher/2.0");
            return client;
        }
    }
}
