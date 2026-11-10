using System;
using System.Windows;
using KasaPos.Data;

namespace KasaPos
{
    public partial class CallerPopup : Window
    {
        public string Phone { get; }
        public bool OpenOrder { get; private set; }

        public CallerPopup(string phone)
        {
            InitializeComponent();
            Phone = phone;
            TxtPhone.Text = phone;
            var c = CustomerRepo.ByPhone(phone);
            if (c == null)
            {
                TxtName.Text = "Kayıtlı olmayan müşteri";
                TxtAddress.Text = "Sipariş açınca ad ve adresi girersen kaydedilir.";
                CallRepo.Log(phone, null);
            }
            else
            {
                TxtName.Text = string.IsNullOrWhiteSpace(c.Name) ? "(adsız müşteri)" : c.Name;
                TxtAddress.Text = c.Address;
                var h = CallRepo.CustomerHistory(c.Id);
                TxtHistory.Text = h.count == 0 ? "Daha önce sipariş yok." :
                    h.count + " sipariş · toplam " + Money.FmtTl(h.total) + (h.last.HasValue ? " · son: " + h.last.Value.ToString("dd.MM.yyyy") : "")
                    + (string.IsNullOrWhiteSpace(c.Note) ? "" : "\nNot: " + c.Note);
                CallRepo.Log(phone, c.Id);
            }
            Loaded += (s, e) =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 16;
                Top = wa.Bottom - ActualHeight - 16;
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            OpenOrder = true;
            Close();
        }
    }
}
