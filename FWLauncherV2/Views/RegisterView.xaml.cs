using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using FWLauncherV2.Dialogs;
using FWLauncherV2.Models;
using FWLauncherV2.Services;

namespace FWLauncherV2.Views
{
    public partial class RegisterView : UserControl
    {
        private const string RegisterApiUrl = "https://pokewing.com/pwapi/register.php";
        private readonly LoginWindow? _loginWindow;

        public RegisterView(LoginWindow? loginWindow)
        {
            InitializeComponent();
            _loginWindow = loginWindow;
        }

        // Tasarım zamanı ctor
        public RegisterView() : this(null) { }

        private async void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            BtnRegister.IsEnabled = false;
            ErrorBox.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(TxtUsername.Text) ||
                string.IsNullOrWhiteSpace(TxtEmail.Text) ||
                string.IsNullOrWhiteSpace(TxtPassword.Password))
            {
                ShowError("Tüm alanlar doldurulmalıdır.");
                BtnRegister.IsEnabled = true;
                return;
            }

            if (TxtPassword.Password != TxtPasswordConfirm.Password)
            {
                ShowError("Girdiğiniz şifreler uyuşmuyor.");
                BtnRegister.IsEnabled = true;
                return;
            }

            var registerData = new
            {
                username = TxtUsername.Text.Trim(),
                email = TxtEmail.Text.Trim(),
                password = TxtPassword.Password,
                deviceId = HardwareId.Get()
            };

            try
            {
                var response = await HttpClientProvider.Api.PostAsJsonAsync(RegisterApiUrl, registerData);

                if (!response.IsSuccessStatusCode)
                {
                    ShowError("Sunucuya bağlanılamadı. Lütfen daha sonra tekrar deneyin.");
                    return;
                }

                var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse>();

                if (apiResponse?.Success == true)
                {
                    FWDialog.Success("Kayıt işlemi başarıyla tamamlandı! Şimdi giriş yapabilirsiniz.");
                    _loginWindow?.SwitchToLogin();
                }
                else
                {
                    ShowError(apiResponse?.Message ?? "Sunucu hatası oluştu.");
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn($"Kayıt ağ hatası: {ex.Message}");
                ShowError("Ağ hatası: " + ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Error("Kayıt sırasında beklenmeyen hata.", ex);
                ShowError("Beklenmeyen hata: " + ex.Message);
            }
            finally
            {
                BtnRegister.IsEnabled = true;
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e) => _loginWindow?.SwitchToLogin();

        private void ShowError(string message)
        {
            TxtErrorMessage.Text = message;
            ErrorBox.Visibility = Visibility.Visible;
        }
    }
}
