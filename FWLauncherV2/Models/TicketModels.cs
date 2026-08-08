using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows;

namespace FWLauncherV2.Models
{
    public class Ticket
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("subject")] public string Subject { get; set; } = "";
        [JsonPropertyName("playerName")] public string PlayerName { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "open";
        [JsonPropertyName("assignedName")] public string? AssignedName { get; set; }
        [JsonPropertyName("rating")] public int Rating { get; set; }
        [JsonPropertyName("updatedAt")] public double UpdatedAt { get; set; }
        [JsonPropertyName("lastBody")] public string? LastBody { get; set; }

        [JsonIgnore] public bool IsOpen => Status == "open";
        [JsonIgnore] public bool IsClosed => Status == "closed";
        [JsonIgnore] public string StatusText => Status switch
        {
            "open" => "Açık",
            "claimed" => string.IsNullOrEmpty(AssignedName) ? "Alındı" : $"Üzerinde: {AssignedName}",
            "closed" => Rating > 0 ? $"Kapandı · {Rating}★" : "Kapandı",
            _ => Status
        };
        [JsonIgnore] public string Preview => string.IsNullOrWhiteSpace(LastBody) ? "—" :
            (LastBody!.Length > 46 ? LastBody.Substring(0, 46) + "…" : LastBody);
    }

    public class TicketMessage
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("sender")] public string Sender { get; set; } = "";
        [JsonPropertyName("senderName")] public string SenderName { get; set; } = "";
        [JsonPropertyName("senderRole")] public string SenderRole { get; set; } = "";
        [JsonPropertyName("body")] public string Body { get; set; } = "";
        [JsonPropertyName("createdAt")] public double CreatedAt { get; set; }
        [JsonPropertyName("mine")] public bool Mine { get; set; }

        [JsonIgnore] public bool IsSystem => SenderRole == "system";
        [JsonIgnore] public bool IsStaffSender =>
            SenderRole is "owner" or "admin" or "moderator" or "rehber";
        [JsonIgnore] public string TimeText
        {
            get
            {
                try { return DateTimeOffset.FromUnixTimeMilliseconds((long)CreatedAt).LocalDateTime.ToString("HH:mm"); }
                catch { return ""; }
            }
        }
    }

    public class TicketListResult
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("staff")] public bool Staff { get; set; }
        [JsonPropertyName("role")] public string Role { get; set; } = "player";
        [JsonPropertyName("tickets")] public List<Ticket> Tickets { get; set; } = new();
    }

    public class TicketMessagesResult
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "open";
        [JsonPropertyName("assignedName")] public string? AssignedName { get; set; }
        [JsonPropertyName("assignedMe")] public bool AssignedMe { get; set; }
        [JsonPropertyName("rating")] public int Rating { get; set; }
        [JsonPropertyName("isOwner")] public bool IsOwner { get; set; }
        [JsonPropertyName("messages")] public List<TicketMessage> Messages { get; set; } = new();
    }
}
