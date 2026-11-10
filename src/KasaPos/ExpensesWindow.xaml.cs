using System;
using System.Linq;
using System.Windows;
using KasaPos.Data;

namespace KasaPos
{
    public partial class ExpensesWindow : Window
    {
        private readonly string _day;

        public ExpensesWindow()
        {
            InitializeComponent();
            _day = Settings.BusinessDay(DateTime.Now);
            TxtTitle.Text = "Giderler — " + DateTime.ParseExact(_day, "yyyy-MM-dd", null).ToString("dd.MM.yyyy");
            Load();
            Loaded += (s, e) => TxtDesc.Focus();
        }

        private void Load()
        {
            var list = ExpenseRepo.ForDay(_day);
            GridData.ItemsSource = list;
            TxtTotal.Text = "Toplam gider: " + Money.FmtTl(list.Sum(x => x.Amount));
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            decimal amount;
            if (string.IsNullOrWhiteSpace(TxtDesc.Text) || !Money.TryParse(TxtAmount.Text, out amount) || amount <= 0)
            {
                MessageBox.Show("Açıklama ve geçerli bir tutar gir.", Title);
                return;
            }
            ExpenseRepo.Add(TxtDesc.Text.Trim(), amount, App.CurrentUser?.Name);
            TxtDesc.Text = "";
            TxtAmount.Text = "";
            TxtDesc.Focus();
            Load();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var x = GridData.SelectedItem as Expense;
            if (x == null) return;
            if (MessageBox.Show(x.Description + " silinsin mi?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            ExpenseRepo.Delete(x.Id);
            Load();
        }
    }
}
