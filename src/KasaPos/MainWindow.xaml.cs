using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KasaPos.Data;
using KasaPos.Printing;

namespace KasaPos
{
    public partial class MainWindow : Window
    {
        private Order _order;
        private List<Category> _categories = new List<Category>();
        private List<Product> _products = new List<Product>();
        private long _selectedCategoryId;
        private bool _loading;
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };

        public MainWindow()
        {
            InitializeComponent();
            Title = AppInfo.Name + " v" + AppInfo.Version;
            ApplyUser();
            ReloadMenu();
            NewOrder();
            RefreshOpenOrders();
            _timer.Tick += (s, e) => UpdateHeader();
            _timer.Start();
        }

        private User Me => App.CurrentUser;

        private void ApplyUser()
        {
            bool admin = Me != null && Me.IsAdmin;
            BtnMenu.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
            BtnSettings.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
            BtnReports.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
            UpdateHeader();
        }

        private void UpdateHeader()
        {
            TxtBiz.Text = Settings.Get("biz_name", AppInfo.Name);
            TxtUser.Text = Me == null ? "" : "Kullanıcı: " + Me.Name;
            var day = DateTime.ParseExact(Settings.BusinessDay(DateTime.Now), "yyyy-MM-dd", null);
            TxtDay.Text = "İş günü: " + day.ToString("dd.MM.yyyy dddd", Money.Tr) + "   " + DateTime.Now.ToString("HH:mm");
        }

        // ---------------- Menü ----------------

        public void ReloadMenu()
        {
            _categories = MenuRepo.Categories();
            _products = MenuRepo.Products();
            PnlCategories.Children.Clear();
            foreach (var c in _categories)
            {
                var b = new Button { Content = c.Name, Tag = c.Id, MinWidth = 110, MinHeight = 46, FontWeight = FontWeights.SemiBold };
                b.Click += (s, e) => { _selectedCategoryId = (long)((Button)s).Tag; ShowProducts(); };
                PnlCategories.Children.Add(b);
            }
            if (!_categories.Any(c => c.Id == _selectedCategoryId))
                _selectedCategoryId = _categories.Count > 0 ? _categories[0].Id : 0;
            ShowProducts();
        }

