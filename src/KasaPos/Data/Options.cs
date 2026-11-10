using System;
using System.Collections.Generic;
using System.Linq;

namespace KasaPos.Data
{
    /// <summary>Ürün seçenek grubu: ör. "Porsiyon" (1 seçim zorunlu), "Sos" (serbest), "Ekstralar" (ücretli).</summary>
    public class OptionGroup
    {
        public long Id { get; set; }
        public string Name { get; set; }
        /// <summary>En az seçim (0 = isteğe bağlı).</summary>
        public int MinSelect { get; set; }
        /// <summary>En çok seçim (0 = sınırsız, 1 = tek seçim).</summary>
        public int MaxSelect { get; set; }
        public int Sort { get; set; }
        public bool Active { get; set; } = true;
        public List<ProductOption> Options { get; set; } = new List<ProductOption>();

        public string RuleText
        {
            get
            {
                if (MaxSelect == 1) return MinSelect >= 1 ? "1 seçim zorunlu" : "en fazla 1 seçim";
                string s = MinSelect > 0 ? "en az " + MinSelect : "isteğe bağlı";
                if (MaxSelect > 1) s += ", en fazla " + MaxSelect;
                return s;
            }
        }
        public override string ToString() => Name;
    }

    public class ProductOption
    {
        public long Id { get; set; }
        public long GroupId { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Sort { get; set; }
        public bool Active { get; set; } = true;
    }

    public class ProductPick
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public bool IsChecked { get; set; }
    }

    public static class OptionRepo
    {
        public static List<OptionGroup> Groups(bool onlyActive = false) =>
            Db.Query("SELECT * FROM option_groups" + (onlyActive ? " WHERE active=1" : "") + " ORDER BY sort, id", r => new OptionGroup
            {
                Id = Db.L(r, "id"),
                Name = Db.S(r, "name"),
                MinSelect = (int)Db.L(r, "min_select"),
                MaxSelect = (int)Db.L(r, "max_select"),
                Sort = (int)Db.L(r, "sort"),
                Active = Db.B(r, "active")
            });

        public static List<ProductOption> Options(long groupId, bool onlyActive = false) =>
            Db.Query("SELECT * FROM options WHERE group_id=@g" + (onlyActive ? " AND active=1" : "") + " ORDER BY sort, id", r => new ProductOption
            {
                Id = Db.L(r, "id"),
                GroupId = Db.L(r, "group_id"),
                Name = Db.S(r, "name"),
                Price = Db.D(r, "price"),
                Sort = (int)Db.L(r, "sort"),
                Active = Db.B(r, "active")
            }, ("@g", groupId));

        public static long SaveGroup(OptionGroup g)
        {
            if (g.Id == 0)
            {
                Db.Exec("INSERT INTO option_groups(name,min_select,max_select,sort,active) VALUES(@n,@mi,@ma,@s,@a)",
                    ("@n", g.Name), ("@mi", g.MinSelect), ("@ma", g.MaxSelect), ("@s", g.Sort), ("@a", g.Active));
                g.Id = Convert.ToInt64(Db.Scalar("SELECT MAX(id) FROM option_groups"));
            }
            else
                Db.Exec("UPDATE option_groups SET name=@n,min_select=@mi,max_select=@ma,sort=@s,active=@a WHERE id=@id",
                    ("@n", g.Name), ("@mi", g.MinSelect), ("@ma", g.MaxSelect), ("@s", g.Sort), ("@a", g.Active), ("@id", g.Id));
            return g.Id;
        }

        public static void SaveOption(ProductOption o)
        {
            if (o.Id == 0)
                Db.Exec("INSERT INTO options(group_id,name,price,sort,active) VALUES(@g,@n,@p,@s,@a)",
                    ("@g", o.GroupId), ("@n", o.Name), ("@p", o.Price), ("@s", o.Sort), ("@a", o.Active));
            else
                Db.Exec("UPDATE options SET group_id=@g,name=@n,price=@p,sort=@s,active=@a WHERE id=@id",
                    ("@g", o.GroupId), ("@n", o.Name), ("@p", o.Price), ("@s", o.Sort), ("@a", o.Active), ("@id", o.Id));
        }

