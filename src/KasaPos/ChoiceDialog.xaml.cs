using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace KasaPos
{
    /// <summary>Büyük dokunmatik düğmelerle listeden seçim yaptırır. İsteğe bağlı elle giriş kutusu içerir.</summary>
    public partial class ChoiceDialog : Window
    {
        public int SelectedIndex { get; private set; } = -1;
        public string ManualValue { get; private set; }

        public ChoiceDialog(string title, string prompt, IList<string> options, string manualLabel = null)
        {
            InitializeComponent();
            Title = title;
            TxtPrompt.Text = prompt;
            for (int i = 0; i < options.Count; i++)
            {
                var b = new Button { Content = options[i], Tag = i, MinWidth = 140, Height = 70, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5) };
                b.Click += (s, e) => { SelectedIndex = (int)((Button)s).Tag; DialogResult = true; };
                PnlChoices.Children.Add(b);
            }
            if (options.Count == 0)
                PnlChoices.Children.Add(new TextBlock { Text = "Seçilebilecek bir şey yok.", FontSize = 15, Margin = new Thickness(6) });
            if (manualLabel != null)
            {
                PnlManual.Visibility = Visibility.Visible;
                TxtManualLabel.Text = manualLabel;
            }
        }

        private void Manual_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtManual.Text)) return;
            ManualValue = TxtManual.Text.Trim();
            DialogResult = true;
        }
    }
}
