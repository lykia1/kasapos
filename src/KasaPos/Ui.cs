using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KasaPos.Data;

namespace KasaPos
{
    /// <summary>Ürün ve kategori renkleri (veritabanında renk adı tutulur).</summary>
    public static class Palette
    {
        public static readonly string[] Names =
            { "", "Turuncu", "Kırmızı", "Pembe", "Mor", "Mavi", "Turkuaz", "Yeşil", "Sarı", "Kahve", "Gri" };

        private static readonly Dictionary<string, string> Hex = new Dictionary<string, string>
        {
            { "Turuncu", "#F76707" }, { "Kırmızı", "#E03131" }, { "Pembe", "#D6336C" }, { "Mor", "#7048E8" },
            { "Mavi", "#1971C2" }, { "Turkuaz", "#0C8599" }, { "Yeşil", "#2F9E44" }, { "Sarı", "#F59F00" },
            { "Kahve", "#8D5524" }, { "Gri", "#868E96" }
        };

        public static Brush Brush(string name, Brush fallback = null)
        {
            string hex;
            if (!string.IsNullOrEmpty(name) && Hex.TryGetValue(name, out hex))
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            }
            return fallback ?? Brushes.Transparent;
        }

        public static Brush Soft(string name)
        {
            string hex;
            if (!string.IsNullOrEmpty(name) && Hex.TryGetValue(name, out hex))
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                var b = new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B));
                b.Freeze();
                return b;
            }
            return Brushes.White;
        }
    }

    /// <summary>Ürün görsellerini dosyayı kilitlemeden ve küçültülmüş olarak yükler.</summary>
    public static class ImageCache
    {
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();

        public static ImageSource Get(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            ImageSource img;
            if (Cache.TryGetValue(fileName, out img)) return img;
            try
            {
                var path = Path.Combine(Db.ImageDir, fileName);
                if (!File.Exists(path) && Db.Backend is RemoteBackend rb)
                {
                    // Terminalde görsel yoksa ana kasadan indir ve sakla
                    var bytes = rb.GetFile(fileName);
                    if (bytes != null)
                    {
                        Directory.CreateDirectory(Db.ImageDir);
                        File.WriteAllBytes(path, bytes);
                    }
                }
                if (!File.Exists(path)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 220;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                Cache[fileName] = bi;
                return bi;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Seçilen görseli veri klasörüne kopyalar, yeni dosya adını döner.</summary>
        public static string Import(string sourcePath)
        {
            Directory.CreateDirectory(Db.ImageDir);
            var name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath).ToLowerInvariant();
            File.Copy(sourcePath, Path.Combine(Db.ImageDir, name), true);
            if (Db.Backend is RemoteBackend rb) rb.PutFile(name, File.ReadAllBytes(sourcePath));
            return name;
        }
    }
}
