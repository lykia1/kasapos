using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KasaPos.Data;

namespace KasaPos
{
    public partial class StockWindow : Window
    {
        private ObservableCollection<StockItem> _items;
        private ObservableCollection<RecipeLine> _recipe = new ObservableCollection<RecipeLine>();

        public StockWindow()
        {
            InitializeComponent();
            LoadItems();
            CmbProduct.ItemsSource = MenuRepo.Products(false);
            GridMoves.ItemsSource = StockRepo.RecentMoves();
        }

        private void LoadItems()
        {
            _items = new ObservableCollection<StockItem>(StockRepo.Items());
            GridItems.ItemsSource = _items;
            ColRecipeItem.ItemsSource = StockRepo.Items(true);
        }

        private void GridItems_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            var s = e.Row.Item as StockItem;
            e.Row.Background = s != null && s.IsLow ? new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0xE3)) : Brushes.White;
        }

        private bool SaveItems()
        {
            GridItems.CommitEdit(DataGridEditingUnit.Row, true);
            foreach (var s in _items.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
            {
                s.Name = s.Name.Trim();
                StockRepo.SaveItem(s);
            }
            return true;
        }

        private void SaveItems_Click(object sender, RoutedEventArgs e)
        {
            SaveItems();
            LoadItems();
            MessageBox.Show("Stok kartları kaydedildi.", Title);
        }

        private StockItem PickItem(string title)
        {
            SaveItems();
            LoadItems();
            var list = _items.Where(x => x.Active).ToList();
            if (list.Count == 0)
            {
                MessageBox.Show("Önce stok kartı ekleyip kaydet.", Title);
                return null;
            }
            var sel = GridItems.SelectedItem as StockItem;
            if (sel != null && sel.Id > 0) return list.FirstOrDefault(x => x.Id == sel.Id);
            var dlg = new ChoiceDialog(title, "Hangi malzeme?", list.Select(x => x.Name + "\n" + x.QtyText).ToList()) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.SelectedIndex < 0) return null;
            return list[dlg.SelectedIndex];
        }

        private void StockIn_Click(object sender, RoutedEventArgs e)
        {
            var it = PickItem("Stok Girişi");
            if (it == null) return;
            var qs = InputDialog.Ask(this, "Stok Girişi", it.Name + " — gelen miktar (" + it.Unit + "):");
            decimal q;
            if (qs == null || !Money.TryParse(qs, out q) || q <= 0) return;
            var cs = InputDialog.Ask(this, "Stok Girişi", "Birim alış fiyatı ₺ (boş bırakırsan mevcut maliyet kullanılır):", it.Cost > 0 ? Money.Fmt(it.Cost) : "");
            decimal cost;
            decimal? uc = (cs != null && Money.TryParse(cs, out cost) && cost > 0) ? cost : (decimal?)null;
            StockRepo.Move(it.Id, q, "GİRİŞ", "Alım", App.CurrentUser?.Name, null, uc);
            Refresh();
        }

        private void Count_Click(object sender, RoutedEventArgs e)
        {
            var it = PickItem("Sayım");
            if (it == null) return;
            var qs = InputDialog.Ask(this, "Sayım", it.Name + " — sayılan gerçek miktar (" + it.Unit + "). Şu an kayıtta: " + it.QtyText, it.Qty.ToString("0.###", Money.Tr));
            decimal q;
            if (qs == null || !Money.TryParse(qs, out q) || q < 0) return;
            decimal diff = q - it.Qty;
            if (diff == 0) return;
            StockRepo.Move(it.Id, diff, "SAYIM", "Sayım farkı", App.CurrentUser?.Name);
            Refresh();
        }

        private void Waste_Click(object sender, RoutedEventArgs e)
        {
            var it = PickItem("Fire / Zayi");
            if (it == null) return;
            var qs = InputDialog.Ask(this, "Fire / Zayi", it.Name + " — atılan / bozulan miktar (" + it.Unit + "):");
            decimal q;
            if (qs == null || !Money.TryParse(qs, out q) || q <= 0) return;
            var note = InputDialog.Ask(this, "Fire / Zayi", "Açıklama (isteğe bağlı):") ?? "";
            StockRepo.Move(it.Id, -q, "FİRE", note, App.CurrentUser?.Name);
            Refresh();
        }

        private void Refresh()
        {
            LoadItems();
            GridMoves.ItemsSource = StockRepo.RecentMoves();
        }

        private void CmbProduct_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var p = CmbProduct.SelectedItem as Product;
            _recipe = p == null ? new ObservableCollection<RecipeLine>() : new ObservableCollection<RecipeLine>(StockRepo.Recipe(p.Id));
            GridRecipe.ItemsSource = _recipe;
            UpdateRecipeCost();
        }

        private void UpdateRecipeCost()
        {
            var items = StockRepo.Items();
            decimal cost = _recipe.Sum(l => (items.FirstOrDefault(i => i.Id == l.StockItemId)?.Cost ?? 0) * l.Qty);
            var p = CmbProduct.SelectedItem as Product;
            if (p == null) { TxtRecipeCost.Text = ""; return; }
            string margin = p.PriceGelAl > 0 ? "  ·  Kâr oranı (gel al fiyatına göre): %" + Math.Round((p.PriceGelAl - cost) / p.PriceGelAl * 100, 0) : "";
            TxtRecipeCost.Text = "Tahmini maliyet: " + Money.FmtTl(Math.Round(cost, 2)) + margin;
        }

        private void SaveRecipe_Click(object sender, RoutedEventArgs e)
        {
            var p = CmbProduct.SelectedItem as Product;
            if (p == null) return;
            GridRecipe.CommitEdit(DataGridEditingUnit.Row, true);
            StockRepo.SaveRecipe(p.Id, _recipe);
            UpdateRecipeCost();
            MessageBox.Show(p.Name + " reçetesi kaydedildi.", Title);
        }
    }
}