        public static List<long> ProductsOfGroup(long groupId) =>
            Db.Query("SELECT product_id FROM product_option_groups WHERE group_id=@g", r => Db.L(r, "product_id"), ("@g", groupId));

        public static void SetProductsOfGroup(long groupId, IEnumerable<long> productIds)
        {
            Db.Exec("DELETE FROM product_option_groups WHERE group_id=@g", ("@g", groupId));
            foreach (var pid in productIds.Distinct())
                Db.Exec("INSERT INTO product_option_groups(product_id,group_id,sort) VALUES(@p,@g,0)", ("@p", pid), ("@g", groupId));
        }

        /// <summary>Ürüne bağlı aktif gruplar ve aktif seçenekleri.</summary>
        public static List<OptionGroup> GroupsForProduct(long productId)
        {
            var groups = Db.Query(@"SELECT g.* FROM option_groups g JOIN product_option_groups pg ON pg.group_id=g.id
WHERE pg.product_id=@p AND g.active=1 ORDER BY g.sort, g.id", r => new OptionGroup
            {
                Id = Db.L(r, "id"),
                Name = Db.S(r, "name"),
                MinSelect = (int)Db.L(r, "min_select"),
                MaxSelect = (int)Db.L(r, "max_select"),
                Sort = (int)Db.L(r, "sort"),
                Active = true
            }, ("@p", productId));
            foreach (var g in groups) g.Options = Options(g.Id, true);
            return groups.Where(g => g.Options.Count > 0).ToList();
        }

        /// <summary>Hangi ürünlerin seçeneği olduğunu tek sorguda döner (satış ekranı için).</summary>
        public static HashSet<long> ProductsWithOptions() =>
            new HashSet<long>(Db.Query(@"SELECT DISTINCT pg.product_id FROM product_option_groups pg
JOIN option_groups g ON g.id=pg.group_id AND g.active=1
JOIN options o ON o.group_id=g.id AND o.active=1", r => Db.L(r, "product_id")));

        /// <summary>İlk kurulumda örnek seçenekler (döner için).</summary>
        public static void SeedSample()
        {
            if (Groups().Count > 0) return;
            var g1 = new OptionGroup { Name = "Porsiyon", MinSelect = 1, MaxSelect = 1, Sort = 0 };
            SaveGroup(g1);
            SaveOption(new ProductOption { GroupId = g1.Id, Name = "Tam", Price = 0, Sort = 0 });
            SaveOption(new ProductOption { GroupId = g1.Id, Name = "Yarım", Price = -40, Sort = 1 });
            SaveOption(new ProductOption { GroupId = g1.Id, Name = "1,5 Porsiyon", Price = 60, Sort = 2 });
            var g2 = new OptionGroup { Name = "Tercih", MinSelect = 0, MaxSelect = 0, Sort = 1 };
            SaveGroup(g2);
            int i = 0;
            foreach (var n in new[] { "Acılı", "Acısız", "Soğansız", "Domatessiz", "Turşusuz", "Bol soslu" })
                SaveOption(new ProductOption { GroupId = g2.Id, Name = n, Price = 0, Sort = i++ });
            var g3 = new OptionGroup { Name = "Ekstralar", MinSelect = 0, MaxSelect = 0, Sort = 2 };
            SaveGroup(g3);
            SaveOption(new ProductOption { GroupId = g3.Id, Name = "Ekstra kaşar", Price = 20, Sort = 0 });
            SaveOption(new ProductOption { GroupId = g3.Id, Name = "Ekstra et", Price = 60, Sort = 1 });
            SaveOption(new ProductOption { GroupId = g3.Id, Name = "Patates", Price = 30, Sort = 2 });
            SaveOption(new ProductOption { GroupId = g3.Id, Name = "Ekstra sos", Price = 10, Sort = 3 });
        }
    }
}
