using System;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using KasaPos.Data;

namespace KasaPos
{
    /// <summary>
    /// Seri port (COM) üzerinden arayan numarayı okuyan Caller ID servisi.
    /// Cihazın gönderdiği metindeki 10-11 haneli telefon numarasını yakalar (ör. "NMBR = 05321234567").
    /// </summary>
    public static class CallerId
    {
        private static SerialPort _port;
        private static readonly StringBuilder Buffer = new StringBuilder();
        private static string _lastNumber;
        private static DateTime _lastAt = DateTime.MinValue;

        public static event Action<string> Call;

        public static bool IsRunning => _port != null && _port.IsOpen;

        public static string[] Ports()
        {
            try { return SerialPort.GetPortNames(); }
            catch { return new string[0]; }
        }

        /// <summary>Ayarlarda açıksa servisi başlatır. Hata olursa mesaj döner.</summary>
        public static string Start()
        {
            Stop();
            if (!Settings.GetBool("callerid_enabled", false)) return null;
            var name = Settings.Get("callerid_port");
            if (string.IsNullOrWhiteSpace(name)) return "Caller ID için COM portu seçilmemiş.";
            try
            {
                _port = new SerialPort(name, Settings.GetInt("callerid_baud", 9600))
                {
                    NewLine = "\r",
                    DtrEnable = true,
                    RtsEnable = true,
                    Encoding = Encoding.ASCII
                };
                _port.DataReceived += OnData;
                _port.Open();
                var init = Settings.Get("callerid_init");
                if (!string.IsNullOrWhiteSpace(init)) _port.Write(init.Trim() + "\r");
                return null;
            }
            catch (Exception ex)
            {
                _port = null;
                return "Caller ID başlatılamadı (" + name + "): " + ex.Message;
            }
        }

        public static void Stop()
        {
            try
            {
                if (_port != null)
                {
                    _port.DataReceived -= OnData;
                    if (_port.IsOpen) _port.Close();
                    _port.Dispose();
                }
            }
            catch { }
            _port = null;
        }

        private static void OnData(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var text = _port.ReadExisting();
                lock (Buffer)
                {
                    Buffer.Append(text);
                    if (Buffer.Length > 2000) Buffer.Remove(0, Buffer.Length - 500);
                    var m = Regex.Match(Buffer.ToString(), @"(?<!\d)(?:\+?90)?0?(\d{10})(?=\D)");
                    if (m.Success)
                    {
                        Buffer.Clear();
                        Raise("0" + m.Groups[1].Value);
                    }
                }
            }
            catch { }
        }

        /// <summary>Gerçek arama ya da test için çağrı olayını tetikler.</summary>
        public static void Raise(string number)
        {
            var phone = CustomerRepo.NormalizePhone(number);
            if (phone.Length < 10) return;
            // Aynı numara birkaç kez gelirse (her çalışta) tek pencere aç
            if (phone == _lastNumber && (DateTime.Now - _lastAt).TotalSeconds < 30) return;
            _lastNumber = phone;
            _lastAt = DateTime.Now;
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                var h = Call;
                if (h != null) h(phone);
            }));
        }
    }
}
