using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using KasaPos.Data;

namespace KasaPos
{
    /// <summary>GitHub sürümlerinden yeni Setup dosyasını bulur, indirir ve sessiz kurar.</summary>
    public static class Updater
    {
        public class UpdateInfo
        {
            public Version Version { get; set; }
            public string Tag { get; set; }
            public string SetupUrl { get; set; }
            public string Notes { get; set; }
        }

        public static string Repo => Settings.Get("update_repo", "lykia1/kasapos");

        private static HttpClient NewClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.Version);
            return c;
        }

        public static Version Current
        {
            get
            {
                Version v;
                return Version.TryParse(AppInfo.Version, out v) ? v : new Version(0, 0, 0);
            }
        }

        /// <summary>Daha yeni bir sürüm varsa bilgisini döner, yoksa null.</summary>
        public static async Task<UpdateInfo> CheckAsync()
        {
            using (var http = NewClient())
            {
                var json = await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest");
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    var tag = root.GetProperty("tag_name").GetString() ?? "";
                    Version v;
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out v)) return null;
                    if (v <= Current) return null;
                    string url = null;
                    foreach (var a in root.GetProperty("assets").EnumerateArray())
                    {
                        var name = a.GetProperty("name").GetString() ?? "";
                        if (name.StartsWith("KasaPOS-Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            url = a.GetProperty("browser_download_url").GetString();
                    }
                    if (url == null) return null;
                    string notes = root.TryGetProperty("body", out var b) ? b.GetString() : "";
                    return new UpdateInfo { Version = v, Tag = tag, SetupUrl = url, Notes = notes };
                }
            }
        }

        /// <summary>Kurulumu ilerleme göstererek indirir, sessiz başlatır ve programı kapatır.</summary>
        public static async Task<string> DownloadAndInstallAsync(UpdateInfo info)
        {
            var win = new UpdateProgressWindow(info.Tag);
            win.Show();
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "KasaPOS-Setup-" + info.Tag + ".exe");
                using (var http = NewClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(15);
                    using (var resp = await http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        resp.EnsureSuccessStatusCode();
                        long total = resp.Content.Headers.ContentLength ?? -1;
                        using (var src = await resp.Content.ReadAsStreamAsync())
                        using (var dst = File.Create(path))
                        {
                            var buf = new byte[81920];
                            long done = 0;
                            int n;
                            var last = DateTime.MinValue;
                            while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                            {
                                await dst.WriteAsync(buf, 0, n);
                                done += n;
                                if ((DateTime.Now - last).TotalMilliseconds > 150)
                                {
                                    win.Report(done, total);
                                    last = DateTime.Now;
                                }
                            }
                            win.Report(done, total);
                        }
                    }
                }
                win.Installing();
                Db.Backup(true);
                await Task.Delay(600);
                Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART") { UseShellExecute = true });
                Application.Current.Shutdown();
                return null;
            }
            catch (Exception ex)
            {
                win.Close();
                return ex.Message;
            }
        }

        /// <summary>Açılışta sessizce kontrol eder; yeni sürüm varsa sorar.</summary>
        public static async void CheckOnStartup(Window owner)
        {
            if (!Settings.GetBool("update_auto", true)) return;
            try
            {
                var info = await CheckAsync();
                if (info == null) return;
                await PromptAndInstall(owner, info);
            }
            catch
            {
                // İnternet yoksa sessizce geç
            }
        }

        public static async Task PromptAndInstall(Window owner, UpdateInfo info)
        {
            var r = MessageBox.Show(owner,
                "Yeni sürüm hazır: " + info.Tag + " (şu anki: v" + AppInfo.Version + ")\n\nŞimdi güncellensin mi? Program kapanıp yeni sürümle yeniden açılacak. Verilerin korunur.",
                AppInfo.Name + " Güncelleme", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r != MessageBoxResult.Yes) return;
            var err = await DownloadAndInstallAsync(info);
            if (err != null) MessageBox.Show(owner, "Güncelleme indirilemedi: " + err, AppInfo.Name);
        }
    }
}
