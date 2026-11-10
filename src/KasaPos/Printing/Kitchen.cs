using System.Linq;
using System.Text;
using KasaPos.Data;

namespace KasaPos.Printing
{
    /// <summary>Yeni eklenen ürünleri mutfağa (yazıcı ve/veya mutfak ekranı) gönderir.</summary>
    public static class Kitchen
    {
        public static bool PrintEnabled => Settings.GetBool("kitchen_auto", false);
        public static bool ScreenEnabled => Settings.GetBool("kitchen_screen", false);

        public static string TitleOf(Order o) =>
            OrderTypes.Label(o.OrderType) + (o.OrderType == OrderTypes.Masa ? " " + o.TableNo : " #" + o.Id)
            + (o.OrderType == OrderTypes.Paket && !string.IsNullOrWhiteSpace(o.CustomerName) ? " · " + o.CustomerName : "");

        /// <summary>Gönderilecek bir şey yoksa ya da başarılıysa null, hata varsa mesaj döner.</summary>
        public static string Send(Order o)
        {
            if (!PrintEnabled && !ScreenEnabled) return null;
            var added = o.Items.Where(i => i.Qty > i.PrintedQty).ToList();
            var removed = o.Items.Where(i => i.PrintedQty > i.Qty).ToList();
            if (added.Count == 0 && removed.Count == 0) return null;

            if (ScreenEnabled)
            {
                var sb = new StringBuilder();
                foreach (var it in added)
                {
                    sb.AppendLine((it.Qty - it.PrintedQty).ToString("0.##", Money.Tr) + " x " + it.Name);
                    if (!string.IsNullOrWhiteSpace(it.Note)) sb.AppendLine("   » " + it.Note);
                }
                foreach (var it in removed)
                    sb.AppendLine("İPTAL: " + (it.PrintedQty - it.Qty).ToString("0.##", Money.Tr) + " x " + it.Name);
                if (!string.IsNullOrWhiteSpace(o.Note)) sb.AppendLine("NOT: " + o.Note);
                KitchenRepo.Add(o.Id, TitleOf(o), sb.ToString().TrimEnd());
            }

            string err = null;
            if (PrintEnabled) err = Printer.PrintKitchen(o);
            if (ScreenEnabled || err == null)
                foreach (var it in o.Items) it.PrintedQty = it.Qty;
            // Tamamen silinen ürünler için iptal bilgisi bir kez gönderildi; artık listeden çıkar
            o.Items.RemoveAll(i => i.Qty <= 0);
            return err;
        }
    }
}
