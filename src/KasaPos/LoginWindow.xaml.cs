using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KasaPos.Data;

namespace KasaPos
{
    public partial class LoginWindow : Window
    {
        public User User { get; private set; }

        public LoginWindow()
        {
            InitializeComponent();
            TxtBiz.Text = Settings.Get("biz_name", AppInfo.Name);
            TxtApp.Text = AppInfo.Name + " v" + AppInfo.Version;
            Loaded += (s, e) => TxtPin.Focus();
        }

        private void Digit_Click(object sender, RoutedEventArgs e)
        {
            TxtPin.Password += (string)((Button)sender).Content;
            TxtError.Text = "";
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            TxtPin.Password = "";
        }

        private void Login_Click(object sender, RoutedEventArgs e) => TryLogin();

        private void TryLogin()
        {
            var u = UserRepo.ByPin(TxtPin.Password.Trim());
            if (u == null)
            {
                TxtError.Text = "Şifre hatalı";
                TxtPin.Password = "";
                TxtPin.Focus();
                return;
            }
            User = u;
            DialogResult = true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryLogin();
                e.Handled = true;
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
