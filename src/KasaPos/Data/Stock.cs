using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KasaPos.Data
{
    public class StockItem
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; } = "adet";
        public decimal Qty { get; set; }
        public decimal MinQty { get; set; }
        public decimal Cost { get; set; }
        public bool Active { get; set; } = true;
        public bool IsLow => Active && MinQty > 0 && Qty <= MinQty;
        public string QtyText => Qty.ToString("0.###", Money.Tr) + " " + Unit;
        public string Display => Name + " (" + Unit + ")";
        public override string ToString() => Display;
    }

    public class RecipeLine
    {
        public long StockItemId { get; set; }
        public decimal Qty { get; set; }
    }

    public class StockMove
    {
        public DateTime CreatedAt { get; set; }
        public string ItemName { get; set; }
        public string Unit { get; set; }
        public decimal Qty { get; set; }
        public string Reason { get; set; }
        public string Note { get; set; }
        public string UserName { get; set; }
        public string TimeText => CreatedAt.ToString("dd.MM HH:mm");
        public string QtyText => (Qty > 0 ? "+" : "") + Qty.ToString("0.###", Money.Tr) + " " + Unit;
    }

    public static class StockRepo
    {
        private const string Iso = "yyyy-MM-dd HH:mm:ss";

        public static List<StockItem> Items(bool onlyActive = false) =>
            Db.Query("SELECT * FROM stock_items" + (onlyActive ? " WHERE active=1" : "") + " ORDER BY name", r => new StockItem
            {
                Id = Db.L(r, "id"),
                Name = Db.S(r, "name"),
                Unit = Db.S(r, "unit"),
                Qty = Math.Round(Convert.ToDecimal(r.GetDouble(r.GetOrdinal("qty"))), 3),
                MinQty = Math.Round(Convert.ToDecimal(r.GetDouble(r.GetOrdinal("min_qty"))), 3),
                Cost = Db.D(r, "cost"),
                Active = Db.B(r, "active")
            });

        public static int LowCount() =>
            Convert.ToInt32(Db.Scalar("SELECT COUNT(*) FROM stock_items WHERE active=1 AND min_qty>0 AND qty<=min_qty"));

        /// <summary>Kartın adını, birimini, kritik seviyesini ve maliyetini kaydeder (miktar hareketlerle değişir).</summary>
        public static void SaveItem(StockItem s)
        {
            if (s.Id == 0)
                Db.Exec("INSERT INTO stock_items(name,unit,qty,min_qty,cost,active) VALUES(@n,@u,0,@m,@c,@a)",
                    ("@n", s.Name), ("@u", string.IsNullOrWhiteSpace(s.Unit) ? "adet" : s.Unit), ("@m", (double)s.MinQty), ("@c", s.Cost), ("@a", s.Active));
            else
                Db.Exec("UPDATE stock_items SET name=@n,unit=@u,min_qty=@m,cost=@c,active=@a WHERE id=@id",
                    ("@n", s.Name), ("@u", string.IsNullOrWhiteSpace(s.Unit) ? "adet" : s.Unit), ("@m", (double)s.MinQty), ("@c", s.Cost), ("@a", s.Active), ("@id", s.Id));
        }

        /// <summary>Stok hareketi ekler (+ giriş, - çıkış) ve mevcut miktarı günceller.</summary>
        public static void Move(long itemId, decimal qty, string reason, string note, string user, long? orderId = null, decimal? unitCost = null)
        {
            var now = DateTime.Now;
            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            {
                decimal cost = unitCost ?? Convert.ToDecimal(ScalarIn(c, tx, "SELECT cost FROM stock_items WHERE id=@id", itemId) ?? 0.0);
                Db.Exec(c, tx, "INSERT INTO stock_moves(created_at,business_day,stock_item_id,qty,unit_cost,reason,order_id,note,user_name) VALUES(@c,@d,@i,@q,@uc,@r,@o,@n,@u)",
                    ("@c", now.ToString(Iso, CultureInfo.InvariantCulture)), ("@d", Settings.BusinessDay(now)), ("@i", itemId),
                    ("@q", (double)qty), ("@uc", cost), ("@r", reason), ("@o", orderId), ("@n", note), ("@u", user));
                Db.Exec(c, tx, "UPDATE stock_items SET qty=qty+@q WHERE id=@i", ("@q", (double)qty), ("@i", itemId));
                if (unitCost.HasValue && unitCost.Value > 0)
                    Db.Exec(c, tx, "UPDATE stock_items SET cost=@uc WHERE id=@i", ("@uc", unitCost.Value), ("@i", itemId));
                tx.Commit();
            }
        }

        private static object ScalarIn(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, string sql, long id)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", id);
                var r = cmd.ExecuteScalar();
                return r == DBNull.Value ? null : r;
            }
        }

        public static List<RecipeLine> Recipe(long productId) =>
            Db.Query("SELECT * FROM recipes WHERE product_id=@p", r => new RecipeLine
            {
                StockItemId = Db.L(r, "stock_item_id"),
                Qty = Math.Round(Convert.ToDecimal(r.GetDouble(r.GetOrdinal("qty"))), 4)
            }, ("@p", productId));

        public static void SaveRecipe(long productId, IEnumerable<RecipeLine> lines)
        {
            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            {
                Db.Exec(c, tx, "DELETE FROM recipes WHERE product_id=@p", ("@p", productId));
                foreach (var l in lines.Where(x => x.StockItemId > 0 && x.Qty > 0).GroupBy(x => x.StockItemId))
                    Db.Exec(c, tx, "INSERT INTO recipes(product_id,stock_item_id,qty) VALUES(@p,@s,@q)",
                        ("@p", productId), ("@s", l.Key), ("@q", (double)l.Sum(x => x.Qty)));
                tx.Commit();
            }
        }

        /// <summary>Ödenen siparişin ürün reçetelerine göre stoktan düşer.</summary>
        public static void ConsumeForOrder(Order o, string user)
        {
            try
            {
                var need = new Dictionary<long, decimal>();
                foreach (var it in o.Items.Where(i => i.Qty > 0 && i.ProductId > 0))
                    foreach (var line in Recipe(it.ProductId))
                    {
                        decimal q;
                        need.TryGetValue(line.StockItemId, out q);
                        need[line.StockItemId] = q + line.Qty * it.Qty;
                    }
                foreach (var kv in need)
                    Move(kv.Key, -kv.Value, "SATIŞ", "Sipariş #" + o.Id, user, o.Id);
            }
            catch
            {
                // Stok düşümü satışı engellememeli
            }
        }

        public static List<StockMove> RecentMoves(int limit = 300) =>
            Db.Query(@"SELECT m.*, s.name AS item_name, s.unit AS unit FROM stock_moves m
LEFT JOIN stock_items s ON s.id=m.stock_item_id ORDER BY m.id DESC LIMIT " + limit, r => new StockMove
            {
                CreatedAt = DateTime.ParseExact(Db.S(r, "created_at"), Iso, CultureInfo.InvariantCulture),
                ItemName = Db.S(r, "item_name"),
                Unit = Db.S(r, "unit"),
                Qty = Math.Round(Convert.ToDecimal(r.GetDouble(r.GetOrdinal("qty"))), 3),
                Reason = Db.S(r, "reason"),
                Note = Db.S(r, "note"),
                UserName = Db.S(r, "user_name")
            });
    }

    public class CallEntry
    {
        public DateTime At { get; set; }
        public string Phone { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
    }

    public static class CallRepo
    {
        public static List<CallEntry> Recent(int n) =>
            Db.Query(@"SELECT c.created_at, c.phone, cu.name, cu.address FROM calls c
LEFT JOIN customers cu ON cu.phone=c.phone ORDER BY c.id DESC LIMIT " + n, r => new CallEntry
            {
                At = DateTime.ParseExact(Db.S(r, "created_at"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Phone = Db.S(r, "phone"),
                Name = Db.S(r, "name"),
                Address = Db.S(r, "address")
            });

        public static void Log(string phone, long? customerId) =>
            Db.Exec("INSERT INTO calls(created_at,phone,customer_id) VALUES(@c,@p,@cid)",
                ("@c", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)), ("@p", phone), ("@cid", customerId));

        public static (int count, decimal total, DateTime? last) CustomerHistory(long customerId)
        {
            var r = Db.Query("SELECT COUNT(*) AS c, SUM(total) AS t, MAX(created_at) AS l FROM orders WHERE status='PAID' AND customer_id=@id",
                x => (Db.L(x, "c"), Db.D(x, "t"), Db.S(x, "l")), ("@id", customerId)).FirstOrDefault();
            DateTime? last = null;
            DateTime d;
            if (!string.IsNullOrEmpty(r.Item3) && DateTime.TryParseExact(r.Item3, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) last = d;
            return ((int)r.Item1, r.Item2, last);
        }
    }
}
