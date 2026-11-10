using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KasaPos.Data;

namespace KasaPos.Printing
{
    /// <summary>
    /// Windows'a kurulu herhangi bir yazıcıya (USB / ağ fiş yazıcısı dahil) metin tabanlı fiş basar.
    /// Satır önekleri: '#' kalın, '^' ortalı, '!' büyük ve kalın.
    /// </summary>
    public static class Printer
    {
        public static int Width => Math.Max(24, Settings.GetInt("receipt_chars", 32));

        public static List<string> InstalledPrinters()
        {
            try
            {
                using (var server = new LocalPrintServer())
                {
                    return server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                        .Select(q => q.FullName).OrderBy(n => n).ToList();
                }
            }
            catch
            {
                return new List<string>();
            }
        }

        private static PrintQueue FindQueue(string name)
        {
            var server = new LocalPrintServer();
            return server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                .FirstOrDefault(q => string.Equals(q.FullName, name, StringComparison.OrdinalIgnoreCase));
        }

        public static string Line(char ch = '-') => new string(ch, Width);

        public static string LR(string left, string right)
        {
            left = left ?? "";
            right = right ?? "";
            int max = Width - right.Length - 1;
            if (max < 4) max = 4;
            if (left.Length > max) left = left.Substring(0, max);
            return left.PadRight(Width - right.Length) + right;
        }

        public static IEnumerable<string> Wrap(string text)
        {
            text = text ?? "";
            int w = Width;
            while (text.Length > w)
            {
                int cut = text.LastIndexOf(' ', w);
                if (cut <= 0) cut = w;
                yield return text.Substring(0, cut).TrimEnd();
                text = text.Substring(cut).TrimStart();
            }
            if (text.Length > 0) yield return text;
        }

        /// <summary>Yazdırır. Başarılıysa null, değilse hata mesajı döner.</summary>
        public static string Print(string printerName, IList<string> lines, string jobName)
        {
            try
            {
                double pageWidth = Settings.GetInt("receipt_width_px", 280);
                var doc = new FlowDocument
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = Settings.GetInt("receipt_font_size", 10) * 96.0 / 72.0,
                    PagePadding = new Thickness(4, 4, 4, 4),
                    PageWidth = pageWidth,
                    ColumnWidth = pageWidth,
                    PageHeight = Math.Max(300, lines.Count * 22 + 120),
                    TextAlignment = TextAlignment.Left
                };
                foreach (var raw in lines)
                {
                    string line = raw ?? "";
                    bool bold = false, center = false, big = false;
                    while (line.Length > 0 && (line[0] == '#' || line[0] == '^' || line[0] == '!'))
                    {
                        if (line[0] == '#') bold = true;
                        else if (line[0] == '^') center = true;
                        else { big = true; bold = true; }
                        line = line.Substring(1);
                    }
                    var p = new Paragraph(new Run(line.Length == 0 ? " " : line))
                    {
                        Margin = new Thickness(0),
                        TextAlignment = center ? TextAlignment.Center : TextAlignment.Left
                    };
                    if (bold) p.FontWeight = FontWeights.Bold;
                    if (big) p.FontSize = doc.FontSize * 1.5;
                    doc.Blocks.Add(p);
                }

                var dlg = new PrintDialog();
                if (string.IsNullOrWhiteSpace(printerName))
                {
                    if (dlg.ShowDialog() != true) return null;
                }
                else
                {
                    var q = FindQueue(printerName);
                    if (q == null) return "Yazıcı bulunamadı: " + printerName;
                    dlg.PrintQueue = q;
                }
                var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
                dlg.PrintDocument(paginator, jobName);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static List<string> Header()
        {
            var l = new List<string>();
            l.Add("!^" + Settings.Get("biz_name", AppInfo.Name));
            foreach (var a in Wrap(Settings.Get("biz_address"))) l.Add("^" + a);
            var tel = Settings.Get("biz_phone");
            if (!string.IsNullOrWhiteSpace(tel)) l.Add("^Tel: " + tel);
            l.Add(Line());
            return l;
        }

        private static void OrderInfo(List<string> l, Order o)
        {
            if (!string.IsNullOrEmpty(o.CourierName)) l.Add("#Kurye: " + o.CourierName);
            l.Add(LR("Tarih: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"), "No: " + o.Id));
            l.Add("#" + OrderTypes.Label(o.OrderType) + (o.OrderType == OrderTypes.Masa ? " " + o.TableNo : ""));
            if (o.OrderType == OrderTypes.Paket)
            {
                if (!string.IsNullOrWhiteSpace(o.CustomerName)) l.Add("Müşteri: " + o.CustomerName);
                if (!string.IsNullOrWhiteSpace(o.CustomerPhone)) l.Add("Tel: " + o.CustomerPhone);
                foreach (var a in Wrap("Adres: " + (o.CustomerAddress ?? ""))) l.Add(a);
            }
            if (!string.IsNullOrWhiteSpace(o.Note)) foreach (var a in Wrap("Not: " + o.Note)) l.Add("#" + a);
            if (!string.IsNullOrWhiteSpace(o.UserName)) l.Add("Kasiyer: " + o.UserName);
        }

