using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace LisansUretici
{
    public partial class MainWindow : Window
    {
        private static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KasaPOS-LisansUretici.txt");

        public MainWindow()
        {
            InitializeComponent();
            try { if (File.Exists(SettingsFile)) TxtKeyPath.Text = File.ReadAllText(SettingsFile).Trim(); } catch { }
        }

        private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "PEM anahtar (*.pem)|*.pem|Tümü (*.*)|*.*" };
            if (d.ShowDialog(this) == true)
            {
                TxtKeyPath.Text = d.FileName;
                try { File.WriteAllText(SettingsFile, d.FileName); } catch { }
            }
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var machine = TxtMachine.Text.Trim().ToUpperInvariant();
                var name = TxtName.Text.Trim().Replace("|", " ");
                if (machine == "" || name == "")
                {
                    MessageBox.Show("Makine kodu ve işletme adı gerekli.", Title);
                    return;
                }
                string exp = "";
                if (!string.IsNullOrWhiteSpace(TxtExpiry.Text))
                {
                    DateTime d;
                    if (!DateTime.TryParseExact(TxtExpiry.Text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    {
                        MessageBox.Show("Tarihi gg.aa.yyyy biçiminde yaz (ör. 31.12.2027).", Title);
                        return;
                    }
                    exp = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                var payload = Encoding.UTF8.GetBytes(machine + "|" + name + "|" + exp + "|std");
                using (var rsa = RSA.Create())
                {
                    rsa.ImportFromPem(File.ReadAllText(TxtKeyPath.Text.Trim()));
                    var sig = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    TxtResult.Text = "KP1-" + B64Url(payload) + "." + B64Url(sig);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Üretilemedi: " + ex.Message, Title);
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(TxtResult.Text); } catch { }
        }
    }
}
