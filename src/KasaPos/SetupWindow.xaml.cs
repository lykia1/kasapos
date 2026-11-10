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
    }
}
