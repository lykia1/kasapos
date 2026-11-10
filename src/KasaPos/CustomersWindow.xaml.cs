using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KasaPos.Data;

namespace KasaPos
{
    public partial class CustomersWindow : Window
    {
        public CustomersWindow()
        {
            InitializeComponent();
            Load();
        }

        private void Load()
        {
            GridData.ItemsSource = CustomerRepo.Search(TxtSearch.Text.Trim());
        }

        private void Search_Click(object sender, RoutedEventArgs e) => Load();

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Load();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            GridData.CommitEdit(DataGridEditingUnit.Row, true);
            var list = GridData.ItemsSource as List<Customer>;
            if (list == null) return;
            foreach (var c in list)
                Db.Exec("UPDATE customers SET name=@n,address=@a,note=@x WHERE id=@id",
                    ("@n", c.Name), ("@a", c.Address), ("@x", c.Note), ("@id", c.Id));
            MessageBox.Show("Kaydedildi.", Title);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var c = GridData.SelectedItem as Customer;
            if (c == null) return;
            if (MessageBox.Show(c.Phone + " " + c.Name + " kalıcı olarak silinsin mi?\nGeçmiş siparişlerdeki ad, telefon ve adres bilgisi de anonimleştirilir. Ciro raporları etkilenmez.",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            CustomerRepo.DeleteAndAnonymize(c.Id);
            Load();
        }
    }
}
