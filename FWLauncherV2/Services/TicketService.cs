using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FWLauncherV2.Models;

namespace FWLauncherV2.Services
{
    /// <summary>Destek/Ticket sistemi sunucu uçları.</summary>
    public static class TicketService
    {
        private static HttpClient Api => HttpClientProvider.Api;

        public static async Task<TicketListResult> ListAsync()
        {
            var (u, t) = UserSession.Credentials();
            try
            {
                var resp = await Api.PostAsJsonAsync(UserSession.ApiBase + "ticket_list.php", new { username = u, token = t });
                var r = await resp.Content.ReadFromJsonAsync<TicketListResult>();
                return r ?? new TicketListResult();
            }
            catch (Exception ex) { Logger.Warn($"ticket_list: {ex.Message}"); return new TicketListResult(); }
        }

        public static async Task<TicketMessagesResult> MessagesAsync(int ticketId, int afterId = 0)
        {
            var (u, t) = UserSession.Credentials();
            try
            {
                var resp = await Api.PostAsJsonAsync(UserSession.ApiBase + "ticket_messages.php",
                    new { username = u, token = t, ticketId, afterId });
                var r = await resp.Content.ReadFromJsonAsync<TicketMessagesResult>();
                return r ?? new TicketMessagesResult { Success = false };
            }
            catch (Exception ex) { Logger.Warn($"ticket_messages: {ex.Message}"); return new TicketMessagesResult { Success = false }; }
        }

        public static Task<ShopResult> CreateAsync(string subject, string message)
        {
            var (u, t) = UserSession.Credentials();
            return Post("ticket_create.php", new { username = u, token = t, subject, message });
        }

        public static Task<ShopResult> SendAsync(int ticketId, string body)
        {
            var (u, t) = UserSession.Credentials();
            return Post("ticket_send.php", new { username = u, token = t, ticketId, body });
        }

        public static Task<ShopResult> ClaimAsync(int ticketId)
        {
            var (u, t) = UserSession.Credentials();
            return Post("ticket_claim.php", new { username = u, token = t, ticketId });
        }

        public static Task<ShopResult> CloseAsync(int ticketId)
        {
            var (u, t) = UserSession.Credentials();
            return Post("ticket_close.php", new { username = u, token = t, ticketId });
        }

        public static Task<ShopResult> DeleteAsync(int ticketId)
        {
            var (u, t) = UserSession.Credentials();
            return Post("ticket_delete.php", new { username = u, token = t, ticketId });
        }

        public static Task<ShopResult> RateAsync(int ticketId, int stars)
        {
            var (u, t) = UserSession.Credentials();
            return Post("ticket_rate.php", new { username = u, token = t, ticketId, stars });
        }

        private static async Task<ShopResult> Post(string endpoint, object body)
        {
            try
            {
                var resp = await Api.PostAsJsonAsync(UserSession.ApiBase + endpoint, body);
                var r = await resp.Content.ReadFromJsonAsync<ShopResult>();
                return r ?? new ShopResult { Success = false, Message = "Sunucu yanıtı okunamadı." };
            }
            catch (Exception ex)
            {
                Logger.Warn($"{endpoint}: {ex.Message}");
                return new ShopResult { Success = false, Message = "Sunucuya bağlanılamadı." };
            }
        }
    }
}
