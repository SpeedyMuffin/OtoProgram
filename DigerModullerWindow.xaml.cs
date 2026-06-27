using System;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Net.NetworkInformation;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Collections.ObjectModel; // Liste koleksiyonu için
using WUApiLib; // COM Referansı: wuapi 2.0 Type Library

namespace OtoProgram
{
    public partial class DigerModullerWindow : Window
    {
        private string _netPath;

        // Bulunan güncellemeleri arayüze bağlamak için kullanılan koleksiyon
        public ObservableCollection<UpdateItem> FoundUpdates { get; set; } = new ObservableCollection<UpdateItem>();

        public DigerModullerWindow(string networkPath)
        {
            InitializeComponent();
            _netPath = networkPath;
            // ListBox'ın veri kaynağını başlatıyoruz
            lstUpdates.ItemsSource = FoundUpdates;

            if (MainWindow.IsDemoMode)
            {
                this.Title = "Diğer Modüller (DEMO MODU)";
                try
                {
                    // Update header veya açıklamasını da güncelleyebiliriz
                    this.Loaded += (s, ev) => {
                        this.Title = "Diğer Modüller (DEMO MODU)";
                    };
                }
                catch { }
            }
        }

        // --- 1. WINDOWS UPDATE: SERVİS KONTROLÜ (Durdur / Başlat) ---
        private void btnUpdateServisKontrol_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string mod = (sender as System.Windows.Controls.Button).Tag.ToString();
            bool baslat = mod == "start";

