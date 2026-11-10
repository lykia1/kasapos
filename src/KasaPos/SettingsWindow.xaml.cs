using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KasaPos.Data;
using KasaPos.Printing;

namespace KasaPos
{
    public partial class SettingsWindow : Window
    {
        private ObservableCollection<User> _users;
        private ObservableCollection<PaymentType> _payTypes;
        private ObservableCollection<Courier> _couriers;

        public SettingsWindow()
        {
            InitializeComponent();

            TxtBiz.Text = Settings.Get("biz_name");
            TxtPhone.Text = Settings.Get("biz_phone");
            TxtAddress.Text = Settings.Get("biz_address");
            TxtFooter.Text = Settings.Get("receipt_footer", "Afiyet olsun!");
            for (int h = 0; h <= 12; h++) CmbCutoff.Items.Add(h.ToString("00") + ":00");
            CmbCutoff.SelectedIndex = Math.Min(12, Math.Max(0, Settings.GetInt("day_cutoff_hour", 6)));

            foreach (var n in Palette.Names) if (n != "") CmbTheme.Items.Add(n);
            CmbTheme.SelectedItem = Settings.Get("theme_color", "Turuncu");
            if (CmbTheme.SelectedIndex < 0) CmbTheme.SelectedItem = "Turuncu";

            var printers = Printer.InstalledPrinters();
            CmbReceipt.Items.Add("");
            CmbKitchen.Items.Add("");
            foreach (var p in printers)
            {
                CmbReceipt.Items.Add(p);
                CmbKitchen.Items.Add(p);
            }
            CmbReceipt.SelectedItem = SelectOrEmpty(printers, Settings.Get("printer_receipt"));
            CmbKitchen.SelectedItem = SelectOrEmpty(printers, Settings.Get("printer_kitchen"));
            ChkReceiptAuto.IsChecked = Settings.GetBool("receipt_auto", false);
            ChkKitchenAuto.IsChecked = Settings.GetBool("kitchen_auto", false);
            ChkKitchenScreen.IsChecked = Settings.GetBool("kitchen_screen", false);
            ChkCourierPrint.IsChecked = Settings.GetBool("courier_print", true);
            _couriers = new ObservableCollection<Courier>(CourierRepo.All(false));
            GridCouriers.ItemsSource = _couriers;
            TxtChars.Text = Settings.GetInt("receipt_chars", 32).ToString();
            TxtWidthPx.Text = Settings.GetInt("receipt_width_px", 280).ToString();
            TxtFontSize.Text = Settings.GetInt("receipt_font_size", 10).ToString();

            ColRole.ItemsSource = new[] { "admin", "kasiyer" };
            _users = new ObservableCollection<User>(UserRepo.All());
            GridUsers.ItemsSource = _users;

            _payTypes = new ObservableCollection<PaymentType>(PaymentRepo.Types(false));
            GridPayTypes.ItemsSource = _payTypes;

            ChkCid.IsChecked = Settings.GetBool("callerid_enabled", false);
            foreach (var port in CallerId.Ports()) CmbCidPort.Items.Add(port);
            CmbCidPort.Text = Settings.Get("callerid_port");
            foreach (var b in new[] { "1200", "2400", "4800", "9600", "19200", "38400", "115200" }) CmbCidBaud.Items.Add(b);
            CmbCidBaud.Text = Settings.GetInt("callerid_baud", 9600).ToString();
            TxtCidInit.Text = Settings.Get("callerid_init");
            TxtCidStatus.Text = CallerId.IsRunning ? "Durum: dinleniyor (" + Settings.Get("callerid_port") + ")" : "Durum: kapalı";

            TxtDataDir.Text = Db.DataDir;
            ChkAutoUpdate.IsChecked = Settings.GetBool("update_auto", true);
            TxtLicense.Text = Licensing.StatusText + "  ·  Makine kodu: " + Licensing.MachineCode;
            TxtVersion.Text = AppInfo.Name + " sürüm " + AppInfo.Version;
        }

        private static string SelectOrEmpty(System.Collections.Generic.List<string> list, string value) =>
            list.Contains(value) ? value : "";

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            GridUsers.CommitEdit(DataGridEditingUnit.Row, true);
            GridPayTypes.CommitEdit(DataGridEditingUnit.Row, true);
            GridCouriers.CommitEdit(DataGridEditingUnit.Row, true);

            if (string.IsNullOrWhiteSpace(TxtBiz.Text))
            {
                MessageBox.Show("İşletme adı boş olamaz.", Title);
                return;
            }

            // Kullanıcı kontrolleri
            var users = _users.Where(u => !string.IsNullOrWhiteSpace(u.Name)).ToList();
            foreach (var u in users)
            {
                u.Pin = (u.Pin ?? "").Trim();
                if (u.Pin.Length < 4 || !u.Pin.All(char.IsDigit))
                {
                    MessageBox.Show(u.Name + " için şifre en az 4 rakam olmalı.", Title);
                    return;
                }
                if (string.IsNullOrEmpty(u.Role)) u.Role = "kasiyer";
            }
            if (users.GroupBy(u => u.Pin).Any(g => g.Count() > 1))
            {
                MessageBox.Show("İki kullanıcının şifresi aynı olamaz.", Title);
                return;
            }
            if (!users.Any(u => u.Active && u.IsAdmin))
            {
                MessageBox.Show("En az bir aktif yönetici (admin) kalmalı.", Title);
                return;
            }

