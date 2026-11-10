using System.Windows;
using System.Windows.Input;

namespace KasaPos
{
    public partial class InputDialog : Window
    {
        private bool _password;
        public string Value => _password ? PwdValue.Password : TxtValue.Text;

        public InputDialog(string title, string prompt, string initial = "")
        {
            InitializeComponent();
            Title = title;
            TxtPrompt.Text = prompt;
            TxtValue.Text = initial ?? "";
            Loaded += (s, e) => { TxtValue.Focus(); TxtValue.SelectAll(); };
        }

        public static string Ask(Window owner, string title, string prompt, string initial = "")
        {
            var d = new InputDialog(title, prompt, initial) { Owner = owner };
            return d.ShowDialog() == true ? d.Value : null;
        }

        /// <summary>Şifre (PIN) sorar; yazılanlar gizlenir.</summary>
        public static string AskPassword(Window owner, string title, string prompt)
        {
            var d = new InputDialog(title, prompt, "") { Owner = owner };
            d._password = true;
            d.TxtValue.Visibility = Visibility.Collapsed;
            d.PwdValue.Visibility = Visibility.Visible;
            d.Loaded += (s, e) => d.PwdValue.Focus();
            return d.ShowDialog() == true ? d.Value : null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void TxtValue_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DialogResult = true;
        }
    }
}
