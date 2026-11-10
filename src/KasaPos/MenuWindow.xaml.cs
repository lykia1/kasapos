using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using KasaPos.Data;

namespace KasaPos
{
    public class HiddenTagConverter : IValueConverter
    {
        public static readonly HiddenTagConverter Instance = new HiddenTagConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is bool b && !b) ? "  (gizli)" : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public partial class MenuWindow : Window
    {
        private ObservableCollection<Product> _products = new ObservableCollection<Product>();
        private Category _current;
        private bool _switching;

        public MenuWindow()
        {
            InitializeComponent();
            ChkSamePrice.IsChecked = Settings.GetBool("same_price_masa_gelal", true);
            ColColor.ItemsSource = Palette.Names;
            foreach (var n in Palette.Names) CmbCatColor.Items.Add(n == "" ? "(renksiz)" : n);
            LoadCategories();
            Closing += (s, e) => SaveCurrent(false);
        }

        private void LoadCategories(long selectId = 0)
        {
            var cats = MenuRepo.Categories(false);
            for (int i = 0; i < cats.Count; i++) cats[i].Sort = i;
            _switching = true;
            LstCats.ItemsSource = cats;
            _switching = false;
            var sel = cats.FirstOrDefault(c => c.Id == selectId) ?? cats.FirstOrDefault();
            LstCats.SelectedItem = sel;
            if (sel == null) ShowCategory(null);
        }

        private void LstCats_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_switching) return;
            if (_current != null) SaveCurrent(false);
            ShowCategory(LstCats.SelectedItem as Category);
        }

        private void ShowCategory(Category c)
        {
            _current = c;
            TxtCatTitle.Text = c == null ? "Önce soldan bir kategori ekle" : c.Name + " — Ürünler";
            _products = c == null ? new ObservableCollection<Product>() : new ObservableCollection<Product>(MenuRepo.Products(false, c.Id));
            GridProducts.ItemsSource = _products;
            GridProducts.IsEnabled = c != null;
            _switching = true;
            int idx = c == null ? 0 : Array.IndexOf(Palette.Names, c.Color ?? "");
            CmbCatColor.SelectedIndex = idx < 0 ? 0 : idx;
            CmbCatColor.IsEnabled = c != null;
            _switching = false;
        }

        private void CmbCatColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_switching || _current == null || CmbCatColor.SelectedIndex < 0) return;
            _current.Color = Palette.Names[CmbCatColor.SelectedIndex];
            MenuRepo.SaveCategory(_current);
        }

        private void ImageAdd_Click(object sender, RoutedEventArgs e)
        {
            var p = GridProducts.SelectedItem as Product;
            if (p == null || string.IsNullOrWhiteSpace(p.Name))
            {
                MessageBox.Show("Önce tablodan bir ürün seç.", Title);
                return;
            }
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = p.Name + " için görsel seç",
                Filter = "Görseller (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                p.Image = ImageCache.Import(dlg.FileName);
                SaveCurrent(false);
                ShowCategory(_current);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Görsel eklenemedi: " + ex.Message, Title);
            }
        }

        private void ImageRemove_Click(object sender, RoutedEventArgs e)
        {
            var p = GridProducts.SelectedItem as Product;
            if (p == null || !p.HasImage) return;
            p.Image = null;
            SaveCurrent(false);
            ShowCategory(_current);
        }

        private bool SaveCurrent(bool showMessage)
        {
            if (_current == null) return true;
            GridProducts.CommitEdit(DataGridEditingUnit.Row, true);
            GridProducts.CommitEdit();
            bool same = ChkSamePrice.IsChecked == true;
            Settings.SetBool("same_price_masa_gelal", same);
            int saved = 0;
            int i = 0;
            foreach (var p in _products.ToList())
            {
                if (string.IsNullOrWhiteSpace(p.Name)) continue;
                p.Name = p.Name.Trim();
                p.CategoryId = _current.Id;
                if (same) p.PriceGelAl = p.PriceMasa;
                if (p.PricePaket == 0) p.PricePaket = p.PriceMasa;
                if (p.Sort == 0) p.Sort = i;
                MenuRepo.SaveProduct(p);
                saved++;
                i++;
            }
            if (showMessage) MessageBox.Show(saved + " ürün kaydedildi.", Title);
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrent(true);
            ShowCategory(_current);
        }

        private void CatAdd_Click(object sender, RoutedEventArgs e)
        {
            var name = InputDialog.Ask(this, "Yeni Kategori", "Kategori adı:");
            if (string.IsNullOrWhiteSpace(name)) return;
            SaveCurrent(false);
            var cats = (LstCats.ItemsSource as System.Collections.Generic.List<Category>) ?? new System.Collections.Generic.List<Category>();
            long id = MenuRepo.SaveCategory(new Category { Name = name.Trim(), Sort = cats.Count, Active = true });
            _current = null;
            LoadCategories(id);
        }

        private void CatRename_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var name = InputDialog.Ask(this, "Kategori", "Yeni ad:", _current.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            SaveCurrent(false);
            _current.Name = name.Trim();
            MenuRepo.SaveCategory(_current);
            long id = _current.Id;
            _current = null;
            LoadCategories(id);
        }

        private void CatToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            SaveCurrent(false);
            _current.Active = !_current.Active;
            MenuRepo.SaveCategory(_current);
            long id = _current.Id;
            _current = null;
            LoadCategories(id);
        }

        private void Move(int dir)
        {
            var cats = LstCats.ItemsSource as System.Collections.Generic.List<Category>;
            if (cats == null || _current == null) return;
            int idx = cats.FindIndex(c => c.Id == _current.Id);
            int j = idx + dir;
            if (idx < 0 || j < 0 || j >= cats.Count) return;
            SaveCurrent(false);
            var a = cats[idx];
            var b = cats[j];
            int t = a.Sort; a.Sort = b.Sort; b.Sort = t;
            MenuRepo.SaveCategory(a);
            MenuRepo.SaveCategory(b);
            long id = _current.Id;
            _current = null;
            LoadCategories(id);
        }

        private void CatUp_Click(object sender, RoutedEventArgs e) => Move(-1);
        private void CatDown_Click(object sender, RoutedEventArgs e) => Move(1);
    }
}
