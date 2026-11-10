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

        public SettingsWindow()
        {
            InitializeComponent();

            TxtBiz.Text = Settings.Get("biz_name");
            TxtPhone.Text = Settings.Get("biz_phone");
            TxtAddress.Text = Settings.Get("biz_address");
            TxtFooter.Text = Settings.Get("receipt_footer", "Afiyet olsun!");
            for (int h = 0; h <= 12; h++) CmbCutoff.Items.Add(h.ToString("00") + ":00");
            CmbCutoff.SelectedIndex = Math.Min(12, Math.Max(0, Settings.GetInt("day_cutoff_hour", 6)));

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
            TxtChars.Text = Settings.GetInt("receipt_chars", 32).ToString();
            TxtWidthPx.Text = Settings.GetInt("receipt_width_px", 280).ToString();
            TxtFontSize.Text = Settings.GetInt("receipt_font_size", 10).ToString();

            ColRole.ItemsSource = new[] { "admin", "kasiyer" };
            _users = new ObservableCollection<User>(UserRepo.All());
            GridUsers.ItemsSource = _users;

            _payTypes = new ObservableCollection<PaymentType>(PaymentRepo.Types(false));
            GridPayTypes.ItemsSource = _payTypes;

            TxtDataDir.Text = Db.DataDir;
            TxtVersion.Text = AppInfo.Name + " sürüm " + AppInfo.Version;
        }

        private static string SelectOrEmpty(System.Collections.Generic.List<string> list, string value) =>
            list.Contains(value) ? value : "";

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            GridUsers.CommitEdit(DataGridEditingUnit.Row, true);
            GridPayTypes.CommitEdit(DataGridEditingUnit.Row, true);

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
            Settings.Set("biz_phone", TxtPhone.Text.Trim());
            Settings.Set("biz_address", TxtAddress.Text.Trim());
            Settings.Set("receipt_footer", TxtFooter.Text.Trim());
            Settings.Set("day_cutoff_hour", Math.Max(0, CmbCutoff.SelectedIndex).ToString());
            Settings.Set("printer_receipt", CmbReceipt.SelectedItem as string ?? "");
            Settings.Set("printer_kitchen", CmbKitchen.SelectedItem as string ?? "");
            Settings.SetBool("receipt_auto", ChkReceiptAuto.IsChecked == true);
            Settings.SetBool("kitchen_auto", ChkKitchenAuto.IsChecked == true);
            Settings.Set("receipt_chars", chars.ToString());
            Settings.Set("receipt_width_px", px.ToString());
            Settings.Set("receipt_font_size", fs.ToString());

            foreach (var u in users) UserRepo.Save(u);
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
