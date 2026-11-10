using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using KasaPos.Data;
using KasaPos.Printing;

namespace KasaPos
{
    public partial class CashWindow : Window
    {
        private CashSession _session;

        public CashWindow()
        {
            InitializeComponent();
            Load();
        }

        private string Me => App.CurrentUser?.Name;

        private void Load()
        {
            _session = CashRepo.Current();
            GridHistory.ItemsSource = CashRepo.Recent();
            if (_session == null)
            {
                TxtState.Text = "Kasa kapalı";
                TxtState.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
                TxtOpened.Text = "Güne başlamak için kasadaki nakdi yazıp kasayı aç.";
                PnlClosed.Visibility = Visibility.Visible;
                PnlOpen.Visibility = Visibility.Collapsed;
                TxtOpening.Focus();
                return;
            }
            TxtState.Text = "Kasa açık";
            TxtState.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
            TxtOpened.Text = "Açan: " + _session.OpenedBy + " · " + _session.OpenedAt.ToString("dd.MM.yyyy HH:mm");
            PnlClosed.Visibility = Visibility.Collapsed;
            PnlOpen.Visibility = Visibility.Visible;

            var s = CashRepo.Summary(_session);
            GridSummary.Children.Clear();
            GridSummary.RowDefinitions.Clear();
            AddRow("Açılış nakdi", s.Opening, false);
            AddRow("Nakit satış", s.CashSales, false);
            AddRow("Para ekleme", s.MovesIn, false);
            AddRow("Para çekme", -s.MovesOut, false);
            AddRow("Giderler", -s.Expenses, false);
            AddRow("Kasada olması gereken", s.Expected, true);
            AddRow("Kart / diğer ödemeler (bilgi)", s.OtherSales, false);
        }

        private void AddRow(string label, decimal value, bool bold)
        {
            int r = GridSummary.RowDefinitions.Count;
            GridSummary.RowDefinitions.Add(new RowDefinition());
            var a = new TextBlock { Text = label, FontSize = bold ? 17 : 14, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, bold ? 8 : 3, 0, 3) };
            var b = new TextBlock { Text = Money.FmtTl(value), FontSize = bold ? 17 : 14, FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold, Margin = new Thickness(0, bold ? 8 : 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(a, r);
            Grid.SetRow(b, r);
            Grid.SetColumn(b, 1);
            GridSummary.Children.Add(a);
            GridSummary.Children.Add(b);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            decimal v;
            if (!Money.TryParse(TxtOpening.Text, out v)) v = 0;
            if (v < 0) return;
            CashRepo.Open(v, Me);
            Load();
        }

        private void MoneyIn_Click(object sender, RoutedEventArgs e) => Move(1);
        private void MoneyOut_Click(object sender, RoutedEventArgs e) => Move(-1);

        private void Move(int sign)
        {
            if (_session == null) return;
            var a = InputDialog.Ask(this, sign > 0 ? "Para Ekle" : "Para Çek", "Tutar ₺:");
            decimal v;
            if (a == null || !Money.TryParse(a, out v) || v <= 0) return;
            var note = InputDialog.Ask(this, sign > 0 ? "Para Ekle" : "Para Çek", "Açıklama (ör. bozuk para, patrona teslim):") ?? "";
            CashRepo.AddMove(_session.Id, sign * v, note, Me);
            Load();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            var sum = CashRepo.Summary(_session);
            var a = InputDialog.Ask(this, "Kasa Kapanışı", "Kasadaki nakdi say ve yaz (₺):");
            decimal counted;
            if (a == null || !Money.TryParse(a, out counted) || counted < 0) return;
            decimal diff = counted - sum.Expected;
            string msg = "Olması gereken: " + Money.FmtTl(sum.Expected) + "\nSayılan: " + Money.FmtTl(counted) + "\nFark: " +
                         (diff == 0 ? "yok" : (diff > 0 ? "+" : "") + Money.FmtTl(diff) + (diff > 0 ? " (fazla)" : " (eksik)")) +
                         "\n\nKasa kapatılsın mı?";
            if (MessageBox.Show(msg, Title, MessageBoxButton.YesNo, diff == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            string note = diff != 0 ? (InputDialog.Ask(this, "Kasa Kapanışı", "Fark için açıklama (isteğe bağlı):") ?? "") : "";
            CashRepo.Close(_session, counted, Me, note);

            if (MessageBox.Show("Kasa kapanış fişi yazdırılsın mı?", Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                var l = new List<string>
                {
                    "!^KASA KAPANIŞ",
                    "^" + Settings.Get("biz_name", AppInfo.Name),
                    "^" + DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
                    Printer.Line(),
                    Printer.LR("Açan", _session.OpenedBy + " " + _session.OpenedAt.ToString("HH:mm")),
                    Printer.LR("Kapatan", Me ?? ""),
                    Printer.Line(),
                    Printer.LR("Açılış nakdi", Money.Fmt(sum.Opening)),
                    Printer.LR("Nakit satış", Money.Fmt(sum.CashSales)),
                    Printer.LR("Para ekleme", Money.Fmt(sum.MovesIn)),
                    Printer.LR("Para çekme", "-" + Money.Fmt(sum.MovesOut)),
                    Printer.LR("Giderler", "-" + Money.Fmt(sum.Expenses)),
                    "#" + Printer.LR("OLMASI GEREKEN", Money.Fmt(sum.Expected)),
                    "#" + Printer.LR("SAYILAN", Money.Fmt(counted)),
                    "#" + Printer.LR("FARK", Money.Fmt(diff)),
                    Printer.Line(),
                    Printer.LR("Kart / diğer", Money.Fmt(sum.OtherSales)),
                    " ", " "
                };
                if (!string.IsNullOrWhiteSpace(note)) l.Insert(l.Count - 2, "Not: " + note);
                var err = Printer.Print(Settings.Get("printer_receipt"), l, "Kasa Kapanış");
                if (err != null) MessageBox.Show("Yazdırılamadı: " + err, Title);
            }
            Load();
        }
    }
}
