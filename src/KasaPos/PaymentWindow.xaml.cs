using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KasaPos.Data;

namespace KasaPos
{
    public partial class PaymentWindow : Window
    {
        private readonly decimal _total;
        private readonly List<PaymentType> _types;

        public List<Payment> Payments { get; } = new List<Payment>();
        public decimal Change { get; private set; }
        public bool PrintReceipt => ChkPrint.IsChecked == true;

        public PaymentWindow(decimal total)
        {
            InitializeComponent();
            _total = total;
            _types = PaymentRepo.Types();
            ChkPrint.IsChecked = Settings.GetBool("receipt_auto", false);
            foreach (var t in _types)
            {
                var b = new Button
                {
                    Content = t.Name,
                    Tag = t,
                    Width = 170,
                    Height = 64,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold
                };
                if (t.IsCash) b.Style = (Style)FindResource("AccentButton");
                b.Click += Type_Click;
                PnlTypes.Children.Add(b);
            }
            Refresh();
            Loaded += (s, e) => { TxtAmount.Focus(); TxtAmount.SelectAll(); };
        }

        private decimal Paid => Payments.Sum(p => p.Amount);
        private decimal Remaining => Math.Max(0, _total - Paid);

        private void Refresh()
        {
            TxtTotal.Text = Money.FmtTl(_total);
            TxtRemaining.Text = Money.FmtTl(Remaining);
            TxtChange.Text = Change > 0 ? "Para üstü: " + Money.FmtTl(Change) : "";
            TxtAmount.Text = Money.Fmt(Remaining);
            LstPayments.ItemsSource = null;
            LstPayments.ItemsSource = Payments;
            BtnDone.IsEnabled = Remaining <= 0;
        }

        private void Type_Click(object sender, RoutedEventArgs e)
        {
            var t = (PaymentType)((Button)sender).Tag;
            decimal amount;
            if (!Money.TryParse(TxtAmount.Text, out amount) || amount <= 0)
            {
                if (Remaining <= 0) return;
                amount = Remaining;
            }
            decimal rem = Remaining;
            if (rem <= 0) return;
            if (amount > rem)
            {
                if (!t.IsCash)
                {
                    MessageBox.Show("Nakit dışındaki ödemelerde tutar kalan tutardan fazla olamaz.", Title);
                    return;
                }
                Change += amount - rem;
                amount = rem;
            }
            var existing = Payments.FirstOrDefault(p => p.PaymentTypeId == t.Id);
            if (existing != null) existing.Amount += amount;
            else Payments.Add(new Payment { PaymentTypeId = t.Id, Name = t.Name, Amount = amount });
            Refresh();
            if (Remaining <= 0 && Change == 0 && !Settings.GetBool("pay_confirm", false))
            {
                // Tam ödeme yapıldıysa hızlı kapanış
                DialogResult = true;
            }
        }

        private void Exact_Click(object sender, RoutedEventArgs e)
        {
            TxtAmount.Text = Money.Fmt(Remaining);
        }

        private void Quick_Click(object sender, RoutedEventArgs e)
        {
            TxtAmount.Text = (string)((Button)sender).Content;
        }

        private void RemovePay_Click(object sender, RoutedEventArgs e)
        {
            var p = LstPayments.SelectedItem as Payment;
            if (p == null) return;
            Payments.Remove(p);
            Change = 0;
            Refresh();
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            if (Remaining > 0) return;
            DialogResult = true;
        }
    }
}
