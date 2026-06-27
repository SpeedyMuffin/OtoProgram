using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;

namespace OtoProgram
{
    public partial class HbysModullerWindow : Window
    {
        private string sourceRoot = @"\\192.168.1.10\p\hbys_otomasyon";
        private string targetRoot = @"C:\hbys_otomasyon";
        private string iconSource = @"\\192.168.1.100\d$\Programlar\OtoProgram\Simgeler";
        private string iconTarget = @"C:\hbys_otomasyon\icons";

        // Modül Eşleştirme Sözlüğü: XAML Content -> { Klasör Adı, Dosya Adı, İkon Adı }
        private Dictionary<string, (string klasor, string exe, string icon)> modulVerileri = new Dictionary<string, (string klasor, string exe, string icon)>
        {
            { "Ambar Modülü (Demo - Yer Tutucu)", ("ambar", "Ambar.bat", "ambar.ico") },
            { "Satınalma Modülü (Demo - Yer Tutucu)", ("satinalma", "YSATINALMA.bat", "satinalma.ico") },
            { "Demirbaş Modülü (Demo - Yer Tutucu)", ("demirbas", "Demirbas.bat", "demirbas.ico") },
            { "Sicil Modülü (Demo - Yer Tutucu)", ("msicil", "Personel.bat", "msicil.ico") },
            { "Muhasebe Modülü (Demo - Yer Tutucu)", ("muhasebe", "Muhasebe.bat", "muhasebe.ico") },
            { "Laboratuvar LIS Modülü (Demo - Yer Tutucu)", ("ProLIS", "LIS.exe", "") }
        };

        public HbysModullerWindow()
        {
            InitializeComponent();
            if (MainWindow.IsDemoMode)
            {
                this.Title = "HBYS Yardımcı Modülleri (DEMO - Yer Tutucu)";
                this.Loaded += (s, ev) => {
                    this.Title = "HBYS Yardımcı Modülleri (DEMO - Yer Tutucu)";
                };
            }
        }

        private async void btnModulKur_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var secilenler = stackYanModuller.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).ToList();

            if (!secilenler.Any() && chkAlisKur.IsChecked != true)
            {
                MessageBox.Show("Lütfen yapılacak bir işlem seçin.");
                return;
            }

            btnModulKur.IsEnabled = false;

