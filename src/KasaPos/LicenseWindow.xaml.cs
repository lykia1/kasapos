using System.Windows;

namespace KasaPos
{
    public partial class LicenseWindow : Window
    {
        public LicenseWindow()
        {
            InitializeComponent();
            TxtMachine.Text = Licensing.MachineCode;
            Refresh();
        }

        private void Refresh()
        {
            TxtStatus.Text = Licensing.StatusText;
            BtnClose.Content = Licensing.State == LicenseState.Expired ? "Programdan Çık" : "Kapat";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(TxtMachine.Text); } catch { }
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (!Licensing.Activate(TxtKey.Text, out err))
            {
                TxtError.Text = err;
                return;
            }
            TxtError.Text = "";
            Refresh();
            MessageBox.Show("Lisans etkinleştirildi. Teşekkürler!", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = Licensing.State != LicenseState.Expired;
        }
    }
}
