using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using KasaPos.Data;

namespace KasaPos
{
    public partial class App : Application
    {
        public static User CurrentUser { get; set; }

        private void App_Startup(object sender, StartupEventArgs e)
        {
            var tr = new CultureInfo("tr-TR");
            Thread.CurrentThread.CurrentCulture = tr;
            Thread.CurrentThread.CurrentUICulture = tr;
            CultureInfo.DefaultThreadCurrentCulture = tr;
            CultureInfo.DefaultThreadCurrentUICulture = tr;
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("tr-TR")));

            if (!StartData())
            {
                Shutdown();
                return;
            }

            if (!UserRepo.Any())
            {
                var setup = new SetupWindow();
                if (setup.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            if (Licensing.State == LicenseState.Expired)
            {
                var lw = new LicenseWindow();
                if (lw.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            ShowLoginThenMain();
        }

        /// <summary>Ağ moduna göre veritabanını hazırlar. Terminal modunda ana kasaya bağlanır.</summary>
        private bool StartData()
        {
            System.IO.Directory.CreateDirectory(Db.DataDir);
            if (LocalConfig.Mode == "client")
            {
                while (true)
                {
                    try
                    {
                        var rb = new RemoteBackend(LocalConfig.Host, LocalConfig.Port, LocalConfig.Key);
                        Db.Backend = rb;
                        rb.Ping();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        var r = MessageBox.Show("Ana kasaya bağlanılamadı (" + LocalConfig.Host + ":" + LocalConfig.Port + ").\n\n" + ex.Message +
                            "\n\nEvet: tekrar dene\nHayır: bu bilgisayarı tek başına çalıştır (bağlantı ayarı silinir)\nİptal: programdan çık",
                            AppInfo.Name, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                        if (r == MessageBoxResult.Yes) continue;
                        if (r == MessageBoxResult.No)
                        {
                            LocalConfig.Set("net_mode", "single");
                            Db.Backend = new LocalBackend();
                            break;
                        }
                        return false;
                    }
                }
            }

            try
            {
                Db.Init();
                Db.Backup();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Veritabanı açılamadı:\n" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (LocalConfig.Mode == "server")
            {
                var err = DbServer.Start(LocalConfig.Port, LocalConfig.Key);
                if (err != null) MessageBox.Show(err + "\n\nBu bilgisayar yine de tek başına çalışacak.", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return true;
        }

        /// <summary>Programı yeniden başlatır (ağ ayarı değişince).</summary>
        public static void Restart()
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch { }
            DbServer.Stop();
            Current.Shutdown();
        }

        public static void ShowLoginThenMain()
        {
            var login = new LoginWindow();
            if (login.ShowDialog() != true)
            {
                Current.Shutdown();
                return;
            }
            CurrentUser = login.User;
            var main = new MainWindow();
            Current.MainWindow = main;
            main.Show();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                File.AppendAllText(Path.Combine(Db.DataDir, "hata.log"),
                    DateTime.Now.ToString("s") + "  " + e.Exception + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            MessageBox.Show("Beklenmeyen bir hata oluştu:\n" + e.Exception.Message + "\n\nAyrıntılar hata.log dosyasına yazıldı.",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
