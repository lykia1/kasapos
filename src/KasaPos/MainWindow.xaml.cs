using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        private string _selectedArea = "";
        private bool _loading;
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };

        public MainWindow()
        {
            InitializeComponent();
            Title = AppInfo.Name + " v" + AppInfo.Version;
            ApplyUser();
            ReloadMenu();
            NewOrder(OrderTypes.GelAl);
            RefreshOpenOrders();
            ShowHome();
            Loaded += (s, e) => Updater.CheckOnStartup(this);
            CallerId.Call += OnIncomingCall;
            var cidErr = CallerId.Start();
            if (cidErr != null) Loaded += (s, e) => MessageBox.Show(cidErr, AppInfo.Name);
            _timer.Tick += (s, e) =>
            {
                UpdateHeader();
                if (ViewSale.Visibility == Visibility.Visible && ViewTables.Visibility == Visibility.Visible) ShowTables();
                if (ViewHome.Visibility == Visibility.Visible) RefreshOpenOrders();
            };
            _timer.Start();
        }

        private User Me => App.CurrentUser;
        private Brush Res(string key) => (Brush)FindResource(key);

        private void ApplyUser()
        {
            bool admin = Me != null && Me.IsAdmin;
            var vis = admin ? Visibility.Visible : Visibility.Collapsed;
            BtnMenu.Visibility = vis;
            BtnTables.Visibility = vis;
            BtnSettings.Visibility = vis;
            BtnReports.Visibility = vis;
            BtnStock.Visibility = vis;
            UpdateHeader();
        }

        private void UpdateHeader()
        {
            var biz = Settings.Get("biz_name", AppInfo.Name);
            TxtBiz.Text = biz;
            TxtLogo.Text = string.IsNullOrEmpty(biz) ? "K" : biz.Substring(0, 1).ToUpper(Money.Tr);
            TxtUser.Text = Me == null ? "" : "👤 " + Me.Name + (Me.IsAdmin ? " (Yönetici)" : "");
            var day = DateTime.ParseExact(Settings.BusinessDay(DateTime.Now), "yyyy-MM-dd", null);
            TxtDay.Text = "İş günü: " + day.ToString("d MMMM dddd", Money.Tr);
            TxtClock.Text = DateTime.Now.ToString("HH:mm");
            var ls = Licensing.State;
            BtnLicense.Visibility = ls == LicenseState.Licensed ? Visibility.Collapsed : Visibility.Visible;
            BtnLicense.Content = ls == LicenseState.Trial ? "Deneme: " + Licensing.TrialDaysLeft + " gün kaldı" : "Lisans gerekli";
        }

        private void BtnLicense_Click(object sender, RoutedEventArgs e)
        {
            new LicenseWindow { Owner = this }.ShowDialog();
            UpdateHeader();
        }

        /// <summary>Deneme süresi bittiyse satış yapılmasın.</summary>
        private bool LicenseOk()
        {
            if (Licensing.State != LicenseState.Expired) return true;
            MessageBox.Show("Deneme süresi doldu. Satışa devam etmek için lisans anahtarını gir.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            new LicenseWindow { Owner = this }.ShowDialog();
            UpdateHeader();
            return Licensing.State != LicenseState.Expired;
        }

        // ================= Menü / ürünler =================

        public void ReloadMenu()
        {
            _categories = MenuRepo.Categories();
            _products = MenuRepo.Products();
            if (!_categories.Any(c => c.Id == _selectedCategoryId))
                _selectedCategoryId = _categories.Count > 0 ? _categories[0].Id : 0;
            BuildCategories();
            ShowProducts();
        }

        private void BuildCategories()
        {
            PnlCategories.Children.Clear();
            bool searching = TxtSearch.Text.Trim().Length > 0;
            foreach (var c in _categories)
            {
                bool sel = !searching && c.Id == _selectedCategoryId;
                var color = Palette.Brush(c.Color, Res("Accent"));
                var b = new Button
                {
                    Content = new TextBlock { Text = c.Name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                    Tag = c.Id,
                    MinHeight = 62,
                    Margin = new Thickness(0, 0, 0, 6),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Background = color,
                    Foreground = Brushes.White,
                    BorderBrush = sel ? Brushes.White : color,
                    BorderThickness = new Thickness(sel ? 4 : 0),
                    Opacity = sel || searching ? 1.0 : 0.82
                };
                b.Click += (s, e) =>
                {
                    _selectedCategoryId = (long)((Button)s).Tag;
                    if (TxtSearch.Text.Length > 0) TxtSearch.Text = "";
                    BuildCategories();
                    ShowProducts();
                    ShowView(false);
                };
                PnlCategories.Children.Add(b);
            }
        }

        private void ShowProducts()
        {
            PnlProducts.Children.Clear();
            string type = _order == null ? OrderTypes.GelAl : _order.OrderType;
            string q = TxtSearch.Text.Trim().ToLower(Money.Tr);
            IEnumerable<Product> list = q.Length > 0
                ? _products.Where(p => (p.Name ?? "").ToLower(Money.Tr).Contains(q))
                : _products.Where(p => p.CategoryId == _selectedCategoryId);

            foreach (var p in list)
            {
                var cat = _categories.FirstOrDefault(c => c.Id == p.CategoryId);
                string colorName = !string.IsNullOrEmpty(p.Color) ? p.Color : cat?.Color;
                var fill = Palette.Brush(colorName, new SolidColorBrush(Color.FromRgb(0x49, 0x50, 0x57)));

                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var img = p.HasImage ? ImageCache.Get(p.Image) : null;
                if (img != null)
                    sp.Children.Add(new Image { Source = img, Height = 70, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 0, 6) });
                sp.Children.Add(new TextBlock
                {
                    Text = p.Name,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                });
                sp.Children.Add(new TextBlock
                {
                    Text = Money.FmtTl(p.PriceFor(type)),
                    TextAlignment = TextAlignment.Center,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xBF)),
                    Margin = new Thickness(0, 6, 0, 0)
                });
                var b = new Button
                {
                    Content = sp,
                    Tag = p,
                    Width = 156,
                    Height = img != null ? 160 : 104,
                    Margin = new Thickness(5),
                    Padding = new Thickness(8),
                    Background = fill,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0)
                };
                b.Click += (s, e) => AddProduct((Product)((Button)s).Tag);
                PnlProducts.Children.Add(b);
            }
            if (_categories.Count == 0)
                PnlProducts.Children.Add(new TextBlock { Text = "Menü boş. Üstteki 'Menü' düğmesinden kategori ve ürün ekle.", FontSize = 16, Margin = new Thickness(10), Foreground = Res("TextSub") });
            else if (PnlProducts.Children.Count == 0 && q.Length > 0)
                PnlProducts.Children.Add(new TextBlock { Text = "\"" + TxtSearch.Text + "\" ile eşleşen ürün yok.", FontSize = 15, Margin = new Thickness(10), Foreground = Res("TextSub") });
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            BtnClearSearch.Visibility = TxtSearch.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildCategories();
            ShowProducts();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
            TxtSearch.Focus();
        }

        // ================= Masa planı =================

        private void ShowView(bool tables)
        {
            ViewTables.Visibility = tables ? Visibility.Visible : Visibility.Collapsed;
            ViewMenu.Visibility = tables ? Visibility.Collapsed : Visibility.Visible;
            if (tables) ShowTables();
        }

        private void ShowTables()
        {
            var tables = TableRepo.All();
            var open = OrderRepo.Open().Where(o => o.OrderType == OrderTypes.Masa).ToList();

            // Bölüm sekmeleri
            var areas = tables.Select(t => t.Area).Distinct().ToList();
            if (_selectedArea != "" && !areas.Contains(_selectedArea)) _selectedArea = "";
            PnlAreas.Children.Clear();
            if (areas.Count > 1)
            {
                foreach (var a in new[] { "" }.Concat(areas))
                {
                    bool sel = a == _selectedArea;
                    var b = new Button
                    {
                        Content = a == "" ? "Tümü" : a,
                        Tag = a,
                        MinWidth = 90,
                        Background = sel ? Res("Dark") : Res("Surface"),
                        Foreground = sel ? Brushes.White : Res("TextMain")
                    };
                    b.Click += (s, e) => { _selectedArea = (string)((Button)s).Tag; ShowTables(); };
                    PnlAreas.Children.Add(b);
                }
            }

            PnlTables.Children.Clear();
            if (tables.Count == 0)
            {
                PnlTables.Children.Add(new TextBlock
                {
                    Text = "Henüz masa tanımlanmamış.\nÜstteki 'Masa Düzeni' düğmesinden masalarını ekle ya da aşağıya masa numarasını yazıp 'Aç'a bas.",
                    FontSize = 15,
                    Margin = new Thickness(10),
                    Foreground = Res("TextSub"),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var t in tables.Where(x => _selectedArea == "" || x.Area == _selectedArea))
            {
                var o = open.FirstOrDefault(x => string.Equals(x.TableNo, t.Name, StringComparison.OrdinalIgnoreCase));
                bool busy = o != null;
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = t.Name, FontSize = 24, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Foreground = Brushes.White });
                if (busy)
                {
                    int mins = (int)Math.Max(0, (DateTime.Now - o.CreatedAt).TotalMinutes);
                    sp.Children.Add(new TextBlock { Text = Money.FmtTl(o.Total), FontSize = 15, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Foreground = Brushes.White });
                    sp.Children.Add(new TextBlock { Text = mins < 60 ? mins + " dk" : (mins / 60) + " sa " + (mins % 60) + " dk", FontSize = 12, TextAlignment = TextAlignment.Center, Foreground = Brushes.White, Opacity = 0.9 });
                }
                else
                {
                    sp.Children.Add(new TextBlock { Text = "Boş", FontSize = 13, TextAlignment = TextAlignment.Center, Foreground = Brushes.White, Opacity = 0.9 });
                }
                if (areas.Count > 1 && _selectedArea == "")
                    sp.Children.Add(new TextBlock { Text = t.Area, FontSize = 11, TextAlignment = TextAlignment.Center, Foreground = Brushes.White, Opacity = 0.8 });

                var b = new Button
                {
                    Content = sp,
                    Tag = t.Name,
                    Width = 136,
                    Height = 112,
                    Margin = new Thickness(6),
                    Background = busy ? new SolidColorBrush(Color.FromRgb(0xE8, 0x59, 0x0C)) : new SolidColorBrush(Color.FromRgb(0x2F, 0x9E, 0x44)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0)
                };
                b.Click += (s, e) => OpenTable((string)((Button)s).Tag);
                PnlTables.Children.Add(b);
            }
        }

        private void OpenTable(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return;
            tableName = tableName.Trim();
            var existing = OrderRepo.Open().FirstOrDefault(o => o.OrderType == OrderTypes.Masa &&
                string.Equals(o.TableNo, tableName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _order = OrderRepo.Get(existing.Id);
            }
            else
            {
                _order = new Order { OrderType = OrderTypes.Masa, TableNo = tableName, UserId = Me?.Id ?? 0, UserName = Me?.Name };
            }
            LoadOrderIntoUi();
            ShowView(false);
        }

        private void TxtTable_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnOpenManualTable_Click(sender, e);
        }

        private void BtnOpenManualTable_Click(object sender, RoutedEventArgs e)
        {
            var t = TxtTable.Text.Trim();
            if (t == "") return;
            TxtTable.Text = "";
            OpenTable(t);
        }

        private void BtnTablePlan_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmLeaveUnsaved()) return;
            NewOrder(OrderTypes.Masa);
        }

        private bool ConfirmLeaveUnsaved()
        {
            if (_order.Id == 0 && _order.Items.Count > 0)
                return MessageBox.Show("Kaydedilmemiş sepet silinecek. Devam edilsin mi?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            return true;
        }

        // ================= Sipariş =================

        private void NewOrder(string type)
        {
            _order = new Order { OrderType = type, UserId = Me?.Id ?? 0, UserName = Me?.Name };
            LoadOrderIntoUi();
            ShowView(type == OrderTypes.Masa);
            LstOpen.SelectedItem = null;
        }

        private void LoadOrderIntoUi()
        {
            _loading = true;
            RbGelAl.IsChecked = _order.OrderType == OrderTypes.GelAl;
            RbMasa.IsChecked = _order.OrderType == OrderTypes.Masa;
            RbPaket.IsChecked = _order.OrderType == OrderTypes.Paket;
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
            PnlPaket.Visibility = _order.OrderType == OrderTypes.Paket ? Visibility.Visible : Visibility.Collapsed;
            BtnTablePlan.Visibility = _order.OrderType == OrderTypes.Masa ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Type_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || _order == null) return;
            string t = RbMasa.IsChecked == true ? OrderTypes.Masa : RbPaket.IsChecked == true ? OrderTypes.Paket : OrderTypes.GelAl;
            if (t == _order.OrderType) return;

            // Boş yeni siparişte Masa seçilirse masa planını aç
            if (t == OrderTypes.Masa && _order.Id == 0 && _order.Items.Count == 0)
            {
                NewOrder(OrderTypes.Masa);
                return;
            }

            _order.OrderType = t;
            if (t != OrderTypes.Masa) _order.TableNo = null;
            foreach (var it in _order.Items)
            {
                var p = _products.FirstOrDefault(x => x.Id == it.ProductId);
                if (p != null) it.UnitPrice = p.PriceFor(t);
            }
            UpdateTypePanels();
            RefreshCart();
            ShowProducts();
            if (t == OrderTypes.Masa && string.IsNullOrEmpty(_order.TableNo))
            {
                // Sepetli siparişi masaya almak için masa seçtir
                var name = InputDialog.Ask(this, "Masa", "Bu sipariş hangi masaya yazılsın?");
                if (string.IsNullOrWhiteSpace(name))
                {
                    _order.OrderType = OrderTypes.GelAl;
                    LoadOrderIntoUi();
                    return;
                }
                _order.TableNo = name.Trim();
                RefreshCart();
            }
            ShowView(false);
            if (t == OrderTypes.Paket) TxtPhone.Focus();
        }

        private void AddProduct(Product p)
        {
            if (_order.OrderType == OrderTypes.Masa && string.IsNullOrEmpty(_order.TableNo))
            {
                ShowView(true);
                return;
            }
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
            var visible = _order.Items.Where(i => i.Qty > 0).ToList();
            LstCart.ItemsSource = visible;
            if (sel != null && visible.Contains(sel)) LstCart.SelectedItem = sel;
            TxtEmptyCart.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            bool masaSaved = _order.OrderType == OrderTypes.Masa && _order.Id > 0;
            BtnMoveTable.IsEnabled = masaSaved;
            BtnMerge.IsEnabled = masaSaved;
            BtnSplit.IsEnabled = visible.Count > 0;
            BtnCourier.IsEnabled = _order.OrderType == OrderTypes.Paket && visible.Count > 0;
            TxtSubtotal.Text = Money.FmtTl(_order.Subtotal);
            TxtDiscount.Text = _order.Discount > 0 ? "-" + Money.FmtTl(_order.Discount) : "-";
            TxtTotal.Text = Money.FmtTl(_order.Total);

            string title;
            if (_order.OrderType == OrderTypes.Masa)
                title = string.IsNullOrEmpty(_order.TableNo) ? "Masa seç" : "Masa " + _order.TableNo;
            else
                title = _order.Id == 0 ? "Yeni Sipariş" : "Sipariş #" + _order.Id;
            TxtOrderTitle.Text = title;
            TxtTypeBadge.Text = OrderTypes.Label(_order.OrderType) + (_order.Id > 0 ? "  #" + _order.Id : "");
            BdType.Background = new SolidColorBrush(_order.OrderType == OrderTypes.Masa ? Color.FromRgb(0x2F, 0x9E, 0x44)
                : _order.OrderType == OrderTypes.Paket ? Color.FromRgb(0x19, 0x71, 0xC2) : Color.FromRgb(0xF7, 0x67, 0x07));
            TxtOrderNote.Text = string.IsNullOrWhiteSpace(_order.Note) ? "" : "Not: " + _order.Note;
        }

        private OrderItem SelectedItem
        {
            get
            {
                var it = LstCart.SelectedItem as OrderItem;
                if (it == null) it = _order.Items.LastOrDefault(i => i.Qty > 0);
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
            if (it.Qty <= 0) RemoveItem(it);
            RefreshCart();
        }

        private void BtnQty_Click(object sender, RoutedEventArgs e)
        {
            var it = SelectedItem;
            if (it == null) return;
            var s = InputDialog.Ask(this, "Adet", it.Name + " için adet (ör. 2 veya 1,5):", it.QtyText);
            decimal q;
            if (s == null || !Money.TryParse(s, out q)) return;
            if (q <= 0) RemoveItem(it); else it.Qty = q;
            RefreshCart();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var it = LstCart.SelectedItem as OrderItem;
            if (it == null) return;
            RemoveItem(it);
            RefreshCart();
        }

        /// <summary>Mutfağa gitmiş ürün silinirse iptal bilgisi gidebilsin diye adedi 0 yapılır.</summary>
        private void RemoveItem(OrderItem it)
        {
            if (it.PrintedQty > 0) it.Qty = 0;
            else _order.Items.Remove(it);
        }

        private void BtnItemNote_Click(object sender, RoutedEventArgs e)
        {
            var it = SelectedItem;
            if (it == null) return;
            var s = InputDialog.Ask(this, "Ürün Notu", it.Name + " için not (ör. acısız, soğansız):", it.Note);
            if (s == null) return;
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

        private bool CollectAndValidate()
        {
            if (!LicenseOk()) return false;
            if (!_order.Items.Any(i => i.Qty > 0))
            {
                MessageBox.Show("Sepet boş.", Title);
                return false;
            }
            _order.CustomerId = null;
            if (_order.OrderType == OrderTypes.Masa)
            {
                var t = (_order.TableNo ?? "").Trim();
                if (t == "")
                {
                    MessageBox.Show("Önce masa planından bir masa seç.", Title);
                    ShowView(true);
                    return false;
                }
                if (OrderRepo.TableIsBusy(t, _order.Id))
                {
                    MessageBox.Show("Masa " + t + " için zaten açık bir sipariş var. Masa planından o masayı aç.", Title);
                    return false;
                }
                _order.TableNo = t;
            }
            else
            {
                _order.TableNo = null;
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
            var err = Kitchen.Send(_order);
            if (err != null) MessageBox.Show("Mutfak fişi yazdırılamadı: " + err, Title);
            _order.Items.RemoveAll(i => i.Qty <= 0);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!CollectAndValidate()) return;
            string type = _order.OrderType;
            _order.Status = "OPEN";
            OrderRepo.Save(_order);
            SendToKitchenIfEnabled();
            OrderRepo.Save(_order);
            NewOrder(type);
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
            CompletePayment(dlg.Payments, dlg.Change, dlg.PrintReceipt);
        }

        /// <summary>Tek dokunuşla tamamını nakit veya kartla öder.</summary>
        private void QuickPay(bool cash)
        {
            if (!CollectAndValidate()) return;
            var types = PaymentRepo.Types();
            PaymentType t = cash
                ? types.FirstOrDefault(x => x.IsCash)
                : types.FirstOrDefault(x => !x.IsCash && (x.Name ?? "").ToLower(Money.Tr).Contains("kart")) ?? types.FirstOrDefault(x => !x.IsCash);
            if (t == null)
            {
                MessageBox.Show("Uygun ödeme türü bulunamadı. Ayarlar > Ödeme Türleri'ni kontrol et.", Title);
                return;
            }
            var pays = new List<Payment>();
            if (_order.Total > 0) pays.Add(new Payment { PaymentTypeId = t.Id, Name = t.Name, Amount = _order.Total });
            CompletePayment(pays, 0, false);
        }

        private void BtnPayCash_Click(object sender, RoutedEventArgs e) => QuickPay(true);
        private void BtnPayCard_Click(object sender, RoutedEventArgs e) => QuickPay(false);

        private void CompletePayment(List<Payment> payments, decimal change, bool printReceipt)
        {
            string type = _order.OrderType;
            _order.Payments = payments;
            _order.Status = "PAID";
            _order.PaidAt = DateTime.Now;
            _order.BusinessDay = Settings.BusinessDay(_order.PaidAt.Value);
            OrderRepo.Save(_order);
            SendToKitchenIfEnabled();
            OrderRepo.Save(_order);
            StockRepo.ConsumeForOrder(_order, Me?.Name);

            if (Settings.GetBool("receipt_auto", false) || printReceipt)
            {
                var err = Printer.PrintReceipt(_order, change);
                if (err != null) MessageBox.Show("Fiş yazdırılamadı: " + err, Title);
            }
            NewOrder(type);
            RefreshOpenOrders();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            string type = _order.OrderType;
            if (_order.Id == 0)
            {
                if (_order.Items.Count > 0 &&
                    MessageBox.Show("Sepet temizlensin mi?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                NewOrder(type);
                return;
            }
            if (MessageBox.Show("Sipariş #" + _order.Id + " iptal edilsin mi?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            OrderRepo.SetStatus(_order.Id, "CANCELLED");
            NewOrder(type);
            RefreshOpenOrders();
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmLeaveUnsaved()) return;
            NewOrder(OrderTypes.GelAl);
        }

        private void RefreshOpenOrders()
        {
            _loading = true;
            var list = OrderRepo.Open();
            LstOpen.ItemsSource = list;
            LstOpen.SelectedItem = null;
            TxtOpenCount.Text = list.Count + " adet";
            _loading = false;
            RefreshStats(list);
            if (ViewTables.Visibility == Visibility.Visible) ShowTables();
        }

        private void LstOpen_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var o = LstOpen.SelectedItem as Order;
            if (o == null) return;
            if (!ConfirmLeaveUnsaved())
            {
                _loading = true;
                LstOpen.SelectedItem = null;
                _loading = false;
                return;
            }
            _order = OrderRepo.Get(o.Id);
            LoadOrderIntoUi();
            ShowSale();
            ShowView(false);
        }

        // ================= Ana ekran =================

        private void ShowHome()
        {
            ViewHome.Visibility = Visibility.Visible;
            ViewSale.Visibility = Visibility.Collapsed;
            BtnHome.Visibility = Visibility.Collapsed;
            RefreshOpenOrders();
        }

        private void ShowSale()
        {
            ViewHome.Visibility = Visibility.Collapsed;
            ViewSale.Visibility = Visibility.Visible;
            BtnHome.Visibility = Visibility.Visible;
        }

        private void RefreshStats(List<Order> open)
        {
            try
            {
                var day = Settings.BusinessDay(DateTime.Now);
                var sum = Db.Scalar("SELECT SUM(total) FROM orders WHERE status='PAID' AND business_day=@d", ("@d", day));
                var cnt = Db.Scalar("SELECT COUNT(*) FROM orders WHERE status='PAID' AND business_day=@d", ("@d", day));
                TxtStatSales.Text = Money.FmtTl(sum == null ? 0m : Math.Round(Convert.ToDecimal(sum), 2));
                TxtStatCount.Text = cnt == null ? "0" : cnt.ToString();
                TxtStatTables.Text = open.Count(o => o.OrderType == OrderTypes.Masa).ToString();
                TxtStatPaket.Text = open.Count(o => o.OrderType == OrderTypes.Paket && o.DispatchedAt.HasValue).ToString();
                TxtStatStock.Text = StockRepo.LowCount().ToString();
            }
            catch { }
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmLeaveUnsaved()) return;
            NewOrder(_order.OrderType);
            ShowHome();
        }

        private void HomeMasa_Click(object sender, RoutedEventArgs e)
        {
            NewOrder(OrderTypes.Masa);
            ShowSale();
        }

        private void HomePaket_Click(object sender, RoutedEventArgs e)
        {
            NewOrder(OrderTypes.Paket);
            ShowSale();
            TxtPhone.Focus();
        }

        private void HomeGelAl_Click(object sender, RoutedEventArgs e)
        {
            NewOrder(OrderTypes.GelAl);
            ShowSale();
        }

        private void BtnStock_Click(object sender, RoutedEventArgs e)
        {
            new StockWindow { Owner = this }.ShowDialog();
            RefreshOpenOrders();
        }

        private void OnIncomingCall(string phone)
        {
            var popup = new CallerPopup(phone);
            popup.Closed += (s, e) =>
            {
                if (!popup.OpenOrder) return;
                if (!ConfirmLeaveUnsaved()) return;
                if (!IsVisible) return;
                NewOrder(OrderTypes.Paket);
                ShowSale();
                TxtPhone.Text = phone;
                FindCustomer();
                Activate();
                if (string.IsNullOrWhiteSpace(TxtCustName.Text)) TxtCustName.Focus();
            };
            popup.Show();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Programdan çıkılsın mı?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Application.Current.Shutdown();
        }

        // ================= Masa taşı / birleştir / böl / kurye =================

        private void BtnMoveTable_Click(object sender, RoutedEventArgs e)
        {
            if (_order.OrderType != OrderTypes.Masa || _order.Id == 0) return;
            var busy = OrderRepo.Open().Where(o => o.OrderType == OrderTypes.Masa).Select(o => o.TableNo).ToList();
            var free = TableRepo.All().Select(t => t.Name).Where(n => !busy.Any(b => string.Equals(b, n, StringComparison.OrdinalIgnoreCase))).ToList();
            var dlg = new ChoiceDialog("Masa Taşı", "Masa " + _order.TableNo + " hangi masaya taşınsın?", free, "Başka masa:") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string target = dlg.ManualValue ?? free[dlg.SelectedIndex];
            if (OrderRepo.TableIsBusy(target, _order.Id))
            {
                MessageBox.Show("Masa " + target + " dolu. Birleştirmek için 'Birleştir'i kullan.", Title);
                return;
            }
            OrderRepo.MoveTable(_order.Id, target);
            if (Kitchen.ScreenEnabled) KitchenRepo.Add(_order.Id, "Masa " + _order.TableNo + " → " + target, "Masa taşındı");
            _order = OrderRepo.Get(_order.Id);
            LoadOrderIntoUi();
            RefreshOpenOrders();
        }

        private void BtnMerge_Click(object sender, RoutedEventArgs e)
        {
            if (_order.OrderType != OrderTypes.Masa || _order.Id == 0) return;
            var others = OrderRepo.Open().Where(o => o.OrderType == OrderTypes.Masa && o.Id != _order.Id).ToList();
            var dlg = new ChoiceDialog("Masa Birleştir", "Hangi masanın siparişi Masa " + _order.TableNo + " ile birleştirilsin?",
                others.Select(o => "Masa " + o.TableNo + "\n" + Money.FmtTl(o.Total)).ToList()) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.SelectedIndex < 0) return;
            var src = others[dlg.SelectedIndex];
            if (MessageBox.Show("Masa " + src.TableNo + " siparişi Masa " + _order.TableNo + " içine alınacak ve Masa " + src.TableNo + " boşalacak. Onaylıyor musun?",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            // Ekrandaki kaydedilmemiş değişiklikleri önce kaydet
            if (CollectAndValidate()) OrderRepo.Save(_order);
            OrderRepo.Merge(src.Id, _order.Id);
            _order = OrderRepo.Get(_order.Id);
            LoadOrderIntoUi();
            RefreshOpenOrders();
        }

        private void BtnSplit_Click(object sender, RoutedEventArgs e)
        {
            if (!CollectAndValidate()) return;
            var split = new SplitWindow(_order) { Owner = this };
            if (split.ShowDialog() != true) return;
            var sel = split.Selected;
            if (sel.Count == 0) return;

            var part = new Order
            {
                OrderType = _order.OrderType,
                TableNo = _order.TableNo,
                CustomerId = _order.CustomerId,
                CustomerName = _order.CustomerName,
                CustomerPhone = _order.CustomerPhone,
                CustomerAddress = _order.CustomerAddress,
                UserId = Me?.Id ?? _order.UserId,
                UserName = Me?.Name ?? _order.UserName,
                CreatedAt = _order.CreatedAt,
                Note = "Bölünmüş hesap"
            };
            foreach (var kv in sel)
                part.Items.Add(new OrderItem { ProductId = kv.Key.ProductId, Name = kv.Key.Name, Qty = kv.Value, UnitPrice = kv.Key.UnitPrice, PrintedQty = kv.Value, Note = kv.Key.Note });

            var pay = new PaymentWindow(part.Total) { Owner = this };
            if (pay.ShowDialog() != true) return;

            // Önce ana siparişi kaydet (yeni kalemler mutfağa gitsin)
            _order.Status = "OPEN";
            OrderRepo.Save(_order);
            SendToKitchenIfEnabled();

            foreach (var kv in sel)
            {
                kv.Key.Qty -= kv.Value;
                kv.Key.PrintedQty = Math.Max(0, kv.Key.PrintedQty - kv.Value);
            }
            _order.Items.RemoveAll(i => i.Qty <= 0);
            if (_order.Discount > _order.Subtotal) _order.Discount = _order.Subtotal;
            if (_order.Items.Count == 0) _order.Status = "SPLIT";
            OrderRepo.Save(_order);

            part.Payments = pay.Payments;
            part.Status = "PAID";
            part.PaidAt = DateTime.Now;
            part.BusinessDay = Settings.BusinessDay(part.PaidAt.Value);
            OrderRepo.Save(part);
            StockRepo.ConsumeForOrder(part, Me?.Name);
            if (Settings.GetBool("receipt_auto", false) || pay.PrintReceipt)
            {
                var err = Printer.PrintReceipt(part, pay.Change);
                if (err != null) MessageBox.Show("Fiş yazdırılamadı: " + err, Title);
            }

            if (_order.Status == "SPLIT") NewOrder(_order.OrderType);
            else
            {
                _order = OrderRepo.Get(_order.Id);
                LoadOrderIntoUi();
            }
            RefreshOpenOrders();
        }

        private void BtnCourier_Click(object sender, RoutedEventArgs e)
        {
            if (_order.OrderType != OrderTypes.Paket) return;
            var couriers = CourierRepo.All();
            if (couriers.Count == 0)
            {
                MessageBox.Show("Önce Ayarlar > Kuryeler sekmesinden kurye ekle.", Title);
                return;
            }
            if (!CollectAndValidate()) return;
            var dlg = new ChoiceDialog("Kurye", "Paket hangi kuryeye verilsin?", couriers.Select(c => c.Name).ToList()) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.SelectedIndex < 0) return;
            var chosen = couriers[dlg.SelectedIndex];
            _order.CourierId = chosen.Id;
            _order.CourierName = chosen.Name;
            _order.DispatchedAt = DateTime.Now;
            _order.Status = "OPEN";
            OrderRepo.Save(_order);
            SendToKitchenIfEnabled();
            OrderRepo.Save(_order);
            if (Settings.GetBool("courier_print", true))
            {
                var err = Printer.PrintReceipt(_order);
                if (err != null) MessageBox.Show("Paket fişi yazdırılamadı: " + err, Title);
            }
            NewOrder(OrderTypes.Paket);
            RefreshOpenOrders();
        }

        private KitchenWindow _kds;

        private void BtnKds_Click(object sender, RoutedEventArgs e)
        {
            if (_kds == null || !_kds.IsLoaded)
            {
                _kds = new KitchenWindow();
                _kds.Show();
            }
            else _kds.Activate();
        }

        // ================= Üst menü =================

        private void BtnMenu_Click(object sender, RoutedEventArgs e)
        {
            new MenuWindow { Owner = this }.ShowDialog();
            ReloadMenu();
        }

        private void BtnTables_Click(object sender, RoutedEventArgs e)
        {
            new TablesWindow { Owner = this }.ShowDialog();
            if (ViewTables.Visibility == Visibility.Visible) ShowTables();
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
                if (_order.Id == 0 && _order.Items.Count == 0) NewOrder(_order.OrderType);
                ShowHome();
                Show();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            CallerId.Stop();
            Application.Current.Shutdown();
        }
    }
}
