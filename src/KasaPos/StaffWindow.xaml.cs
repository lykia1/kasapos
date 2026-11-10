using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KasaPos.Data;

namespace KasaPos
{
    public partial class StaffWindow : Window
    {
        public StaffWindow()
        {
            InitializeComponent();
            bool admin = App.CurrentUser != null && App.CurrentUser.IsAdmin;
            PnlReport.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
            ThisMonth_Click(null, null);
            LoadStaff();
        }

        private void LoadStaff()
        {
            PnlStaff.Children.Clear();
            foreach (var s in StaffRepo.Status())
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock { Text = s.Name, FontSize = 17, FontWeight = FontWeights.SemiBold });
                info.Children.Add(new TextBlock { Text = s.StatusText, FontSize = 13, Foreground = s.Inside ? (Brush)FindResource("Ok") : Brushes.Gray });
                g.Children.Add(info);
                var b = new Button
                {
                    Content = s.Inside ? "Çıkış" : "Giriş",
                    Tag = s,
                    Width = 110,
                    Style = (Style)FindResource(s.Inside ? "AccentButton" : "OkButton")
                };
                b.Click += Toggle_Click;
                Grid.SetColumn(b, 1);
                g.Children.Add(b);
                PnlStaff.Children.Add(new Border { Child = g, Padding = new Thickness(6, 8, 6, 8), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 0, 0, 1) });
            }
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            var s = (StaffRow)((Button)sender).Tag;
            var pin = InputDialog.AskPassword(this, s.Inside ? "Mesai Çıkışı" : "Mesai Girişi", s.Name + " — şifreni gir:");
            if (pin == null) return;
            var u = UserRepo.All(true).FirstOrDefault(x => x.Id == s.UserId);
            if (u == null || u.Pin != pin.Trim())
            {
                MessageBox.Show("Şifre hatalı.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (s.Inside) StaffRepo.ClockOut(u); else StaffRepo.ClockIn(u);
            LoadStaff();
            Report_Click(null, null);
        }

        private void ThisMonth_Click(object sender, RoutedEventArgs e)
        {
            DpFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DpTo.SelectedDate = DateTime.Today;
            Report_Click(null, null);
        }

        private void Report_Click(object sender, RoutedEventArgs e)
        {
            if (PnlReport.Visibility != Visibility.Visible) return;
            var f = DpFrom.SelectedDate ?? DateTime.Today;
            var t = DpTo.SelectedDate ?? DateTime.Today;
            if (f > t) { var x = f; f = t; t = x; }
            GridTotals.ItemsSource = StaffRepo.Totals(f, t);
        }
    }
}
