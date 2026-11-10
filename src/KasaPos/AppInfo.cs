using System;
using System.Globalization;

namespace KasaPos
{
    public static class AppInfo
    {
        // Ürün adı tek yerden değişir (çalışma adı).
        public const string Name = "KasaPOS";

        public static string Version
        {
            get
            {
                var v = typeof(AppInfo).Assembly.GetName().Version;
                return v == null ? "0.1.0" : v.ToString(3);
            }
        }
    }

    public static class Money
    {
        public static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        public static string Fmt(decimal d) => d.ToString("N2", Tr);
        public static string FmtTl(decimal d) => d.ToString("N2", Tr) + " ₺";

        public static bool TryParse(string s, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Replace("₺", "").Replace(" ", "").Trim();
            if (s.Contains(","))
            {
                s = s.Replace(".", "");
                return decimal.TryParse(s, NumberStyles.Number, Tr, out value);
            }
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }
    }
}
