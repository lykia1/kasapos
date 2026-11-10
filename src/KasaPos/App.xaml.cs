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

            try
            {
                Db.Init();
                Db.Backup();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Veritabanı açılamadı:\n" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
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

            ShowLoginThenMain();
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
