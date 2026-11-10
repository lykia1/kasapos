using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KasaPos.Data;

namespace KasaPos
{
    public partial class TablesWindow : Window
    {
        private ObservableCollection<DiningTable> _tables;

        public TablesWindow()
        {
            InitializeComponent();
            Load();
        }

        private void Load()
        {
            _tables = new ObservableCollection<DiningTable>(TableRepo.All(false));
            GridTables.ItemsSource = _tables;
        }

        private void Bulk_Click(object sender, RoutedEventArgs e)
        {
            int start, count;
            if (!int.TryParse(TxtStart.Text, out start) || !int.TryParse(TxtCount.Text, out count) || count <= 0 || count > 200)
            {
                MessageBox.Show("Başlangıç numarası ve masa sayısını (1-200) kontrol et.", Title);
                return;
            }
            if (!SaveGrid(false)) return;
            int added = TableRepo.BulkAdd(TxtArea.Text, TxtPrefix.Text.Trim(), start, count);
            Load();
            MessageBox.Show(added + " masa eklendi.", Title);
        }

        private bool SaveGrid(bool showMessage)
        {
            GridTables.CommitEdit(DataGridEditingUnit.Row, true);
            var rows = _tables.Where(t => !string.IsNullOrWhiteSpace(t.Name)).ToList();
            foreach (var t in rows)
            {
                t.Name = t.Name.Trim();
                t.Area = string.IsNullOrWhiteSpace(t.Area) ? "Salon" : t.Area.Trim();
            }
            var dup = rows.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
            {
                MessageBox.Show("Aynı adda birden fazla masa var: " + dup.Key, Title);
                return false;
            }
            try
            {
                foreach (var t in rows) TableRepo.Save(t);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kaydedilemedi: " + ex.Message, Title);
                return false;
            }
            if (showMessage) MessageBox.Show("Kaydedildi.", Title);
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (SaveGrid(true)) Load();
        }
    }
}