            string cmd = baslat
                ? "/c sc config wuauserv start= demand && net start wuauserv"
                : "/c net stop wuauserv && sc config wuauserv start= disabled";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd)
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = "runas"
                };
                Process.Start(psi);
                MessageBox.Show(baslat ? "Update servisi aktif edildi." : "Update servisi tamamen kilitlendi.", "Servis Yönetimi");
            }
            catch (Exception ex) { MessageBox.Show("Yetki Hatası: " + ex.Message); }
        }

        // --- 2. WINDOWS UPDATE: TARAMA VE MB HESAPLAMA ---
        private async void btnUpdateKontrol_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                btnUpdateKontrol.IsEnabled = false;
                btnUpdateKontrol.Content = "Kaynak Bağlantısı Kuruluyor...";

                // UI Değerlerini ana thread üzerinde okuyoruz
                bool isOptionalChecked = chkIstegeBagli.IsChecked == true;
                bool isBypassChecked = chkDirectUpdate.IsChecked == true;

                Dispatcher.Invoke(() => FoundUpdates.Clear());

                await Task.Run(() =>
                {
                    UpdateSession updateSession = new UpdateSession();
                    IUpdateSearcher updateSearcher = updateSession.CreateUpdateSearcher();
                    UpdateServiceManager serviceManager = new UpdateServiceManager();

                    string microsoftUpdateGuid = "7971f918-a847-4430-9279-4a52d16e76d3";
                    updateSearcher.Online = true; // Her zaman canlı sor

                    if (isBypassChecked)
                    {
                        // --- WSUS BAYPAS MODU (Doğrudan Microsoft Update) ---
                        try
                        {
                            // Servis kayıtlı mı kontrol et ve yoksa ekle
                            bool registered = false;
                            foreach (IUpdateService s in serviceManager.Services)
                                if (s.ServiceID.Equals(microsoftUpdateGuid, StringComparison.OrdinalIgnoreCase)) { registered = true; break; }

                            if (!registered) serviceManager.AddService2(microsoftUpdateGuid, 2, "");

                            updateSearcher.ServerSelection = ServerSelection.ssOthers;
                            updateSearcher.ServiceID = microsoftUpdateGuid;
                        }
                        catch { updateSearcher.ServerSelection = ServerSelection.ssWindowsUpdate; }
                    }
                    else
                    {
                        // --- STANDART MOD (Varsayılan Sistem / WSUS Ayarı) ---
                        updateSearcher.ServerSelection = ServerSelection.ssWindowsUpdate;
                    }

                    // Kriter Belirleme
                    string criteria = isOptionalChecked ? "IsInstalled=0 and IsHidden=0" : "IsInstalled=0 and IsHidden=0 and Type='Software'";

                    // ARAMA
                    ISearchResult searchResult = updateSearcher.Search(criteria);

                    Dispatcher.Invoke(() =>
                    {
                        foreach (IUpdate update in searchResult.Updates)
                        {
                            double mbSize = (double)update.MaxDownloadSize / (1024 * 1024);
                            FoundUpdates.Add(new UpdateItem
                            {
                                Title = update.Title,
                                IsSelected = true,
                                SizeDisplay = mbSize > 0 ? $"[{mbSize.ToString("N2")} MB]" : "[Bilinmiyor]",
                                UnderlyingUpdate = update
                            });
                        }
                        if (FoundUpdates.Count == 0) MessageBox.Show("Seçilen kaynakta güncelleme bulunamadı.");
                    });
                });
            }
            catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
            finally
            {
                btnUpdateKontrol.IsEnabled = true;
                btnUpdateKontrol.Content = "GÜNCELLEMELERİ TARA";
            }
        }

        // --- 3. WINDOWS UPDATE: SEÇİLENLERİ İNDİR ---
        private async void btnDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var selectedUpdates = FoundUpdates.Where(x => x.IsSelected).ToList();

            if (!selectedUpdates.Any())
            {
                MessageBox.Show("Lütfen indirilecek güncellemeleri listeden seçin.");
                return;
            }

            try
            {
                btnDownloadSelected.IsEnabled = false;
                btnDownloadSelected.Content = "İndiriliyor...";

                await Task.Run(() =>
                {
                    UpdateSession session = new UpdateSession();
                    UpdateCollection col = new UpdateCollection();

                    foreach (var item in selectedUpdates) col.Add(item.UnderlyingUpdate);

                    UpdateDownloader downloader = session.CreateUpdateDownloader();
                    downloader.Updates = col;
                    downloader.Download();

                    Dispatcher.Invoke(() => MessageBox.Show("Seçilen tüm güncellemeler başarıyla indirildi.", "İşlem Tamam"));
                });
            }
            catch (Exception ex) { MessageBox.Show("İndirme Hatası: " + ex.Message); }
            finally
            {
                btnDownloadSelected.IsEnabled = true;
                btnDownloadSelected.Content = "SEÇİLENLERİ İNDİR";
            }
        }

        // --- 4. SEÇİM BUTONLARI ---
        private void btnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in FoundUpdates) item.IsSelected = true;
            lstUpdates.Items.Refresh();
        }

        private void btnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in FoundUpdates) item.IsSelected = false;
            lstUpdates.Items.Refresh();
        }

        // --- 5. ORANT / BIN ONARIM ---
        private async void btnFixOrant_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string localOrant = @"C:\orant";
            string networkOrant = @"\\192.168.1.10\p\orant";
            string binPath = @"C:\orant\BIN";

            try
            {
                btnFixOrant.IsEnabled = false;
                if (!Directory.Exists(localOrant))
                {
                    MessageBox.Show("C:\\orant eksik. Sunucudan kopyalanıyor...");
                    await Task.Run(() =>
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("robocopy", $"\"{networkOrant}\" \"{localOrant}\" /MIR /R:3 /W:5")
                        {
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using (Process p = Process.Start(psi)) { p?.WaitForExit(); }
                    });
                }

                await Task.Run(() =>
                {
                    var scope = EnvironmentVariableTarget.Machine;
                    string path = Environment.GetEnvironmentVariable("Path", scope);
                    if (!path.Contains(binPath))
                    {
                        string newPath = path.EndsWith(";") ? path + binPath : path + ";" + binPath;
                        Environment.SetEnvironmentVariable("Path", newPath, scope);
                    }
                });
                MessageBox.Show("Oracle ORANT onarıldı ve PATH'e eklendi.", "Başarılı");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { btnFixOrant.IsEnabled = true; }
        }

        // --- 6. BARKOD ONAR (LPT1) ---
        private void btnAutoBarcodeFix_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                string pc = Environment.MachineName;
                string printer = "pro_etiket";

                // PowerShell ile Paylaşıma Aç
                Process.Start(new ProcessStartInfo("powershell", $"-Command \"Set-Printer -Name '{printer}' -Shared $true -ShareName '{printer}'\"") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, Verb = "runas" }).WaitForExit();

                // CMD ile LPT1'e bağla
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c net use lpt1 /delete /y && net use lpt1 \\\\{pc}\\{printer} /persistent:yes") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, Verb = "runas" }).WaitForExit();

                MessageBox.Show("Barkod yazıcı paylaşıldı ve LPT1 portuna başarıyla bağlandı.");
            }
            catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
        }

        // --- 7. STANDART ARAÇLAR ---
        private void btnHbysModuller_Click(object sender, RoutedEventArgs e) => new HbysModullerWindow().Show();

        private void btnListeyiAc_Click(object sender, RoutedEventArgs e)
        {
            string f = Path.Combine(_netPath, "Raporlar", "Tum_Envanter_Listesi.csv");
            if (File.Exists(f)) Process.Start(new ProcessStartInfo(f) { UseShellExecute = true });
        }

        private void btnPing_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                PingReply r = new Ping().Send(txtPingIp.Text, 1000);
                MessageBox.Show(r.Status == IPStatus.Success ? $"Bağlantı Başarılı! {r.RoundtripTime}ms" : "Bağlantı Yok.");
            }
            catch { MessageBox.Show("IP Geçersiz."); }
        }

        private void btnTempSil_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            int adet = 0;
            foreach (FileInfo f in new DirectoryInfo(Path.GetTempPath()).GetFiles()) { try { f.Delete(); adet++; } catch { } }
            MessageBox.Show($"{adet} dosya temizlendi.");
        }

        private void btnRestartExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            foreach (var p in Process.GetProcessesByName("explorer")) try { p.Kill(); } catch { }
            Process.Start("explorer.exe");
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

    // --- VERİ MODELİ ---
    public class UpdateItem
    {
        public string Title { get; set; }
        public bool IsSelected { get; set; }
        public string SizeDisplay { get; set; } // MB bilgisini burada tutuyoruz
        public IUpdate UnderlyingUpdate { get; set; }
    }
}