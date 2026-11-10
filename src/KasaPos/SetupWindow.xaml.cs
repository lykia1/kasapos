using System;
using System.Linq;
using System.Windows;
using KasaPos.Data;

namespace KasaPos
{
    public partial class SetupWindow : Window
    {
        public SetupWindow()
        {
            InitializeComponent();
            TxtTitle.Text = AppInfo.Name + "'a hoş geldin";
            Loaded += (s, e) => TxtBiz.Focus();
        }

        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            string biz = TxtBiz.Text.Trim();
            string admin = TxtAdmin.Text.Trim();
            string pin = TxtPin.Password.Trim();
            if (biz == "" || admin == "")
            {
                MessageBox.Show("İşletme adı ve yönetici adı zorunlu.", Title);
                return;
            }
            if (pin.Length < 4 || !pin.All(char.IsDigit))
            {
                MessageBox.Show("Şifre en az 4 rakamdan oluşmalı.", Title);
                return;
            }

            Settings.Set("biz_name", biz);
            Settings.Set("biz_phone", TxtPhone.Text.Trim());
            Settings.Set("biz_address", TxtAddress.Text.Trim());
            Settings.Set("day_cutoff_hour", "6");
            Settings.Set("receipt_footer", "Afiyet olsun!");
            UserRepo.Save(new User { Name = admin, Pin = pin, Role = "admin", Active = true });
            if (ChkSample.IsChecked == true && MenuRepo.Categories(false).Count == 0)
                MenuRepo.SeedSampleMenu();

            DialogResult = true;
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            int port;
            if (!int.TryParse(TxtPort.Text.Trim(), out port) || port <= 0) port = 8787;
            try
            {
                var rb = new RemoteBackend(TxtHost.Text.Trim(), port, TxtKey.Text.Trim());
                rb.Ping();
                var users = Convert.ToInt64(rb.Scalar("SELECT COUNT(*) FROM users", null) ?? 0L);
                if (users == 0)
                {
                    MessageBox.Show("Ana kasaya bağlanıldı ama orada henüz kurulum yapılmamış. Önce ana kasada kurulumu tamamla.", Title);
                    return;
                }
                LocalConfig.Set("net_mode", "client");
                LocalConfig.Set("net_host", TxtHost.Text.Trim());
                LocalConfig.Set("net_port", port.ToString());
                LocalConfig.Set("net_key", TxtKey.Text.Trim());
                Db.Backend = rb;
                MessageBox.Show("Ana kasaya bağlanıldı. Ana kasadaki kullanıcı şifrenle giriş yapabilirsin.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Bağlanılamadı: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
