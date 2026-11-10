using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KasaPos.Data
{
    public static class Db
    {
        public static string DataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);

        public static string DbPath => Path.Combine(DataDir, "kasa.db");
        public static string BackupDir => Path.Combine(DataDir, "Yedekler");

        public static SqliteConnection Open()
        {
            var c = new SqliteConnection("Data Source=" + DbPath);
            c.Open();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
                cmd.ExecuteNonQuery();
            }
            return c;
        }

        public static void Init()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(BackupDir);
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE IF NOT EXISTS users (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  pin TEXT NOT NULL,
  role TEXT NOT NULL DEFAULT 'kasiyer',
  active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS categories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  sort INTEGER NOT NULL DEFAULT 0,
  active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS products (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  category_id INTEGER NOT NULL REFERENCES categories(id),
  name TEXT NOT NULL,
  price_masa REAL NOT NULL DEFAULT 0,
  price_gelal REAL NOT NULL DEFAULT 0,
  price_paket REAL NOT NULL DEFAULT 0,
  sort INTEGER NOT NULL DEFAULT 0,
  active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS payment_types (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  is_cash INTEGER NOT NULL DEFAULT 0,
  sort INTEGER NOT NULL DEFAULT 0,
  active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS customers (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  phone TEXT NOT NULL UNIQUE,
  name TEXT,
  address TEXT,
  note TEXT,
  created_at TEXT);
CREATE TABLE IF NOT EXISTS orders (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at TEXT NOT NULL,
  paid_at TEXT,
  business_day TEXT NOT NULL,
  order_type TEXT NOT NULL,
  table_no TEXT,
  customer_id INTEGER,
  customer_name TEXT,
  customer_phone TEXT,
  customer_address TEXT,
  subtotal REAL NOT NULL DEFAULT 0,
  discount REAL NOT NULL DEFAULT 0,
  total REAL NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT 'OPEN',
  user_id INTEGER,
  user_name TEXT,
  note TEXT);
CREATE TABLE IF NOT EXISTS order_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
  product_id INTEGER,
  name TEXT NOT NULL,
  qty REAL NOT NULL,
  unit_price REAL NOT NULL,
  printed_qty REAL NOT NULL DEFAULT 0,
  note TEXT);
CREATE TABLE IF NOT EXISTS payments (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
  payment_type_id INTEGER,
  payment_name TEXT NOT NULL,
  amount REAL NOT NULL);
CREATE TABLE IF NOT EXISTS expenses (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at TEXT NOT NULL,
  business_day TEXT NOT NULL,
  description TEXT NOT NULL,
  amount REAL NOT NULL,
  user_name TEXT);
CREATE INDEX IF NOT EXISTS ix_orders_day ON orders(business_day, status);
CREATE INDEX IF NOT EXISTS ix_items_order ON order_items(order_id);
CREATE INDEX IF NOT EXISTS ix_pay_order ON payments(order_id);
";
                cmd.ExecuteNonQuery();
            }

            // v0.2 eklemeleri
            Exec(@"CREATE TABLE IF NOT EXISTS dining_tables (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  area TEXT NOT NULL DEFAULT 'Salon',
  name TEXT NOT NULL UNIQUE,
  sort INTEGER NOT NULL DEFAULT 0,
  active INTEGER NOT NULL DEFAULT 1)");
            EnsureColumn("products", "color", "TEXT");
            EnsureColumn("products", "image", "TEXT");
            EnsureColumn("categories", "color", "TEXT");
            Directory.CreateDirectory(ImageDir);

            // v0.3 eklemeleri
            Exec(@"CREATE TABLE IF NOT EXISTS couriers (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  phone TEXT,
  active INTEGER NOT NULL DEFAULT 1)");
            Exec(@"CREATE TABLE IF NOT EXISTS kitchen_tickets (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  order_id INTEGER,
  created_at TEXT NOT NULL,
  title TEXT NOT NULL,
  body TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'NEW',
  done_at TEXT)");
            EnsureColumn("orders", "courier_id", "INTEGER");
            EnsureColumn("orders", "courier_name", "TEXT");
            EnsureColumn("orders", "dispatched_at", "TEXT");

            // v0.5 eklemeleri: stok, reçete, arama kaydı
            Exec(@"CREATE TABLE IF NOT EXISTS stock_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  unit TEXT NOT NULL DEFAULT 'adet',
  qty REAL NOT NULL DEFAULT 0,
  min_qty REAL NOT NULL DEFAULT 0,
  cost REAL NOT NULL DEFAULT 0,
  active INTEGER NOT NULL DEFAULT 1)");
            Exec(@"CREATE TABLE IF NOT EXISTS recipes (
  product_id INTEGER NOT NULL,
  stock_item_id INTEGER NOT NULL,
  qty REAL NOT NULL,
  PRIMARY KEY(product_id, stock_item_id))");
            Exec(@"CREATE TABLE IF NOT EXISTS stock_moves (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at TEXT NOT NULL,
  business_day TEXT NOT NULL,
  stock_item_id INTEGER NOT NULL,
  qty REAL NOT NULL,
  unit_cost REAL NOT NULL DEFAULT 0,
  reason TEXT NOT NULL,
  order_id INTEGER,
  note TEXT,
  user_name TEXT)");
            Exec(@"CREATE TABLE IF NOT EXISTS calls (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at TEXT NOT NULL,
  phone TEXT NOT NULL,
  customer_id INTEGER)");
            Exec("CREATE INDEX IF NOT EXISTS ix_moves_day ON stock_moves(business_day)");

            // v0.9 eklemeleri: ürün seçenekleri, kasa oturumları, personel mesaisi
            Exec(@"CREATE TABLE IF NOT EXISTS option_groups (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  min_select INTEGER NOT NULL DEFAULT 0,
  max_select INTEGER NOT NULL DEFAULT 0,
  sort INTEGER NOT NULL DEFAULT 0,
  active INTEGER NOT NULL DEFAULT 1)");
            Exec(@"CREATE TABLE IF NOT EXISTS options (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  group_id INTEGER NOT NULL,
  name TEXT NOT NULL,
  price REAL NOT NULL DEFAULT 0,
  sort INTEGER NOT NULL DEFAULT 0,
  active INTEGER NOT NULL DEFAULT 1)");
            Exec(@"CREATE TABLE IF NOT EXISTS product_option_groups (
  product_id INTEGER NOT NULL,
  group_id INTEGER NOT NULL,
  sort INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY(product_id, group_id))");
            EnsureColumn("order_items", "options", "TEXT");
            EnsureColumn("order_items", "options_price", "REAL NOT NULL DEFAULT 0");
            Exec(@"CREATE TABLE IF NOT EXISTS cash_sessions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  business_day TEXT NOT NULL,
  opened_at TEXT NOT NULL,
  opened_by TEXT,
  opening_cash REAL NOT NULL DEFAULT 0,
  closed_at TEXT,
  closed_by TEXT,
  counted_cash REAL,
  expected_cash REAL,
  note TEXT)");
            Exec(@"CREATE TABLE IF NOT EXISTS cash_moves (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  amount REAL NOT NULL,
  note TEXT,
  user_name TEXT)");
            EnsureColumn("users", "hourly_wage", "REAL NOT NULL DEFAULT 0");
            Exec(@"CREATE TABLE IF NOT EXISTS staff_shifts (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id INTEGER NOT NULL,
  user_name TEXT,
  start_at TEXT NOT NULL,
  end_at TEXT)");

            if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM payment_types")) == 0)
            {
                var defaults = new[] { "Nakit", "Kredi Kartı", "Multinet", "Pluxee (Sodexo)", "Edenred (Ticket)", "Setcard", "Metropol", "Açık Hesap" };
                for (int i = 0; i < defaults.Length; i++)
                    Exec("INSERT INTO payment_types(name,is_cash,sort) VALUES(@n,@c,@s)",
                        ("@n", defaults[i]), ("@c", i == 0 ? 1 : 0), ("@s", i));
            }
        }

        public static string ImageDir => Path.Combine(DataDir, "Gorseller");

        private static void EnsureColumn(string table, string column, string type)
        {
            var cols = Query("SELECT name FROM pragma_table_info('" + table + "')", r => r.GetString(0));
            if (!cols.Any(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase)))
                Exec("ALTER TABLE " + table + " ADD COLUMN " + column + " " + type);
        }

        /// <summary>Veritabanı arka ucu: yerel SQLite ya da ağdaki ana kasa.</summary>
        public static IDbBackend Backend { get; set; } = new LocalBackend();
        public static bool IsRemote => Backend is RemoteBackend;

        internal static void AddParams(SqliteCommand cmd, (string, object)[] p)
        {
            if (p == null) return;
            foreach (var (name, val) in p)
                cmd.Parameters.AddWithValue(name, Normalize(val) ?? DBNull.Value);
        }

        /// <summary>Parametre değerlerini SQLite'ın ve ağ protokolünün anladığı türlere çevirir.</summary>
        internal static object Normalize(object v)
        {
            if (v == null || v == DBNull.Value) return null;
            if (v is decimal d) return (double)Math.Round(d, 2);
            if (v is bool b) return b ? 1L : 0L;
            if (v is int i) return (long)i;
            if (v is float f) return (double)f;
            return v;
        }

        public static int Exec(string sql, params (string, object)[] p) => Backend.Exec(sql, p);

        public static object Scalar(string sql, params (string, object)[] p) => Backend.Scalar(sql, p);

        public static List<T> Query<T>(string sql, Func<DbRow, T> map, params (string, object)[] p) =>
            Backend.Query(sql, p).Select(map).ToList();

        /// <summary>Tek işlem (transaction) içinde çalıştırır; hata olursa hepsi geri alınır.</summary>
        public static void Tx(Action<IDbTx> work) => Backend.Tx(work);

        /// <summary>Kayıt ekler ve yeni satırın id'sini döner.</summary>
        public static long Insert(string sql, params (string, object)[] p)
        {
            long id = 0;
            Tx(t => id = t.Insert(sql, p));
            return id;
        }

        // Okuma yardımcıları
        public static string S(DbRow r, string col)
        {
            int i = r.GetOrdinal(col);
            return r.IsDBNull(i) ? null : r.GetValue(i).ToString();
        }

        public static long L(DbRow r, string col)
        {
            int i = r.GetOrdinal(col);
            return r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));
        }

        public static decimal D(DbRow r, string col)
        {
            int i = r.GetOrdinal(col);
            return r.IsDBNull(i) ? 0m : Math.Round(Convert.ToDecimal(r.GetValue(i)), 2);
        }

        public static bool B(DbRow r, string col) => L(r, col) != 0;

        /// <summary>Uygulama her açıldığında günlük yedek alır, son 30 yedeği tutar.</summary>
        public static string Backup(bool force = false)
        {
            if (IsRemote) return null; // yedeği ana kasa alır
            try
            {
                Directory.CreateDirectory(BackupDir);
                string name = "kasa_" + DateTime.Now.ToString(force ? "yyyy-MM-dd_HHmmss" : "yyyy-MM-dd") + ".db";
                string target = Path.Combine(BackupDir, name);
                if (!force && File.Exists(target)) return target;
                if (File.Exists(target)) File.Delete(target);
                using (var c = Open())
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "VACUUM INTO @p";
                    cmd.Parameters.AddWithValue("@p", target);
                    cmd.ExecuteNonQuery();
                }
                var old = new DirectoryInfo(BackupDir).GetFiles("kasa_*.db")
                    .OrderByDescending(f => f.CreationTimeUtc).Skip(30).ToList();
                foreach (var f in old) { try { f.Delete(); } catch { } }
                return target;
            }
            catch
            {
                return null;
            }
        }
    }

    public static class Settings
    {
        public static string Get(string key, string def = "")
        {
            string local;
            if (LocalConfig.IsLocalKey(key) && LocalConfig.TryGet(key, out local)) return local;
            var v = Db.Scalar("SELECT value FROM settings WHERE key=@k", ("@k", key));
            return v == null ? def : v.ToString();
        }

        public static int GetInt(string key, int def)
        {
            int r;
            return int.TryParse(Get(key, def.ToString()), out r) ? r : def;
        }

        public static bool GetBool(string key, bool def) => Get(key, def ? "1" : "0") == "1";

        public static void Set(string key, string value)
        {
            if (LocalConfig.IsLocalKey(key))
            {
                LocalConfig.Set(key, value ?? "");
                return;
            }
            Db.Exec("INSERT INTO settings(key,value) VALUES(@k,@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                ("@k", key), ("@v", value ?? ""));
        }

        public static void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

        /// <summary>İş günü: kapanış saatinden önceki satışlar bir önceki güne yazılır.</summary>
        public static string BusinessDay(DateTime t)
        {
            int cut = GetInt("day_cutoff_hour", 6);
            var d = t.Hour < cut ? t.Date.AddDays(-1) : t.Date;
            return d.ToString("yyyy-MM-dd");
        }
    }
}
