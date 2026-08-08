using FWLauncherV2.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace FWLauncherV2.Dialogs
{
    public partial class TransferDialog : Window
    {
        public TransferDialog()
        {
            InitializeComponent();

            TxtHint.Text = UserSession.IsAdmin
                ? "Bir oyuncuya coin gönder. (Yönetici olduğun için sınırsız.)"
                : $"Bir oyuncuya coin gönder. Bakiyen: {UserSession.Coins:N0} Coin.";

            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (owner != null && !ReferenceEquals(owner, this)) Owner = owner;
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            ErrorBox.Visibility = Visibility.Collapsed;

            var to = TxtUser.Text.Trim();
            if (string.IsNullOrWhiteSpace(to))
            {
                ShowError("Kullanıcı adı boş olamaz.");
                return;
            }
            if (!long.TryParse(TxtAmount.Text.Trim(), out long amount) || amount <= 0)
            {
                ShowError("Geçerli bir miktar gir.");
                return;
            }
            if (!UserSession.IsAdmin && amount > UserSession.Coins)
            {
                ShowError("Yetersiz coin.");
                return;
            }

            BtnSend.IsEnabled = false;
            BtnSend.Content = "Gönderiliyor...";

            var result = await ShopService.TransferAsync(to, amount);

            BtnSend.IsEnabled = true;
            BtnSend.Content = "Gönder";

            if (result.Success)
            {
                if (result.Coins.HasValue) UserSession.SetCoins(result.Coins.Value);
                FWDialog.Success(result.Message ?? "Coin gönderildi.");
                DialogResult = true;
                Close();
            }
            else
            {
                ShowError(result.Message ?? "Gönderim başarısız oldu.");
            }
        }

        private void ShowError(string msg)
        {
            TxtError.Text = msg;
            ErrorBox.Visibility = Visibility.Visible;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
    }
}
