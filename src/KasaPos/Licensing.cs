using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using KasaPos.Data;

namespace KasaPos
{
    public enum LicenseState { Licensed, Trial, Expired }

    /// <summary>
    /// İmzalı lisans anahtarı + 30 günlük deneme.
    /// Anahtar biçimi: KP1-{payload}.{imza}  (base64url). Payload: makineKodu|işletme|bitiş(yyyy-MM-dd veya boş)|sürüm
    /// İmza RSA-SHA256 ile üretilir; özel anahtar yalnızca lisans üretim aracındadır.
    /// </summary>
    public static class Licensing
    {
        private const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAuhiZ46AYFAHixbw6uT+h
IpeHUnQ5Us8/c1xVmUnwj2R1b9YDgVx+Rf73RSRenoWRzZVS76AhvWRpxpZx3W6D
AtJt9B/rIbRZ1ITh+usNdQinmHfhA/FlAJwduzgnKw0IZ6eLcDXB7TEwSKSh0aIR
9WQbt96TIjDnAlLzQaZdsyOYyzEHU+VWEcAqp6KLthM8kZe1bBpu5yAXTLFcB85Q
2xzQLYDg2hs2q7cLdejNnlvgN1EEpzOyHENckPJ+4vf+atsZx4ZFfGgH+XKMaav9
4OR27uRYQt2257gaf52n2XBDohU19dJUR0Wi+Fu+ba6I7FR1093/v2IhfIpa3aOG
gwIDAQAB
-----END PUBLIC KEY-----";

        public const int TrialDays = 30;
        private const string RegPath = @"Software\KasaPOS";

        public static string LicensedTo { get; private set; }
        public static DateTime? ExpiresOn { get; private set; }

        /// <summary>Bu bilgisayara özel kod (müşteri lisans isterken bunu gönderir).</summary>
        public static string MachineCode
        {
            get
            {
                string guid = "";
                try
                {
                    using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                        guid = k?.GetValue("MachineGuid") as string ?? "";
                }
                catch { }
                if (string.IsNullOrEmpty(guid)) guid = Environment.MachineName;
                using (var sha = SHA256.Create())
                {
                    var h = sha.ComputeHash(Encoding.UTF8.GetBytes("KasaPOS|" + guid.ToUpperInvariant()));
                    var hex = BitConverter.ToString(h).Replace("-", "").Substring(0, 12);
                    return hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" + hex.Substring(8, 4);
                }
            }
        }

        private static byte[] B64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        /// <summary>Anahtarı doğrular; geçerliyse payload alanlarını döner, değilse hata mesajı.</summary>
        public static bool TryValidate(string key, out string name, out DateTime? expiry, out string error)
        {
            name = null; expiry = null; error = null;
            try
            {
                key = new string((key ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
                if (!key.StartsWith("KP1-")) { error = "Anahtar biçimi hatalı."; return false; }
                var parts = key.Substring(4).Split('.');
                if (parts.Length != 2) { error = "Anahtar biçimi hatalı."; return false; }
                var payload = B64UrlDecode(parts[0]);
                var sig = B64UrlDecode(parts[1]);
                using (var rsa = RSA.Create())
                {
                    rsa.ImportFromPem(PublicKeyPem);
                    if (!rsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    {
                        error = "Anahtar geçersiz (imza doğrulanamadı).";
                        return false;
                    }
                }
                var f = Encoding.UTF8.GetString(payload).Split('|');
                if (f.Length < 3) { error = "Anahtar içeriği hatalı."; return false; }
                if (f[0] != "*" && !string.Equals(f[0], MachineCode, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Bu anahtar başka bir bilgisayar için üretilmiş.";
                    return false;
                }
                name = f[1];
                if (!string.IsNullOrWhiteSpace(f[2]))
                {
                    expiry = DateTime.ParseExact(f[2], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (expiry.Value.Date < DateTime.Today)
                    {
                        error = "Lisansın süresi " + expiry.Value.ToString("dd.MM.yyyy") + " tarihinde dolmuş.";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Anahtar okunamadı: " + ex.Message;
                return false;
            }
        }

        public static bool Activate(string key, out string error)
        {
            string name; DateTime? exp;
            if (!TryValidate(key, out name, out exp, out error)) return false;
            Settings.Set("license_key", key.Trim());
            LicensedTo = name;
            ExpiresOn = exp;
            return true;
        }

        private static DateTime TrialStart()
        {
            DateTime fromDb = DateTime.MinValue, fromReg = DateTime.MinValue;
            DateTime.TryParseExact(Settings.Get("trial_start"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fromDb);
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                    DateTime.TryParseExact(k?.GetValue("ts") as string ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fromReg);
            }
            catch { }
            var candidates = new[] { fromDb, fromReg }.Where(d => d > DateTime.MinValue).ToList();
            var start = candidates.Count > 0 ? candidates.Min() : DateTime.Today;
            if (start > DateTime.Today) start = DateTime.Today;
            var s = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Settings.Set("trial_start", s);
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegPath)) k?.SetValue("ts", s);
            }
            catch { }
            return start;
        }

        public static int TrialDaysLeft => Math.Max(0, TrialDays - (int)(DateTime.Today - TrialStart()).TotalDays);

        public static LicenseState State
        {
            get
            {
                string name, err; DateTime? exp;
                var key = Settings.Get("license_key");
                if (!string.IsNullOrWhiteSpace(key) && TryValidate(key, out name, out exp, out err))
                {
                    LicensedTo = name;
                    ExpiresOn = exp;
                    return LicenseState.Licensed;
                }
                LicensedTo = null;
                ExpiresOn = null;
                return TrialDaysLeft > 0 ? LicenseState.Trial : LicenseState.Expired;
            }
        }

        public static string StatusText
        {
            get
            {
                switch (State)
                {
                    case LicenseState.Licensed:
                        return "Lisanslı: " + LicensedTo + (ExpiresOn.HasValue ? " (bitiş " + ExpiresOn.Value.ToString("dd.MM.yyyy") + ")" : " (süresiz)");
                    case LicenseState.Trial:
                        return "Deneme sürümü — " + TrialDaysLeft + " gün kaldı";
                    default:
                        return "Deneme süresi doldu — lisans gerekli";
                }
            }
        }
    }
}
