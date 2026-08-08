using FWLauncherV2.Dialogs;
using FWLauncherV2.Models;
using FWLauncherV2.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FWLauncherV2.Views
{
    public partial class DestekView : UserControl
    {
        private readonly DispatcherTimer _poll;
        private bool _staff;
        private int _ticketId;
        private int _lastMsgId;
        private string _status = "";
        private bool _assignedMe;
        private bool _isOwner;
        private int _rating;
        private bool _busy;

        private static readonly Brush BubbleMine = new SolidColorBrush(Color.FromRgb(0x22, 0x3A, 0x6E));
        private static readonly Brush BubbleIn   = new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x3F));
        private static readonly Brush TextLight  = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFF));
        private static readonly Brush TextMuted2  = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xCE));
        private static readonly Brush GoldBrush  = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x1E));

        public DestekView()
        {
            InitializeComponent();
            _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _poll.Tick += async (s, e) => await Tick();
            Loaded += async (s, e) => { _poll.Start(); await LoadList(); };
            Unloaded += (s, e) => _poll.Stop();
        }

        private async Task Tick()
        {
            await LoadList();
            if (_ticketId != 0) await LoadMessages();
        }

        // ===================== LİSTE =====================
        private async Task LoadList()
        {
            var r = await TicketService.ListAsync();
            _staff = r.Staff;
            TxtListTitle.Text = _staff ? "Personel Kuyruğu" : "Destek";
            TxtListSub.Text = _staff ? "Açık talepleri üstlen ve yanıtla." : "Bir talep aç, anında yanıt al.";
            TicketList.ItemsSource = r.Tickets;
            EmptyHint.Visibility = r.Tickets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Ticket_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Ticket t) return;
            OpenTicket(t.Id, t.Subject);
        }

        private async void OpenTicket(int id, string subject)
        {
            _ticketId = id; _lastMsgId = 0;
            MessagesPanel.Children.Clear();
            TxtChatSubject.Text = subject;
            TxtNoTicket.Visibility = Visibility.Collapsed;
            await LoadMessages(true);
        }

        // ===================== MESAJLAR =====================
        private async Task LoadMessages(bool forceScroll = false)
        {
            if (_ticketId == 0 || _busy) return;
            _busy = true;
            try
            {
                var r = await TicketService.MessagesAsync(_ticketId, _lastMsgId);
                if (!r.Success) return;

                bool added = false;
                foreach (var m in r.Messages)
                {
                    AddBubble(m);
                    if (m.Id > _lastMsgId) _lastMsgId = m.Id;
                    added = true;
                }

                _status = r.Status; _assignedMe = r.AssignedMe; _isOwner = r.IsOwner; _rating = r.Rating;
                UpdateChatControls(r.AssignedName);

                if (added || forceScroll) MsgScroll.ScrollToEnd();
            }
            finally { _busy = false; }
        }

        private void AddBubble(TicketMessage m)
        {
            if (m.IsSystem)
            {
                MessagesPanel.Children.Add(new Border
                {
                    Background = BubbleIn,
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(12, 5, 12, 5),
                    Margin = new Thickness(0, 6, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock { Text = m.Body, Foreground = TextMuted2, FontSize = 11.5, TextWrapping = TextWrapping.Wrap }
                });
                return;
            }

            var stack = new StackPanel();
            if (!m.Mine && m.IsStaffSender)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"{m.SenderName} · yetkili",
                    Foreground = GoldBrush, FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 3)
                });
            }
            stack.Children.Add(new TextBlock { Text = m.Body, Foreground = TextLight, FontSize = 13.5, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock
            {
                Text = m.TimeText, Foreground = TextMuted2, FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 0)
            });

            MessagesPanel.Children.Add(new Border
            {
                Background = m.Mine ? BubbleMine : BubbleIn,
                CornerRadius = m.Mine ? new CornerRadius(14, 14, 4, 14) : new CornerRadius(14, 14, 14, 4),
                Padding = new Thickness(13, 9, 13, 7),
                Margin = new Thickness(0, 4, 0, 4),
                MaxWidth = 460,
                HorizontalAlignment = m.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Child = stack
            });
        }

        private void UpdateChatControls(string? assignedName)
        {
            bool closed = _status == "closed";
            BtnClaim.Visibility = (_staff && _status == "open") ? Visibility.Visible : Visibility.Collapsed;
            BtnClose.Visibility = (!closed && (UserSession.IsAdmin || _assignedMe)) ? Visibility.Visible : Visibility.Collapsed;
            BtnDelete.Visibility = (closed && UserSession.IsAdmin) ? Visibility.Visible : Visibility.Collapsed;

            bool canSend = !closed && (_isOwner || UserSession.IsAdmin || _assignedMe);
            TxtInput.IsEnabled = canSend;
            BtnSend.IsEnabled = canSend;

            RatingBar.Visibility = (closed && _isOwner && _rating == 0) ? Visibility.Visible : Visibility.Collapsed;

            TxtChatStatus.Text = _status switch
            {
                "open" => "Açık · yanıt bekleniyor",
                "claimed" => string.IsNullOrEmpty(assignedName) ? "Alındı" : $"Üzerinde: {assignedName}",
                "closed" => _rating > 0 ? $"Kapandı · {_rating}★" : "Kapandı",
                _ => _status
            };
        }

        // ===================== EYLEMLER =====================
        private async void Send_Click(object sender, RoutedEventArgs e) => await DoSend();
        private async void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                await DoSend();
            }
        }

        private async Task DoSend()
        {
            var text = TxtInput.Text.Trim();
            if (string.IsNullOrEmpty(text) || _ticketId == 0) return;
            BtnSend.IsEnabled = false;
            var r = await TicketService.SendAsync(_ticketId, text);
            if (r.Success) { TxtInput.Clear(); await LoadMessages(true); }
            else FWDialog.Error(r.Message ?? "Gönderilemedi.");
            BtnSend.IsEnabled = TxtInput.IsEnabled;
        }

        private async void Claim_Click(object sender, RoutedEventArgs e)
        {
            var r = await TicketService.ClaimAsync(_ticketId);
            if (r.Success) { await LoadMessages(true); await LoadList(); }
            else FWDialog.Error(r.Message ?? "Alınamadı.");
        }

        private async void CloseTicket_Click(object sender, RoutedEventArgs e)
        {
            if (!FWDialog.Confirm("Bu talebi kapatmak istiyor musun?", "Talebi Kapat", "Kapat", "Vazgeç")) return;
            var r = await TicketService.CloseAsync(_ticketId);
            if (r.Success) { await LoadMessages(true); await LoadList(); }
            else FWDialog.Error(r.Message ?? "Kapatılamadı.");
        }

        private async void DeleteTicket_Click(object sender, RoutedEventArgs e)
        {
            if (_ticketId == 0) return;
            if (!FWDialog.Confirm("Bu talebi kalıcı olarak silmek istiyor musun?", "Talebi Sil", "Sil", "Vazgeç")) return;
            var r = await TicketService.DeleteAsync(_ticketId);
            if (r.Success)
            {
                _ticketId = 0; _lastMsgId = 0;
                MessagesPanel.Children.Clear();
                TxtChatSubject.Text = "Bir talep seç";
                TxtChatStatus.Text = "";
                TxtNoTicket.Visibility = Visibility.Visible;
                BtnClaim.Visibility = BtnClose.Visibility = BtnDelete.Visibility = Visibility.Collapsed;
                RatingBar.Visibility = Visibility.Collapsed;
                TxtInput.IsEnabled = BtnSend.IsEnabled = false;
                await LoadList();
            }
            else FWDialog.Error(r.Message ?? "Silinemedi.");
        }

        private async void Star_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out int stars)) return;
            RatingBar.IsEnabled = false;
            var r = await TicketService.RateAsync(_ticketId, stars);
            RatingBar.IsEnabled = true;
            if (r.Success)
            {
                FWDialog.Success($"{stars}★ puanın için teşekkürler!");
                await LoadMessages(true);
            }
            else FWDialog.Error(r.Message ?? "Puanlanamadı.");
        }

        // ===================== YENİ TICKET =====================
        private void New_Click(object sender, RoutedEventArgs e)
        {
            TxtNewSubject.Text = ""; TxtNewMessage.Text = "";
            NewTicketPanel.Visibility = Visibility.Visible;
        }

        private void NewCancel_Click(object sender, RoutedEventArgs e) => NewTicketPanel.Visibility = Visibility.Collapsed;

        private async void NewSubmit_Click(object sender, RoutedEventArgs e)
        {
            var subj = TxtNewSubject.Text.Trim();
            var msg = TxtNewMessage.Text.Trim();
            if (string.IsNullOrEmpty(subj)) { FWDialog.Warn("Konu boş olamaz."); return; }
            if (string.IsNullOrEmpty(msg)) { FWDialog.Warn("Mesaj boş olamaz."); return; }

            BtnNewSubmit.IsEnabled = false;
            var r = await TicketService.CreateAsync(subj, msg);
            BtnNewSubmit.IsEnabled = true;

            if (r.Success)
            {
                NewTicketPanel.Visibility = Visibility.Collapsed;
                await LoadList();
                if (r.Id.HasValue) OpenTicket(r.Id.Value, subj);
            }
            else FWDialog.Error(r.Message ?? "Talep açılamadı.");
        }
    }
}
