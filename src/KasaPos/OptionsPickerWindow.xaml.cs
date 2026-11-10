using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using KasaPos.Data;

namespace KasaPos
{
    public partial class OptionsPickerWindow : Window
    {
        private readonly decimal _basePrice;
        private readonly List<OptionGroup> _groups;
        private readonly Dictionary<ToggleButton, (OptionGroup g, ProductOption o)> _map = new Dictionary<ToggleButton, (OptionGroup, ProductOption)>();

        public string OptionsText { get; private set; }
        public decimal OptionsPrice { get; private set; }

        public OptionsPickerWindow(Product p, decimal basePrice, List<OptionGroup> groups)
        {
            InitializeComponent();
            _basePrice = basePrice;
            _groups = groups;
            TxtProduct.Text = p.Name;
            var style = (Style)FindResource("OptToggle");
            foreach (var g in groups)
            {
                PnlGroups.Children.Add(new TextBlock
                {
                    Text = g.Name + "  ·  " + g.RuleText,
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(5, 12, 5, 2),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextMain")
                });
                var wrap = new WrapPanel();
                foreach (var o in g.Options)
                {
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = o.Name, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
                    if (o.Price != 0)
                        sp.Children.Add(new TextBlock { Text = (o.Price > 0 ? "+" : "") + Money.FmtTl(o.Price), FontSize = 13, FontWeight = FontWeights.Normal, TextAlignment = TextAlignment.Center, Opacity = 0.85 });
                    var t = new ToggleButton { Content = sp, Style = style };
                    _map[t] = (g, o);
                    t.Checked += Toggle_Changed;
                    t.Unchecked += Toggle_Changed;
                    wrap.Children.Add(t);
                }
                PnlGroups.Children.Add(wrap);
                // Tek seçimli zorunlu grupta ilk seçeneği hazır işaretle
                if (g.MinSelect >= 1 && g.MaxSelect == 1)
                {
                    var first = _map.First(k => k.Value.g == g && k.Value.o == g.Options[0]).Key;
                    first.IsChecked = true;
                }
            }
            UpdatePrice();
        }

        private bool _updating;

        private void Toggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_updating) return;
            var t = (ToggleButton)sender;
            var (g, _) = _map[t];
            if (t.IsChecked == true && g.MaxSelect == 1)
            {
                _updating = true;
                foreach (var kv in _map.Where(k => k.Value.g == g && k.Key != t)) kv.Key.IsChecked = false;
                _updating = false;
            }
            else if (t.IsChecked == true && g.MaxSelect > 1)
            {
                int count = _map.Count(k => k.Value.g == g && k.Key.IsChecked == true);
                if (count > g.MaxSelect)
                {
                    _updating = true;
                    t.IsChecked = false;
                    _updating = false;
                    TxtError.Text = g.Name + " için en fazla " + g.MaxSelect + " seçim yapılabilir.";
                }
            }
            UpdatePrice();
        }

        private IEnumerable<(OptionGroup g, ProductOption o)> Selected =>
            _map.Where(k => k.Key.IsChecked == true).Select(k => k.Value);

        private void UpdatePrice()
        {
            OptionsPrice = Selected.Sum(x => x.o.Price);
            TxtPrice.Text = Money.FmtTl(_basePrice + OptionsPrice);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            foreach (var g in _groups)
            {
                int c = Selected.Count(x => x.g == g);
                if (c < g.MinSelect)
                {
                    TxtError.Text = g.Name + " için seçim yapmalısın.";
                    return;
                }
            }
            // Grup sırasına göre metin
            var parts = new List<string>();
            foreach (var g in _groups)
                foreach (var o in g.Options)
                    if (Selected.Any(x => x.o == o))
                        parts.Add(o.Price > 0 ? o.Name + " (+" + Money.Fmt(o.Price) + ")" : o.Name);
            OptionsText = string.Join(" · ", parts);
            DialogResult = true;
        }
    }
}
