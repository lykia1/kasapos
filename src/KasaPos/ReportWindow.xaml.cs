using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using KasaPos.Data;
using KasaPos.Printing;

namespace KasaPos
{
    public partial class ReportWindow : Window
    {
        private List<string> _lines = new List<string>();

        public ReportWindow()
        {
            InitializeComponent();
            var today = DateTime.ParseExact(Settings.BusinessDay(DateTime.Now), "yyyy-MM-dd", null);
            DpFrom.SelectedDate = today;
            DpTo.SelectedDate = today;
            Build();
        }

        private void Today_Click(object sender, RoutedEventArgs e)
        {
            var d = DateTime.ParseExact(Settings.BusinessDay(DateTime.Now), "yyyy-MM-dd", null);
            DpFrom.SelectedDate = d;
            DpTo.SelectedDate = d;
            Build();
        }

        private void Yesterday_Click(object sender, RoutedEventArgs e)
        {
            var d = DateTime.ParseExact(Settings.BusinessDay(DateTime.Now), "yyyy-MM-dd", null).AddDays(-1);
            DpFrom.SelectedDate = d;
            DpTo.SelectedDate = d;
            Build();
        }

        private void Show_Click(object sender, RoutedEventArgs e) => Build();

        private void Build()
        {
            var from = (DpFrom.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            var to = (DpTo.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            if (string.CompareOrdinal(from, to) > 0) { var t = from; from = to; to = t; }
            var p = new (string, object)[] { ("@f", from), ("@t", to) };
            const string where = " business_day BETWEEN @f AND @t ";

            var l = new List<string>();
            l.Add("!^GÜN SONU RAPORU");
            l.Add("^" + Settings.Get("biz_name", AppInfo.Name));
            l.Add("^" + Fmt(from) + (from == to ? "" : " - " + Fmt(to)));
            l.Add("^Yazdırma: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            l.Add(Printer.Line());

            long count = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM orders WHERE status='PAID' AND" + where, p));
            decimal gross = Dec(Db.Scalar("SELECT SUM(subtotal) FROM orders WHERE status='PAID' AND" + where, p));
            decimal disc = Dec(Db.Scalar("SELECT SUM(discount) FROM orders WHERE status='PAID' AND" + where, p));
            decimal net = Dec(Db.Scalar("SELECT SUM(total) FROM orders WHERE status='PAID' AND" + where, p));
            long cancelled = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM orders WHERE status='CANCELLED' AND" + where, p));
            decimal cancelledSum = Dec(Db.Scalar("SELECT SUM(total) FROM orders WHERE status='CANCELLED' AND" + where, p));
            long open = Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM orders WHERE status='OPEN'"));
            decimal openSum = Dec(Db.Scalar("SELECT SUM(total) FROM orders WHERE status='OPEN'"));
            decimal expenses = Dec(Db.Scalar("SELECT SUM(amount) FROM expenses WHERE" + where, p));

            l.Add(Printer.LR("Sipariş sayısı", count.ToString()));
            l.Add(Printer.LR("Brüt satış", Money.Fmt(gross)));
            l.Add(Printer.LR("İndirimler", "-" + Money.Fmt(disc)));
            l.Add("#" + Printer.LR("NET CİRO", Money.Fmt(net)));
            if (count > 0) l.Add(Printer.LR("Ortalama sipariş", Money.Fmt(Math.Round(net / count, 2))));
            l.Add(Printer.Line());

            l.Add("#ÖDEME TÜRLERİ");
            var pays = Db.Query(@"SELECT pm.payment_name AS n, SUM(pm.amount) AS a FROM payments pm
JOIN orders o ON o.id=pm.order_id WHERE o.status='PAID' AND o." + where.Trim() + " GROUP BY pm.payment_name ORDER BY a DESC",
                r => (Db.S(r, "n"), Db.D(r, "a")), p);
            foreach (var x in pays) l.Add(Printer.LR(x.Item1, Money.Fmt(x.Item2)));
            l.Add(Printer.Line());

            l.Add("#SİPARİŞ TÜRLERİ");
            var types = Db.Query("SELECT order_type AS t, COUNT(*) AS c, SUM(total) AS a FROM orders WHERE status='PAID' AND" + where + " GROUP BY order_type",
                r => (Db.S(r, "t"), Db.L(r, "c"), Db.D(r, "a")), p);
            foreach (var x in types) l.Add(Printer.LR(OrderTypes.Label(x.Item1) + " (" + x.Item2 + ")", Money.Fmt(x.Item3)));
            l.Add(Printer.Line());

            l.Add("#ÜRÜN SATIŞLARI");
            var items = Db.Query(@"SELECT i.name AS n, SUM(i.qty) AS q, SUM(i.qty*i.unit_price) AS a FROM order_items i
JOIN orders o ON o.id=i.order_id WHERE o.status='PAID' AND o." + where.Trim() + " GROUP BY i.name ORDER BY q DESC",
                r => (Db.S(r, "n"), Db.D(r, "q"), Db.D(r, "a")), p);
            foreach (var x in items) l.Add(Printer.LR(x.Item2.ToString("0.##", Money.Tr) + " x " + x.Item1, Money.Fmt(x.Item3)));
            l.Add(Printer.Line());

            l.Add(Printer.LR("Giderler", "-" + Money.Fmt(expenses)));
            var cashName = PaymentRepo.Types(false).Where(t => t.IsCash).Select(t => t.Name).ToList();
            decimal cash = pays.Where(x => cashName.Contains(x.Item1)).Sum(x => x.Item2);
            l.Add("#" + Printer.LR("Kasadaki nakit", Money.Fmt(cash - expenses)));
            l.Add("(nakit satış - giderler)");
            l.Add(Printer.Line());
            l.Add(Printer.LR("İptal edilen (" + cancelled + ")", Money.Fmt(cancelledSum)));
            l.Add(Printer.LR("Şu an açık (" + open + ")", Money.Fmt(openSum)));
            l.Add(" ");
            l.Add(" ");

            _lines = l;
            TxtReport.Text = string.Join(Environment.NewLine, l.Select(Strip));
        }

        private static string Strip(string s)
        {
            while (s.Length > 0 && (s[0] == '#' || s[0] == '^' || s[0] == '!')) s = s.Substring(1);
            return s;
        }

        private static string Fmt(string isoDay) => DateTime.ParseExact(isoDay, "yyyy-MM-dd", null).ToString("dd.MM.yyyy");

        private static decimal Dec(object o) => o == null ? 0m : Math.Round(Convert.ToDecimal(o), 2);

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            var err = Printer.Print(Settings.Get("printer_receipt"), _lines, "Gün Sonu Raporu");
            if (err != null) MessageBox.Show("Yazdırılamadı: " + err, Title);
        }
    }
}
