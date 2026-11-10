using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KasaPos.Data;

namespace KasaPos
{
    public partial class SplitWindow : Window
    {
        private readonly List<OrderItem> _items;
        private readonly Dictionary<OrderItem, decimal> _pick = new Dictionary<OrderItem, decimal>();
        private readonly Dictionary<OrderItem, TextBlock> _labels = new Dictionary<OrderItem, TextBlock>();

        /// <summary>Seçilen kalemler ve ödenecek adetleri.</summary>
        public Dictionary<OrderItem, decimal> Selected => _pick.Where(k => k.Value > 0).ToDictionary(k => k.Key, k => k.Value);

        public SplitWindow(Order order)
        {
            InitializeComponent();
            _items = order.Items.Where(i => i.Qty > 0).ToList();
            foreach (var it in _items)
            {
                _pick[it] = 0;
                var g = new Grid { Margin = new Thickness(6, 4, 6, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = it.QtyText + " x " + it.Name + (string.IsNullOrWhiteSpace(it.Note) ? "" : " (" + it.Note + ")") + "\n" + Money.FmtTl(it.UnitPrice) + " / adet",
                    FontSize = 15,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                g.Children.Add(name);

                var minus = new Button { Content = "−", Width = 54, FontSize = 20, Tag = it };
                minus.Click += (s, e) => Change((OrderItem)((Button)s).Tag, -1);
                Grid.SetColumn(minus, 1);
                g.Children.Add(minus);

                var lbl = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lbl, 2);
                g.Children.Add(lbl);
                _labels[it] = lbl;

                var plus = new Button { Content = "+", Width = 54, FontSize = 20, Tag = it };
                plus.Click += (s, e) => Change((OrderItem)((Button)s).Tag, 1);
                Grid.SetColumn(plus, 3);
                g.Children.Add(plus);

                var all = new Button { Content = "Tümü", Tag = it };
                all.Click += (s, e) => { var x = (OrderItem)((Button)s).Tag; _pick[x] = x.Qty; Refresh(); };
                Grid.SetColumn(all, 4);
                g.Children.Add(all);

                PnlRows.Children.Add(g);
                PnlRows.Children.Add(new Border { Height = 1, Background = (System.Windows.Media.Brush)FindResource("Line") });
            }
            Refresh();
        }

        private void Change(OrderItem it, int dir)
        {
            decimal v = _pick[it] + dir;
            if (v < 0) v = 0;
            if (v > it.Qty) v = it.Qty;
            _pick[it] = v;
            Refresh();
        }

        public decimal Sum => _pick.Sum(k => Math.Round(k.Value * k.Key.UnitPrice, 2));

        private void Refresh()
        {
            foreach (var kv in _labels)
                kv.Value.Text = _pick[kv.Key].ToString("0.##", Money.Tr) + " / " + kv.Key.QtyText;
            TxtSum.Text = "Ödenecek: " + Money.FmtTl(Sum);
            BtnPay.IsEnabled = _pick.Values.Any(v => v > 0);
        }

        private void Pay_Click(object sender, RoutedEventArgs e)
        {
            if (!_pick.Values.Any(v => v > 0)) return;
            DialogResult = true;
        }
    }
}