        private void ShowProducts()
        {
            foreach (Button b in PnlCategories.Children.OfType<Button>())
            {
                bool sel = (long)b.Tag == _selectedCategoryId;
                b.Background = sel ? (System.Windows.Media.Brush)FindResource("Accent") : System.Windows.Media.Brushes.White;
                b.Foreground = sel ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;
            }
            PnlProducts.Children.Clear();
            string type = _order == null ? OrderTypes.GelAl : _order.OrderType;
            foreach (var p in _products.Where(x => x.CategoryId == _selectedCategoryId))
            {
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = p.Name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 15, FontWeight = FontWeights.SemiBold });
                sp.Children.Add(new TextBlock { Text = Money.FmtTl(p.PriceFor(type)), TextAlignment = TextAlignment.Center, FontSize = 13, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 4, 0, 0) });
                var b = new Button { Content = sp, Tag = p, Width = 150, Height = 90, Margin = new Thickness(4) };
                b.Click += (s, e) => AddProduct((Product)((Button)s).Tag);
                PnlProducts.Children.Add(b);
            }
            if (_categories.Count == 0)
                PnlProducts.Children.Add(new TextBlock { Text = "Menü boş. Üstteki 'Menü' düğmesinden kategori ve ürün ekle.", FontSize = 16, Margin = new Thickness(10) });
        }

        // ---------------- Sipariş ----------------

        private void NewOrder()
        {
            _order = new Order { OrderType = OrderTypes.GelAl, UserId = Me?.Id ?? 0, UserName = Me?.Name };
            LoadOrderIntoUi();
        }

        private void LoadOrderIntoUi()
        {
            _loading = true;
            RbGelAl.IsChecked = _order.OrderType == OrderTypes.GelAl;
            RbMasa.IsChecked = _order.OrderType == OrderTypes.Masa;
            RbPaket.IsChecked = _order.OrderType == OrderTypes.Paket;
            TxtTable.Text = _order.TableNo ?? "";
            TxtPhone.Text = _order.CustomerPhone ?? "";
            TxtCustName.Text = _order.CustomerName ?? "";
            TxtCustAddress.Text = _order.CustomerAddress ?? "";
            _loading = false;
            UpdateTypePanels();
            RefreshCart();
            ShowProducts();
        }

        private void UpdateTypePanels()
        {
            PnlMasa.Visibility = _order.OrderType == OrderTypes.Masa ? Visibility.Visible : Visibility.Collapsed;
            PnlPaket.Visibility = _order.OrderType == OrderTypes.Paket ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Type_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || _order == null) return;
            string t = RbMasa.IsChecked == true ? OrderTypes.Masa : RbPaket.IsChecked == true ? OrderTypes.Paket : OrderTypes.GelAl;
            if (t == _order.OrderType) return;
            _order.OrderType = t;
            // Fiyatları yeni sipariş türüne göre güncelle
            foreach (var it in _order.Items)
            {
                var p = _products.FirstOrDefault(x => x.Id == it.ProductId);
                if (p != null) it.UnitPrice = p.PriceFor(t);
            }
            UpdateTypePanels();
            RefreshCart();
            ShowProducts();
            if (t == OrderTypes.Masa) TxtTable.Focus();
            if (t == OrderTypes.Paket) TxtPhone.Focus();
        }

        private void AddProduct(Product p)
        {
            var existing = _order.Items.FirstOrDefault(i => i.ProductId == p.Id && string.IsNullOrEmpty(i.Note));
            if (existing != null) existing.Qty += 1;
            else _order.Items.Add(new OrderItem { ProductId = p.Id, Name = p.Name, Qty = 1, UnitPrice = p.PriceFor(_order.OrderType) });
            RefreshCart();
            LstCart.SelectedItem = existing ?? _order.Items.Last();
            LstCart.ScrollIntoView(LstCart.SelectedItem);
        }

        private void RefreshCart()
        {
            var sel = LstCart.SelectedItem;
            LstCart.ItemsSource = null;
            LstCart.ItemsSource = _order.Items;
            if (sel != null && _order.Items.Contains(sel)) LstCart.SelectedItem = sel;
            TxtSubtotal.Text = Money.FmtTl(_order.Subtotal);
            TxtDiscount.Text = _order.Discount > 0 ? "-" + Money.FmtTl(_order.Discount) : "-";
            TxtTotal.Text = Money.FmtTl(_order.Total);
            TxtOrderTitle.Text = _order.Id == 0 ? "Yeni Sipariş" : "Sipariş #" + _order.Id;
            TxtOrderNote.Text = string.IsNullOrWhiteSpace(_order.Note) ? "" : "Not: " + _order.Note;
        }

        private OrderItem SelectedItem
        {
            get
            {
                var it = LstCart.SelectedItem as OrderItem;
                if (it == null && _order.Items.Count > 0) it = _order.Items.Last();
                return it;
            }
        }

        private void BtnPlus_Click(object sender, RoutedEventArgs e)
        {
            var it = SelectedItem;
            if (it == null) return;
            it.Qty += 1;
            RefreshCart();
        }

        private void BtnMinus_Click(object sender, RoutedEventArgs e)
        {
            var it = SelectedItem;
            if (it == null) return;
            it.Qty -= 1;
            if (it.Qty <= 0) _order.Items.Remove(it);
            RefreshCart();
        }

        private void BtnQty_Click(object sender, RoutedEventArgs e)
        {
            var it = SelectedItem;
            if (it == null) return;
            var s = InputDialog.Ask(this, "Adet", it.Name + " için adet (ör. 2 veya 1,5):", it.QtyText);
            decimal q;
            if (s == null || !Money.TryParse(s, out q)) return;
            if (q <= 0) _order.Items.Remove(it); else it.Qty = q;
            RefreshCart();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var it = LstCart.SelectedItem as OrderItem;
            if (it == null) return;
            _order.Items.Remove(it);
            RefreshCart();
        }

        private void BtnItemNote_Click(object sender, RoutedEventArgs e)
        {
            var it = SelectedItem;
            if (it == null) return;
            var s = InputDialog.Ask(this, "Ürün Notu", it.Name + " için not (ör. acısız, soğansız):", it.Note);
            if (s == null) return;
            // Tek adetten fazlaysa notu bir adet için ayır
            if (it.Qty > 1 && string.IsNullOrEmpty(it.Note) && !string.IsNullOrWhiteSpace(s))
            {
                it.Qty -= 1;
                var n = new OrderItem { ProductId = it.ProductId, Name = it.Name, Qty = 1, UnitPrice = it.UnitPrice, Note = s.Trim() };
                _order.Items.Insert(_order.Items.IndexOf(it) + 1, n);
                RefreshCart();
                LstCart.SelectedItem = n;
                return;
            }
            it.Note = s.Trim();
            RefreshCart();
        }

        private void BtnOrderNote_Click(object sender, RoutedEventArgs e)
        {
            var s = InputDialog.Ask(this, "Sipariş Notu", "Sipariş notu:", _order.Note);
            if (s == null) return;
            _order.Note = s.Trim();
            RefreshCart();
        }

        private void BtnDiscount_Click(object sender, RoutedEventArgs e)
        {
            var s = InputDialog.Ask(this, "İndirim", "İndirim tutarı (₺) veya yüzde (ör. %10):", _order.Discount > 0 ? Money.Fmt(_order.Discount) : "");
            if (s == null) return;
            s = s.Trim();
            decimal v;
            if (s.StartsWith("%"))
            {
                if (!Money.TryParse(s.Substring(1), out v)) return;
                _order.Discount = Math.Round(_order.Subtotal * v / 100m, 2);
            }
            else if (s == "") _order.Discount = 0;
            else
            {
                if (!Money.TryParse(s, out v)) return;
                _order.Discount = Math.Min(v, _order.Subtotal);
            }
            RefreshCart();
        }

        private void TxtPhone_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) FindCustomer();
        }

        private void BtnFindCustomer_Click(object sender, RoutedEventArgs e) => FindCustomer();

        private void FindCustomer()
        {
            var c = CustomerRepo.ByPhone(TxtPhone.Text);
            if (c == null)
            {
                TxtPhone.Text = CustomerRepo.NormalizePhone(TxtPhone.Text);
                TxtCustName.Focus();
                return;
            }
            TxtPhone.Text = c.Phone;
            TxtCustName.Text = c.Name;
            TxtCustAddress.Text = c.Address;
        }

        /// <summary>Ekrandaki bilgileri siparişe aktarır, eksik varsa uyarır.</summary>
        private bool CollectAndValidate()
        {
            if (_order.Items.Count == 0)
            {
                MessageBox.Show("Sepet boş.", Title);
                return false;
            }
            _order.TableNo = null;
            _order.CustomerId = null;
            if (_order.OrderType == OrderTypes.Masa)
            {
                var t = TxtTable.Text.Trim();
                if (t == "")
                {
                    MessageBox.Show("Masa numarasını gir.", Title);
                    TxtTable.Focus();
                    return false;
                }
                if (OrderRepo.TableIsBusy(t, _order.Id))
                {
                    MessageBox.Show("Masa " + t + " için zaten açık bir sipariş var. Soldaki listeden o siparişi aç.", Title);
                    return false;
                }
                _order.TableNo = t;
            }
            if (_order.OrderType == OrderTypes.Paket)
            {
                var phone = CustomerRepo.NormalizePhone(TxtPhone.Text);
                if (phone == "" && TxtCustAddress.Text.Trim() == "")
                {
                    MessageBox.Show("Paket sipariş için telefon veya adres gir.", Title);
                    TxtPhone.Focus();
                    return false;
                }
                _order.CustomerPhone = phone;
                _order.CustomerName = TxtCustName.Text.Trim();
                _order.CustomerAddress = TxtCustAddress.Text.Trim();
                if (phone != "") _order.CustomerId = CustomerRepo.Upsert(phone, _order.CustomerName, _order.CustomerAddress);
            }
            else
            {
                _order.CustomerPhone = null;
                _order.CustomerName = null;
                _order.CustomerAddress = null;
            }
            if (_order.UserId == 0 && Me != null)
            {
                _order.UserId = Me.Id;
                _order.UserName = Me.Name;
            }
            return true;
        }

        private void SendToKitchenIfEnabled()
        {
            if (!Settings.GetBool("kitchen_auto", false)) return;
            var err = Printer.PrintKitchen(_order);
            if (err != null) MessageBox.Show("Mutfak fişi yazdırılamadı: " + err, Title);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!CollectAndValidate()) return;
            _order.Status = "OPEN";
            OrderRepo.Save(_order);
            SendToKitchenIfEnabled();
            OrderRepo.Save(_order); // yazdırılan miktarları kaydet
            NewOrder();
            RefreshOpenOrders();
        }

        private void BtnPrintBill_Click(object sender, RoutedEventArgs e)
        {
            if (_order.Items.Count == 0) return;
            var err = Printer.PrintReceipt(_order);
            if (err != null) MessageBox.Show("Yazdırılamadı: " + err, Title);
        }

        private void BtnPay_Click(object sender, RoutedEventArgs e)
        {
            if (!CollectAndValidate()) return;
            var dlg = new PaymentWindow(_order.Total) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            _order.Payments = dlg.Payments;
            _order.Status = "PAID";
            _order.PaidAt = DateTime.Now;
            _order.BusinessDay = Settings.BusinessDay(_order.PaidAt.Value);
            OrderRepo.Save(_order);
            SendToKitchenIfEnabled();
            OrderRepo.Save(_order);

            if (Settings.GetBool("receipt_auto", false) || dlg.PrintReceipt)
            {
                var err = Printer.PrintReceipt(_order, dlg.Change);
                if (err != null) MessageBox.Show("Fiş yazdırılamadı: " + err, Title);
            }
            NewOrder();
            RefreshOpenOrders();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_order.Id == 0)
            {
                if (_order.Items.Count > 0 &&
                    MessageBox.Show("Sepet temizlensin mi?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                NewOrder();
                return;
            }
            if (MessageBox.Show("Sipariş #" + _order.Id + " iptal edilsin mi?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            OrderRepo.SetStatus(_order.Id, "CANCELLED");
            NewOrder();
            RefreshOpenOrders();
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            NewOrder();
            LstOpen.SelectedItem = null;
        }

        private void RefreshOpenOrders()
        {
            _loading = true;
            LstOpen.ItemsSource = OrderRepo.Open();
            LstOpen.SelectedItem = null;
            _loading = false;
        }

        private void LstOpen_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var o = LstOpen.SelectedItem as Order;
            if (o == null) return;
            _order = OrderRepo.Get(o.Id);
            LoadOrderIntoUi();
        }

        // ---------------- Üst menü ----------------

        private void BtnMenu_Click(object sender, RoutedEventArgs e)
        {
            new MenuWindow { Owner = this }.ShowDialog();
            ReloadMenu();
        }

        private void BtnCustomers_Click(object sender, RoutedEventArgs e)
        {
            new CustomersWindow { Owner = this }.ShowDialog();
        }

        private void BtnExpenses_Click(object sender, RoutedEventArgs e)
        {
            new ExpensesWindow { Owner = this }.ShowDialog();
        }

        private void BtnReports_Click(object sender, RoutedEventArgs e)
        {
            new ReportWindow { Owner = this }.ShowDialog();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow { Owner = this }.ShowDialog();
            UpdateHeader();
        }

        private void BtnLock_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            var login = new LoginWindow();
            if (login.ShowDialog() == true)
            {
                App.CurrentUser = login.User;
                ApplyUser();
                if (_order.Id == 0 && _order.Items.Count == 0) NewOrder();
                Show();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
