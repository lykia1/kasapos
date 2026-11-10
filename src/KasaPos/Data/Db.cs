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
                cmd.CommandText = "PRAGMA foreign_keys=ON;";
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

        private static void AddParams(SqliteCommand cmd, (string, object)[] p)
        {
            if (p == null) return;
            foreach (var (name, val) in p)
            {
                object v = val ?? DBNull.Value;
                if (v is decimal d) v = (double)Math.Round(d, 2);
                if (v is bool b) v = b ? 1 : 0;
                cmd.Parameters.AddWithValue(name, v);
            }
        }

        public static int Exec(string sql, params (string, object)[] p)
        {
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = sql;
                AddParams(cmd, p);
                return cmd.ExecuteNonQuery();
            }
        }

        public static int Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] p)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                AddParams(cmd, p);
                return cmd.ExecuteNonQuery();
            }
        }

        public static long Insert(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] p)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql + "; SELECT last_insert_rowid();";
                AddParams(cmd, p);
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        public static object Scalar(string sql, params (string, object)[] p)
        {
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = sql;
                AddParams(cmd, p);
                var r = cmd.ExecuteScalar();
                return r == DBNull.Value ? null : r;
            }
        }

        public static List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object)[] p)
        {
            var list = new List<T>();
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = sql;
                AddParams(cmd, p);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(map(r));
                }
            }
            return list;
        }

        // Okuma yardımcıları
        public static string S(SqliteDataReader r, string col)
        {
            int i = r.GetOrdinal(col);
            return r.IsDBNull(i) ? null : r.GetValue(i).ToString();
        }

        public static long L(SqliteDataReader r, string col)
        {
            int i = r.GetOrdinal(col);
            return r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));
        }

        public static decimal D(SqliteDataReader r, string col)
        {
            int i = r.GetOrdinal(col);
            return r.IsDBNull(i) ? 0m : Math.Round(Convert.ToDecimal(r.GetValue(i)), 2);
        }

        public static bool B(SqliteDataReader r, string col) => L(r, col) != 0;

        /// <summary>Uygulama her açıldığında günlük yedek alır, son 30 yedeği tutar.</summary>
        public static string Backup(bool force = false)
        {
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