            int chars, px, fs;
            if (!int.TryParse(TxtChars.Text, out chars) || chars < 20 || chars > 64) chars = 32;
            if (!int.TryParse(TxtWidthPx.Text, out px) || px < 150 || px > 800) px = 280;
            if (!int.TryParse(TxtFontSize.Text, out fs) || fs < 6 || fs > 20) fs = 10;

            Settings.Set("biz_name", TxtBiz.Text.Trim());
            Settings.Set("theme_color", CmbTheme.SelectedItem as string ?? "Turuncu");
            Settings.Set("biz_phone", TxtPhone.Text.Trim());
            Settings.Set("biz_address", TxtAddress.Text.Trim());
            Settings.Set("receipt_footer", TxtFooter.Text.Trim());
            Settings.Set("day_cutoff_hour", Math.Max(0, CmbCutoff.SelectedIndex).ToString());
            Settings.Set("printer_receipt", CmbReceipt.SelectedItem as string ?? "");
            Settings.Set("printer_kitchen", CmbKitchen.SelectedItem as string ?? "");
            Settings.SetBool("receipt_auto", ChkReceiptAuto.IsChecked == true);
            Settings.SetBool("kitchen_auto", ChkKitchenAuto.IsChecked == true);
            Settings.SetBool("kitchen_screen", ChkKitchenScreen.IsChecked == true);
            Settings.SetBool("courier_print", ChkCourierPrint.IsChecked == true);
            Settings.Set("receipt_chars", chars.ToString());
            Settings.Set("receipt_width_px", px.ToString());
            Settings.Set("receipt_font_size", fs.ToString());

            int baud;
            if (!int.TryParse((CmbCidBaud.Text ?? "").Trim(), out baud) || baud <= 0) baud = 9600;
            bool cidChanged = Settings.GetBool("callerid_enabled", false) != (ChkCid.IsChecked == true)
                || Settings.Get("callerid_port") != (CmbCidPort.Text ?? "").Trim()
                || Settings.GetInt("callerid_baud", 9600) != baud
                || Settings.Get("callerid_init") != TxtCidInit.Text.Trim();
            Settings.SetBool("update_auto", ChkAutoUpdate.IsChecked == true);
            Settings.SetBool("callerid_enabled", ChkCid.IsChecked == true);
            Settings.Set("callerid_port", (CmbCidPort.Text ?? "").Trim());
            Settings.Set("callerid_baud", baud.ToString());
            Settings.Set("callerid_init", TxtCidInit.Text.Trim());
            if (cidChanged)
            {
                var cidErr = CallerId.Start();
                if (cidErr != null) MessageBox.Show(cidErr, Title);
            }

            foreach (var u in users) UserRepo.Save(u);
            foreach (var c in _couriers.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
            {
                c.Name = c.Name.Trim();
                CourierRepo.Save(c);
            }
            foreach (var p in _payTypes.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
            {
                p.Name = p.Name.Trim();
                PaymentRepo.Save(p);
            }

            DialogResult = true;
        }

        private void TestPrint_Click(object sender, RoutedEventArgs e)
        {
            int chars;
            if (int.TryParse(TxtChars.Text, out chars)) Settings.Set("receipt_chars", chars.ToString());
            int px;
            if (int.TryParse(TxtWidthPx.Text, out px)) Settings.Set("receipt_width_px", px.ToString());
            int fs;
            if (int.TryParse(TxtFontSize.Text, out fs)) Settings.Set("receipt_font_size", fs.ToString());
            var lines = new System.Collections.Generic.List<string>
            {
                "!^TEST FİŞİ",
                "^" + (TxtBiz.Text.Trim() == "" ? AppInfo.Name : TxtBiz.Text.Trim()),
                Printer.Line(),
                Printer.LR("2 x Tavuk Döner Dürüm", "300,00"),
                Printer.LR("1 x Ayran", "30,00"),
                Printer.Line(),
                "#" + Printer.LR("TOPLAM", "330,00 ₺"),
                Printer.Line(),
                "^Türkçe karakter: ğüşıöç ĞÜŞİÖÇ",
                "^" + new string('1', Printer.Width),
                " ", " "
            };
            var err = Printer.Print(CmbReceipt.SelectedItem as string, lines, "Test");
            if (err != null) MessageBox.Show("Yazdırılamadı: " + err, Title);
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var info = await Updater.CheckAsync();
                if (info == null)
                {
                    MessageBox.Show("En güncel sürümü kullanıyorsun (v" + AppInfo.Version + ").", Title);
                    return;
                }
                await Updater.PromptAndInstall(this, info);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Güncelleme kontrol edilemedi: " + ex.Message, Title);
            }
        }

        private void License_Click(object sender, RoutedEventArgs e)
        {
            new LicenseWindow { Owner = this }.ShowDialog();
            TxtLicense.Text = Licensing.StatusText + "  ·  Makine kodu: " + Licensing.MachineCode;
        }

        private void CidTest_Click(object sender, RoutedEventArgs e)
        {
            var n = InputDialog.Ask(this, "Test Araması", "Denemek için bir telefon numarası yaz:", "05321234567");
            if (string.IsNullOrWhiteSpace(n)) return;
            CallerId.Raise(n);
        }

        private void BackupNow_Click(object sender, RoutedEventArgs e)
        {
            var path = Db.Backup(true);
            MessageBox.Show(path == null ? "Yedek alınamadı." : "Yedek alındı:\n" + path, Title);
        }

        private void OpenBackups_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Db.BackupDir + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, Title); }
        }
    }
}
