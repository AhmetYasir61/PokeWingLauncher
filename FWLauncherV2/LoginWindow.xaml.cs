using System.Windows;
using System.Windows.Input;
using FWLauncherV2.Views;

namespace FWLauncherV2
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();

            // Uygulama açıldığında ilk olarak LoginView göster
            ContentArea.Content = new LoginView(this);
        }

        public void SwitchToRegister()
        {
            ContentArea.Content = new RegisterView(this);
        }

        public void SwitchToLogin()
        {
            ContentArea.Content = new LoginView(this);
        }

        public void LoginSuccess(string username)
        {
            // Giriş başarılı → MainWindow'u aç, kullanıcı adını ilet
            MainWindow main = new MainWindow();
            main.NavigateToMainView(username);
            main.Show();

            // Login penceresini kapat
            this.Close();
        }

        // ✅ XAML'deki CloseButton_Click için
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // Pencereyi üst bardan sürükle
        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }
    }
}
