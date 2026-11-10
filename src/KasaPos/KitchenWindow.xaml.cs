using System;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KasaPos.Data;

namespace KasaPos
{
    /// <summary>Mutfak ekranı: gelen siparişleri kart olarak gösterir, "HAZIR" ile kapatılır.</summary>
    public partial class KitchenWindow : Window
    {
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        private long _lastMaxId = -1;

        public KitchenWindow()
        {
            InitializeComponent();
            try { KitchenRepo.Cleanup(); } catch { }
            _timer.Tick += (s, e) => Load();
            _timer.Start();
            Closed += (s, e) => _timer.Stop();
            Load();
        }

        private static Brush Hex(string h) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));

        private void Load()
        {
            TxtClock.Text = DateTime.Now.ToString("HH:mm");
            var list = KitchenRepo.Pending();
            TxtCount.Text = list.Count + " bekleyen";

            long maxId = list.Count == 0 ? 0 : list.Max(t => t.Id);
            if (_lastMaxId >= 0 && maxId > _lastMaxId)
            {
                try { SystemSounds.Exclamation.Play(); } catch { }
            }
            _lastMaxId = Math.Max(_lastMaxId, maxId);

            PnlTickets.Children.Clear();
            foreach (var t in list)
            {
                int mins = (int)Math.Max(0, (DateTime.Now - t.CreatedAt).TotalMinutes);
                string head = mins < 10 ? "#2F9E44" : mins < 20 ? "#F08C00" : "#E03131";

                var card = new Border
                {
                    Width = 280,
                    Margin = new Thickness(8),
                    CornerRadius = new CornerRadius(12),
                    Background = Hex("#FFFFFF"),
                    ClipToBounds = true
                };
                var dp = new DockPanel();

                var header = new Border { Background = Hex(head), Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(12, 12, 0, 0) };
                var hdp = new DockPanel();
                var time = new TextBlock { Text = mins + " dk", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold };
                DockPanel.SetDock(time, Dock.Right);
                hdp.Children.Add(time);
                hdp.Children.Add(new TextBlock { Text = t.Title, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
                header.Child = hdp;
                DockPanel.SetDock(header, Dock.Top);
                dp.Children.Add(header);

                var btn = new Button
                {
                    Content = "HAZIR",
                    Tag = t.Id,
                    Style = (Style)FindResource("OkButton"),
                    FontSize = 20,
                    MinHeight = 56,
                    Margin = new Thickness(10)
                };
                btn.Click += (s, e) => { KitchenRepo.Done((long)((Button)s).Tag); Load(); };
                DockPanel.SetDock(btn, Dock.Bottom);
                dp.Children.Add(btn);

                var sub = new TextBlock { Text = t.CreatedAt.ToString("HH:mm") + "  ·  Sipariş #" + t.OrderId, Foreground = Hex("#868E96"), FontSize = 12, Margin = new Thickness(12, 8, 12, 0) };
                DockPanel.SetDock(sub, Dock.Top);
                dp.Children.Add(sub);

                dp.Children.Add(new TextBlock
                {
                    Text = t.Body,
                    FontSize = 19,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Hex("#212529"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(12, 8, 12, 4)
                });
                card.Child = dp;
                PnlTickets.Children.Add(card);
            }
            if (list.Count == 0)
                PnlTickets.Children.Add(new TextBlock { Text = "Bekleyen sipariş yok", Foreground = Hex("#868E96"), FontSize = 22, Margin = new Thickness(20) });
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            KitchenRepo.UndoLast();
            Load();
        }

        private void Full_Click(object sender, RoutedEventArgs e)
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                BtnFull.Content = "Tam Ekran";
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                BtnFull.Content = "Pencere";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