            try
            {
                // 0. Simgeleri Güncelle
                lblDurum.Text = "Simgeler ağdan yerel diske kopyalanıyor...";
                await Task.Run(() => ExecuteCommand("robocopy", $"\"{iconSource}\" \"{iconTarget}\" /MIR /R:1 /W:1"));

                // 1. HBYS MODÜLLERİNİ KUR
                foreach (var cb in secilenler)
                {
                    string cbContent = cb.Content.ToString();
                    if (modulVerileri.ContainsKey(cbContent))
                    {
                        var veri = modulVerileri[cbContent];
                        lblDurum.Text = $"{cbContent} kuruluyor...";

                        // Klasörü kopyala
                        await Task.Run(() => ModulKopyala(veri.klasor));

                        // Kısayol oluştur
                        string hedefDosya = Path.Combine(targetRoot, veri.klasor, veri.exe);
                        KisayolOlustur(cbContent, hedefDosya, veri.icon);
                    }
                }

                // 2. LABORATUVAR MODÜLÜ KURULUMU (Hibrit ve Beklemeli)
                if (chkAlisKur.IsChecked == true)
                {
                    await LabKurulumuAdimlari();
                }

                lblDurum.Text = "Tüm işlemler başarıyla tamamlandı.";
                MessageBox.Show("Modüller, Laboratuvar Modülü, ODAC ve Simgeler başarıyla kuruldu.", "Kurulum Başarılı");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Hata oluştu: " + ex.Message);
            }
            finally
            {
                btnModulKur.IsEnabled = true;
            }
        }
        private async Task LabKurulumuAdimlari()
        {
            try
            {
                // A. Laboratuvar Dosyaları (Boşluklu yol yönetimi düzeltildi)
                lblDurum.Text = "Laboratuvar uygulama dosyaları kopyalanıyor...";
                string labKaynak = @"\\192.168.1.10\p\LabModulu\Lab Yazilimi";
                string labHedef = @"C:\Lab Yazilimi";

                // Hedef klasörü kopyalamadan önce oluşturmak robocopy'nin işini kolaylaştırır
                if (!Directory.Exists(labHedef)) Directory.CreateDirectory(labHedef);

                // Komutu oluştururken tırnakları \" şeklinde kaçırmak en garanti yoldur
                await Task.Run(() => ExecuteCommand("robocopy", $"\"{labKaynak}\" \"{labHedef}\" /E /MT /R:3 /W:5"));

                // B. Instant Client & TNS Yapılandırması
                lblDurum.Text = "Oracle Instant Client yapılandırılıyor...";
                string icHedef = @"C:\instantclient_23_6";
                string tnsHedefYolu = Path.Combine(icHedef, "network");

                await Task.Run(() => ExecuteCommand("robocopy", $"\"\\\\192.168.1.150\\d$\\YazılımDestek\\kullanici\\OracleDatabaseClient\\instantclient_23_6\" \"{icHedef}\" /E /y"));

                if (!Directory.Exists(tnsHedefYolu)) Directory.CreateDirectory(tnsHedefYolu);

                lblDurum.Text = "TNS dosyaları ağdan kopyalanıyor...";
                string gizliKaynak = @"\\192.168.1.150\d$\YazılımDestek\kullanici\tnsnames_config";
                await Task.Run(() => ExecuteCommand("robocopy", $"\"{gizliKaynak}\" \"{tnsHedefYolu}\" /E /y"));

                await Task.Run(() => Environment.SetEnvironmentVariable("TNS_ADMIN", tnsHedefYolu, EnvironmentVariableTarget.Machine));

                // C. Bölge Ayarları
                LabBolgeAyarlariniYap();

                // D. RESMİ ODAC KURULUMU (Klasör doluysa atla)
                string odacYolu = @"C:\odac";
                bool odacDoluMu = Directory.Exists(odacYolu) && Directory.GetFileSystemEntries(odacYolu).Length > 0;

                if (odacDoluMu)
                {
                    lblDurum.Text = "ODAC zaten mevcut, atlanıyor...";
                    await Task.Delay(1000);
                }
                else
                {
                    lblDurum.Text = "Resmi ODAC kuruluyor...";
                    string odacSetup = @"\\192.168.1.10\p\diger_dosyalar\OdacSetup\ODTwithODAC112012\setup.exe";
                    string respFile = @"\\192.168.1.10\p\diger_dosyalar\OdacSetup\response_file\odacInstall";
                    await Task.Run(() => ExecuteCommand(odacSetup, $"-responseFile {respFile} -force -silent"));

                    string bitisKontrol = Path.Combine(odacYolu, "bin", "oracle.key");
                    int timeout = 0;
                    while (!File.Exists(bitisKontrol) && timeout < 60)
                    {
                        timeout++;
                        lblDurum.Text = $"ODAC bekleniyor... ({timeout}/60)";
                        await Task.Delay(5000);
                    }
                }

                // E. Kısayol Oluşturma (Burada kontrol ekledik)
                lblDurum.Text = "Kısayol oluşturuluyor...";
                string alisExeYolu = @"C:\Lab Yazilimi\Lab\LabOtomasyon.exe";

                if (File.Exists(alisExeYolu))
                {
                    KisayolOlustur("Laboratuvar Modulu", alisExeYolu, "");
                }
                else
                {
                    // Eğer dosya yoksa kopyalamada bir sorun olmuştur
                    MessageBox.Show("Hata: LabOtomasyon.exe bulunamadı. Kopyalama işlemini kontrol edin.");
                }

                lblDurum.Text = "İşlem tamamlandı.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Hata: " + ex.Message);
            }
        }

        private void KisayolOlustur(string modulAdi, string hedefDosya, string ikonAdi)
        {
            try
            {
                string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                string kisayolYolu = Path.Combine(publicDesktop, modulAdi + ".lnk");
                string iconLocation = string.IsNullOrEmpty(ikonAdi) ? hedefDosya : Path.Combine(iconTarget, ikonAdi);

                string psCommand = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{kisayolYolu}');" +
                                   $"$s.TargetPath='{hedefDosya}';" +
                                   $"$s.WorkingDirectory='{Path.GetDirectoryName(hedefDosya)}';" +
                                   $"$s.IconLocation='{iconLocation}';" +
                                   "$s.Save()";

                ExecuteCommand("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"");
            }
            catch { }
        }

        private void ModulKopyala(string klasor)
        {
            try
            {
                if (!Directory.Exists(targetRoot)) Directory.CreateDirectory(targetRoot);
                string source = Path.Combine(sourceRoot, klasor);
                string target = Path.Combine(targetRoot, klasor);
                ExecuteCommand("robocopy", $"\"{source}\" \"{target}\" /MIR /R:1 /W:1");
            }
            catch { }
        }

        private void ExecuteCommand(string fileName, string args)
        {
            try
            {
                Environment.SetEnvironmentVariable("SEE_MASK_NOZONECHECKS", "1");
                ProcessStartInfo psi = new ProcessStartInfo(fileName, args)
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using (Process p = Process.Start(psi)) p?.WaitForExit();
            }
            catch { }
            finally { Environment.SetEnvironmentVariable("SEE_MASK_NOZONECHECKS", null); }
        }

        private void LabBolgeAyarlariniYap()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\International", true))
                {
                    if (key != null)
                    {
                        key.SetValue("sDecimal", ".");
                        key.SetValue("sThousand", ",");
                        key.SetValue("sList", ",");
                    }
                }
            }
            catch { }
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void BtnKapat_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}