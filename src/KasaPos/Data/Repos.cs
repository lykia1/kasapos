using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KasaPos.Data
{
    public static class UserRepo
    {
        public static List<User> All(bool onlyActive = false) =>
            Db.Query("SELECT * FROM users" + (onlyActive ? " WHERE active=1" : "") + " ORDER BY name", r => new User
            {
                Id = Db.L(r, "id"),
                Name = Db.S(r, "name"),
                Pin = Db.S(r, "pin"),
                Role = Db.S(r, "role"),
                Active = Db.B(r, "active")
            });

        public static User ByPin(string pin) => All(true).FirstOrDefault(u => u.Pin == pin);

        public static bool Any() => Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM users")) > 0;

        public static void Save(User u)
        {
            if (u.Id == 0)
                Db.Exec("INSERT INTO users(name,pin,role,active) VALUES(@n,@p,@r,@a)",
                    ("@n", u.Name), ("@p", u.Pin), ("@r", u.Role ?? "kasiyer"), ("@a", u.Active));
            else
                Db.Exec("UPDATE users SET name=@n,pin=@p,role=@r,active=@a WHERE id=@id",
                    ("@n", u.Name), ("@p", u.Pin), ("@r", u.Role ?? "kasiyer"), ("@a", u.Active), ("@id", u.Id));
        }
    }

    public static class MenuRepo
    {
        public static List<Category> Categories(bool onlyActive = true) =>
            Db.Query("SELECT * FROM categories" + (onlyActive ? " WHERE active=1" : "") + " ORDER BY sort, name", r => new Category
            {
                Id = Db.L(r, "id"),
                Name = Db.S(r, "name"),
                Sort = (int)Db.L(r, "sort"),
                Active = Db.B(r, "active")
            });

        public static List<Product> Products(bool onlyActive = true, long? categoryId = null)
        {
            string sql = "SELECT * FROM products WHERE 1=1";
            if (onlyActive) sql += " AND active=1";
            if (categoryId.HasValue) sql += " AND category_id=@c";
            sql += " ORDER BY sort, name";
            return Db.Query(sql, r => new Product
            {
                Id = Db.L(r, "id"),
                CategoryId = Db.L(r, "category_id"),
                Name = Db.S(r, "name"),
                PriceMasa = Db.D(r, "price_masa"),
                PriceGelAl = Db.D(r, "price_gelal"),
                PricePaket = Db.D(r, "price_paket"),
                Sort = (int)Db.L(r, "sort"),
                Active = Db.B(r, "active")
            }, ("@c", categoryId ?? 0));
        }

        public static long SaveCategory(Category c)
        {
            if (c.Id == 0)
            {
                using (var con = Db.Open())
                    return Db.Insert(con, null, "INSERT INTO categories(name,sort,active) VALUES(@n,@s,@a)",
                        ("@n", c.Name), ("@s", c.Sort), ("@a", c.Active));
            }
            Db.Exec("UPDATE categories SET name=@n,sort=@s,active=@a WHERE id=@id",
                ("@n", c.Name), ("@s", c.Sort), ("@a", c.Active), ("@id", c.Id));
            return c.Id;
        }

        public static void SaveProduct(Product p)
        {
            if (p.Id == 0)
                Db.Exec("INSERT INTO products(category_id,name,price_masa,price_gelal,price_paket,sort,active) VALUES(@c,@n,@pm,@pg,@pp,@s,@a)",
                    ("@c", p.CategoryId), ("@n", p.Name), ("@pm", p.PriceMasa), ("@pg", p.PriceGelAl), ("@pp", p.PricePaket), ("@s", p.Sort), ("@a", p.Active));
            else
                Db.Exec("UPDATE products SET category_id=@c,name=@n,price_masa=@pm,price_gelal=@pg,price_paket=@pp,sort=@s,active=@a WHERE id=@id",
                    ("@c", p.CategoryId), ("@n", p.Name), ("@pm", p.PriceMasa), ("@pg", p.PriceGelAl), ("@pp", p.PricePaket), ("@s", p.Sort), ("@a", p.Active), ("@id", p.Id));
        }

        /// <summary>İlk kurulumda isteğe bağlı örnek döner menüsü.</summary>
        public static void SeedSampleMenu()
        {
            var data = new (string cat, (string name, decimal masa, decimal paket)[] items)[]
            {
                ("Dürümler", new[] { ("Tavuk Döner Dürüm", 150m, 165m), ("Et Döner Dürüm", 250m, 270m), ("Zurna Tavuk Dürüm", 190m, 205m) }),
                ("Porsiyonlar", new[] { ("Tavuk Döner Porsiyon", 220m, 240m), ("Et Döner Porsiyon", 350m, 375m), ("İskender", 380m, 400m), ("Pilav Üstü Tavuk Döner", 230m, 250m) }),
                ("Ekmek Arası", new[] { ("Yarım Ekmek Tavuk Döner", 140m, 155m), ("Tombik Tavuk Döner", 150m, 165m) }),
                ("İçecekler", new[] { ("Ayran", 30m, 35m), ("Kola", 50m, 55m), ("Şalgam", 35m, 40m), ("Su", 15m, 20m) }),
                ("Ekstralar", new[] { ("Patates Kızartması", 70m, 80m), ("Ekstra Sos", 10m, 10m) }),
            };
            int cs = 0;
            foreach (var cat in data)
            {
                long cid = SaveCategory(new Category { Name = cat.cat, Sort = cs++ });
                int ps = 0;
                foreach (var it in cat.items)
                    SaveProduct(new Product { CategoryId = cid, Name = it.name, PriceMasa = it.masa, PriceGelAl = it.masa, PricePaket = it.paket, Sort = ps++ });
            }
        }
    }

    public static class PaymentRepo
    {
        public static List<PaymentType> Types(bool onlyActive = true) =>
            Db.Query("SELECT * FROM payment_types" + (onlyActive ? " WHERE active=1" : "") + " ORDER BY sort, id", r => new PaymentType
            {
                Id = Db.L(r, "id"),
                Name = Db.S(r, "name"),
                IsCash = Db.B(r, "is_cash"),
                Sort = (int)Db.L(r, "sort"),
                Active = Db.B(r, "active")
            });

        public static void Save(PaymentType p)
        {
            if (p.Id == 0)
                Db.Exec("INSERT INTO payment_types(name,is_cash,sort,active) VALUES(@n,@c,@s,@a)",
                    ("@n", p.Name), ("@c", p.IsCash), ("@s", p.Sort), ("@a", p.Active));
            else
                Db.Exec("UPDATE payment_types SET name=@n,is_cash=@c,sort=@s,active=@a WHERE id=@id",
                    ("@n", p.Name), ("@c", p.IsCash), ("@s", p.Sort), ("@a", p.Active), ("@id", p.Id));
        }
    }

    public static class CustomerRepo
    {
        public static string NormalizePhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return "";
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.StartsWith("90") && digits.Length == 12) digits = digits.Substring(2);
            if (digits.Length == 10) digits = "0" + digits;
            return digits;
        }

        public static Customer ByPhone(string phone)
        {
            phone = NormalizePhone(phone);
            if (phone == "") return null;
            return Db.Query("SELECT * FROM customers WHERE phone=@p", r => new Customer
            {
                Id = Db.L(r, "id"),
                Phone = Db.S(r, "phone"),
                Name = Db.S(r, "name"),
                Address = Db.S(r, "address"),
                Note = Db.S(r, "note")
            }, ("@p", phone)).FirstOrDefault();
        }

        public static List<Customer> Search(string q) =>
            Db.Query("SELECT * FROM customers WHERE phone LIKE @q OR name LIKE @q ORDER BY name LIMIT 200", r => new Customer
            {
                Id = Db.L(r, "id"),
                Phone = Db.S(r, "phone"),
                Name = Db.S(r, "name"),
                Address = Db.S(r, "address"),
                Note = Db.S(r, "note")
            }, ("@q", "%" + (q ?? "") + "%"));

        public static long Upsert(string phone, string name, string address)
        {
            phone = NormalizePhone(phone);
            var existing = ByPhone(phone);
            if (existing == null)
            {
                using (var c = Db.Open())
                    return Db.Insert(c, null, "INSERT INTO customers(phone,name,address,created_at) VALUES(@p,@n,@a,@t)",
                        ("@p", phone), ("@n", name), ("@a", address), ("@t", DateTime.Now.ToString("s")));
            }
            Db.Exec("UPDATE customers SET name=@n,address=@a WHERE id=@id", ("@n", name), ("@a", address), ("@id", existing.Id));
            return existing.Id;
        }

        /// <summary>KVKK: müşteri kaydını siler, geçmiş siparişlerdeki kişisel bilgileri anonimleştirir.</summary>
        public static void DeleteAndAnonymize(long id)
        {
            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            {
                Db.Exec(c, tx, "UPDATE orders SET customer_id=NULL, customer_name='Silinmiş müşteri', customer_phone=NULL, customer_address=NULL WHERE customer_id=@id", ("@id", id));
                Db.Exec(c, tx, "DELETE FROM customers WHERE id=@id", ("@id", id));
                tx.Commit();
            }
        }
    }

    public static class OrderRepo
    {
        private const string Iso = "yyyy-MM-dd HH:mm:ss";

        private static Order MapOrder(Microsoft.Data.Sqlite.SqliteDataReader r)
        {
            var o = new Order
            {
                Id = Db.L(r, "id"),
                CreatedAt = DateTime.ParseExact(Db.S(r, "created_at"), Iso, CultureInfo.InvariantCulture),
                BusinessDay = Db.S(r, "business_day"),
                OrderType = Db.S(r, "order_type"),
                TableNo = Db.S(r, "table_no"),
                CustomerName = Db.S(r, "customer_name"),
                CustomerPhone = Db.S(r, "customer_phone"),
                CustomerAddress = Db.S(r, "customer_address"),
                Discount = Db.D(r, "discount"),
                Status = Db.S(r, "status"),
                UserId = Db.L(r, "user_id"),
                UserName = Db.S(r, "user_name"),
                Note = Db.S(r, "note")
            };
            long cid = Db.L(r, "customer_id");
            o.CustomerId = cid == 0 ? (long?)null : cid;
            var paid = Db.S(r, "paid_at");
            if (!string.IsNullOrEmpty(paid)) o.PaidAt = DateTime.ParseExact(paid, Iso, CultureInfo.InvariantCulture);
            return o;
        }

        public static List<Order> Open()
        {
            var list = Db.Query("SELECT * FROM orders WHERE status='OPEN' ORDER BY created_at", MapOrder);
            foreach (var o in list) o.Items = Items(o.Id);
            return list;
        }

        public static Order Get(long id)
        {
            var o = Db.Query("SELECT * FROM orders WHERE id=@id", MapOrder, ("@id", id)).FirstOrDefault();
            if (o != null) o.Items = Items(id);
            return o;
        }

        public static List<OrderItem> Items(long orderId) =>
            Db.Query("SELECT * FROM order_items WHERE order_id=@o ORDER BY id", r => new OrderItem
            {
                Id = Db.L(r, "id"),
                ProductId = Db.L(r, "product_id"),
                Name = Db.S(r, "name"),
                Qty = Db.D(r, "qty"),
                UnitPrice = Db.D(r, "unit_price"),
                PrintedQty = Db.D(r, "printed_qty"),
                Note = Db.S(r, "note")
            }, ("@o", orderId));

        public static bool TableIsBusy(string tableNo, long exceptOrderId) =>
            Convert.ToInt64(Db.Scalar("SELECT COUNT(*) FROM orders WHERE status='OPEN' AND order_type='MASA' AND table_no=@t AND id<>@id",
                ("@t", tableNo), ("@id", exceptOrderId))) > 0;

        /// <summary>Siparişi ve kalemlerini kaydeder. Ödendiyse ödemeleri de yazar.</summary>
        public static void Save(Order o)
        {
            if (string.IsNullOrEmpty(o.BusinessDay)) o.BusinessDay = Settings.BusinessDay(o.CreatedAt);
            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            {
                var p = new (string, object)[]
                {
                    ("@ca", o.CreatedAt.ToString(Iso, CultureInfo.InvariantCulture)),
                    ("@pa", o.PaidAt.HasValue ? o.PaidAt.Value.ToString(Iso, CultureInfo.InvariantCulture) : null),
                    ("@bd", o.BusinessDay), ("@ot", o.OrderType), ("@tn", o.TableNo),
                    ("@cid", o.CustomerId), ("@cn", o.CustomerName), ("@cp", o.CustomerPhone), ("@cad", o.CustomerAddress),
                    ("@st", o.Subtotal), ("@dc", o.Discount), ("@tt", o.Total), ("@s", o.Status),
                    ("@uid", o.UserId), ("@un", o.UserName), ("@nt", o.Note)
                };
                if (o.Id == 0)
                {
                    o.Id = Db.Insert(c, tx, @"INSERT INTO orders(created_at,paid_at,business_day,order_type,table_no,customer_id,customer_name,customer_phone,customer_address,subtotal,discount,total,status,user_id,user_name,note)
VALUES(@ca,@pa,@bd,@ot,@tn,@cid,@cn,@cp,@cad,@st,@dc,@tt,@s,@uid,@un,@nt)", p);
                }
                else
                {
                    var p2 = p.ToList();
                    p2.Add(("@id", o.Id));
                    Db.Exec(c, tx, @"UPDATE orders SET created_at=@ca,paid_at=@pa,business_day=@bd,order_type=@ot,table_no=@tn,customer_id=@cid,customer_name=@cn,customer_phone=@cp,customer_address=@cad,
subtotal=@st,discount=@dc,total=@tt,status=@s,user_id=@uid,user_name=@un,note=@nt WHERE id=@id", p2.ToArray());
                    Db.Exec(c, tx, "DELETE FROM order_items WHERE order_id=@id", ("@id", o.Id));
                    Db.Exec(c, tx, "DELETE FROM payments WHERE order_id=@id", ("@id", o.Id));
                }
                foreach (var it in o.Items)
                {
                    it.Id = Db.Insert(c, tx, "INSERT INTO order_items(order_id,product_id,name,qty,unit_price,printed_qty,note) VALUES(@o,@p,@n,@q,@u,@pq,@nt)",
                        ("@o", o.Id), ("@p", it.ProductId), ("@n", it.Name), ("@q", it.Qty), ("@u", it.UnitPrice), ("@pq", it.PrintedQty), ("@nt", it.Note));
                }
                foreach (var pay in o.Payments)
                {
                    Db.Exec(c, tx, "INSERT INTO payments(order_id,payment_type_id,payment_name,amount) VALUES(@o,@t,@n,@a)",
                        ("@o", o.Id), ("@t", pay.PaymentTypeId), ("@n", pay.Name), ("@a", pay.Amount));
                }
                tx.Commit();
            }
        }

        public static void SetStatus(long id, string status) =>
            Db.Exec("UPDATE orders SET status=@s WHERE id=@id", ("@s", status), ("@id", id));
    }

    public class Expense
    {
        public long Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string BusinessDay { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public string UserName { get; set; }
        public string TimeText => CreatedAt.ToString("HH:mm");
        public string AmountText => Money.FmtTl(Amount);
    }

    public static class ExpenseRepo
    {
        public static List<Expense> ForDay(string day) =>
            Db.Query("SELECT * FROM expenses WHERE business_day=@d ORDER BY created_at", r => new Expense
            {
                Id = Db.L(r, "id"),
                CreatedAt = DateTime.ParseExact(Db.S(r, "created_at"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                BusinessDay = Db.S(r, "business_day"),
                Description = Db.S(r, "description"),
                Amount = Db.D(r, "amount"),
                UserName = Db.S(r, "user_name")
            }, ("@d", day));

        public static void Add(string desc, decimal amount, string user)
        {
            var now = DateTime.Now;
            Db.Exec("INSERT INTO expenses(created_at,business_day,description,amount,user_name) VALUES(@c,@d,@x,@a,@u)",
                ("@c", now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)), ("@d", Settings.BusinessDay(now)),
                ("@x", desc), ("@a", amount), ("@u", user));
        }

        public static void Delete(long id) => Db.Exec("DELETE FROM expenses WHERE id=@id", ("@id", id));
    }
}