        /// <summary>Müşteri adisyonu / fiş.</summary>
        public static string PrintReceipt(Order o, decimal change = 0)
        {
            var l = Header();
            OrderInfo(l, o);
            l.Add(Line());
            foreach (var it in o.Items.Where(i => i.Qty > 0))
            {
                l.Add(LR(it.QtyText + " x " + it.Name, Money.Fmt(it.Total)));
                if (!string.IsNullOrWhiteSpace(it.Options)) foreach (var w in Wrap("   " + it.Options)) l.Add(w);
                if (!string.IsNullOrWhiteSpace(it.Note)) l.Add("   " + it.Note);
            }
            l.Add(Line());
            if (o.Discount > 0)
            {
                l.Add(LR("Ara Toplam", Money.Fmt(o.Subtotal)));
                l.Add(LR("İndirim", "-" + Money.Fmt(o.Discount)));
            }
            l.Add("#" + LR("TOPLAM", Money.FmtTl(o.Total)));
            if (o.Payments.Count > 0)
            {
                foreach (var p in o.Payments) l.Add(LR(p.Name, Money.Fmt(p.Amount)));
                if (change > 0) l.Add(LR("Para Üstü", Money.Fmt(change)));
            }
            l.Add(Line());
            foreach (var f in Wrap(Settings.Get("receipt_footer", "Afiyet olsun!"))) l.Add("^" + f);
            l.Add("^Mali değeri yoktur.");
            l.Add(" ");
            l.Add(" ");
            return Print(Settings.Get("printer_receipt"), l, "Adisyon " + o.Id);
        }

        /// <summary>Mutfağa yalnızca henüz gönderilmemiş miktarları basar ve işaretler.</summary>
        public static string PrintKitchen(Order o, bool onlyNew = true)
        {
            var newItems = o.Items.Where(i => !onlyNew || i.Qty > i.PrintedQty).ToList();
            if (newItems.Count == 0 && !o.Items.Any(i => i.PrintedQty > i.Qty)) return null;
            var printer = Settings.Get("printer_kitchen");
            if (string.IsNullOrWhiteSpace(printer)) printer = Settings.Get("printer_receipt");
            var l = new List<string>();
            l.Add("!^MUTFAK");
            l.Add("!" + OrderTypes.Label(o.OrderType) + (o.OrderType == OrderTypes.Masa ? " " + o.TableNo : " #" + o.Id));
            if (!string.IsNullOrEmpty(o.CourierName)) l.Add("#Kurye: " + o.CourierName);
            l.Add(DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            if (o.OrderType == OrderTypes.Paket && !string.IsNullOrWhiteSpace(o.CustomerName)) l.Add(o.CustomerName);
            l.Add(Line('='));
            foreach (var it in newItems)
            {
                decimal q = onlyNew ? it.Qty - it.PrintedQty : it.Qty;
                l.Add("!" + q.ToString("0.##", Money.Tr) + " x " + it.Name);
                if (!string.IsNullOrWhiteSpace(it.Options)) foreach (var w in Wrap("   " + it.Options)) l.Add("#" + w);
                if (!string.IsNullOrWhiteSpace(it.Note)) l.Add("#   >> " + it.Note);
            }
            var removed = o.Items.Where(i => i.PrintedQty > i.Qty).ToList();
            foreach (var it in removed) l.Add("#İPTAL: " + (it.PrintedQty - it.Qty).ToString("0.##", Money.Tr) + " x " + it.Name);
            if (!string.IsNullOrWhiteSpace(o.Note)) foreach (var a in Wrap("NOT: " + o.Note)) l.Add("#" + a);
            l.Add(Line('='));
            l.Add(" ");
            l.Add(" ");
            var err = Print(printer, l, "Mutfak " + o.Id);
            if (err == null) foreach (var it in o.Items) it.PrintedQty = it.Qty;
            return err;
        }
    }
}
