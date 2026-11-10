using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KasaPos.Data
{
    public class CashSession
    {
        public long Id { get; set; }
        public string BusinessDay { get; set; }
        public DateTime OpenedAt { get; set; }
        public string OpenedBy { get; set; }
        public decimal OpeningCash { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string ClosedBy { get; set; }
        public decimal? CountedCash { get; set; }
        public decimal? ExpectedCash { get; set; }
        public string Note { get; set; }

        public bool IsOpen => !ClosedAt.HasValue;
        public decimal? Diff => CountedCash.HasValue && ExpectedCash.HasValue ? CountedCash - ExpectedCash : null;
        public string OpenedText => OpenedAt.ToString("dd.MM.yyyy HH:mm") + " · " + OpenedBy;
        public string ClosedText => ClosedAt.HasValue ? ClosedAt.Value.ToString("dd.MM.yyyy HH:mm") + " · " + ClosedBy : "Açık";
        public string OpeningText => Money.Fmt(OpeningCash);
        public string ExpectedText => ExpectedCash.HasValue ? Money.Fmt(ExpectedCash.Value) : "";
        public string CountedText => CountedCash.HasValue ? Money.Fmt(CountedCash.Value) : "";
        public string DiffText => Diff.HasValue ? (Diff.Value > 0 ? "+" : "") + Money.Fmt(Diff.Value) : "";
    }

    public class CashSummary
    {
        public decimal Opening { get; set; }
        public decimal CashSales { get; set; }
        public decimal OtherSales { get; set; }
        public decimal MovesIn { get; set; }
        public decimal MovesOut { get; set; }
        public decimal Expenses { get; set; }
        public decimal Expected => Opening + CashSales + MovesIn - MovesOut - Expenses;
    }

    public static class CashRepo
    {
        private const string Iso = "yyyy-MM-dd HH:mm:ss";

        private static DateTime? Dt(string s) =>
            string.IsNullOrEmpty(s) ? (DateTime?)null : DateTime.ParseExact(s, Iso, CultureInfo.InvariantCulture);

        private static decimal? Dn(object v) => v == null || v == DBNull.Value ? (decimal?)null : Math.Round(Convert.ToDecimal(v), 2);

        public static List<CashSession> Recent(int n = 60) =>
            Db.Query("SELECT * FROM cash_sessions ORDER BY id DESC LIMIT " + n, r => new CashSession
            {
                Id = Db.L(r, "id"),
                BusinessDay = Db.S(r, "business_day"),
                OpenedAt = Dt(Db.S(r, "opened_at")).Value,
                OpenedBy = Db.S(r, "opened_by"),
                OpeningCash = Db.D(r, "opening_cash"),
                ClosedAt = Dt(Db.S(r, "closed_at")),
                ClosedBy = Db.S(r, "closed_by"),
                CountedCash = Dn(r.GetValue(r.GetOrdinal("counted_cash"))),
                ExpectedCash = Dn(r.GetValue(r.GetOrdinal("expected_cash"))),
                Note = Db.S(r, "note")
            });

        public static CashSession Current() => Recent(5).FirstOrDefault(s => s.IsOpen);

        public static void Open(decimal opening, string user)
        {
            var now = DateTime.Now;
            Db.Exec("INSERT INTO cash_sessions(business_day,opened_at,opened_by,opening_cash) VALUES(@d,@t,@u,@c)",
                ("@d", Settings.BusinessDay(now)), ("@t", now.ToString(Iso, CultureInfo.InvariantCulture)), ("@u", user), ("@c", opening));
        }

        public static void AddMove(long sessionId, decimal amount, string note, string user) =>
            Db.Exec("INSERT INTO cash_moves(session_id,created_at,amount,note,user_name) VALUES(@s,@t,@a,@n,@u)",
                ("@s", sessionId), ("@t", DateTime.Now.ToString(Iso, CultureInfo.InvariantCulture)), ("@a", amount), ("@n", note), ("@u", user));

        public static CashSummary Summary(CashSession s)
        {
            string from = s.OpenedAt.ToString(Iso, CultureInfo.InvariantCulture);
            string to = (s.ClosedAt ?? DateTime.Now.AddMinutes(1)).ToString(Iso, CultureInfo.InvariantCulture);
            var p = new (string, object)[] { ("@f", from), ("@t", to), ("@s", s.Id) };
            decimal cash = Dec(Db.Scalar(@"SELECT SUM(pm.amount) FROM payments pm JOIN orders o ON o.id=pm.order_id
LEFT JOIN payment_types pt ON pt.id=pm.payment_type_id
WHERE o.status='PAID' AND o.paid_at>=@f AND o.paid_at<@t AND COALESCE(pt.is_cash,0)=1", p));
            decimal all = Dec(Db.Scalar("SELECT SUM(pm.amount) FROM payments pm JOIN orders o ON o.id=pm.order_id WHERE o.status='PAID' AND o.paid_at>=@f AND o.paid_at<@t", p));
            decimal mIn = Dec(Db.Scalar("SELECT SUM(amount) FROM cash_moves WHERE session_id=@s AND amount>0", p));
            decimal mOut = -Dec(Db.Scalar("SELECT SUM(amount) FROM cash_moves WHERE session_id=@s AND amount<0", p));
            decimal exp = Dec(Db.Scalar("SELECT SUM(amount) FROM expenses WHERE created_at>=@f AND created_at<@t", p));
            return new CashSummary { Opening = s.OpeningCash, CashSales = cash, OtherSales = all - cash, MovesIn = mIn, MovesOut = mOut, Expenses = exp };
        }

        public static void Close(CashSession s, decimal counted, string user, string note)
        {
            var sum = Summary(s);
            Db.Exec("UPDATE cash_sessions SET closed_at=@t, closed_by=@u, counted_cash=@c, expected_cash=@e, note=@n WHERE id=@id",
                ("@t", DateTime.Now.ToString(Iso, CultureInfo.InvariantCulture)), ("@u", user), ("@c", counted), ("@e", sum.Expected), ("@n", note), ("@id", s.Id));
        }

        private static decimal Dec(object o) => o == null ? 0m : Math.Round(Convert.ToDecimal(o), 2);
    }

    public class StaffRow
    {
        public long UserId { get; set; }
        public string Name { get; set; }
        public bool Inside { get; set; }
        public DateTime? Since { get; set; }
        public string StatusText => Inside && Since.HasValue ? "İçeride · " + Since.Value.ToString("HH:mm") + "'dan beri" : "Dışarıda";
    }

    public class StaffTotal
    {
        public string Name { get; set; }
        public double Hours { get; set; }
        public int Days { get; set; }
        public decimal Wage { get; set; }
        public decimal Pay => Math.Round((decimal)Hours * Wage, 2);
        public string HoursText => Math.Floor(Hours) + " sa " + (int)Math.Round((Hours - Math.Floor(Hours)) * 60) + " dk";
        public string WageText => Wage > 0 ? Money.Fmt(Wage) : "-";
        public string PayText => Wage > 0 ? Money.FmtTl(Pay) : "-";
    }

    public static class StaffRepo
    {
        private const string Iso = "yyyy-MM-dd HH:mm:ss";

        public static List<StaffRow> Status()
        {
            var open = Db.Query("SELECT user_id, start_at FROM staff_shifts WHERE end_at IS NULL", r => (Db.L(r, "user_id"), Db.S(r, "start_at")));
            return UserRepo.All(true).Select(u =>
            {
                var o = open.FirstOrDefault(x => x.Item1 == u.Id);
                return new StaffRow
                {
                    UserId = u.Id,
                    Name = u.Name,
                    Inside = o.Item2 != null,
                    Since = o.Item2 != null ? DateTime.ParseExact(o.Item2, Iso, CultureInfo.InvariantCulture) : (DateTime?)null
                };
            }).ToList();
        }

        public static void ClockIn(User u) =>
            Db.Exec("INSERT INTO staff_shifts(user_id,user_name,start_at) VALUES(@u,@n,@s)",
                ("@u", u.Id), ("@n", u.Name), ("@s", DateTime.Now.ToString(Iso, CultureInfo.InvariantCulture)));

        public static void ClockOut(User u) =>
            Db.Exec("UPDATE staff_shifts SET end_at=@e WHERE user_id=@u AND end_at IS NULL",
                ("@e", DateTime.Now.ToString(Iso, CultureInfo.InvariantCulture)), ("@u", u.Id));

        public static List<StaffTotal> Totals(DateTime from, DateTime to)
        {
            string f = from.Date.ToString(Iso, CultureInfo.InvariantCulture);
            string t = to.Date.AddDays(1).ToString(Iso, CultureInfo.InvariantCulture);
            var rows = Db.Query("SELECT user_id, user_name, start_at, end_at FROM staff_shifts WHERE start_at>=@f AND start_at<@t",
                r => (Db.L(r, "user_id"), Db.S(r, "user_name"), Db.S(r, "start_at"), Db.S(r, "end_at")), ("@f", f), ("@t", t));
            var wages = Db.Query("SELECT id, hourly_wage FROM users", r => (Db.L(r, "id"), Db.D(r, "hourly_wage"))).ToDictionary(x => x.Item1, x => x.Item2);
            return rows.GroupBy(x => x.Item1).Select(g =>
            {
                double hours = 0;
                var days = new HashSet<string>();
                foreach (var x in g)
                {
                    var s = DateTime.ParseExact(x.Item3, Iso, CultureInfo.InvariantCulture);
                    var e = string.IsNullOrEmpty(x.Item4) ? DateTime.Now : DateTime.ParseExact(x.Item4, Iso, CultureInfo.InvariantCulture);
                    hours += Math.Max(0, (e - s).TotalHours);
                    days.Add(s.ToString("yyyy-MM-dd"));
                }
                decimal w;
                wages.TryGetValue(g.Key, out w);
                return new StaffTotal { Name = g.First().Item2, Hours = hours, Days = days.Count, Wage = w };
            }).OrderBy(x => x.Name).ToList();
        }
    }
}
