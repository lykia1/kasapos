using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KasaPos.Data
{
    /// <summary>
    /// Menüyü CSV (Excel'den "CSV olarak kaydet") ile içe/dışa aktarır.
    /// Sütunlar: Kategori; Ürün; Masa Fiyatı; Gel Al Fiyatı; Paket Fiyatı  (tek fiyat sütunu varsa üçüne de yazılır)
    /// </summary>
    public static class MenuIO
    {
        public class Result
        {
            public int NewCategories;
            public int Added;
            public int Updated;
            public int Skipped;
        }

        private static List<string> SplitLine(string line, char sep)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') q = false;
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == sep) { res.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(c);
            }
            res.Add(sb.ToString().Trim());
            return res;
        }

        private static string ReadText(string path)
        {
            // Dosya Excel'de açık olsa bile okuyabilmek için paylaşımlı aç
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }
            // UTF-8 değilse Türkçe Windows kodlamasıyla (Excel'in eski CSV'leri) oku
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1254).GetString(bytes);
            }
        }

        public static Result Import(string path, bool hideExisting)
        {
            var text = ReadText(path);
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count == 0) throw new InvalidDataException("Dosya boş.");
            char sep = lines[0].Count(c => c == ';') >= lines[0].Count(c => c == ',') ? ';' : ',';
            if (lines[0].Contains('\t')) sep = '\t';

            var res = new Result();
            if (hideExisting) Db.Exec("UPDATE products SET active=0");

            var cats = MenuRepo.Categories(false);
            var products = MenuRepo.Products(false);
            int colorIdx = cats.Count;
            int sortBase = products.Count;

            foreach (var line in lines)
            {
                var f = SplitLine(line, sep);
                if (f.Count < 3) { res.Skipped++; continue; }
                string catName = f[0].Trim();
                string name = f[1].Trim();
                decimal pm, pg, pp;
                if (!Money.TryParse(f[2], out pm)) { res.Skipped++; continue; } // başlık satırı ya da hatalı satır
                if (f.Count < 4 || !Money.TryParse(f[3], out pg)) pg = pm;
                if (f.Count < 5 || !Money.TryParse(f[4], out pp)) pp = pm;
                string desc = f.Count >= 6 ? f[5].Trim() : null;
                if (catName == "" || name == "") { res.Skipped++; continue; }

                var cat = cats.FirstOrDefault(c => string.Equals(c.Name, catName, StringComparison.CurrentCultureIgnoreCase));
                if (cat == null)
                {
                    cat = new Category { Name = catName, Sort = cats.Count, Active = true, Color = KasaPos.Palette.Names[1 + (colorIdx++ % (KasaPos.Palette.Names.Length - 1))] };
                    cat.Id = MenuRepo.SaveCategory(cat);
                    cats.Add(cat);
                    res.NewCategories++;
                }
                else if (!cat.Active)
                {
                    cat.Active = true;
                    MenuRepo.SaveCategory(cat);
                }

                var p = products.FirstOrDefault(x => x.CategoryId == cat.Id && string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase));
                if (p == null)
                {
                    p = new Product { CategoryId = cat.Id, Name = name, Sort = sortBase++, Active = true };
                    res.Added++;
                }
                else res.Updated++;
                p.PriceMasa = pm;
                p.PriceGelAl = pg;
                p.PricePaket = pp;
                if (desc != null) p.Description = desc;
                p.Active = true;
                MenuRepo.SaveProduct(p);
                if (p.Id == 0)
                {
                    // yeni eklenen ürün bir sonraki satırlarda tekrar eşleşebilsin
                    products = MenuRepo.Products(false);
                }
            }
            return res;
        }

        private static string Q(string s)
        {
            s = s ?? "";
            return s.Contains(';') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public static int Export(string path)
        {
            var cats = MenuRepo.Categories(false).ToDictionary(c => c.Id, c => c.Name);
            var sb = new StringBuilder();
            sb.AppendLine("Kategori;Ürün;Masa Fiyatı;Gel Al Fiyatı;Paket Fiyatı;Açıklama");
            int n = 0;
            foreach (var p in MenuRepo.Products(true))
            {
                string cn;
                if (!cats.TryGetValue(p.CategoryId, out cn)) continue;
                sb.AppendLine(Q(cn) + ";" + Q(p.Name) + ";" + Money.Fmt(p.PriceMasa).Replace(".", "") + ";" +
                              Money.Fmt(p.PriceGelAl).Replace(".", "") + ";" + Money.Fmt(p.PricePaket).Replace(".", "") + ";" + Q(p.Description));
                n++;
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return n;
        }
    }
}
