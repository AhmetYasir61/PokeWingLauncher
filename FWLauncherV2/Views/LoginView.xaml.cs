using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using FWLauncherV2.Models;
using FWLauncherV2.Services;

namespace FWLauncherV2.Views
{
    public partial class LoginView : UserControl
    {
        private const string LoginApiUrl = "https://pokewing.com/pwapi/login.php";
        private readonly LoginWindow? _loginWindow;

        public LoginView(LoginWindow? loginWindow)
        {
            InitializeComponent();
            _loginWindow = loginWindow;
        }

        // Tasarım zamanı ctor
        public LoginView() : this(null) { }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            BtnLogin.IsEnabled = false;
            ErrorBox.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(TxtUsername.Text) || string.IsNullOrWhiteSpace(TxtPassword.Password))
            {
                ShowError("Kullanıcı adı ve şifre boş bırakılamaz.");
                BtnLogin.IsEnabled = true;
                return;
            }

            var loginData = new
            {
                username = TxtUsername.Text.Trim(),
                password = TxtPassword.Password,
                deviceId = HardwareId.Get()
            };

            try
            {
                var response = await HttpClientProvider.Api.PostAsJsonAsync(LoginApiUrl, loginData);

                if (!response.IsSuccessStatusCode)
                {
                    ShowError($"Sunucu hatası. (Kod: {response.StatusCode})");
                    return;
                }

                var apiResponse = await response.Content.ReadFromJsonAsync<LoginApiResponse>();

                if (apiResponse?.Success == true)
                {
                    // Mevcut ayarları koru (Java yolu, RAM); yalnızca oturum alanlarını güncelle.
                    var settings = SettingsService.Load();
                    settings.LastUsername = apiResponse.Username;
                    settings.SessionToken = apiResponse.Token;
                    settings.DeviceId = HardwareId.Get();
                    SettingsService.Save(settings);

                    Logger.Info($"Giriş başarılı: {apiResponse.Username}");
                    _loginWindow?.LoginSuccess(apiResponse.Username);
                }
                else
                {
                    ShowError(apiResponse?.Message ?? "Bilinmeyen sunucu hatası.");
                }
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn($"Giriş ağ hatası: {ex.Message}");
                ShowError("Ağ hatası: " + ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Error("Giriş sırasında beklenmeyen hata.", ex);
                ShowError("Beklenmeyen hata: " + ex.Message);
            }
            finally
            {
                BtnLogin.IsEnabled = true;
            }
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e) => _loginWindow?.SwitchToRegister();

        private void ShowError(string message)
        {
            TxtErrorMessage.Text = message;
            ErrorBox.Visibility = Visibility.Visible;
        }
    }
}
