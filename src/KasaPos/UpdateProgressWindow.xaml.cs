using System.Windows;

namespace KasaPos
{
    public partial class UpdateProgressWindow : Window
    {
        public UpdateProgressWindow(string tag)
        {
            InitializeComponent();
            TxtTitle.Text = AppInfo.Name + " " + tag + " güncelleniyor";
        }

        public void Report(long done, long total)
        {
            if (total > 0)
            {
                double p = done * 100.0 / total;
                Bar.IsIndeterminate = false;
                Bar.Value = p;
                TxtPercent.Text = (done / 1048576.0).ToString("0.0") + " / " + (total / 1048576.0).ToString("0.0") + " MB  ·  %" + p.ToString("0");
            }
            else
            {
                Bar.IsIndeterminate = true;
                TxtPercent.Text = (done / 1048576.0).ToString("0.0") + " MB";
            }
        }

        public void Installing()
        {
            Bar.IsIndeterminate = true;
            TxtInfo.Text = "İndirme tamamlandı. Kurulum başlatılıyor, program birazdan yeniden açılacak…";
            TxtPercent.Text = "";
        }
    }
}
