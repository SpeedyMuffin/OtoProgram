using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OtoProgram
{
    public partial class QuickServicesWindow : Window
    {
        public string SelectedServiceName { get; private set; }

        public class QuickServiceInfo
        {
            public string FriendlyName { get; set; }
            public string ServiceName { get; set; }
            public string Icon { get; set; }
            public string Description { get; set; }
            public string Tip { get; set; }
        }

        private List<QuickServiceInfo> _services = new List<QuickServiceInfo>
        {
            new QuickServiceInfo
            {
                FriendlyName = "Yazıcı Biriktiricisi",
                ServiceName = "Spooler",
                Icon = "🖨️",
                Description = "Yazıcı işlerini arka planda yönetir ve kuyruğa alır. Yazdırma birimini yöneten en kritik servistir.",
                Tip = "Yazıcı donmaları, çıktı alamama, kuyrukta iş takılması ve ağ yazıcılarına erişim sorunlarında resetlenmelidir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Windows Defender Antivirüs",
                ServiceName = "WinDefend",
                Icon = "🔒",
                Description = "Windows Güvenliği ve antivirüs korumasını (Microsoft Defender) gerçek zamanlı olarak yönetir.",
                Tip = "Güvenlik merkezi uyarıları, gerçek zamanlı koruma açılmama problemleri ve zararlı yazılım bloklamalarında kontrol edilmelidir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Uzak Yordam Çağrısı (RPC)",
                ServiceName = "RpcSs",
                Icon = "🔌",
                Description = "Windows işletim sisteminin diğer tüm servis ve süreçleri ile haberleşmesini sağlayan çekirdek servistir.",
                Tip = "RPC sunucusu kullanılamıyor hatalarında bakılır. Durdurulması sistem kararsızlığına yol açacağı için çok hassastır."
            },
            new QuickServiceInfo
            {
                FriendlyName = "DNS İstemcisi (DNS Client)",
                ServiceName = "Dnscache",
                Icon = "🌐",
                Description = "Domain adlarının IP karşılıklarını (DNS kayıtlarını) yerel bilgisayarda önbelleğe alır ve hızlı erişim sağlar.",
                Tip = "IP ile bağlanılabilen ama web/alan adıyla bağlanılamayan ağ sorunlarında önbelleğin temizlenmesi ve bu servisin resetlenmesi gerekir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Windows Güncelleştirmeleri",
                ServiceName = "wuauserv",
                Icon = "🔄",
                Description = "Windows Update güncelleme paketlerini, güvenlik yamalarını denetler, arka planda indirir ve sisteme kurar.",
                Tip = "Güncelleme aranırken donma, 0x80... hata kodları alma durumunda durdurulup C:\\Windows\\SoftwareDistribution temizlenerek yeniden başlatılır."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Windows Yükleyici (Installer)",
                ServiceName = "msiserver",
                Icon = "📦",
                Description = "MSI paketleri (.msi uzantılı dosyalar) ile kurulan programların yükleme, onarım ve kaldırma işlemlerini yürütür.",
                Tip = "Bir program yüklenirken veya kaldırılırken 'Installer servisine erişilemedi' veya kilitlenme hatası alındığında yeniden başlatılmalıdır."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Windows Zamanı (Windows Time)",
                ServiceName = "W32Time",
                Icon = "⏱️",
                Description = "Ağdaki tüm bilgisayarların, etki alanındaki (Domain) sunucuların tarih ve saat senkronizasyonunu yönetir.",
                Tip = "Bilgisayar saatinin geri kalması, Kerberos yetkilendirme hataları veya etki alanına (AD) güven ilişkisi bozulduğunda kontrol edilmelidir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Uzak Kayıt Defteri (Remote Registry)",
                ServiceName = "RemoteRegistry",
                Icon = "⚙️",
                Description = "Uzak yöneticilerin ve yetkilendirilmiş programların bilgisayardaki Kayıt Defteri (Registry) ayarlarını değiştirmesini sağlar.",
                Tip = "Uzaktan envanter çekme, uzaktan kurulum veya yönetim araçları erişim hataları alıyorsa bu servisin 'Çalışıyor' durumuna getirilmesi gerekir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Görev Zamanlayıcı",
                ServiceName = "Schedule",
                Icon = "📅",
                Description = "Bilgisayarda önceden planlanmış veya belirli olaylar gerçekleştikten sonra çalışacak otomatik görevleri koordine eder.",
                Tip = "Zamanlanmış yedekleme betikleri, arka plan temizlik makroları veya periyodik görevler tetiklenmediğinde kontrol edilmelidir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Windows Arama (Search)",
                ServiceName = "WSearch",
                Icon = "🔍",
                Description = "Dosyalar, e-postalar, uygulamalar ve diğer içerikler için gelişmiş dizin oluşturma (indeksleme) ve hızlı arama sağlar.",
                Tip = "Windows arama kutusu çalışmadığında veya arama yavaşsa yeniden başlatılır. Yüksek Disk/CPU kullanımında geçici olarak durdurulabilir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Windows Uzak Yönetim (WinRM)",
                ServiceName = "WinRM",
                Icon = "💻",
                Description = "Uzaktan sistem yönetimi için kullanılan WS-Management protokolünü uygular (PowerShell Remoting altyapısı).",
                Tip = "Uzaktan PowerShell komutları çalıştırma (Enter-PSSession, Invoke-Command) veya WMI v2 bağlantı hatalarında çalışıyor olmalıdır."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Windows Güvenlik Duvarı",
                ServiceName = "mpssvc",
                Icon = "🛡️",
                Description = "Gelen ve giden ağ trafiğini tanımlanmış kurallara göre izleyerek bilgisayarı yetkisiz erişimlerden korur.",
                Tip = "Ağ paylaşımlarına erişememe, uzak masaüstü bağlantı engelleri veya uygulama portlarının bloklanması durumunda kuralları için kontrol edilir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Uzak Masaüstü Hizmetleri",
                ServiceName = "TermService",
                Icon = "🖥️",
                Description = "Kullanıcıların bilgisayara ağ üzerinden grafiksel arayüzle Uzak Masaüstü (RDP) bağlantısı yapmasını sağlar.",
                Tip = "RDP bağlantı hataları, oturum limiti aşımları veya uzak masaüstü portunun yanıt vermemesi durumunda kontrol edilmesi gereken ana servistir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Birim Gölge Kopyası (VSS)",
                ServiceName = "VSS",
                Icon = "💾",
                Description = "Yedekleme yazılımlarının ve sistem korumasının o andaki disk görüntüsünün (shadow copy) yedeğini almasını sağlar.",
                Tip = "Sistem geri yükleme noktası oluşturma başarısızlıklarında veya yedekleme programlarının kilitlenme/okuma hatalarında resetlenir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "Kullanıcı Profili Hizmeti",
                ServiceName = "ProfSvc",
                Icon = "👤",
                Description = "Kullanıcı profillerinin yüklenmesi, oluşturulması ve oturum kapatılırken profillerin kaydedilmesini yönetir.",
                Tip = "'Kullanıcı profili yüklenemedi' hatası veya geçici profil (temp profile) ile açılma durumlarında kontrol edilmelidir."
            },
            new QuickServiceInfo
            {
                FriendlyName = "WMI Yönetim Hizmeti",
                ServiceName = "winmgmt",
                Icon = "🤖",
                Description = "Windows Yönetim Araçları (WMI) verilerini ve işletim sistemi detaylarını programlara sunan temel arabirimdir.",
                Tip = "Donanım bilgisi sorgularında hata çıkması, WMI sorgulamalarının yanıt vermemesi veya sistem analiz kilitlenmelerinde resetlenmelidir."
            }
        };

        public QuickServicesWindow()
        {
            InitializeComponent();
            lstServices.ItemsSource = _services;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void BtnKapat_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void lstServices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstServices.SelectedItem is QuickServiceInfo info)
            {
                pnlDetails.Visibility = Visibility.Visible;
                lblDetailIcon.Text = info.Icon;
                lblDetailFriendlyName.Text = info.FriendlyName;
                lblDetailServiceName.Text = info.ServiceName;
                lblDetailDescription.Text = info.Description;
                lblDetailTip.Text = info.Tip;
            }
            else
            {
                pnlDetails.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (lstServices.SelectedItem is QuickServiceInfo info)
            {
                SelectedServiceName = info.ServiceName;
                this.DialogResult = true;
                this.Close();
            }
        }
    }
}
