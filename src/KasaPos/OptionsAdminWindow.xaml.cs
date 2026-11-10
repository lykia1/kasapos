using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KasaPos.Data;

namespace KasaPos
{
    public partial class OptionsAdminWindow : Window
    {
        private ObservableCollection<OptionGroup> _groups;
        private ObservableCollection<ProductOption> _options = new ObservableCollection<ProductOption>();
        private List<ProductPick> _products = new List<ProductPick>();
        private OptionGroup _current;
        private bool _loading;

        public OptionsAdminWindow()
        {
            InitializeComponent();
            LoadGroups();
            Closing += (s, e) => SaveCurrent();
        }

        private void LoadGroups(long selectId = 0)
        {
            _loading = true;
            _groups = new ObservableCollection<OptionGroup>(OptionRepo.Groups());
            GridGroups.ItemsSource = _groups;
            _loading = false;
            var sel = _groups.FirstOrDefault(g => g.Id == selectId) ?? _groups.FirstOrDefault();
            if (sel != null) GridGroups.SelectedItem = sel;
            else ShowGroup(null);
        }

        private void GridGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var g = GridGroups.SelectedItem as OptionGroup;
            if (g == _current) return;
            SaveCurrent();
            ShowGroup(g);
        }

        private void ShowGroup(OptionGroup g)
        {
            _current = g;
            bool ok = g != null && g.Id > 0;
            TxtOptTitle.Text = ok ? g.Name + " — seçenekler" : "Seçenekler (önce grubu kaydet)";
            _options = ok ? new ObservableCollection<ProductOption>(OptionRepo.Options(g.Id)) : new ObservableCollection<ProductOption>();
            GridOptions.ItemsSource = _options;
            GridOptions.IsEnabled = ok;

            var assigned = ok ? new HashSet<long>(OptionRepo.ProductsOfGroup(g.Id)) : new HashSet<long>();
            _products = MenuRepo.Products(true).Select(p => new ProductPick { Id = p.Id, Name = p.Name, IsChecked = assigned.Contains(p.Id) }).ToList();
            LstProducts.ItemsSource = _products;
            LstProducts.IsEnabled = ok;
            TxtProdTitle.Text = ok ? "\"" + g.Name + "\" hangi ürünlerde çıksın?" : "Bu grubun çıkacağı ürünler";
        }

        /// <summary>Grupları, seçili grubun seçeneklerini ve ürün atamalarını kaydeder.</summary>
        private void SaveCurrent()
        {
            GridGroups.CommitEdit(DataGridEditingUnit.Row, true);
            GridOptions.CommitEdit(DataGridEditingUnit.Row, true);
            foreach (var g in _groups.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
            {
                g.Name = g.Name.Trim();
                if (g.MinSelect < 0) g.MinSelect = 0;
                if (g.MaxSelect < 0) g.MaxSelect = 0;
                if (g.MaxSelect > 0 && g.MinSelect > g.MaxSelect) g.MinSelect = g.MaxSelect;
                OptionRepo.SaveGroup(g);
            }
            if (_current != null && _current.Id > 0)
            {
                foreach (var o in _options.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
                {
                    o.Name = o.Name.Trim();
                    o.GroupId = _current.Id;
                    OptionRepo.SaveOption(o);
                }
                OptionRepo.SetProductsOfGroup(_current.Id, _products.Where(p => p.IsChecked).Select(p => p.Id));
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrent();
            long id = _current?.Id ?? 0;
            _current = null;
            LoadGroups(id);
            MessageBox.Show("Kaydedildi.", Title);
        }

        private void AllProducts_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _products) p.IsChecked = true;
            LstProducts.Items.Refresh();
        }

        private void NoProducts_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _products) p.IsChecked = false;
            LstProducts.Items.Refresh();
        }

        private void Sample_Click(object sender, RoutedEventArgs e)
        {
            if (OptionRepo.Groups().Count > 0)
            {
                MessageBox.Show("Zaten seçenek grupların var; örnekler sadece hiç grup yokken yüklenir.", Title);
                return;
            }
            OptionRepo.SeedSample();
            _current = null;
            LoadGroups();
            MessageBox.Show("Örnek gruplar yüklendi: Porsiyon, Tercih, Ekstralar.\nŞimdi her grubu seçip sağdan hangi ürünlerde çıkacağını işaretle.", Title);
        }
    }
}
