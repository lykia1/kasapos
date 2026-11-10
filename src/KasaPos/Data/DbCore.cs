using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace KasaPos.Data
{
    /// <summary>Sorgu sonucundaki tek satır (yerel ve ağ bağlantısında aynı).</summary>
    public class DbRow
    {
        private readonly Dictionary<string, int> _cols;
        private readonly object[] _vals;

        public DbRow(Dictionary<string, int> cols, object[] vals)
        {
            _cols = cols;
            _vals = vals;
        }

        public int GetOrdinal(string name)
        {
            int i;
            if (_cols.TryGetValue(name, out i)) return i;
            throw new IndexOutOfRangeException("Sütun yok: " + name);
        }

        public bool IsDBNull(int i) => _vals[i] == null || _vals[i] == DBNull.Value;
        public object GetValue(int i) => _vals[i] ?? DBNull.Value;
        public string GetString(int i) => Convert.ToString(_vals[i]);
        public double GetDouble(int i) => _vals[i] == null ? 0 : Convert.ToDouble(_vals[i], System.Globalization.CultureInfo.InvariantCulture);
        public long GetInt64(int i) => _vals[i] == null ? 0 : Convert.ToInt64(_vals[i]);
    }

    public interface IDbTx
    {
        int Exec(string sql, params (string, object)[] p);
        long Insert(string sql, params (string, object)[] p);
        object Scalar(string sql, params (string, object)[] p);
        List<DbRow> Query(string sql, params (string, object)[] p);
    }

    public interface IDbBackend
    {
        int Exec(string sql, (string, object)[] p);
        object Scalar(string sql, (string, object)[] p);
        List<DbRow> Query(string sql, (string, object)[] p);
        void Tx(Action<IDbTx> work);
    }

    // ===================== Yerel SQLite =====================

    internal static class SqliteRunner
    {
        public static int Exec(SqliteConnection c, SqliteTransaction tx, string sql, (string, object)[] p)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                Db.AddParams(cmd, p);
                return cmd.ExecuteNonQuery();
            }
        }

        public static object Scalar(SqliteConnection c, SqliteTransaction tx, string sql, (string, object)[] p)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                Db.AddParams(cmd, p);
                var r = cmd.ExecuteScalar();
                return r == DBNull.Value ? null : r;
            }
        }

        public static long Insert(SqliteConnection c, SqliteTransaction tx, string sql, (string, object)[] p)
        {
            Exec(c, tx, sql, p);
            return Convert.ToInt64(Scalar(c, tx, "SELECT last_insert_rowid()", null));
        }

        public static List<DbRow> Query(SqliteConnection c, SqliteTransaction tx, string sql, (string, object)[] p)
        {
            var list = new List<DbRow>();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                Db.AddParams(cmd, p);
                using (var r = cmd.ExecuteReader())
                {
                    var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < r.FieldCount; i++)
                        if (!cols.ContainsKey(r.GetName(i))) cols[r.GetName(i)] = i;
                    while (r.Read())
                    {
                        var vals = new object[r.FieldCount];
                        for (int i = 0; i < r.FieldCount; i++) vals[i] = r.IsDBNull(i) ? null : r.GetValue(i);
                        list.Add(new DbRow(cols, vals));
                    }
                }
            }
            return list;
        }
    }

    internal class LocalTx : IDbTx
    {
        private readonly SqliteConnection _c;
        private readonly SqliteTransaction _tx;
        public LocalTx(SqliteConnection c, SqliteTransaction tx) { _c = c; _tx = tx; }
        public int Exec(string sql, params (string, object)[] p) => SqliteRunner.Exec(_c, _tx, sql, p);
        public long Insert(string sql, params (string, object)[] p) => SqliteRunner.Insert(_c, _tx, sql, p);
        public object Scalar(string sql, params (string, object)[] p) => SqliteRunner.Scalar(_c, _tx, sql, p);
        public List<DbRow> Query(string sql, params (string, object)[] p) => SqliteRunner.Query(_c, _tx, sql, p);
    }

    public class LocalBackend : IDbBackend
    {
        public int Exec(string sql, (string, object)[] p) { using (var c = Db.Open()) return SqliteRunner.Exec(c, null, sql, p); }
        public object Scalar(string sql, (string, object)[] p) { using (var c = Db.Open()) return SqliteRunner.Scalar(c, null, sql, p); }
        public List<DbRow> Query(string sql, (string, object)[] p) { using (var c = Db.Open()) return SqliteRunner.Query(c, null, sql, p); }

        public void Tx(Action<IDbTx> work)
        {
            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            {
                work(new LocalTx(c, tx));
                tx.Commit();
            }
        }
    }

    // ===================== Ağ protokolü =====================
    // Her istek ve yanıt tek satır JSON'dur.
    // İstek: {"op":"q|s|x|i|b|c|r|hello|img|putimg","sql":"...","p":[["@a",değer],...],"k":"anahtar","name":"...","data":"base64"}
    // Yanıt: {"ok":true,"cols":[...],"rows":[[...]],"v":değer,"n":sayı,"id":sayı} veya {"ok":false,"err":"..."}

    internal static class Proto
    {
        public static void WriteValue(Utf8JsonWriter w, object v)
        {
            v = Db.Normalize(v);
            if (v == null) w.WriteNullValue();
            else if (v is long l) w.WriteNumberValue(l);
            else if (v is double d) w.WriteNumberValue(d);
            else if (v is byte[] bytes) w.WriteStringValue(Convert.ToBase64String(bytes));
            else w.WriteStringValue(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
        }

        public static object ReadValue(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;
                case JsonValueKind.Number:
                    long l;
                    if (e.TryGetInt64(out l)) return l;
                    return e.GetDouble();
                case JsonValueKind.True: return 1L;
                case JsonValueKind.False: return 0L;
                default: return e.GetString();
            }
        }

        public static string Request(string op, string sql, (string, object)[] p, Action<Utf8JsonWriter> extra = null)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteString("op", op);
                    if (sql != null) w.WriteString("sql", sql);
                    w.WriteStartArray("p");
                    if (p != null)
                        foreach (var (n, v) in p)
                        {
                            w.WriteStartArray();
                            w.WriteStringValue(n);
                            WriteValue(w, v);
                            w.WriteEndArray();
                        }
                    w.WriteEndArray();
                    extra?.Invoke(w);
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static (string, object)[] ReadParams(JsonElement root)
        {
            JsonElement arr;
            if (!root.TryGetProperty("p", out arr) || arr.ValueKind != JsonValueKind.Array) return new (string, object)[0];
            return arr.EnumerateArray().Select(x => (x[0].GetString(), ReadValue(x[1]))).ToArray();
        }

        public static List<DbRow> ReadRows(JsonElement root)
        {
            var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (var c in root.GetProperty("cols").EnumerateArray())
            {
                var n = c.GetString();
                if (!cols.ContainsKey(n)) cols[n] = i;
                i++;
            }
            var list = new List<DbRow>();
            foreach (var r in root.GetProperty("rows").EnumerateArray())
                list.Add(new DbRow(cols, r.EnumerateArray().Select(ReadValue).ToArray()));
            return list;
        }
    }

    /// <summary>Ana kasaya TCP ile bağlanan istemci. Tek kalıcı bağlantı kullanır.</summary>
    public class RemoteBackend : IDbBackend
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _key;
        private TcpClient _tcp;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly object _lock = new object();

        public RemoteBackend(string host, int port, string key)
        {
            _host = host;
            _port = port;
            _key = key ?? "";
        }

        public string Endpoint => _host + ":" + _port;

        private void EnsureConnected()
        {
            if (_tcp != null && _tcp.Connected) return;
            Close();
            _tcp = new TcpClient { NoDelay = true, ReceiveTimeout = 30000, SendTimeout = 30000 };
            var ar = _tcp.BeginConnect(_host, _port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(5000))
            {
                Close();
                throw new IOException("Ana kasaya bağlanılamadı (" + Endpoint + "). Ana kasa açık mı, aynı ağda mısınız?");
            }
            _tcp.EndConnect(ar);
            var ns = _tcp.GetStream();
            _reader = new StreamReader(ns, new UTF8Encoding(false));
            _writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var hello = Send(Proto.Request("hello", null, null, w => w.WriteString("k", _key)), false);
            hello.Dispose();
        }

        private void Close()
        {
            try { _tcp?.Close(); } catch { }
            _tcp = null;
            _reader = null;
            _writer = null;
        }

        private JsonDocument Send(string json, bool connect = true)
        {
            if (connect) EnsureConnected();
            _writer.WriteLine(json);
            var line = _reader.ReadLine();
            if (line == null) { Close(); throw new IOException("Ana kasa bağlantısı koptu."); }
            var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.GetProperty("ok").GetBoolean())
            {
                var err = doc.RootElement.TryGetProperty("err", out var e) ? e.GetString() : "Bilinmeyen hata";
                doc.Dispose();
                throw new InvalidOperationException("Ana kasa: " + err);
            }
            return doc;
        }

        /// <summary>Bağlantı koparsa bir kez yeniden dener (işlem dışı çağrılar için).</summary>
        private T Call<T>(string json, Func<JsonElement, T> read)
        {
            lock (_lock)
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        using (var doc = Send(json)) return read(doc.RootElement);
                    }
                    catch (IOException) when (attempt == 0)
                    {
                        Close();
                    }
                }
            }
        }

        public int Exec(string sql, (string, object)[] p) =>
            Call(Proto.Request("x", sql, p), r => r.GetProperty("n").GetInt32());

        public object Scalar(string sql, (string, object)[] p) =>
            Call(Proto.Request("s", sql, p), r => Proto.ReadValue(r.GetProperty("v")));

        public List<DbRow> Query(string sql, (string, object)[] p) =>
            Call(Proto.Request("q", sql, p), Proto.ReadRows);

        public void Ping() => Call(Proto.Request("ping", null, null), r => 0);

        private class RemoteTx : IDbTx
        {
            private readonly RemoteBackend _b;
            public RemoteTx(RemoteBackend b) { _b = b; }
            private T Do<T>(string json, Func<JsonElement, T> read) { using (var d = _b.Send(json)) return read(d.RootElement); }
            public int Exec(string sql, params (string, object)[] p) => Do(Proto.Request("x", sql, p), r => r.GetProperty("n").GetInt32());
            public long Insert(string sql, params (string, object)[] p) => Do(Proto.Request("i", sql, p), r => r.GetProperty("id").GetInt64());
            public object Scalar(string sql, params (string, object)[] p) => Do(Proto.Request("s", sql, p), r => Proto.ReadValue(r.GetProperty("v")));
            public List<DbRow> Query(string sql, params (string, object)[] p) => Do(Proto.Request("q", sql, p), Proto.ReadRows);
        }

        public void Tx(Action<IDbTx> work)
        {
            lock (_lock)
            {
                using (Send(Proto.Request("b", null, null))) { }
                try
                {
                    work(new RemoteTx(this));
                    using (Send(Proto.Request("c", null, null))) { }
                }
                catch
                {
                    try { using (Send(Proto.Request("r", null, null))) { } } catch { Close(); }
                    throw;
                }
            }
        }

        /// <summary>Ana kasadaki ürün görselini indirir.</summary>
        public byte[] GetFile(string name)
        {
            return Call(Proto.Request("img", null, null, w => w.WriteString("name", name)),
                r => r.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String ? Convert.FromBase64String(d.GetString()) : null);
        }

        public void PutFile(string name, byte[] data)
        {
            Call(Proto.Request("putimg", null, null, w => { w.WriteString("name", name); w.WriteString("data", Convert.ToBase64String(data)); }), r => 0);
        }
    }

    /// <summary>Ana kasada çalışan ağ sunucusu. Her terminal için ayrı bağlantı ve işlem.</summary>
    public static class DbServer
    {
        private static TcpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;
        private static string _key;
        public static int ClientCount;

        public static bool IsRunning => _running;

        public static string Start(int port, string key)
        {
            Stop();
            try
            {
                _key = key ?? "";
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _running = true;
                _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "KasaPOS sunucu" };
                _thread.Start();
                return null;
            }
            catch (Exception ex)
            {
                _running = false;
                return "Ana kasa sunucusu başlatılamadı (port " + port + "): " + ex.Message;
            }
        }

        public static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        private static void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    var t = new Thread(() => Serve(client)) { IsBackground = true };
                    t.Start();
                }
                catch
                {
                    if (!_running) return;
                    Thread.Sleep(200);
                }
            }
        }

        private static string Ok(Action<Utf8JsonWriter> body = null)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteBoolean("ok", true);
                    body?.Invoke(w);
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static string Err(string msg)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteBoolean("ok", false);
                    w.WriteString("err", msg);
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static void Serve(TcpClient client)
        {
            Interlocked.Increment(ref ClientCount);
            SqliteConnection con = null;
            SqliteTransaction tx = null;
            bool authed = false;
            try
            {
                client.NoDelay = true;
                var ns = client.GetStream();
                var reader = new StreamReader(ns, new UTF8Encoding(false));
                var writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                con = Db.Open();
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    string resp;
                    try
                    {
                        using (var doc = JsonDocument.Parse(line))
                        {
                            var root = doc.RootElement;
                            var op = root.GetProperty("op").GetString();
                            if (op == "hello")
                            {
                                var k = root.TryGetProperty("k", out var ke) ? ke.GetString() : "";
                                authed = string.Equals(k ?? "", _key, StringComparison.Ordinal);
                                resp = authed ? Ok(w => w.WriteString("ver", AppInfo.Version)) : Err("Ağ şifresi hatalı.");
                            }
                            else if (!authed) resp = Err("Önce bağlantı şifresi gerekli.");
                            else
                            {
                                string sql = root.TryGetProperty("sql", out var se) ? se.GetString() : null;
                                var p = Proto.ReadParams(root);
                                switch (op)
                                {
                                    case "ping": resp = Ok(); break;
                                    case "x":
                                        int n = SqliteRunner.Exec(con, tx, sql, p);
                                        resp = Ok(w => w.WriteNumber("n", n));
                                        break;
                                    case "i":
                                        long id = SqliteRunner.Insert(con, tx, sql, p);
                                        resp = Ok(w => w.WriteNumber("id", id));
                                        break;
                                    case "s":
                                        var v = SqliteRunner.Scalar(con, tx, sql, p);
                                        resp = Ok(w => { w.WritePropertyName("v"); Proto.WriteValue(w, v); });
                                        break;
                                    case "q":
                                        var rows = SqliteRunner.Query(con, tx, sql, p);
                                        var colNames = ColumnNames(con, tx, sql, p, rows);
                                        resp = Ok(w =>
                                        {
                                            w.WriteStartArray("cols");
                                            foreach (var c in colNames) w.WriteStringValue(c);
                                            w.WriteEndArray();
                                            w.WriteStartArray("rows");
                                            foreach (var r in rows)
                                            {
                                                w.WriteStartArray();
                                                for (int i = 0; i < colNames.Count; i++) Proto.WriteValue(w, r.IsDBNull(i) ? null : r.GetValue(i));
                                                w.WriteEndArray();
                                            }
                                            w.WriteEndArray();
                                        });
                                        break;
                                    case "b":
                                        tx?.Rollback();
                                        tx = con.BeginTransaction();
                                        resp = Ok();
                                        break;
                                    case "c":
                                        tx?.Commit();
                                        tx = null;
                                        resp = Ok();
                                        break;
                                    case "r":
                                        tx?.Rollback();
                                        tx = null;
                                        resp = Ok();
                                        break;
                                    case "img":
                                        var name = Path.GetFileName(root.GetProperty("name").GetString() ?? "");
                                        var path = Path.Combine(Db.ImageDir, name);
                                        resp = File.Exists(path) ? Ok(w => w.WriteString("data", Convert.ToBase64String(File.ReadAllBytes(path)))) : Ok();
                                        break;
                                    case "putimg":
                                        var pname = Path.GetFileName(root.GetProperty("name").GetString() ?? "");
                                        Directory.CreateDirectory(Db.ImageDir);
                                        File.WriteAllBytes(Path.Combine(Db.ImageDir, pname), Convert.FromBase64String(root.GetProperty("data").GetString()));
                                        resp = Ok();
                                        break;
                                    default:
                                        resp = Err("Bilinmeyen işlem: " + op);
                                        break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        resp = Err(ex.Message);
                    }
                    writer.WriteLine(resp);
                }
            }
            catch { }
            finally
            {
                try { tx?.Rollback(); } catch { }
                try { con?.Dispose(); } catch { }
                try { client.Close(); } catch { }
                Interlocked.Decrement(ref ClientCount);
            }
        }

        /// <summary>Sütun adlarını boş sonuçta da döndürebilmek için okuyucudan alır.</summary>
        private static List<string> ColumnNames(SqliteConnection con, SqliteTransaction tx, string sql, (string, object)[] p, List<DbRow> rows)
        {
            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                Db.AddParams(cmd, p);
                using (var r = cmd.ExecuteReader(System.Data.CommandBehavior.SchemaOnly))
                {
                    var list = new List<string>();
                    for (int i = 0; i < r.FieldCount; i++) list.Add(r.GetName(i));
                    return list;
                }
            }
        }

        /// <summary>Bu bilgisayarın yerel ağ (IPv4) adresleri.</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(a.Address.ToString());
                }
            }
            catch { }
            return list.Distinct().ToList();
        }
    }

    /// <summary>
    /// Bu bilgisayara özel ayarlar (yazıcılar, Caller ID, lisans, ağ modu).
    /// Ağda her terminalin kendi yazıcısı ve lisansı olduğu için ortak veritabanına yazılmaz.
    /// </summary>
    public static class LocalConfig
    {
        private static readonly string[] LocalKeys =
        {
            "printer_receipt", "printer_kitchen", "receipt_auto", "kitchen_auto", "kitchen_screen", "courier_print",
            "receipt_chars", "receipt_width_px", "receipt_font_size",
            "callerid_enabled", "callerid_port", "callerid_baud", "callerid_init",
            "update_auto", "license_key", "trial_start",
            "net_mode", "net_host", "net_port", "net_key"
        };

        private static Dictionary<string, string> _values;
        private static string FilePath => Path.Combine(Db.DataDir, "bu-bilgisayar.json");

        public static bool IsLocalKey(string key) => LocalKeys.Contains(key);

        private static void EnsureLoaded()
        {
            if (_values != null) return;
            _values = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
                    if (d != null) _values = d;
                }
            }
            catch { }
        }

        public static bool TryGet(string key, out string value)
        {
            EnsureLoaded();
            return _values.TryGetValue(key, out value);
        }

        public static string Get(string key, string def = "")
        {
            string v;
            return TryGet(key, out v) ? v : def;
        }

        public static void Set(string key, string value)
        {
            EnsureLoaded();
            _values[key] = value ?? "";
            try
            {
                Directory.CreateDirectory(Db.DataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static string Mode => Get("net_mode", "single");
        public static string Host => Get("net_host", "");
        public static int Port
        {
            get { int p; return int.TryParse(Get("net_port", "8787"), out p) && p > 0 ? p : 8787; }
        }
        public static string Key => Get("net_key", "");
    }
}
