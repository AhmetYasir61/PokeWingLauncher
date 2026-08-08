using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FWLauncherV2.Services
{
    public readonly record struct ServerStatus(bool Online, int OnlinePlayers, int MaxPlayers)
    {
        public static ServerStatus Offline => new(false, 0, 0);
    }

    /// <summary>
    /// Minecraft "Server List Ping" (SLP) protokolü ile sunucunun çevrimiçi olup olmadığını ve
    /// oyuncu sayısını sorgular. Tüm iş, UI'yi bloklamamak için bir arka plan iş parçacığında çalışır.
    /// Hata/zaman aşımı durumunda Offline döner.
    /// </summary>
    public static class MinecraftServerPinger
    {
        public static Task<ServerStatus> PingAsync(string host, int port, int timeoutMs = 2500)
            => Task.Run(() => Ping(host, port, timeoutMs));

        private static ServerStatus Ping(string host, int port, int timeoutMs)
        {
            using var client = new TcpClient();

            // 1. Aşama: TCP bağlantısı. Bağlanamazsa sunucu gerçekten çevrimdışıdır.
            try
            {
                var ar = client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                    return ServerStatus.Offline;
                client.EndConnect(ar);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Sunucuya bağlanılamadı ({host}:{port}): {ex.Message}");
                return ServerStatus.Offline;
            }

            // 2. Aşama: SLP durum sorgusu. ÇEVRİMİÇİ saymak için GEÇERLİ bir yanıt şarttır.
            // (FeatherMC vb. proxy'ler sunucu KAPALIYKEN TCP'yi kabul eder ama SLP'ye 0 byte döner;
            //  bu yüzden yalnızca TCP bağlanması "online" demek DEĞİLDİR — gerçek yanıt aranır.)
            try
            {
                using var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                stream.WriteTimeout = timeoutMs;

                // 1) Handshake (next state = 1: status)
                using (var hs = new MemoryStream())
                {
                    WriteVarInt(hs, 0x00);     // packet id
                    WriteVarInt(hs, 765);      // protocol sürümü (herhangi biri kabul edilir)
                    WriteString(hs, host);
                    hs.WriteByte((byte)(port >> 8));
                    hs.WriteByte((byte)(port & 0xFF));
                    WriteVarInt(hs, 0x01);     // next state
                    WritePacket(stream, hs.ToArray());
                }

                // 2) Status request
                using (var sr = new MemoryStream())
                {
                    WriteVarInt(sr, 0x00);
                    WritePacket(stream, sr.ToArray());
                }
                stream.Flush();

                // 3) Status response — yanıt yoksa/bozuksa ÇEVRİMDIŞI.
                ReadVarInt(stream);                  // toplam paket uzunluğu (kullanılmıyor)
                int packetId = ReadVarInt(stream);
                if (packetId != 0x00)
                    return ServerStatus.Offline;

                int jsonLen = ReadVarInt(stream);
                if (jsonLen <= 0 || jsonLen > 2_000_000)
                    return ServerStatus.Offline;

                var buffer = new byte[jsonLen];
                int read = 0;
                while (read < jsonLen)
                {
                    int r = stream.Read(buffer, read, jsonLen - read);
                    if (r <= 0) break;
                    read += r;
                }
                if (read == 0)
                    return ServerStatus.Offline;

                var json = Encoding.UTF8.GetString(buffer, 0, read);
                using var doc = JsonDocument.Parse(json);

                // Geçerli bir durum yanıtında en azından players/version/description bulunur.
                var root = doc.RootElement;
                if (!root.TryGetProperty("players", out var players)
                    && !root.TryGetProperty("version", out _)
                    && !root.TryGetProperty("description", out _))
                    return ServerStatus.Offline;

                int online = 0, max = 0;
                if (players.ValueKind == JsonValueKind.Object)
                {
                    if (players.TryGetProperty("online", out var o)) online = o.GetInt32();
                    if (players.TryGetProperty("max", out var m)) max = m.GetInt32();
                }
                return new ServerStatus(true, online, max);
            }
            catch (Exception ex)
            {
                // TCP bağlandı ama geçerli SLP yanıtı yok → ÇEVRİMDIŞI (sunucu kapalı/uyuyor).
                Logger.Warn($"Sunucu SLP yanıtı vermedi ({host}:{port}): {ex.Message}");
                return ServerStatus.Offline;
            }
        }

        private static void WritePacket(Stream stream, byte[] data)
        {
            using var ms = new MemoryStream();
            WriteVarInt(ms, data.Length);
            ms.Write(data, 0, data.Length);
            var arr = ms.ToArray();
            stream.Write(arr, 0, arr.Length);
        }

        private static void WriteVarInt(Stream s, int value)
        {
            uint v = (uint)value;
            do
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) b |= 0x80;
                s.WriteByte(b);
            } while (v != 0);
        }

        private static void WriteString(Stream s, string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            WriteVarInt(s, bytes.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        private static int ReadVarInt(Stream s)
        {
            int numRead = 0, result = 0;
            byte readByte;
            do
            {
                int b = s.ReadByte();
                if (b == -1) throw new EndOfStreamException();
                readByte = (byte)b;
                result |= (readByte & 0x7F) << (7 * numRead);
                numRead++;
                if (numRead > 5) throw new InvalidDataException("VarInt çok büyük");
            } while ((readByte & 0x80) != 0);
            return result;
        }
    }
}
