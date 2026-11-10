using System;
using System.Collections.Generic;
using System.Linq;

namespace KasaPos.Data
{
    public static class OrderTypes
    {
        public const string Masa = "MASA";
        public const string GelAl = "GELAL";
        public const string Paket = "PAKET";

        public static string Label(string t)
        {
            switch (t)
            {
                case Masa: return "Masa";
                case Paket: return "Paket";
                default: return "Al Götür";
            }
        }
    }

    public class User
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Pin { get; set; }
        public string Role { get; set; } = "kasiyer";
        public bool Active { get; set; } = true;
        public bool IsAdmin => Role == "admin";
    }

    public class Category
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int Sort { get; set; }
        public bool Active { get; set; } = true;
        public string Color { get; set; }
        public override string ToString() => Name;
    }

    public class DiningTable
    {
        public long Id { get; set; }
        public string Area { get; set; } = "Salon";
        public string Name { get; set; }
        public int Sort { get; set; }
        public bool Active { get; set; } = true;
    }

    public class Product
    {
        public long Id { get; set; }
        public long CategoryId { get; set; }
        public string Name { get; set; }
        public decimal PriceMasa { get; set; }
        public decimal PriceGelAl { get; set; }
        public decimal PricePaket { get; set; }
        public int Sort { get; set; }
        public bool Active { get; set; } = true;
        public string Color { get; set; }
        public string Image { get; set; }
        public bool HasImage => !string.IsNullOrEmpty(Image);

        public decimal PriceFor(string orderType)
        {
            switch (orderType)
            {
                case OrderTypes.Masa: return PriceMasa;
                case OrderTypes.Paket: return PricePaket;
                default: return PriceGelAl;
            }
        }
    }

    public class Courier
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public bool Active { get; set; } = true;
    }

    public class KitchenTicket
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Status { get; set; }
    }

    public class PaymentType
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public bool IsCash { get; set; }
        public int Sort { get; set; }
        public bool Active { get; set; } = true;
    }

    public class Customer
    {
        public long Id { get; set; }
        public string Phone { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string Note { get; set; }
    }

    public class OrderItem
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public string Name { get; set; }
        public decimal Qty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal PrintedQty { get; set; }
        public string Note { get; set; }

        public decimal Total => Math.Round(Qty * UnitPrice, 2);
        public string QtyText => Qty.ToString("0.##", Money.Tr);
        public string TotalText => Money.Fmt(Total);
        public string DisplayName => string.IsNullOrWhiteSpace(Note) ? Name : Name + "\n   ↳ " + Note;
    }

    public class Payment
    {
        public long PaymentTypeId { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public string Display => Name + "  " + Money.FmtTl(Amount);
    }

    public class Order
    {
        public long Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? PaidAt { get; set; }
        public string BusinessDay { get; set; }
        public string OrderType { get; set; } = OrderTypes.GelAl;
        public string TableNo { get; set; }
        public long? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string CustomerPhone { get; set; }
        public string CustomerAddress { get; set; }
        public decimal Discount { get; set; }
        public string Status { get; set; } = "OPEN";
        public long UserId { get; set; }
        public string UserName { get; set; }
        public string Note { get; set; }
        public long? CourierId { get; set; }
        public string CourierName { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
        public List<Payment> Payments { get; set; } = new List<Payment>();

        public decimal Subtotal => Items.Sum(i => i.Total);
        public decimal Total => Math.Max(0, Subtotal - Discount);

        public string Title
        {
            get
            {
                string head;
                if (OrderType == OrderTypes.Masa) head = "Masa " + TableNo;
                else if (OrderType == OrderTypes.Paket)
                    head = "Paket: " + (string.IsNullOrWhiteSpace(CustomerName) ? CustomerPhone : CustomerName);
                else head = "Al Götür #" + Id;
                string line = head + "\n" + Money.FmtTl(Total) + "  ·  " + CreatedAt.ToString("HH:mm");
                if (!string.IsNullOrEmpty(CourierName) && DispatchedAt.HasValue)
                    line += "\n🛵 " + CourierName + " · " + (int)Math.Max(0, (DateTime.Now - DispatchedAt.Value).TotalMinutes) + " dk";
                return line;
            }
        }
    }
}
