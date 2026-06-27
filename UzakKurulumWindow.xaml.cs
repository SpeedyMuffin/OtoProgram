using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.DirectoryServices.AccountManagement;
using System.Globalization;
using System.Runtime.InteropServices; // Win32 API için ŞART
using System.Windows.Data;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Management;

namespace OtoProgram
{
    // ==========================================
    // 1. MODEL SINIFLARI
    // ==========================================

    public class AdUserItem : INotifyPropertyChanged
    {
        private string _displayName;
        private string _samAccountName;
        private string _durum;
        private bool _isLocked;
        private bool _isEnabled;

        private string _email;
        private string _phone;
        private string _department;
        private string _ou;
        private string _createdDate;
        private string _lastLogon;
        private string _passwordLastSet;
        private bool _passwordNeverExpires;

        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }
        public string SamAccountName
        {
            get => _samAccountName;
            set { _samAccountName = value; OnPropertyChanged(); }
        }
        public string Durum
        {
            get => _durum;
            set { _durum = value; OnPropertyChanged(); }
        }
        public bool IsLocked
        {
            get => _isLocked;
            set { _isLocked = value; OnPropertyChanged(); }
        }
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public string Email
        {
            get => _email;
            set { _email = value; OnPropertyChanged(); }
        }
        public string Phone
        {
            get => _phone;
            set { _phone = value; OnPropertyChanged(); }
        }
        public string Department
        {
            get => _department;
            set { _department = value; OnPropertyChanged(); }
        }
        public string OU
        {
            get => _ou;
            set { _ou = value; OnPropertyChanged(); }
        }
        public string CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(); }
        }
        public string LastLogon
        {
            get => _lastLogon;
            set { _lastLogon = value; OnPropertyChanged(); }
        }
        public string PasswordLastSet
        {
            get => _passwordLastSet;
            set { _passwordLastSet = value; OnPropertyChanged(); }
        }
        public bool PasswordNeverExpires
        {
            get => _passwordNeverExpires;
            set { _passwordNeverExpires = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PcItem : INotifyPropertyChanged
    {
        private string _ip;
        private string _hostname;
        private string _durum;
        private bool _isSelected;

        public string IP
        {
            get => _ip;
            set { _ip = value; OnPropertyChanged(); }
        }
        public string Hostname
        {
            get => _hostname;
            set { _hostname = value; OnPropertyChanged(); }
        }
        public string Durum
        {
            get => _durum;
            set { _durum = value; OnPropertyChanged(); }
        }
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RemoteUser
    {
        public string Username { get; set; }
        public string Status { get; set; }
    }

    public class ChatMessageItem
    {
        public string SenderIcon { get; set; }
        public string Message { get; set; }
        public string Time { get; set; }
        public Brush Color { get; set; }
        public FontWeight Weight { get; set; }
    }

    // ==========================================
    // 2. GARANTİ AĞ ERİŞİM SINIFI (Win32 API)
    // ==========================================
    public class NetworkImpersonation : IDisposable
    {
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LogonUser(String lpszUsername, String lpszDomain, String lpszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public extern static bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public extern static bool ImpersonateLoggedOnUser(IntPtr hToken);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public extern static bool RevertToSelf();

        private IntPtr _tokenHandle = IntPtr.Zero;
        private bool _isImpersonating = false;

        public NetworkImpersonation(string domain, string username, string password)
        {
            const int LOGON32_LOGON_NEW_CREDENTIALS = 9;
            const int LOGON32_PROVIDER_DEFAULT = 0;

            bool success = LogonUser(username, domain, password, LOGON32_LOGON_NEW_CREDENTIALS, LOGON32_PROVIDER_DEFAULT, out _tokenHandle);
            if (success)
            {
                if (ImpersonateLoggedOnUser(_tokenHandle))
                {
                    _isImpersonating = true;
                }
            }
        }

        public void Dispose()
        {
            if (_isImpersonating) RevertToSelf();
            if (_tokenHandle != IntPtr.Zero) CloseHandle(_tokenHandle);
        }
    }

    // ==========================================
    // 3. ANA PENCERE (LOGIC)
    // ==========================================
    public partial class UzakKurulumWindow : Window
    {
        private string _kullanici;
        private string _sifre;
        private string _domain = "domain.local";
        private string _embeddedPsPath;

        private ObservableCollection<CategoryItem> _tumListeler;
        public ObservableCollection<PcItem> BulunanCihazlar { get; set; } = new ObservableCollection<PcItem>();

        private bool _tumunuSecildiMi = false;
        private bool _isUninstallMode = false;

        // Kurulu programlar listesi filtresi için RAM verisi
        private List<InstalledAppItem> _allInstalledApps = new List<InstalledAppItem>();

        // Sohbet geçmişi (Local dosya takibi yok artık, sadece RAM'de tutuyoruz)
        private string _chatLocalPath = "";

        private System.Threading.CancellationTokenSource _chatCts = new System.Threading.CancellationTokenSource();

        public UzakKurulumWindow(string user, string pass, ObservableCollection<CategoryItem> gelenListe, string targetIp = null)
        {
            InitializeComponent();

            if (user.Contains(@"\")) _kullanici = user.Split('\\')[1]; else _kullanici = user;
            _sifre = pass;
            _tumListeler = gelenListe;

            _embeddedPsPath = PreparePsExecNextToExe();

            itemsKategoriler.ItemsSource = _tumListeler;
            gridPcs.ItemsSource = BulunanCihazlar;

            foreach (var cat in _tumListeler) { cat.IsSelected = false; foreach (var app in cat.Apps) app.IsSelected = false; }

            this.Closed += UzakKurulumWindow_Closed;

            if (MainWindow.IsDemoMode)
            {
                this.Title = "Uzak Kurulum & Yönetim Paneli (DEMO MODU)";
                try
                {
                    this.Loaded += (s, ev) => {
                        this.Title = "Uzak Kurulum & Yönetim Paneli (DEMO MODU)";
                    };
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(targetIp))
            {
                txtSmartSearch.Text = targetIp;
            }

            LoadMacros();
            RecentManager.AddRecentAction(
                "Uzak Kurulum",
                "Uzak Dağıtım & Yönetim",
                "📡",
                "#8B5CF6",
                "Module",
                "UzakKurulum");
        }

        private void UzakKurulumWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                _chatCts?.Cancel();
                _chatCts?.Dispose();
            }
            catch { }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }
        private void BtnKapat_Click(object sender, RoutedEventArgs e) => this.Close();

        // --- ARAYÜZ ---

        private void btnModDegistir_Click(object sender, RoutedEventArgs e)
        {
            _isUninstallMode = btnModDegistir.IsChecked == true;
            if (_isUninstallMode) { btnBaslat.Content = "🗑️ SEÇİLİ PROGRAMLARI KALDIR"; btnBaslat.Background = (Brush)new BrushConverter().ConvertFrom("#DC2626"); Logla("⚠️ DİKKAT: Kaldırma moduna geçildi."); }
            else { btnBaslat.Content = "🚀 SEÇİLİ CİHAZLARA DAĞITIMI BAŞLAT"; btnBaslat.Background = (Brush)new BrushConverter().ConvertFrom("#10B981"); Logla("ℹ️ Kurulum moduna dönüldü."); }
        }

        private void btnTemizle_Click(object sender, RoutedEventArgs e)
        {
            if (BulunanCihazlar.Count > 0) { BulunanCihazlar.Clear(); _tumunuSecildiMi = false; btnTumuSec.Content = "Tümünü Seç"; Logla("ℹ️ Liste temizlendi."); }
        }

        private void btnTumuSec_Click(object sender, RoutedEventArgs e)
        {
            _tumunuSecildiMi = !_tumunuSecildiMi;
            foreach (var item in BulunanCihazlar) item.IsSelected = _tumunuSecildiMi;
            btnTumuSec.Content = _tumunuSecildiMi ? "Seçimi Kaldır" : "Tümünü Seç";
        }

        private void btnTersCevir_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in BulunanCihazlar) item.IsSelected = !item.IsSelected;
            _tumunuSecildiMi = false;
            btnTumuSec.Content = "Tümünü Seç";
        }

        // --- TARAMA ---
        private async void btnSmartTara_Click(object sender, RoutedEventArgs e)
        {
            string query = "";
            Dispatcher.Invoke(() => query = txtSmartSearch.Text.Trim());

            if (string.IsNullOrEmpty(query))
            {
                MessageBox.Show("Lütfen taranacak IP, IP bloğu veya bilgisayar adı girin.", "Arama Kutusu Boş");
                return;
            }

            btnSmartTara.IsEnabled = false;
            btnBaslat.IsEnabled = false;
            Logla("🔍 Akıllı ağ taraması başlatılıyor...");

            // Listeyi temizle
            BulunanCihazlar.Clear();

            if (MainWindow.IsDemoMode)
            {
                Logla("🔍 (DEMO MODU) Akıllı ağ taraması simüle ediliyor...");
                await Task.Delay(1000);
                BulunanCihazlar.Add(new PcItem { IP = "192.168.1.15", Hostname = "MUHASEBE-PC", Durum = "Açık", IsSelected = false });
                BulunanCihazlar.Add(new PcItem { IP = "192.168.1.20", Hostname = "VEZNE-PC", Durum = "Açık", IsSelected = false });
                BulunanCihazlar.Add(new PcItem { IP = "192.168.1.35", Hostname = "BASHEKIM-PC", Durum = "Açık", IsSelected = false });
                Logla("✅ (DEMO MODU) Tarama bitti. 3 adet simüle cihaz bulundu.");
                btnSmartTara.IsEnabled = true;
                btnBaslat.IsEnabled = true;
                return;
            }

            await Task.Run(async () =>
            {
                var iplerToScan = new List<string>();
                var parts = query.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    string cleanPart = part.Trim();
                    if (string.IsNullOrEmpty(cleanPart)) continue;

                    // 1. IP Bloğu mu? (Örn: 10.235.80.1-50)
                    var rangeMatch = System.Text.RegularExpressions.Regex.Match(cleanPart, @"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.)(\d{1,3})-(\d{1,3})$");
                    if (rangeMatch.Success)
                    {
                        string ipBase = rangeMatch.Groups[1].Value;
                        int start = int.Parse(rangeMatch.Groups[2].Value);
                        int end = int.Parse(rangeMatch.Groups[3].Value);

                        if (start >= 0 && end <= 255 && start <= end)
                        {
                            for (int i = start; i <= end; i++)
                            {
                                iplerToScan.Add(ipBase + i);
                            }
                        }
                        continue;
                    }

                    // 2. Yıldızlı Blok mu? (Örn: 10.235.80.*)
                    var wildMatch = System.Text.RegularExpressions.Regex.Match(cleanPart, @"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.)\*$");
                    if (wildMatch.Success)
                    {
                        string ipBase = wildMatch.Groups[1].Value;
                        for (int i = 1; i <= 254; i++)
                        {
                            iplerToScan.Add(ipBase + i);
                        }
                        continue;
                    }

                    // 3. Tek IP mi? (Örn: 10.235.80.5)
                    var ipMatch = System.Text.RegularExpressions.Regex.IsMatch(cleanPart, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$");
                    if (ipMatch)
                    {
                        iplerToScan.Add(cleanPart);
                        continue;
                    }

                    // 4. Bilgisayar adı mı? (Hostname ise DNS ile çözmeyi dene)
                    try
                    {
                        IPHostEntry entry = Dns.GetHostEntry(cleanPart);
                        foreach (var ipAddress in entry.AddressList)
                        {
                            if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                iplerToScan.Add(ipAddress.ToString());
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Çözemezse ekleme, loga yaz
                        Dispatcher.Invoke(() => Logla($"⚠️ Cihaz adı IP'ye çözümlenemedi: {cleanPart}"));
                    }
                }

                // Aynı IP'leri teke indir
                iplerToScan = iplerToScan.Distinct().ToList();

                if (iplerToScan.Count == 0) return;

                var tasks = new List<Task>();
                foreach (string currentIp in iplerToScan)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        using (Ping ping = new Ping())
                        {
                            try
                            {
                                bool isOnline = false;

                                // A. Ping gönder
                                try
                                {
                                    PingReply reply = await ping.SendPingAsync(currentIp, 400);
                                    if (reply.Status == IPStatus.Success) isOnline = true;
                                }
                                catch { }

                                // B. Ping başarısızsa TCP 135 (RPC/WMI) portunu kontrol et (Kaspersky Engeli Bypass)
                                if (!isOnline)
                                {
                                    try
                                    {
                                        using (var tcpClient = new System.Net.Sockets.TcpClient())
                                        {
                                            var ar = tcpClient.BeginConnect(currentIp, 135, null, null);
                                            isOnline = ar.AsyncWaitHandle.WaitOne(250);
                                            if (isOnline) tcpClient.EndConnect(ar);
                                        }
                                    }
                                    catch { }
                                }

                                if (isOnline)
                                {
                                    string hostName = "Bilinmiyor";
                                    try
                                    {
                                        IPHostEntry entry = await Dns.GetHostEntryAsync(currentIp);
                                        hostName = entry.HostName;
                                    }
                                    catch { }

                                    Dispatcher.Invoke(() =>
                                    {
                                        if (!BulunanCihazlar.Any(x => x.IP == currentIp))
                                        {
                                            BulunanCihazlar.Add(new PcItem { IP = currentIp, Hostname = hostName, Durum = "Açık", IsSelected = false });
                                        }
                                    });
                                }
                            }
                            catch { }
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            });

            Logla($"✅ Tarama bitti. {BulunanCihazlar.Count} cihaz bulundu.");
            btnSmartTara.IsEnabled = true;
            btnBaslat.IsEnabled = true;
        }

        private string PreparePsExecNextToExe()
        {
            if (MainWindow.IsDemoMode)
            {
                return "PsExec.exe";
            }
            try
            {
                // HEDEF: Programın çalıştığı klasör (OtoProgram.exe'nin yanı)
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string localPath = Path.Combine(appDirectory, "PsExec.exe");

                // KAYNAK: Sunucu Yolu
                string networkSource = @"\\192.168.1.100\d$\Programlar\OtoProgram\PsExec.exe";

                // 1. PsExec'in o meşhur "Lisans Kabul Et" penceresini Registry'den sustur
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Sysinternals\PsExec"))
                    {
                        key.SetValue("EulaAccepted", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }
                catch { }

                // 2. Dosyayı Sunucudan Kopyala (Yerelde yoksa kopyala)
                bool kopyalandi = false;
                if (!File.Exists(localPath))
                {
                    try
                    {
                        using (new NetworkImpersonation(_domain, _kullanici, _sifre))
                        {
                            if (File.Exists(networkSource))
                            {
                                File.Copy(networkSource, localPath, true);
                                kopyalandi = true;
                            }
                        }
                    }
                    catch (IOException)
                    {
                    }
                }

                // 3. Dosyanın "Engellemesini" Kaldır (Sadece yeni kopyalandıysa)
                if (File.Exists(localPath))
                {
                    if (kopyalandi)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "powershell",
                                Arguments = $"-NoProfile -Command \"Unblock-File -Path '{localPath}'\"",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            })?.WaitForExit();
                        }
                        catch { }
                    }

                    // Başarılıysa dosyanın tam yolunu dön
                    return localPath;
                }
            }
            catch (Exception ex)
            {
                // Hata durumunda loga yaz veya sessizce geç
                // MessageBox.Show("PsExec Hazırlama Hatası: " + ex.Message);
            }

            // Her şey ters giderse varsayılanı dön
            return "PsExec.exe";
        }

        // --- 2. YAZILIM DAĞITIM ---
        private async void btnBaslat_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pcler = BulunanCihazlar.Where(x => x.IsSelected).ToList();
            var apps = _tumListeler.SelectMany(k => k.Apps).Where(a => a.IsSelected).ToList();

            if (pcler.Count == 0 || apps.Count == 0)
            {
                MessageBox.Show("Lütfen PC ve UYGULAMA seçin.");
                return;
            }

            btnBaslat.IsEnabled = false;
            string islemAdi = _isUninstallMode ? "KALDIRMA" : "KURULUM";
            Logla($"🚀 {islemAdi} İŞLEMİ BAŞLIYOR...");

            await Task.Run(async () =>
            {
                foreach (var pc in pcler)
                {
                    Logla($"\n>> HEDEF: {pc.IP} ({pc.Hostname})");
                    KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                    foreach (var app in apps)
                    {
                        string agYolu = app.FullPath;
                        string dosyaAdi = app.FileName;

                        try
                        {
                            if (_isUninstallMode)
                            {
                                Logla($"   🗑️ {dosyaAdi} kaldırılıyor...");
                                string uninstallCommand = GetUninstallArguments(dosyaAdi, agYolu);
                                if (File.Exists("PsExec.exe"))
                                {
                                    bool sonuc = PsExecAgdanCalistir(pc.IP, agYolu, uninstallCommand);
                                    if (sonuc) Logla("      ✅ Komut iletildi.");
                                    else Logla("      ❌ Hata.");
                                }
                            }
                            else
                            {
                                Logla($"   📦 {dosyaAdi} kuruluyor...");
                                if (dosyaAdi.ToLower().Contains("kaspersky")) { Logla("      ⚠️ Kaspersky atlandı."); continue; }
                                string installCommand = GetSilentArguments(dosyaAdi, agYolu);
                                if (File.Exists("PsExec.exe"))
                                {
                                    bool sonuc = PsExecAgdanCalistir(pc.IP, agYolu, installCommand);
                                    if (sonuc) Logla("      ✅ Başarılı.");
                                    else Logla("      ❌ Hata.");
                                }
                            }
                        }
                        catch (Exception ex) { Logla($"      ❌ İstisna: {ex.Message}"); }
                        await Task.Delay(2000);
                    }
                    KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /delete /y");
                }
                Logla("\n🏁 İŞLEM TAMAMLANDI");
            });
            btnBaslat.IsEnabled = true;
        }

        private string PreparePsExecFromNetwork()
        {
            // Hedef: Bilgisayarın Temp klasörü
            string tempFolder = Path.Combine(Path.GetTempPath(), "OtoProgram_Tools");
            string localPath = Path.Combine(tempFolder, "PsExec.exe");

            // Kaynak: Senin belirttiğin sunucu yolu
            // NOT: d$ paylaşımına erişim için kullanılan kullanıcının o sunucuda ADMIN yetkisi olmalı.
            string networkSource = @"\\192.168.1.100\d$\Programlar\OtoProgram\PsExec.exe";

            try
            {
                if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

                // Dosya zaten varsa bile (güncelleme ihtimaline karşı) tarih/boyut kontrolü yapmadan 
                // her seferinde çekmek yerine, önce bir deneyelim.

                // Ağ erişimi için giriş bilgilerini kullanıyoruz
                using (new NetworkImpersonation(_domain, _kullanici, _sifre))
                {
                    if (File.Exists(networkSource))
                    {
                        File.Copy(networkSource, localPath, true); // true = üzerine yaz
                    }
                    else
                    {
                        MessageBox.Show($"Sunucuda dosya bulunamadı!\nYol: {networkSource}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return "PsExec.exe"; // Dosya yoksa çık
                    }
                }

                // Kopyalama başarılı mı kontrol et
                if (File.Exists(localPath))
                {
                    return localPath;
                }
                else
                {
                    MessageBox.Show("Kopyalama komutu çalıştı ama dosya yerelde oluşmadı.", "Hata");
                    return "PsExec.exe";
                }
            }
            catch (Exception ex)
            {
                // İŞTE BURASI: Hatayı artık yutmuyoruz, ekrana basıyoruz.
                MessageBox.Show($"PsExec Kopyalama Hatası:\n{ex.Message}\n\nOlası Sebepler:\n1. Kullanıcı adı/şifre sunucuda yetkisiz.\n2. 192.168.1.100 erişilemiyor.\n3. Antivirüs engelliyor.",
                                "Ağ Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                return "PsExec.exe";
            }
        }

        // --- 3. SİSTEM & GÜVENLİK ---
        private async void btnFirewallOff_Click(object sender, RoutedEventArgs e) => await FirewallIslemi("off");
        private async void btnFirewallOn_Click(object sender, RoutedEventArgs e) => await FirewallIslemi("on");

        private async void btnDisableTelemetry_Click(object sender, RoutedEventArgs e)
        {
            var seciliPcler = BulunanCihazlar.Where(x => x.IsSelected).ToList();
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (seciliPcler.Count == 0) { MessageBox.Show("Soldan en az bir bilgisayar seçin.", "Cihaz Seçilmedi"); return; }

            string cmd = "cmd.exe /c sc config DiagTrack start= disabled && net stop DiagTrack && " +
                         "sc config dmwappushservice start= disabled && net stop dmwappushservice && " +
                         "reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection\" /v AllowTelemetry /t REG_DWORD /d 0 /f && " +
                         "reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Error Reporting\" /v Disabled /t REG_DWORD /d 1 /f";

            await TopluPsExecKomutu(seciliPcler, cmd, "Telemetri ve Hata Raporlama Kapatılıyor...");
            MessageBox.Show("Telemetri ve Hata Raporlama kapatma komutları uzak bilgisayarlara gönderildi.", "Telemetri Kapatıldı");
        }

        private async Task FirewallIslemi(string state)
        {
            var seciliPcler = BulunanCihazlar.Where(x => x.IsSelected).ToList();
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (seciliPcler.Count == 0) { MessageBox.Show("Soldan en az bir bilgisayar seçin."); return; }
            string komut = $"netsh advfirewall set allprofiles state {state}";
            await TopluPsExecKomutu(seciliPcler, komut, $"Firewall {state.ToUpper()} yapılıyor...");
        }

        // --- KULLANICI LİSTELEME (DÜZELTİLMİŞ: Sadece Gerçek Kullanıcılar) ---
        // --- KULLANICI LİSTELEME (FİLTRESİZ FULL LİSTE) ---
        private async void btnKullanicilariGetir_Click(object sender, RoutedEventArgs e)
        {
            var hedefPc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (hedefPc == null) { MessageBox.Show("Lütfen soldan bir bilgisayara tik atın."); return; }

            Logla($"🔍 {hedefPc.IP} kullanıcı klasörleri (Filtresiz) getiriliyor...");
            gridRemoteUsers.ItemsSource = null;
            List<RemoteUser> usersFound = new List<RemoteUser>();

            if (MainWindow.IsDemoMode)
            {
                usersFound.Add(new RemoteUser { Username = "kullanici.adi", Status = "Klasör" });
                usersFound.Add(new RemoteUser { Username = "hbys_admin", Status = "Klasör" });
                usersFound.Add(new RemoteUser { Username = "Ortak_Paylasim", Status = "Klasör" });
                gridRemoteUsers.ItemsSource = usersFound;
                Logla($"✅ (DEMO MODU) 3 adet simüle klasör bulundu.");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // 1. Yetkili Bağlantıyı Kur
                    KomutCalistir("net", $@"use \\{hedefPc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                    string remotePath = $@"\\{hedefPc.IP}\c$\Users";

                    if (Directory.Exists(remotePath))
                    {
                        // Tüm klasörleri çek (Gizli klasörler dahil gelebilir)
                        var directories = Directory.GetDirectories(remotePath);

                        foreach (var dir in directories)
                        {
                            // Sadece klasör adını alıyoruz
                            string folderName = new DirectoryInfo(dir).Name;

                            // HİÇBİR FİLTRE YOK: Ne varsa ekle.
                            usersFound.Add(new RemoteUser { Username = folderName, Status = "Klasör" });
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() => Logla($"❌ {hedefPc.IP} 'C$' paylaşımına erişilemedi."));
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"❌ Hata: {ex.Message}"));
                }
                finally
                {
                    // 2. Bağlantıyı Temizle
                    KomutCalistir("net", $@"use \\{hedefPc.IP}\ipc$ /delete /y");
                }
            });

            if (usersFound.Count > 0)
            {
                gridRemoteUsers.ItemsSource = usersFound;
                Logla($"✅ {usersFound.Count} adet klasör bulundu.");
            }
            else
            {
                Logla("⚠️ Klasör bulunamadı.");
            }
        }

        private async void btnMakeAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var hedefPc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            var seciliUser = gridRemoteUsers.SelectedItem as RemoteUser;
            if (hedefPc == null || seciliUser == null) { MessageBox.Show("PC ve Kullanıcı seçilmedi."); return; }

            string userFull = seciliUser.Username;
            if (!userFull.Contains("\\") && !userFull.Contains("@")) userFull = $"{_domain.Split('.')[0]}\\{userFull}";

            string komut = $"net localgroup Administrators \"{userFull}\" /add";
            Logla($"👑 {hedefPc.IP} -> {userFull} Admin yapılıyor...");
            await Task.Run(() => PsExecAgdanCalistir(hedefPc.IP, "", komut));
        }

        private async void btnRemoveAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var hedefPc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            var seciliUser = gridRemoteUsers.SelectedItem as RemoteUser;
            if (hedefPc == null || seciliUser == null) return;

            string userFull = $"{_domain.Split('.')[0]}\\{seciliUser.Username}";
            string komut = $"net localgroup Administrators \"{userFull}\" /delete";
            Logla($"❌ {hedefPc.IP} -> {userFull} Admin yetkisi alınıyor...");
            await Task.Run(() => PsExecAgdanCalistir(hedefPc.IP, "", komut));
        }

        // --- 4. AD İŞLEMLERİ ---
        // --- YENİ AD ARAMA VE İŞLEM METOTLARI ---

        // Arama Kutusu Placeholder Efekti
        private void TxtADSearch_GotFocus(object sender, RoutedEventArgs e) { if (txtADSearch.Text == "Ad Soyad veya Kullanıcı Adı...") { txtADSearch.Text = ""; txtADSearch.Foreground = Brushes.Black; } }
        private void TxtADSearch_LostFocus(object sender, RoutedEventArgs e) { if (string.IsNullOrWhiteSpace(txtADSearch.Text)) { txtADSearch.Text = "Ad Soyad veya Kullanıcı Adı..."; txtADSearch.Foreground = Brushes.Gray; } }
        private void txtADSearch_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) btnADSearch_Click(sender, e); }

        // 1. AD Kullanıcı Arama
        private async void btnADSearch_Click(object sender, RoutedEventArgs e)
        {
            string keyword = txtADSearch.Text.Trim();
            if (string.IsNullOrEmpty(keyword) || keyword == "Ad Soyad veya Kullanıcı Adı...") return;

            btnADSearch.IsEnabled = false;
            gridAdUsers.ItemsSource = null;
            Logla($"🔍 AD Aranıyor: {keyword}...");

            List<AdUserItem> results = new List<AdUserItem>();

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(500);
                results.Add(new AdUserItem { DisplayName = "Demo Kullanıcı", SamAccountName = "demo.kullanici", IsLocked = false, Durum = "Aktif" });
                results.Add(new AdUserItem { DisplayName = "Test Hesap", SamAccountName = "test.hesap", IsLocked = true, Durum = "KİLİTLİ" });
                results.Add(new AdUserItem { DisplayName = "Misafir Girişi", SamAccountName = "misafir", IsLocked = false, Durum = "Pasif" });
                gridAdUsers.ItemsSource = results;
                Logla($"✅ (DEMO MODU) AD arama sonuçları simüle edildi.");
                btnADSearch.IsEnabled = true;
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    using (PrincipalContext ctx = new PrincipalContext(ContextType.Domain, _domain, _kullanici, _sifre))
                    {
                        // İsme göre ara
                        UserPrincipal qryName = new UserPrincipal(ctx);
                        qryName.DisplayName = "*" + keyword + "*";
                        PrincipalSearcher searcher = new PrincipalSearcher(qryName);

                        foreach (var result in searcher.FindAll())
                        {
                            if (result is UserPrincipal u)
                            {
                                results.Add(new AdUserItem
                                {
                                    DisplayName = u.DisplayName,
                                    SamAccountName = u.SamAccountName,
                                    IsLocked = u.IsAccountLockedOut(),
                                    Durum = u.IsAccountLockedOut() ? "KİLİTLİ" : (u.Enabled == true ? "Aktif" : "Pasif")
                                });
                            }
                        }

                        // Kullanıcı adına göre de ara (Eğer farklıysa ekle)
                        UserPrincipal qryUser = new UserPrincipal(ctx);
                        qryUser.SamAccountName = "*" + keyword + "*";
                        searcher = new PrincipalSearcher(qryUser);

                        foreach (var result in searcher.FindAll())
                        {
                            if (result is UserPrincipal u)
                            {
                                if (!results.Any(x => x.SamAccountName == u.SamAccountName)) // Çift kayıt olmasın
                                {
                                    results.Add(new AdUserItem
                                    {
                                        DisplayName = u.DisplayName,
                                        SamAccountName = u.SamAccountName,
                                        IsLocked = u.IsAccountLockedOut(),
                                        Durum = u.IsAccountLockedOut() ? "KİLİTLİ" : (u.Enabled == true ? "Aktif" : "Pasif")
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla("AD Arama Hatası: " + ex.Message));
                }
            });

            if (results.Count > 0)
            {
                gridAdUsers.ItemsSource = results;
                Logla($"✅ {results.Count} kullanıcı bulundu.");
            }
            else
            {
                Logla("⚠️ Kullanıcı bulunamadı.");
            }
            btnADSearch.IsEnabled = true;
        }

        // 2. Seçili Kullanıcıyı Alma Yardımcısı
        private AdUserItem GetSelectedAdUser()
        {
            var user = gridAdUsers.SelectedItem as AdUserItem;
            if (user == null)
            {
                MessageBox.Show("Lütfen listeden bir kullanıcı seçin.", "Seçim Yapılmadı");
                return null;
            }
            return user;
        }

        // 3. Şifre Sıfırlama İşlemleri
        private void btnResetStandard_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var user = GetSelectedAdUser();
            if (user == null) return;

            string username = user.SamAccountName; // Örn: kullanici.adi
            string yeniSifre = "Sifre123"; // Varsayılan (eğer format uymazsa)

            try
            {
                // Türkçe karakter desteği için Culture ayarı (i -> İ dönüşümü için şart)
                var trKultur = new CultureInfo("tr-TR");

                // Kullanıcı adını noktadan ayırıyoruz
                var parcalar = username.Split('.');

                if (parcalar.Length >= 2)
                {
                    // İlk parçanın baş harfi BÜYÜK, ikinci parçanın baş harfi küçük
                    string ilkHarf = parcalar[0].Substring(0, 1).ToUpper(trKultur);
                    string ikinciHarf = parcalar[1].Substring(0, 1).ToLower(trKultur);

                    yeniSifre = ilkHarf + ikinciHarf + "123456";
                }
            }
            catch (Exception ex)
            {
                Logla("Şifre üretim hatası: " + ex.Message);
            }

            // Üretilen şifre ile işlemi başlat
            SifreSifirla(username, yeniSifre);
        }

        private void btnResetCustom_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var user = GetSelectedAdUser();
            if (user == null) return;
            string pass = txtNewPass.Text.Trim();
            if (pass.Length < 6) { MessageBox.Show("Şifre en az 6 karakter olmalı."); return; }
            SifreSifirla(user.SamAccountName, pass);
        }

        private void SifreSifirla(string username, string yeniSifre)
        {
            // Onay kutusu (Yanlışlıkla basmaya karşı sigorta)
            var sonuc = MessageBox.Show($"{username} kullanıcısı için:\n\nŞifre: {yeniSifre}\n\nOnaylıyor musunuz?",
                                        "Şifre Değiştirme Onayı",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Question);

            if (sonuc != MessageBoxResult.Yes) return;

            try
            {
                using (PrincipalContext pc = new PrincipalContext(ContextType.Domain, _domain, _kullanici, _sifre))
                {
                    UserPrincipal user = UserPrincipal.FindByIdentity(pc, username);
                    if (user != null)
                    {
                        // Şifreyi ayarla
                        user.SetPassword(yeniSifre);

                        // Checkbox seçiliyse "İlk girişte değiştir" özelliğini aç
                        if (chkZorunlu.IsChecked == true)
                            user.ExpirePasswordNow();
                        else
                            user.PasswordNeverExpires = false;

                        // Hesap kilitliyse kilidi de aç (İş bir kerede bitsin)
                        if (user.IsAccountLockedOut())
                            user.UnlockAccount();

                        user.Save();

                        Logla($"✅ AD: {username} için şifre '{yeniSifre}' yapıldı.");
                        MessageBox.Show($"İşlem Başarılı!\nKullanıcı: {username}\nYeni Şifre: {yeniSifre}", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Kullanıcı bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("AD Şifre Sıfırlama Hatası:\n" + ex.Message, "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 4. Kilit Açma
        private void btnADUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var user = GetSelectedAdUser();
            if (user == null) return;

            try
            {
                using (PrincipalContext pc = new PrincipalContext(ContextType.Domain, _domain, _kullanici, _sifre))
                {
                    UserPrincipal u = UserPrincipal.FindByIdentity(pc, user.SamAccountName);
                    if (u != null)
                    {
                        if (u.IsAccountLockedOut())
                        {
                            u.UnlockAccount();
                            u.Save();
                            Logla($"✅ AD: {user.SamAccountName} kilidi açıldı.");
                            MessageBox.Show("Kilit başarıyla açıldı.", "Başarılı");

                            user.IsLocked = false;
                            user.Durum = user.IsEnabled ? "Aktif" : "Pasif";
                            BindUserDetails(user);
                        }
                        else MessageBox.Show("Hesap zaten kilitli değil.");
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
        }

        // 5. Gelişmiş AD Detay & Yönetim Metotları (v3.7)
        private string ParseOU(string dn)
        {
            if (string.IsNullOrEmpty(dn)) return "-";
            try
            {
                var parts = dn.Split(',')
                              .Where(x => x.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                              .Select(x => x.Substring(3))
                              .Reverse();
                return parts.Any() ? string.Join(" / ", parts) : "Users";
            }
            catch { return "Users"; }
        }

        private void SetDetailsLoading(bool isLoading)
        {
            if (isLoading)
            {
                lblLoadingDetails.Visibility = Visibility.Visible;
                pnlUserDetails.Visibility = Visibility.Collapsed;
                lblNoUserSelected.Visibility = Visibility.Collapsed;
            }
            else
            {
                lblLoadingDetails.Visibility = Visibility.Collapsed;
                pnlUserDetails.Visibility = Visibility.Visible;
            }
        }

        private void BindUserDetails(AdUserItem user)
        {
            lblAdDisplayName.Text = user.DisplayName ?? "-";
            lblAdUsername.Text = "@" + user.SamAccountName;
            lblAdStatus.Text = user.Durum ?? "-";

            if (user.Durum == "KİLİTLİ")
                lblAdStatus.Foreground = Brushes.Red;
            else if (user.Durum == "Aktif")
                lblAdStatus.Foreground = Brushes.Green;
            else
                lblAdStatus.Foreground = Brushes.Gray;

            lblAdDept.Text = user.Department ?? "-";
            lblAdEmail.Text = user.Email ?? "-";
            lblAdPhone.Text = user.Phone ?? "-";
            lblAdOU.Text = user.OU ?? "-";
            lblAdCreated.Text = user.CreatedDate ?? "-";
            lblAdLastLogon.Text = user.LastLogon ?? "-";
            lblAdPassLastSet.Text = user.PasswordLastSet ?? "-";

            chkNeverExpires.IsChecked = user.PasswordNeverExpires;
        }

        private async void gridAdUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = gridAdUsers.SelectedItem as AdUserItem;
            if (selectedItem == null)
            {
                lblNoUserSelected.Visibility = Visibility.Visible;
                pnlUserDetails.Visibility = Visibility.Collapsed;
                lblLoadingDetails.Visibility = Visibility.Collapsed;
                return;
            }

            SetDetailsLoading(true);
            string username = selectedItem.SamAccountName;

            await Task.Run(() =>
            {
                try
                {
                    using (PrincipalContext pc = new PrincipalContext(ContextType.Domain, _domain, _kullanici, _sifre))
                    {
                        UserPrincipal u = UserPrincipal.FindByIdentity(pc, username);
                        if (u != null)
                        {
                            string email = u.EmailAddress ?? "-";
                            string phone = u.VoiceTelephoneNumber ?? "-";
                            string lastLogon = u.LastLogon?.ToString("dd.MM.yyyy HH:mm") ?? "-";
                            string pwdSet = u.LastPasswordSet?.ToString("dd.MM.yyyy HH:mm") ?? "-";
                            bool neverExpires = u.PasswordNeverExpires;
                            bool isLocked = u.IsAccountLockedOut();
                            bool isEnabled = u.Enabled == true;

                            string created = "-";
                            string dept = "-";
                            try
                            {
                                var de = u.GetUnderlyingObject() as System.DirectoryServices.DirectoryEntry;
                                if (de != null)
                                {
                                    dept = de.Properties["department"]?.Value?.ToString() 
                                           ?? de.Properties["description"]?.Value?.ToString() 
                                           ?? "-";

                                    if (de.Properties["whenCreated"]?.Value is DateTime dt)
                                    {
                                        created = dt.ToString("dd.MM.yyyy HH:mm");
                                    }
                                    else if (de.Properties["whenCreated"]?.Value != null)
                                    {
                                        created = de.Properties["whenCreated"].Value.ToString();
                                    }
                                }
                            }
                            catch { }

                            string ou = ParseOU(u.DistinguishedName);

                            Dispatcher.Invoke(() =>
                            {
                                var currentSelected = gridAdUsers.SelectedItem as AdUserItem;
                                if (currentSelected != null && currentSelected.SamAccountName == username)
                                {
                                    currentSelected.Email = email;
                                    currentSelected.Phone = phone;
                                    currentSelected.CreatedDate = created;
                                    currentSelected.LastLogon = lastLogon;
                                    currentSelected.PasswordLastSet = pwdSet;
                                    currentSelected.PasswordNeverExpires = neverExpires;
                                    currentSelected.Department = dept;
                                    currentSelected.OU = ou;
                                    currentSelected.IsLocked = isLocked;
                                    currentSelected.IsEnabled = isEnabled;
                                    currentSelected.Durum = isLocked ? "KİLİTLİ" : (isEnabled ? "Aktif" : "Pasif");

                                    BindUserDetails(currentSelected);
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla("Kullanıcı detayları yüklenemedi: " + ex.Message));
                }
                finally
                {
                    Dispatcher.Invoke(() => SetDetailsLoading(false));
                }
            });
        }

        private void btnEnableAccount_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var user = GetSelectedAdUser();
            if (user == null) return;

            var confirm = MessageBox.Show($"{user.SamAccountName} kullanıcısını AKTİF hale getirmek istiyor musunuz?", "Hesap Aktifleştirme Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using (PrincipalContext pc = new PrincipalContext(ContextType.Domain, _domain, _kullanici, _sifre))
                {
                    UserPrincipal u = UserPrincipal.FindByIdentity(pc, user.SamAccountName);
                    if (u != null)
                    {
                        u.Enabled = true;
                        u.Save();
                        Logla($"✅ AD: {user.SamAccountName} hesabı aktifleştirildi.");
                        
                        user.IsEnabled = true;
                        user.Durum = u.IsAccountLockedOut() ? "KİLİTLİ" : "Aktif";
                        BindUserDetails(user);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
        }

        private void btnDisableAccount_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var user = GetSelectedAdUser();
            if (user == null) return;

            var confirm = MessageBox.Show($"{user.SamAccountName} kullanıcısını PASİF (Devre Dışı) hale getirmek istiyor musunuz?", "Hesap Kapatma Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using (PrincipalContext pc = new PrincipalContext(ContextType.Domain, _domain, _kullanici, _sifre))
                {
                    UserPrincipal u = UserPrincipal.FindByIdentity(pc, user.SamAccountName);
                    if (u != null)
                    {
                        u.Enabled = false;
                        u.Save();
                        Logla($"✅ AD: {user.SamAccountName} hesabı devre dışı bırakıldı.");
                        
                        user.IsEnabled = false;
                        user.Durum = "Pasif";
                        BindUserDetails(user);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
        }

        private void chkNeverExpires_Click(object sender, RoutedEventArgs e)
        {
            var user = GetSelectedAdUser();
            if (user == null) return;

            bool targetVal = chkNeverExpires.IsChecked == true;
            string msg = targetVal 
                ? $"{user.SamAccountName} için 'Şifre Süresi Hiç Dolmasın' seçeneğini AKTİF etmek istiyor musunuz?"
                : $"{user.SamAccountName} için 'Şifre Süresi Hiç Dolmasın' seçeneğini KALDIRMAK istiyor musunuz?";

            var confirm = MessageBox.Show(msg, "Şifre Politikası Değişikliği", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                chkNeverExpires.IsChecked = !targetVal;
                return;
            }

            try
            {
                using (PrincipalContext pc = new PrincipalContext(ContextType.Domain, _domain, _kullanici, _sifre))
                {
                    UserPrincipal u = UserPrincipal.FindByIdentity(pc, user.SamAccountName);
                    if (u != null)
                    {
                        u.PasswordNeverExpires = targetVal;
                        u.Save();
                        Logla($"✅ AD: {user.SamAccountName} şifre süresi hiç dolmasın durumu '{targetVal}' olarak güncellendi.");
                        user.PasswordNeverExpires = targetVal;
                    }
                }
            }
            catch (Exception ex) 
            { 
                MessageBox.Show("Hata: " + ex.Message); 
                chkNeverExpires.IsChecked = !targetVal;
            }
        }



        // ==========================================
        // 5. GELİŞMİŞ TEK YÖNLÜ MESAJ GÖNDERME (Active Session Finder)
        // ==========================================

        private string EscapeVbsString(string text)
        {
            if (string.IsNullOrEmpty(text)) return "\"\"";
            string escaped = text.Replace("\"", "\"\"");
            escaped = escaped.Replace("\r\n", "\" & vbCrLf & \"").Replace("\n", "\" & vbCrLf & \"");
            return "\"" + escaped + "\"";
        }

        private async void btnSendMessage_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pcler = BulunanCihazlar.Where(x => x.IsSelected).ToList();
            string mesaj = txtChatMessage.Text.Trim();

            if (pcler.Count == 0 || string.IsNullOrEmpty(mesaj))
            {
                MessageBox.Show("Lütfen PC seçin ve bir mesaj yazın.");
                return;
            }

            // Kendi ekranımıza mesajı yazalım (Log gibi)
            EkranaMesajYaz("Ben", mesaj, Brushes.Black, FontWeights.Bold);
            txtChatMessage.Clear();

            string escapedMessage = EscapeVbsString(mesaj);

            await Task.Run(() =>
            {
                foreach (var pc in pcler)
                {
                    Logla($"{pc.IP} adresine mesaj iletiliyor...");

                    // VBScript İçeriği (İnteraktif InputBox)
                    // sohbet.vbs kendi kendini en sonda silecektir (fso.DeleteFile WScript.ScriptFullName, True)
                    string vbsContent = 
                        "Dim res\r\n" +
                        $"res = InputBox(\"Bilgi İşlem Departmanı Sohbet Mesajı:\" & vbCrLf & vbCrLf & {escapedMessage} & vbCrLf & vbCrLf & \"Yanıtınızı yazıp Tamam'a tıklayınız:\", \"Bilgi İşlem Canlı Sohbet\")\r\n" +
                        "Dim fso, f\r\n" +
                        "Set fso = CreateObject(\"Scripting.FileSystemObject\")\r\n" +
                        "Set f = fso.CreateTextFile(\"C:\\Users\\Public\\sohbet_yanit.txt\", True, True)\r\n" +
                        "f.Write res\r\n" +
                        "f.Close\r\n" +
                        "On Error Resume Next\r\n" +
                        "fso.DeleteFile WScript.ScriptFullName, True\r\n";

                    string remoteVbsPath = $@"\\{pc.IP}\c$\Users\Public\sohbet.vbs";
                    string localExecPath = @"C:\Users\Public\sohbet.vbs";
                    string responsePath = $@"\\{pc.IP}\c$\Users\Public\sohbet_yanit.txt";

                    try
                    {
                        using (new NetworkImpersonation(_domain, _kullanici, _sifre))
                        {
                            // Varsa eski yanıtı temizle
                            if (File.Exists(responsePath)) File.Delete(responsePath);

                            // VBScript dosyasını ağdan karşıya yaz (Unicode / UTF-16 LE)
                            File.WriteAllText(remoteVbsPath, vbsContent, System.Text.Encoding.Unicode);

                            // Aktif oturum ID'sini bul
                            string sessionId = GetRealActiveSessionId(pc.IP);

                            if (string.IsNullOrEmpty(sessionId) || sessionId == "0")
                            {
                                Dispatcher.Invoke(() => Logla($"⚠️ {pc.IP} -> Aktif kullanıcı yok (PC kilitli veya boş). Mesaj iletilemedi."));
                                // VBS dosyasını temizle
                                if (File.Exists(remoteVbsPath)) File.Delete(remoteVbsPath);
                                continue;
                            }

                            // PsExec ile kullanıcının ekranına yansıt
                            string psExecArgs = $@"\\{pc.IP} -u {_domain}\{_kullanici} -p {_sifre} -i {sessionId} -s -d -accepteula wscript.exe ""{localExecPath}""";

                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = "PsExec.exe",
                                Arguments = psExecArgs,
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            Process.Start(psi);

                            Dispatcher.Invoke(() => Logla($"✅ {pc.IP} -> Sohbet penceresi gönderildi."));
                        }

                        // Asenkron olarak yanıtı bekle
                        _ = Task.Run(async () =>
                        {
                            int maxWaitSeconds = 120;
                            int elapsed = 0;
                            bool replied = false;

                            while (elapsed < maxWaitSeconds)
                            {
                                if (_chatCts.Token.IsCancellationRequested) break;
                                await Task.Delay(1000);
                                if (_chatCts.Token.IsCancellationRequested) break;
                                elapsed++;

                                bool exists = false;
                                using (new NetworkImpersonation(_domain, _kullanici, _sifre))
                                {
                                    exists = File.Exists(responsePath);
                                }

                                if (exists)
                                {
                                    string replyText = "";
                                    using (new NetworkImpersonation(_domain, _kullanici, _sifre))
                                    {
                                        try
                                        {
                                            replyText = File.ReadAllText(responsePath, System.Text.Encoding.Unicode).Trim();
                                            File.Delete(responsePath); // Yanıtı sil
                                        }
                                        catch { }
                                    }

                                    replied = true;
                                    if (!_chatCts.Token.IsCancellationRequested)
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            if (string.IsNullOrEmpty(replyText))
                                            {
                                                EkranaMesajYaz(pc.Hostname, $"[{pc.IP} - {pc.Hostname}]: [Yanıt yazılmadan kapatıldı]", Brushes.Gray, FontWeights.Light);
                                            }
                                            else
                                            {
                                                EkranaMesajYaz(pc.Hostname, $"[{pc.IP} - {pc.Hostname}]: {replyText}", Brushes.Blue, FontWeights.Normal);
                                            }
                                        });
                                    }
                                    break;
                                }
                            }

                            if (!replied)
                            {
                                using (new NetworkImpersonation(_domain, _kullanici, _sifre))
                                {
                                    try
                                    {
                                        if (File.Exists(remoteVbsPath)) File.Delete(remoteVbsPath);
                                    }
                                    catch { }
                                }
                                if (!_chatCts.Token.IsCancellationRequested)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        EkranaMesajYaz(pc.Hostname, $"[{pc.IP} - {pc.Hostname}]: [Zaman aşımı / Yanıt verilmedi]", Brushes.Red, FontWeights.Light);
                                    });
                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => Logla($"❌ {pc.IP} Hata: " + ex.Message));
                    }
                }
            });
        }

        private async void btnSendAnnouncement_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pcler = BulunanCihazlar.Where(x => x.IsSelected).ToList();
            string mesaj = txtChatMessage.Text.Trim();

            if (pcler.Count == 0 || string.IsNullOrEmpty(mesaj))
            {
                MessageBox.Show("Lütfen PC seçin ve bir duyuru mesajı yazın.");
                return;
            }

            // Kendi ekranımıza duyuruyu yazalım
            EkranaMesajYaz("Duyuru", $"📢 [Tüm Cihazlara]: {mesaj}", Brushes.Red, FontWeights.Bold);
            txtChatMessage.Clear();

            string escapedMessage = EscapeVbsString(mesaj);

            await Task.Run(() =>
            {
                foreach (var pc in pcler)
                {
                    Logla($"{pc.IP} adresine duyuru gönderiliyor...");

                    // VBScript İçeriği (Tek yönlü MsgBox, System Modal, Bilgi İkonu)
                    // duyuru.vbs kendi kendini en sonda silecektir (fso.DeleteFile WScript.ScriptFullName, True)
                    string vbsContent =
                        $"MsgBox \"Bilgi İşlem Departmanı Duyurusu:\" & vbCrLf & vbCrLf & {escapedMessage}, 4160, \"Bilgi İşlem Duyurusu\"\r\n" +
                        "Dim fso\r\n" +
                        "Set fso = CreateObject(\"Scripting.FileSystemObject\")\r\n" +
                        "On Error Resume Next\r\n" +
                        "fso.DeleteFile WScript.ScriptFullName, True\r\n";

                    string remoteVbsPath = $@"\\{pc.IP}\c$\Users\Public\duyuru.vbs";
                    string localExecPath = @"C:\Users\Public\duyuru.vbs";

                    try
                    {
                        using (new NetworkImpersonation(_domain, _kullanici, _sifre))
                        {
                            // VBScript dosyasını ağdan karşıya yaz (Unicode / UTF-16 LE)
                            File.WriteAllText(remoteVbsPath, vbsContent, System.Text.Encoding.Unicode);

                            // Aktif oturum ID'sini bul
                            string sessionId = GetRealActiveSessionId(pc.IP);

                            if (string.IsNullOrEmpty(sessionId) || sessionId == "0")
                            {
                                Dispatcher.Invoke(() => Logla($"⚠️ {pc.IP} -> Aktif kullanıcı yok. Duyuru iletilemedi."));
                                if (File.Exists(remoteVbsPath)) File.Delete(remoteVbsPath);
                                continue;
                            }

                            // PsExec ile kullanıcının ekranına yansıt
                            string psExecArgs = $@"\\{pc.IP} -u {_domain}\{_kullanici} -p {_sifre} -i {sessionId} -s -d -accepteula wscript.exe ""{localExecPath}""";

                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = "PsExec.exe",
                                Arguments = psExecArgs,
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            Process.Start(psi);

                            Dispatcher.Invoke(() => Logla($"✅ {pc.IP} -> Duyuru gönderildi."));
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => Logla($"❌ {pc.IP} Hata: " + ex.Message));
                    }
                }
            });
        }

        // --- GELİŞMİŞ OTURUM BULUCU (ACTIVE OLAN KULLANICIYI BULUR) ---
        private string GetRealActiveSessionId(string ip)
        {
            try
            {
                // 'quser' komutu ile aktif kullanıcıları listele
                // Çıktı Örneği:
                // KULLANICI   OTURUMAD      KİMLİK  DURUM    BOŞTA KALMA   OTURUM AÇMA ZAMANI
                // kullanici   console           2  Active      none   15.01.2024 08:30

                string args = $@"\\{ip} -u {_domain}\{_kullanici} -p {_sifre} -s -accepteula cmd /c quser";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "PsExec.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();

                    // Çıktıyı satır satır analiz et
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains("USERNAME") || line.Contains("KULLANICI")) continue;

                        // Sadece durumu "Active" veya "Etkin" olan satırı al
                        if (line.Contains("Active") || line.Contains("Etkin"))
                        {
                            // Satırı boşluklara göre böl ve ID'yi (rakam olanı) bul
                            // Genelde 2. veya 3. sütundadır.
                            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var part in parts)
                            {
                                if (int.TryParse(part, out int id) && id > 0)
                                {
                                    return id.ToString(); // Bulunan ID'yi döndür
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Eğer quser başarısız olursa (bazı win sürümlerinde yoktur) yedek olarak explorer.exe ID'sini al
            return GetExplorerSessionId(ip);
        }

        private string GetExplorerSessionId(string ip)
        {
            try
            {
                string cmd = "powershell -command \"(Get-Process -Name explorer -ErrorAction SilentlyContinue | Select-Object -First 1).SessionId\"";
                string args = $@"\\{ip} -u {_domain}\{_kullanici} -p {_sifre} -s -accepteula {cmd}";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "PsExec.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return output.Trim();
                }
            }
            catch { return ""; }
        }

        private async void txtChatMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) btnSendMessage_Click(sender, e);
        }

        private void EkranaMesajYaz(string kim, string mesaj, Brush renk, FontWeight kalinlik)
        {
            var chatItem = new ChatMessageItem
            {
                SenderIcon = kim == "Ben" ? "👤" : "🖥️",
                Message = mesaj,
                Time = DateTime.Now.ToString("HH:mm"),
                Color = renk,
                Weight = kalinlik
            };
            lstChatHistory.Items.Add(chatItem);
            if (lstChatHistory.Items.Count > 0) lstChatHistory.ScrollIntoView(lstChatHistory.Items[lstChatHistory.Items.Count - 1]);
        }

        // --- YARDIMCILAR ---
        private async void MenuRestart_Click(object sender, RoutedEventArgs e) => await TopluKomutCalistir("shutdown /r /f /t 0", "Yeniden Başlatılıyor...");
        private async void MenuShutdown_Click(object sender, RoutedEventArgs e) => await TopluKomutCalistir("shutdown /s /f /t 0", "Kapatılıyor...");
        private async void MenuSpooler_Click(object sender, RoutedEventArgs e) => await TopluPsExecKomutu(BulunanCihazlar.Where(x => x.IsSelected).ToList(), "net stop Spooler & net start Spooler", "Spooler Resetleniyor...");

        private async Task TopluKomutCalistir(string komut, string mesaj)
        {
            var list = BulunanCihazlar.Where(x => x.IsSelected).ToList(); if (list.Count == 0) return;
            foreach (var pc in list) { Logla($"{pc.IP} -> {mesaj}"); await Task.Run(() => KomutCalistir("cmd.exe", $"/c {komut} /m \\\\{pc.IP}")); }
        }

        private async Task TopluPsExecKomutu(List<PcItem> pcler, string komut, string mesaj)
        {
            foreach (var pc in pcler) { Logla($"{pc.IP} -> {mesaj}"); await Task.Run(() => PsExecAgdanCalistir(pc.IP, "", komut)); }
        }

        private string GetSilentArguments(string f, string p)
        {
            string l = f.ToLower();
            if (l.EndsWith(".msi")) return $"msiexec.exe /i \"{p}\" /quiet /qn /norestart ALLUSERS=1";
            if (l.Contains("adobe")) return $"\"{p}\" /sAll /rs /msi EULA_ACCEPT=YES";
            if (l.Contains("chrome")) return $"\"{p}\" /silent /install";
            if (l.Contains("jre") || l.Contains("java")) return $"\"{p}\" /s REBOOT=0 SPONSORS=0";
            if (l.Contains("firefox")) return $"\"{p}\" -ms";
            if (l.Contains("edge")) return $"\"{p}\" /silent /install";
            if (l.Contains("zip") || l.Contains("rar")) return $"\"{p}\" /S";
            return $"\"{p}\" /S /verysilent /suppressmsgboxes /norestart";
        }

        private string GetUninstallArguments(string f, string p)
        {
            string l = f.ToLower();
            if (l.EndsWith(".msi")) return $"msiexec.exe /x \"{p}\" /quiet /qn /norestart";

            // Java ve Arksigner için özel toplu silme komutu
            if (l.Contains("java") || l.Contains("jre"))
                return "wmic product where \"name like 'Java%%'\" call uninstall /nointeractive";

            if (l.Contains("arksigner"))
                return "wmic product where \"name like '%ArkSigner%'\" call uninstall /nointeractive";

            // Standart Uninstall String Bulma
            string t = Path.GetFileNameWithoutExtension(f).Replace("_", " ").Replace("-", " ").Replace("Setup", "").Replace("x64", "").Replace("x86", "").Trim();
            string k = "*" + string.Join("*", t.Split(' ')) + "*";
            return $"powershell -NoProfile -Command \"$a=Get-WmiObject Win32_Product|Where {{$_.Name -like '{k}'}}; if($a){{$a.Uninstall()}} else {{ $r=Get-ItemProperty HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\* | Where {{$_.DisplayName -like '{k}'}}; if($r){{ cmd /c $r.QuietUninstallString }} }}\"";
        }

        private bool PsExecAgdanCalistir(string ip, string tamDosyaYolu, string komut)
        {
            try
            {
                string serverRoot = @"\\192.168.1.100\d$";
                if (!string.IsNullOrEmpty(tamDosyaYolu) && tamDosyaYolu.StartsWith(@"\\")) { try { Uri u = new Uri(tamDosyaYolu); serverRoot = $@"\\{u.Host}\{u.Segments[1].Trim('/')}"; } catch { } }
                string z = $@"cmd.exe /c ""net use ""{serverRoot}"" ""{_sifre}"" /user:{_domain}\{_kullanici} & start /wait """" {komut} & net use ""{serverRoot}"" /delete /y""";
                string args = $@"\\{ip} -u {_domain}\{_kullanici} -p {_sifre} -s -i -w ""C:\Windows\Temp"" -accepteula {z}";
                ProcessStartInfo psi = new ProcessStartInfo { FileName = "PsExec.exe", Arguments = args, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (Process p = Process.Start(psi)) { p.WaitForExit(); return (p.ExitCode == 0 || p.ExitCode == 3010); }
            }
            catch { return false; }
        }

        private void KomutCalistir(string dosya, string arg) { try { Process.Start(new ProcessStartInfo(dosya, arg) { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })?.WaitForExit(); } catch { } }
        private void Logla(string m) { Dispatcher.Invoke(() => { txtLog.Text += $"\n[{DateTime.Now:HH:mm}] {m}"; scrollLog.ScrollToEnd(); }); }

        // ==========================================
        // 6. UZAK ANALİZ (DONANIM) & SÜREÇLER SİSTEMİ (v3.6)
        // ==========================================

        private async void btnGetSystemInfo_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnGetSystemInfo.IsEnabled = false;
            btnGetSystemInfo.Content = "Sorgulanıyor...";
            Logla($"{pc.IP} için sistem bilgileri çekiliyor...");

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(800);
                lblSysOS.Text = "Microsoft Windows 11 Enterprise (DEMO)";
                lblSysCPU.Text = "Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz";
                lblSysRAM.Text = "16.0 GB";
                lblSysUptime = FindName("lblSysUptime") as TextBlock;
                if (lblSysUptime != null) lblSysUptime.Text = "4 Gün, 12 Saat, 32 Dakika";
                lblSysLastBoot = FindName("lblSysLastBoot") as TextBlock;
                if (lblSysLastBoot != null) lblSysLastBoot.Text = "Son Başlangıç: " + DateTime.Now.AddDays(-4).ToString("dd.MM.yyyy HH:mm:ss");
                lblSysDisk = FindName("lblSysDisk") as TextBlock;
                if (lblSysDisk != null) lblSysDisk.Text = "85.2 GB Boş / 250.0 GB Toplam";
                barSysDisk.Value = 65.9;
                lblSysDiskPct = FindName("lblSysDiskPct") as TextBlock;
                if (lblSysDiskPct != null) lblSysDiskPct.Text = "Doluluk Oranı: %65.9 (164.8 GB Dolu)";
                lblSysDiskHealth = FindName("lblSysDiskHealth") as TextBlock;
                if (lblSysDiskHealth != null) lblSysDiskHealth.Text = "NVMe Samsung SSD 980 - Sağlıklı (OK)\n(S/N: S64GNS0T123456)";
                
                btnGetSystemInfo.IsEnabled = true;
                btnGetSystemInfo.Content = "🔍 Sistem Bilgisi Sorgula";
                Logla($"✅ (DEMO MODU) Sistem bilgileri başarıyla simüle edildi.");
                return;
            }

            lblSysOS.Text = "Çekiliyor...";
            lblSysCPU.Text = "Çekiliyor...";
            lblSysRAM.Text = "Çekiliyor...";
            lblSysUptime.Text = "Çekiliyor...";
            lblSysLastBoot.Text = "Çekiliyor...";
            lblSysDisk.Text = "Çekiliyor...";
            barSysDisk.Value = 0;
            lblSysDiskPct.Text = "Doluluk Oranı: %0";
            lblSysDiskHealth.Text = "Çekiliyor...";

            string ip = pc.IP;

            await Task.Run(() =>
            {
                string os = "Çekilemedi";
                string cpu = "Çekilemedi";
                string ram = "Çekilemedi";
                string uptime = "Çekilemedi";
                string lastBootStr = "Çekilemedi";
                string disk = "Çekilemedi";
                double diskPct = 0;
                string diskPctText = "Doluluk Oranı: %0";
                string diskHealth = "Çekilemedi";

                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    // 1. Operating System & Uptime
                    try
                    {
                        ObjectQuery qOS = new ObjectQuery("SELECT Caption, LastBootUpTime FROM Win32_OperatingSystem");
                        using (ManagementObjectSearcher sOS = new ManagementObjectSearcher(scope, qOS))
                        {
                            foreach (ManagementObject obj in sOS.Get())
                            {
                                os = obj["Caption"]?.ToString().Trim();
                                if (obj["LastBootUpTime"] != null)
                                {
                                    try
                                    {
                                        DateTime lastBoot = ManagementDateTimeConverter.ToDateTime(obj["LastBootUpTime"].ToString());
                                        TimeSpan ts = DateTime.Now - lastBoot;
                                        uptime = $"{(int)ts.TotalDays} Gün, {ts.Hours} Saat, {ts.Minutes} Dakika";
                                        lastBootStr = "Son Başlangıç: " + lastBoot.ToString("dd.MM.yyyy HH:mm:ss");
                                    }
                                    catch { uptime = "Hesaplanamadı"; }
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex) { os = "Erişim Reddedildi: " + ex.Message; }

                    // 2. CPU
                    try
                    {
                        ObjectQuery qCPU = new ObjectQuery("SELECT Name FROM Win32_Processor");
                        using (ManagementObjectSearcher sCPU = new ManagementObjectSearcher(scope, qCPU))
                        {
                            foreach (ManagementObject obj in sCPU.Get())
                            {
                                cpu = obj["Name"]?.ToString().Trim();
                                break;
                            }
                        }
                    }
                    catch { cpu = "Çekilemedi"; }

                    // 3. RAM
                    try
                    {
                        ObjectQuery qRAM = new ObjectQuery("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                        using (ManagementObjectSearcher sRAM = new ManagementObjectSearcher(scope, qRAM))
                        {
                            foreach (ManagementObject obj in sRAM.Get())
                            {
                                if (long.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out long totalMem))
                                {
                                    ram = $"{Math.Round((double)totalMem / (1024 * 1024 * 1024), 1)} GB";
                                }
                                break;
                            }
                        }
                    }
                    catch { ram = "Çekilemedi"; }

                    // 4. Disk C:
                    try
                    {
                        ObjectQuery qDisk = new ObjectQuery("SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID = 'C:'");
                        using (ManagementObjectSearcher sDisk = new ManagementObjectSearcher(scope, qDisk))
                        {
                            foreach (ManagementObject obj in sDisk.Get())
                            {
                                if (long.TryParse(obj["Size"]?.ToString(), out long sizeBytes) &&
                                    long.TryParse(obj["FreeSpace"]?.ToString(), out long freeBytes))
                                {
                                    double totalGB = Math.Round((double)sizeBytes / (1024 * 1024 * 1024), 1);
                                    double freeGB = Math.Round((double)freeBytes / (1024 * 1024 * 1024), 1);
                                    double usedGB = Math.Round(totalGB - freeGB, 1);
                                    
                                    disk = $"{freeGB} GB Boş / {totalGB} GB Toplam";
                                    if (sizeBytes > 0)
                                    {
                                        diskPct = Math.Round((double)(sizeBytes - freeBytes) / sizeBytes * 100, 1);
                                        diskPctText = $"Doluluk Oranı: %{diskPct} ({usedGB} GB Dolu)";
                                    }
                                }
                                break;
                            }
                        }
                    }
                    catch { disk = "Çekilemedi"; }

                    // 5. Disk Drive S.M.A.R.T & Serial/Model
                    try
                    {
                        ObjectQuery qDiskDrive = new ObjectQuery("SELECT Model, SerialNumber, Status FROM Win32_DiskDrive");
                        using (ManagementObjectSearcher sDiskDrive = new ManagementObjectSearcher(scope, qDiskDrive))
                        {
                            List<string> diskInfos = new List<string>();
                            foreach (ManagementObject obj in sDiskDrive.Get())
                            {
                                string model = obj["Model"]?.ToString().Trim() ?? "Bilinmeyen Model";
                                string serial = obj["SerialNumber"]?.ToString().Trim() ?? "Bilinmiyor";
                                string status = obj["Status"]?.ToString().Trim() ?? "OK";
                                
                                string statusText = (status.Equals("OK", StringComparison.OrdinalIgnoreCase)) ? "Sağlıklı (OK)" : $"Riskli/Hata ({status})";
                                diskInfos.Add($"{model} - {statusText}\n(S/N: {serial})");
                            }
                            if (diskInfos.Count > 0)
                            {
                                diskHealth = string.Join("\n", diskInfos);
                            }
                            else
                            {
                                diskHealth = "Disk Bulunamadı";
                            }
                        }
                    }
                    catch { diskHealth = "Çekilemedi"; }
                }
                catch (Exception ex)
                {
                    os = cpu = ram = disk = uptime = lastBootStr = diskHealth = "Bağlantı Hatası";
                    Dispatcher.Invoke(() => Logla($"❌ {ip} WMI Bağlantı Hatası: " + ex.Message));
                }

                Dispatcher.Invoke(() =>
                {
                    lblSysOS.Text = os;
                    lblSysCPU.Text = cpu;
                    lblSysRAM.Text = ram;
                    lblSysUptime.Text = uptime;
                    lblSysLastBoot.Text = lastBootStr;
                    lblSysDisk.Text = disk;
                    barSysDisk.Value = diskPct;
                    lblSysDiskPct.Text = diskPctText;
                    lblSysDiskHealth.Text = diskHealth;

                    btnGetSystemInfo.IsEnabled = true;
                    btnGetSystemInfo.Content = "🔍 Sistem Bilgisi Sorgula";
                    Logla($"✅ {ip} sistem bilgileri sorgulandı.");

                    RecentManager.AddRecentAction(
                        (pc != null && !string.IsNullOrEmpty(pc.Hostname) && pc.Hostname != "Bilinmiyor") ? pc.Hostname : ip,
                        "Son Bağlanılan PC",
                        "💻",
                        "#3B82F6",
                        "PC",
                        ip);
                });
            });
        }

        private async void btnGetProcesses_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnGetProcesses.IsEnabled = false;
            btnGetProcesses.Content = "Yükleniyor...";
            Logla($"{pc.IP} çalışan süreçler listesi çekiliyor...");

            string ip = pc.IP;

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(600);
                var fakeProcesses = new List<ProcessItem>
                {
                    new ProcessItem { Name = "explorer.exe", Id = 4512, MemoryUsage = "45.2 MB" },
                    new ProcessItem { Name = "chrome.exe", Id = 8912, MemoryUsage = "128.5 MB" },
                    new ProcessItem { Name = "OtoProgram.exe", Id = 2304, MemoryUsage = "24.1 MB" },
                    new ProcessItem { Name = "svchost.exe", Id = 988, MemoryUsage = "12.3 MB" },
                    new ProcessItem { Name = "taskmgr.exe", Id = 10452, MemoryUsage = "18.9 MB" }
                };
                gridProcesses.ItemsSource = fakeProcesses;
                btnGetProcesses.IsEnabled = true;
                btnGetProcesses.Content = "🔄 Süreçleri Getir";
                Logla($"✅ (DEMO MODU) Süreç listesi başarıyla simüle edildi.");
                return;
            }

            await Task.Run(() =>
            {
                var processList = new List<ProcessItem>();
                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(8)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    ObjectQuery qProc = new ObjectQuery("SELECT ProcessId, Name, WorkingSetSize FROM Win32_Process");
                    using (ManagementObjectSearcher sProc = new ManagementObjectSearcher(scope, qProc))
                    {
                        foreach (ManagementObject obj in sProc.Get())
                        {
                            string name = obj["Name"]?.ToString() ?? "Bilinmiyor";
                            int pid = int.TryParse(obj["ProcessId"]?.ToString(), out int parsedPid) ? parsedPid : 0;
                            
                            string memUsage = "Bilinmiyor";
                            if (ulong.TryParse(obj["WorkingSetSize"]?.ToString(), out ulong memBytes))
                            {
                                memUsage = $"{Math.Round((double)memBytes / (1024 * 1024), 1)} MB";
                            }

                            processList.Add(new ProcessItem { Name = name, Id = pid, MemoryUsage = memUsage });
                        }
                    }

                    processList = processList.OrderBy(x => x.Name.ToLower()).ToList();
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"❌ {ip} süreç listesi çekilemedi: " + ex.Message));
                }

                Dispatcher.Invoke(() =>
                {
                    gridProcesses.ItemsSource = processList;
                    btnGetProcesses.IsEnabled = true;
                    btnGetProcesses.Content = "🔄 Süreçleri Getir";
                    Logla($"✅ {ip} süreç listesi güncellendi ({processList.Count} süreç).");
                });
            });
        }

        private async void btnKillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            string processName = "";
            int targetPid = -1;

            var selectedProc = gridProcesses.SelectedItem as ProcessItem;
            if (selectedProc != null)
            {
                processName = selectedProc.Name;
                targetPid = selectedProc.Id;
            }
            else
            {
                string input = txtKillProcessName.Text.Trim();
                if (string.IsNullOrEmpty(input) || input == "Süreç adını girin (Örn: chrome.exe veya hbys.exe)...")
                {
                    MessageBox.Show("Lütfen sonlandırılacak süreci seçin veya adını yazın.");
                    return;
                }
                processName = input;
            }

            string confirmationMsg = targetPid != -1 
                ? $"{pc.Hostname} bilgisayarındaki {processName} (PID: {targetPid}) sürecini sonlandırmak istiyor musunuz?"
                : $"{pc.Hostname} bilgisayarındaki '{processName}' adlı TÜM süreçleri sonlandırmak istiyor musunuz?";

            var confirmResult = MessageBox.Show(confirmationMsg, "Süreç Sonlandırma Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmResult != MessageBoxResult.Yes) return;

            btnKillProcess.IsEnabled = false;
            Logla($"{pc.IP} üzerinde süreç sonlandırma tetiklendi: {processName}");

            string ip = pc.IP;

            await Task.Run(() =>
            {
                bool success = false;
                string logMsg = "";

                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    string queryStr = targetPid != -1 
                        ? $"SELECT * FROM Win32_Process WHERE ProcessId = {targetPid}"
                        : $"SELECT * FROM Win32_Process WHERE Name LIKE '{processName}' OR Name LIKE '{processName}.exe'";

                    ObjectQuery qProc = new ObjectQuery(queryStr);
                    using (ManagementObjectSearcher sProc = new ManagementObjectSearcher(scope, qProc))
                    {
                        int terminatedCount = 0;
                        foreach (ManagementObject obj in sProc.Get())
                        {
                            try
                            {
                                obj.InvokeMethod("Terminate", null);
                                terminatedCount++;
                                success = true;
                            }
                            catch (Exception killEx)
                            {
                                logMsg += $" WMI Hatası: {killEx.Message}.";
                            }
                        }

                        if (success)
                        {
                            logMsg = $"Sonlandırıldı ({terminatedCount} adet süreç).";
                        }
                        else if (terminatedCount == 0)
                        {
                            success = KillProcessFallback(ip, processName, targetPid);
                            logMsg = success ? "Sonlandırıldı (taskkill ile)." : "Süreç bulunamadı veya yetki hatası.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    success = KillProcessFallback(ip, processName, targetPid);
                    logMsg = success ? "Sonlandırıldı (taskkill ile)." : "Bağlantı/WMI Hatası: " + ex.Message;
                }

                Dispatcher.Invoke(() =>
                {
                    btnKillProcess.IsEnabled = true;
                    Logla(success ? $"✅ {ip} -> {logMsg}" : $"❌ {ip} -> {logMsg}");
                    MessageBox.Show(success ? "Süreç başarıyla sonlandırıldı." : "Süreç sonlandırılamadı: " + logMsg, "Sonuç");
                    
                    btnGetProcesses_Click(null, null);
                });
            });
        }

        private bool KillProcessFallback(string ip, string processName, int pid)
        {
            try
            {
                string args = pid != -1
                    ? $"/s {ip} /u {_domain}\\{_kullanici} /p {_sifre} /f /pid {pid}"
                    : $"/s {ip} /u {_domain}\\{_kullanici} /p {_sifre} /f /im \"{processName}\"";

                if (pid == -1 && !processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    args = $"/s {ip} /u {_domain}\\{_kullanici} /p {_sifre} /f /im \"{processName}.exe\"";
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private void TxtKillProcessName_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtKillProcessName.Text == "Süreç adını girin (Örn: chrome.exe veya hbys.exe)...")
            {
                txtKillProcessName.Text = "";
                txtKillProcessName.Foreground = Brushes.Black;
            }
        }

        private void TxtKillProcessName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtKillProcessName.Text))
            {
                txtKillProcessName.Text = "Süreç adını girin (Örn: chrome.exe veya hbys.exe)...";
                txtKillProcessName.Foreground = Brushes.Gray;
            }
        }

        private void TxtRemoteCmd_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtRemoteCmd.Text == "Çalıştırılacak komut (Örn: ipconfig veya gpupdate /force)...")
            {
                txtRemoteCmd.Text = "";
                txtRemoteCmd.Foreground = Brushes.Black;
            }
        }

        private void TxtRemoteCmd_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRemoteCmd.Text))
            {
                txtRemoteCmd.Text = "Çalıştırılacak komut (Örn: ipconfig veya gpupdate /force)...";
                txtRemoteCmd.Foreground = Brushes.Gray;
            }
        }

        private async void btnRunRemoteCmd_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            string cmd = txtRemoteCmd.Text.Trim();
            if (string.IsNullOrEmpty(cmd) || cmd == "Çalıştırılacak komut (Örn: ipconfig veya gpupdate /force)...")
            {
                MessageBox.Show("Lütfen çalıştırmak istediğiniz komutu yazın.", "Komut Girilmedi");
                return;
            }

            RecentManager.AddRecentAction(
                cmd.Length > 15 ? cmd.Substring(0, 12) + "..." : cmd,
                "Son Çalıştırılan Makro",
                "⚡",
                "#F59E0B",
                "Macro",
                cmd);

            bool isPowerShell = cmbShellType.SelectedIndex == 1;

            btnRunRemoteCmd.IsEnabled = false;
            btnRunRemoteCmd.Content = "Çalışıyor...";
            string shellName = isPowerShell ? "PowerShell" : "CMD";
            Logla($"{pc.IP} üzerinde {shellName} komutu çalıştırılıyor: \"{cmd}\"");

            string ip = pc.IP;

            await Task.Run(() =>
            {
                string output = PsExecAgdanCalistirOutput(ip, cmd, isPowerShell);
                
                Dispatcher.Invoke(() =>
                {
                    Logla($"\n=== [{ip} Komut Çıktısı] ===");
                    Logla(output);
                    Logla("===============================");
                    btnRunRemoteCmd.IsEnabled = true;
                    btnRunRemoteCmd.Content = "▶ KOMUTU ÇALIŞTIR";
                });
            });
        }

        private string PsExecAgdanCalistirOutput(string ip, string komut, bool isPowerShell)
        {
            try
            {
                string cmdWrapper;
                if (isPowerShell)
                {
                    byte[] bytes = System.Text.Encoding.Unicode.GetBytes(komut);
                    string base64 = Convert.ToBase64String(bytes);
                    cmdWrapper = $@"powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand {base64}";
                }
                else
                {
                    cmdWrapper = $@"cmd.exe /c ""{komut}""";
                }

                string args = $@"\\{ip} -u {_domain}\{_kullanici} -p {_sifre} -s -i -w ""C:\Windows\Temp"" -accepteula {cmdWrapper}";
                
                try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); } catch { }
                System.Text.Encoding oemEncoding = null;
                try { oemEncoding = System.Text.Encoding.GetEncoding(857); } catch { oemEncoding = System.Text.Encoding.UTF8; }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "PsExec.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = oemEncoding,
                    StandardErrorEncoding = oemEncoding
                };

                using (Process p = Process.Start(psi))
                {
                    string outStr = p.StandardOutput.ReadToEnd();
                    string errStr = p.StandardError.ReadToEnd();
                    p.WaitForExit();

                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    if (!string.IsNullOrWhiteSpace(outStr))
                    {
                        sb.AppendLine(outStr);
                    }
                    if (!string.IsNullOrWhiteSpace(errStr))
                    {
                        sb.AppendLine("HATA / UYARI ÇIKTISI:");
                        sb.AppendLine(errStr);
                    }

                    if (sb.Length == 0)
                    {
                        return $"(İşlem tamamlandı, çıkış kodu: {p.ExitCode})";
                    }
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                return "Bağlantı/Çalıştırma Hatası: " + ex.Message;
            }
        }

        private void TxtRemoteCmd_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            string initialText = txtRemoteCmd.Text;
            if (initialText == "Çalıştırılacak komut (Örn: ipconfig veya gpupdate /force)...")
            {
                initialText = "";
            }

            var editor = new TerminalDetayWindow(
                initialText, 
                isReadOnly: false, 
                title: "Gelişmiş Komut / Betik Editörü", 
                icon: "📝"
            );

            if (editor.ShowDialog() == true && editor.IsApplied)
            {
                txtRemoteCmd.Text = editor.ResultText;
                txtRemoteCmd.Foreground = Brushes.Black;
            }
        }

        private void TxtLog_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                e.Handled = true;

                var viewer = new TerminalDetayWindow(
                    txtLog.Text, 
                    isReadOnly: true, 
                    title: "Detaylı Çalıştırma Çıktısı & Log", 
                    icon: "📜"
                );

                viewer.ShowDialog();
            }
        }

        private void CalibMode_Changed(object sender, RoutedEventArgs e)
        {
            if (lblCalibTarget == null || txtCalibTarget == null) return;

            if (rbCalibNetwork.IsChecked == true)
            {
                lblCalibTarget.Text = "Yazıcı IP Adresi (Port 9100)";
                txtCalibTarget.Text = "10.235.80.200";
            }
            else
            {
                lblCalibTarget.Text = "Yazıcı Paylaşım Adı (Uzak PC Üzerindeki)";
                txtCalibTarget.Text = "Barkod_Yazici";
            }
        }

        private void cmbCalibPreset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (txtCalibCommand == null || cmbCalibPreset == null) return;

            var selectedItem = cmbCalibPreset.SelectedItem as System.Windows.Controls.ComboBoxItem;
            if (selectedItem == null) return;

            string content = selectedItem.Content.ToString();
            if (content.Contains("Zebra"))
            {
                txtCalibCommand.Text = "~JC";
            }
            else if (content.Contains("TSC"))
            {
                txtCalibCommand.Text = "GAPDETECT\r\n";
            }
            else if (content.Contains("Argox"))
            {
                txtCalibCommand.Text = "~S";
            }
            else if (content.Contains("Honeywell"))
            {
                txtCalibCommand.Text = "TESTFEED\r\n";
            }
            else
            {
                txtCalibCommand.Text = "";
            }
        }

        private async void btnInstallPrinter_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pcler = BulunanCihazlar.Where(x => x.IsSelected).ToList();
            if (pcler.Count == 0)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden en az bir PC seçin.");
                return;
            }

            string prName = txtNewPrinterName.Text.Trim();
            string prIP = txtNewPrinterIP.Text.Trim();
            string prDriver = cmbNewPrinterDriver.Text.Trim();
            bool isShared = chkNewPrinterShare.IsChecked == true;

            if (string.IsNullOrEmpty(prName) || string.IsNullOrEmpty(prIP) || string.IsNullOrEmpty(prDriver))
            {
                MessageBox.Show("Lütfen Yazıcı Adı, IP Adresi ve Sürücü alanlarını doldurun.");
                return;
            }

            btnInstallPrinter.IsEnabled = false;
            Logla($"\n🖨️ YAZICI DAĞITIMI BAŞLIYOR... (Hedef sayısı: {pcler.Count})");

            await Task.Run(async () =>
            {
                foreach (var pc in pcler)
                {
                    Logla($"\n>> HEDEF PC: {pc.IP} ({pc.Hostname})");
                    
                    // IPC$ Bağlantısı
                    KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                    try
                    {
                        Logla($"   1/3 Yazıcı Portu oluşturuluyor (IP_{prIP})...");
                        string portCmd = $"powershell -NoProfile -Command \"Add-PrinterPort -Name 'IP_{prIP}' -PrinterHostAddress '{prIP}' -ErrorAction SilentlyContinue\"";
                        PsExecAgdanCalistir(pc.IP, "", portCmd);

                        Logla($"   2/3 Sürücü tanımlanıyor ({prDriver})...");
                        string driverCmd = $"powershell -NoProfile -Command \"Add-PrinterDriver -Name '{prDriver}' -ErrorAction SilentlyContinue\"";
                        PsExecAgdanCalistir(pc.IP, "", driverCmd);

                        Logla($"   3/3 Yazıcı ekleniyor ({prName})...");
                        string addPrinterCmd = $"powershell -NoProfile -Command \"Add-Printer -Name '{prName}' -PortName 'IP_{prIP}' -DriverName '{prDriver}' -ErrorAction SilentlyContinue\"";
                        PsExecAgdanCalistir(pc.IP, "", addPrinterCmd);

                        if (isShared)
                        {
                            Logla($"   [*] Yazıcı paylaşıma açılıyor ({prName})...");
                            string shareCmd = $"powershell -NoProfile -Command \"Set-Printer -Name '{prName}' -Shared $true -ShareName '{prName}' -ErrorAction SilentlyContinue\"";
                            PsExecAgdanCalistir(pc.IP, "", shareCmd);
                        }

                        Logla("   ✅ Yazıcı kurulum komutları uzak makineye iletildi.");
                    }
                    catch (Exception ex)
                    {
                        Logla($"   ❌ Hata oluştu: {ex.Message}");
                    }

                    // Bağlantıyı kes
                    KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /delete /y");
                    await Task.Delay(1000);
                }
                Logla("\n🏁 YAZICI DAĞITIMI TAMAMLANDI");
            });

            btnInstallPrinter.IsEnabled = true;
        }

        private async void btnSendCalib_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string cmdText = txtCalibCommand.Text;
            if (string.IsNullOrEmpty(cmdText))
            {
                MessageBox.Show("Lütfen gönderilecek komutu girin.");
                return;
            }

            btnSendCalib.IsEnabled = false;

            if (rbCalibNetwork.IsChecked == true)
            {
                // Network Mode: TCP Client Port 9100
                string ip = txtCalibTarget.Text.Trim();
                if (string.IsNullOrEmpty(ip))
                {
                    MessageBox.Show("Lütfen Yazıcı IP Adresini girin.");
                    btnSendCalib.IsEnabled = true;
                    return;
                }

                Logla($"\n📡 TCP/IP üzerinden kalibrasyon komutu gönderiliyor... ({ip}:9100)");

                try
                {
                    await Task.Run(async () =>
                    {
                        using (var client = new System.Net.Sockets.TcpClient())
                        {
                            var connectTask = client.ConnectAsync(ip, 9100);
                            if (await Task.WhenAny(connectTask, Task.Delay(5000)) == connectTask)
                            {
                                if (client.Connected)
                                {
                                    using (var stream = client.GetStream())
                                    {
                                        byte[] data = System.Text.Encoding.ASCII.GetBytes(cmdText);
                                        await stream.WriteAsync(data, 0, data.Length);
                                        Logla("   ✅ Komut başarıyla gönderildi (TCP RAW).");
                                    }
                                }
                                else
                                {
                                    Logla("   ❌ Bağlantı başarısız.");
                                }
                            }
                            else
                            {
                                Logla("   ❌ Zaman aşımı: Cihaza 9100 portundan erişilemedi.");
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logla($"   ❌ TCP Gönderim Hatası: {ex.Message}");
                }
            }
            else
            {
                // Local Spooler Mode: PsExec -> File -> Spool
                var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
                if (pc == null)
                {
                    MessageBox.Show("Lütfen sol taraftaki listeden hedef bilgisayarı (Uzak PC) seçin.");
                    btnSendCalib.IsEnabled = true;
                    return;
                }

                string printerName = txtCalibTarget.Text.Trim();
                if (string.IsNullOrEmpty(printerName))
                {
                    MessageBox.Show("Lütfen Yazıcı Paylaşım Adını girin.");
                    btnSendCalib.IsEnabled = true;
                    return;
                }

                Logla($"\n📡 Uzak PC ({pc.IP}) üzerindeki yerel yazıcıya ({printerName}) kalibrasyon gönderiliyor...");

                await Task.Run(async () =>
                {
                    // IPC$ Bağlantısı
                    KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                    try
                    {
                        byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(cmdText);
                        string base64Cmd = Convert.ToBase64String(textBytes);

                        Logla("   [1/3] Komut uzak PC'ye aktarılıyor...");
                        string writeCmd = $"powershell -NoProfile -Command \"$b = [System.Convert]::FromBase64String('{base64Cmd}'); [System.IO.File]::WriteAllBytes('C:\\Windows\\Temp\\calib_cmd.txt', $b)\"";
                        PsExecAgdanCalistir(pc.IP, "", writeCmd);

                        Logla("   [2/3] Yazıcı paylaşım durumu doğrulanıyor...");
                        string shareVerifyCmd = $"powershell -NoProfile -Command \"Set-Printer -Name '{printerName}' -Shared $true -ShareName '{printerName}' -ErrorAction SilentlyContinue\"";
                        PsExecAgdanCalistir(pc.IP, "", shareVerifyCmd);

                        Logla("   [3/3] Komut yazıcı kuyruğuna gönderiliyor...");
                        string printCmd = $"cmd.exe /c \"copy /b C:\\Windows\\Temp\\calib_cmd.txt \\\\127.0.0.1\\{printerName}\"";
                        PsExecAgdanCalistir(pc.IP, "", printCmd);

                        string cleanCmd = "cmd.exe /c del /f /q C:\\Windows\\Temp\\calib_cmd.txt";
                        PsExecAgdanCalistir(pc.IP, "", cleanCmd);

                        Logla("   ✅ Kalibrasyon komutu başarıyla kuyruğa gönderildi.");
                    }
                    catch (Exception ex)
                    {
                        Logla($"   ❌ Hata: {ex.Message}");
                    }

                    // Bağlantıyı kes
                    KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /delete /y");
                });
            }

            btnSendCalib.IsEnabled = true;
        }

        private async void btnGetPrinters_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden sorgulanacak bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnGetPrinters.IsEnabled = false;
            btnGetPrinters.Content = "Yükleniyor...";
            Logla($"{pc.IP} için takılı yazıcılar sorgulanıyor...");

            gridPrinters.ItemsSource = null;
            string ip = pc.IP;

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(600);
                var fakePrinters = new List<PrinterItem>
                {
                    new PrinterItem { Name = "HP LaserJet MFP M227-M231", Port = "USB001", Driver = "HP LaserJet MFP M227-M231 PCL 6", Shared = false, ShareName = "" },
                    new PrinterItem { Name = "Zebra GK420t", Port = "USB002", Driver = "Zebra GK420t", Shared = true, ShareName = "Zebra_Barkod" },
                    new PrinterItem { Name = "Microsoft Print to PDF", Port = "PORTPROMPT:", Driver = "Microsoft Print to PDF", Shared = false, ShareName = "" }
                };
                gridPrinters.ItemsSource = fakePrinters;
                btnGetPrinters.IsEnabled = true;
                btnGetPrinters.Content = "🔄 Listele";
                Logla($"✅ (DEMO MODU) Yazıcı listesi başarıyla simüle edildi.");
                return;
            }

            await Task.Run(() =>
            {
                var printers = new List<PrinterItem>();

                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    ObjectQuery query = new ObjectQuery("SELECT Name, PortName, DriverName, Shared, ShareName FROM Win32_Printer");
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            printers.Add(new PrinterItem
                            {
                                Name = obj["Name"]?.ToString() ?? "",
                                Port = obj["PortName"]?.ToString() ?? "",
                                Driver = obj["DriverName"]?.ToString() ?? "",
                                Shared = Convert.ToBoolean(obj["Shared"] ?? false),
                                ShareName = obj["ShareName"]?.ToString() ?? ""
                            });
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        gridPrinters.ItemsSource = printers;
                        Logla($"   ✅ {printers.Count} adet yazıcı başarıyla listelendi.");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"   ❌ WMI Hatası: {ex.Message}"));
                }
            });

            btnGetPrinters.IsEnabled = true;
            btnGetPrinters.Content = "🔄 Listele";
        }

        private async void btnInstallNetworkPrinter_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pcler = BulunanCihazlar.Where(x => x.IsSelected).ToList();
            if (pcler.Count == 0)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden en az bir PC seçin.");
                return;
            }

            string prPath = txtNetworkPrinterPath.Text.Trim();
            if (string.IsNullOrEmpty(prPath))
            {
                MessageBox.Show("Lütfen Ağ Yazıcı Yolunu girin (Örn: \\\\192.168.1.10\\Barkod_Yazici).");
                return;
            }

            btnInstallNetworkPrinter.IsEnabled = false;
            Logla($"\n🔗 PAYLAŞILAN AĞ YAZICISI BAĞLAMA BAŞLIYOR... (Hedef sayısı: {pcler.Count})");

            await Task.Run(async () =>
            {
                foreach (var pc in pcler)
                {
                    Logla($"\n>> HEDEF PC: {pc.IP} ({pc.Hostname})");
                    
                    // IPC$ Bağlantısı
                    KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                    try
                    {
                        Logla($"   Yazıcı ağ yolu bağlanıyor ({prPath})...");
                        // printui.dll kullanarak makine genelinde (per-machine) bağlantı oluşturulur.
                        string addNetPrinterCmd = $"rundll32.exe printui.dll,PrintUIEntry /ga /n\"{prPath}\"";
                        PsExecAgdanCalistir(pc.IP, "", addNetPrinterCmd);

                        // Spooler resetlenmesi bağlantının görünmesi için yararlıdır
                        Logla("   Spooler servisi resetleniyor...");
                        string resetSpoolerCmd = "cmd.exe /c \"net stop Spooler & net start Spooler\"";
                        PsExecAgdanCalistir(pc.IP, "", resetSpoolerCmd);

                        Logla("   ✅ Ağ yazıcısı ekleme komutları uzak makineye iletildi.");
                    }
                    catch (Exception ex)
                    {
                        Logla($"   ❌ Hata oluştu: {ex.Message}");
                    }

                    // Bağlantıyı kes
                    KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /delete /y");
                    await Task.Delay(1000);
                }
                Logla("\n🏁 AĞ YAZICISI BAĞLAMA TAMAMLANDI");
            });

            btnInstallNetworkPrinter.IsEnabled = true;
        }

        private void gridPrinters_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selectedPrinter = gridPrinters.SelectedItem as PrinterItem;
            if (selectedPrinter == null) return;

            if (rbCalibLocal.IsChecked == true)
            {
                txtCalibTarget.Text = selectedPrinter.Name;
            }
        }

        private async void MenuSetDefaultPrinter_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            var selectedPrinter = gridPrinters.SelectedItem as PrinterItem;
            if (pc == null || selectedPrinter == null)
            {
                MessageBox.Show("Lütfen bir bilgisayar ve listeden varsayılan yapılacak bir yazıcı seçin.");
                return;
            }

            string prName = selectedPrinter.Name;
            string prPort = selectedPrinter.Port;

            Logla($"\n⭐ {pc.IP} üzerinde '{prName}' varsayılan yazıcı yapılıyor...");

            await Task.Run(async () =>
            {
                // IPC$ Bağlantısı
                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                try
                {
                    // 1. printui ile varsayılan yapmayı dene (Interactive shell)
                    string printuiCmd = $"rundll32.exe printui.dll,PrintUIEntry /y /n \"{prName}\"";
                    PsExecAgdanCalistir(pc.IP, "", printuiCmd);

                    // 2. Aktif oturum açmış kullanıcının Registry kaydını güncelle
                    string psCmd = $"powershell -NoProfile -Command \"" +
                                   $"$u = (Get-CimInstance Win32_ComputerSystem).UserName; " +
                                   $"if ($u) {{ " +
                                   $"  $name = $u.Substring($u.IndexOf('\\\\') + 1); " +
                                   $"  $sid = (New-Object System.Security.Principal.NTAccount($name)).Translate([System.Security.Principal.SecurityIdentifier]).Value; " +
                                   $"  $p = 'Registry::HKEY_USERS\\\\' + $sid + '\\\\Software\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Windows'; " +
                                   $"  if (Test-Path $p) {{ " +
                                   $"    Set-ItemProperty -Path $p -Name 'Device' -Value '{prName},winspool,{prPort}' -Force; " +
                                   $"  }} " +
                                   $"}}\"";
                    PsExecAgdanCalistir(pc.IP, "", psCmd);

                    Logla($"   ✅ '{prName}' varsayılan yazıcı yapma komutları gönderildi.");
                }
                catch (Exception ex)
                {
                    Logla($"   ❌ Hata: {ex.Message}");
                }

                // Bağlantıyı kes
                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /delete /y");
            });
        }

        private async void btnDiskCleanup_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden işlem yapılacak bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnDiskCleanup.IsEnabled = false;
            btnDiskCleanup.Content = "Temizleniyor...";
            Logla($"\n🧹 {pc.IP} üzerinde disk temizliği başlatılıyor...");

            string ip = pc.IP;

            await Task.Run(async () =>
            {
                // IPC$ Bağlantısı
                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                try
                {
                    // PowerShell temizlik scriptini çalıştır (WMI Process ile)
                    string psCleanup = "powershell -NoProfile -Command \"" +
                                       "Remove-Item -Path 'C:\\\\Windows\\\\Temp\\\\*' -Force -Recurse -ErrorAction SilentlyContinue; " +
                                       "Get-ChildItem -Path 'C:\\\\Users' -Directory | ForEach-Object { " +
                                       "  $t = Join-Path $_.FullName 'AppData\\\\Local\\\\Temp\\\\*'; " +
                                       "  if (Test-Path $t) { Remove-Item -Path $t -Force -Recurse -ErrorAction SilentlyContinue } " +
                                       "}; " +
                                       "Remove-Item -Path 'C:\\\\Windows\\\\SoftwareDistribution\\\\Download\\\\*' -Force -Recurse -ErrorAction SilentlyContinue\"";

                    Logla("   [1/2] Geçici dosyalar ve Windows Update indirme önbelleği temizleniyor...");
                    
                    // WMI scope oluştur
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    // WMI Process oluşturma
                    var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                    var inParams = processClass.GetMethodParameters("Create");
                    inParams["CommandLine"] = psCleanup;
                    processClass.InvokeMethod("Create", inParams, null);

                    // Kısa bir bekleme
                    await Task.Delay(3000);
                    Logla("   ✅ Disk temizliği komutu iletildi. Disk durumu yeniden sorgulanıyor...");
                }
                catch (Exception ex)
                {
                    Logla($"   ❌ Temizlik Hatası: {ex.Message}");
                }

                // Bağlantıyı kes
                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /delete /y");
            });

            btnDiskCleanup.IsEnabled = true;
            btnDiskCleanup.Content = "🧹 Alan Boşalt / Temizle";

            // Disk durumunu yenile
            btnGetSystemInfo_Click(null, null);
        }

        private async void btnGetActiveSessions_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden sorgulanacak bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnGetActiveSessions.IsEnabled = false;
            btnGetActiveSessions.Content = "Yeniliyor...";
            Logla($"\n👥 {pc.IP} için aktif oturumlar sorgulanıyor...");

            gridActiveSessions.ItemsSource = null;
            string ip = pc.IP;

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(500);
                var fakeSessions = new List<ActiveSessionItem>
                {
                    new ActiveSessionItem { Username = "kullanici.adi", SessionName = "console", SessionId = "1", State = "Active" },
                    new ActiveSessionItem { Username = "hbys_temp", SessionName = "rdp-tcp#0", SessionId = "2", State = "Listen" }
                };
                gridActiveSessions.ItemsSource = fakeSessions;
                btnGetActiveSessions.IsEnabled = true;
                btnGetActiveSessions.Content = "🔄 Oturumları Getir";
                Logla($"✅ (DEMO MODU) Aktif oturumlar başarıyla simüle edildi.");
                return;
            }

            await Task.Run(async () =>
            {
                var sessions = new List<ActiveSessionItem>();

                // IPC$ Bağlantısı
                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                try
                {
                    // quser komutunu WMI ile uzak makinede çalıştırıp dosyaya yaz
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    string outFilePath = @"C:\Windows\Temp\quser_out.txt";
                    string qCmd = $@"cmd.exe /c quser > {outFilePath}";

                    var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                    var inParams = processClass.GetMethodParameters("Create");
                    inParams["CommandLine"] = qCmd;
                    processClass.InvokeMethod("Create", inParams, null);

                    // Komutun tamamlanması için kısa bir süre bekle
                    await Task.Delay(2000);

                    // Dosyayı oku ve ayrıştır
                    string sharePath = $@"\\{ip}\c$\Windows\Temp\quser_out.txt";
                    if (File.Exists(sharePath))
                    {
                        string output = File.ReadAllText(sharePath);

                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            if (line.Contains("USERNAME") || line.Contains("KULLANICI")) continue;

                            var parts = System.Text.RegularExpressions.Regex.Split(line.Trim(), @"\s{2,}");
                            if (parts.Length >= 4)
                            {
                                string userVal = parts[0].Replace(">", "").Trim();
                                string sessionNameVal = "";
                                string idVal = "";
                                string stateVal = "";

                                int tempId;
                                if (int.TryParse(parts[1], out tempId))
                                {
                                    sessionNameVal = "-";
                                    idVal = parts[1];
                                    stateVal = parts[2];
                                }
                                else
                                {
                                    sessionNameVal = parts[1];
                                    idVal = parts[2];
                                    stateVal = parts[3];
                                }

                                sessions.Add(new ActiveSessionItem
                                {
                                    Username = userVal,
                                    SessionName = sessionNameVal,
                                    SessionId = idVal,
                                    State = stateVal
                                });
                            }
                        }

                        try { File.Delete(sharePath); } catch { }

                        Dispatcher.Invoke(() =>
                        {
                            gridActiveSessions.ItemsSource = sessions;
                            Logla($"   ✅ {sessions.Count} adet aktif oturum listelendi.");
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() => Logla("   ⚠️ Aktif oturum bilgisi alınamadı (quser çıktısı oluşmadı)."));
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"   ❌ WMI/Oturum Hatası: {ex.Message}"));
                }

                // Bağlantıyı kes
                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /delete /y");
            });

            btnGetActiveSessions.IsEnabled = true;
            btnGetActiveSessions.Content = "🔄 Yenile";
        }

        private async void MenuLogoffSession_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            var selectedSession = gridActiveSessions.SelectedItem as ActiveSessionItem;
            if (pc == null || selectedSession == null)
            {
                MessageBox.Show("Lütfen bir bilgisayar ve sonlandırılacak bir aktif oturum seçin.");
                return;
            }

            string sessionId = selectedSession.SessionId;
            string username = selectedSession.Username;

            Logla($"\n❌ {pc.IP} üzerinde '{username}' kullanıcısının oturumu (ID: {sessionId}) sonlandırılıyor...");

            await Task.Run(async () =>
            {
                // IPC$ Bağlantısı
                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{pc.IP}\root\cimv2", options);
                    scope.Connect();

                    string logoffCmd = $@"cmd.exe /c logoff {sessionId}";

                    var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                    var inParams = processClass.GetMethodParameters("Create");
                    inParams["CommandLine"] = logoffCmd;
                    processClass.InvokeMethod("Create", inParams, null);

                    Logla($"   ✅ Oturumu kapatma komutu iletildi.");
                }
                catch (Exception ex)
                {
                    Logla($"   ❌ Kapatma Hatası: {ex.Message}");
                }

                // Bağlantıyı kes
                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /delete /y");
            });

            await Task.Delay(2000);
            btnGetActiveSessions_Click(null, null);
        }

        private void btnShadowSession_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var selectedSession = gridActiveSessions.SelectedItem as ActiveSessionItem;
            if (selectedSession == null)
            {
                MessageBox.Show("Lütfen aktif oturumlar listesinden bir oturum seçin.", "Oturum Seçilmedi");
                return;
            }
            ShadowSession(selectedSession);
        }

        private void MenuShadowSession_Click(object sender, RoutedEventArgs e)
        {
            var selectedSession = gridActiveSessions.SelectedItem as ActiveSessionItem;
            if (selectedSession == null) return;
            ShadowSession(selectedSession);
        }

        private async void ShadowSession(ActiveSessionItem selectedSession)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null || selectedSession == null)
            {
                MessageBox.Show("Lütfen bir bilgisayar ve aktif bir oturum seçin.");
                return;
            }

            string targetIp = pc.IP;
            string sessionId = selectedSession.SessionId;
            string username = selectedSession.Username;

            Logla($"\n🖥️ {targetIp} üzerinde '{username}' kullanıcısının oturumuna (ID: {sessionId}) gölge bağlantı (Shadow) kuruluyor...");

            // UI düğmesini geçici olarak devre dışı bırak
            btnShadowSession.IsEnabled = false;

            await Task.Run(async () =>
            {
                // IPC$ Bağlantısı kurarak yetkilendirmeyi garanti et
                KomutCalistir("net", $@"use \\{targetIp}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{targetIp}\root\cimv2", options);
                    scope.Connect();

                    // WMI Process oluşturma sınıfı kullanarak reg ve sc komutları göndereceğiz
                    var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                    
                    Dispatcher.Invoke(() => Logla("   ⚙️ Uzak bilgisayarda RDP, Hizmet ve Gölge politikaları yapılandırılıyor..."));

                    // 1. RDP'yi etkinleştir
                    string enableRdpCmd = @"cmd.exe /c reg add ""HKLM\System\CurrentControlSet\Control\Terminal Server"" /v fDenyTSConnections /t REG_DWORD /d 0 /f";
                    var inParamsRdp = processClass.GetMethodParameters("Create");
                    inParamsRdp["CommandLine"] = enableRdpCmd;
                    processClass.InvokeMethod("Create", inParamsRdp, null);

                    // 2. Gölge oturum politikasını ayarla (Shadow = 1 -> Kullanıcı izniyle tam kontrol)
                    string setShadowCmd = @"cmd.exe /c reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services"" /v Shadow /t REG_DWORD /d 1 /f";
                    var inParamsShadow = processClass.GetMethodParameters("Create");
                    inParamsShadow["CommandLine"] = setShadowCmd;
                    processClass.InvokeMethod("Create", inParamsShadow, null);

                    // 3. TermService hizmetini başlat ve otomatiğe al
                    string startSrvCmd = @"cmd.exe /c sc config TermService start= auto && net start TermService";
                    var inParamsSrv = processClass.GetMethodParameters("Create");
                    inParamsSrv["CommandLine"] = startSrvCmd;
                    processClass.InvokeMethod("Create", inParamsSrv, null);

                    // 4. Güvenlik duvarı kuralını etkinleştir
                    string enableFwCmd = @"cmd.exe /c netsh advfirewall firewall set rule group=""remote desktop"" new enable=Yes";
                    var inParamsFw = processClass.GetMethodParameters("Create");
                    inParamsFw["CommandLine"] = enableFwCmd;
                    processClass.InvokeMethod("Create", inParamsFw, null);

                    // Değişikliklerin oturması için kısa bir süre bekle
                    await Task.Delay(1500);

                    // Yerel makinede mstsc.exe shadow komutunu başlat
                    Dispatcher.Invoke(() =>
                    {
                        Logla("   🚀 Bağlantı başlatılıyor. Lütfen uzak kullanıcının ekranındaki onay kutusuna tıklamasını bekleyin...");
                        string commandArgs = $"/shadow:{sessionId} /v:{targetIp} /control";
                        Process.Start(new ProcessStartInfo("mstsc.exe", commandArgs) { UseShellExecute = true });
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"   ❌ Gölge Bağlantı Hatası: {ex.Message}"));
                }

                // Bağlantıyı kes
                KomutCalistir("net", $@"use \\{targetIp}\ipc$ /delete /y");
            });

            Dispatcher.Invoke(() =>
            {
                btnShadowSession.IsEnabled = true;
            });

            // Son işlemler geçmişine ekle
            RecentManager.AddRecentAction(
                (pc != null && !string.IsNullOrEmpty(pc.Hostname) && pc.Hostname != "Bilinmiyor") ? pc.Hostname : targetIp,
                $"RDP Shadow Bağlantısı - Kullanıcı: {username} (ID: {sessionId})",
                "🖥️",
                "#3B82F6",
                "rdp_shadow",
                $"{targetIp}:{sessionId}"
            );
        }
        // -------------------------------------------------------
        // --- 8. TOPLU PC RAPORU (CSV Export) ---
        // -------------------------------------------------------
        private async void BtnRaporla_Click(object sender, RoutedEventArgs e)
        {
            var seciliPcler = BulunanCihazlar.Where(x => x.IsSelected).ToList();
            if (seciliPcler.Count == 0)
            {
                MessageBox.Show("Lütfen raporlanacak en az bir PC seçin.", "Rapor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Raporu Kaydet",
                Filter = "CSV Dosyası (*.csv)|*.csv",
                FileName = $"PC_Raporu_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                DefaultExt = ".csv"
            };
            if (dlg.ShowDialog() != true) return;

            string dosyaYolu = dlg.FileName;
            Logla($"📊 {seciliPcler.Count} PC için rapor hazırlanıyor...");
            btnRaporla.IsEnabled = false;

            var satirlar = new System.Collections.Generic.List<string>();
            satirlar.Add("IP;Bilgisayar Adı;İşletim Sistemi;İşlemci;RAM (GB);C: Toplam (GB);C: Boş (GB);C: Doluluk %;Son Başlangıç");

            await Task.Run(() =>
            {
                foreach (var pc in seciliPcler)
                {
                    Dispatcher.Invoke(() => Logla($"  🔍 {pc.IP} ({pc.Hostname}) sorgulanıyor..."));
                    try
                    {
                        string os = "-", cpu = "-", ram = "-", diskToplam = "-", diskBos = "-", diskPct = "-", lastBoot = "-";

                        using (var kims = new NetworkImpersonation(_domain, _kullanici, _sifre))
                        {
                            var scope = new System.Management.ManagementScope(
                                $@"\\{pc.IP}\root\cimv2",
                                new System.Management.ConnectionOptions
                                {
                                    Username = $@"{_domain}\{_kullanici}",
                                    Password = _sifre,
                                    Impersonation = System.Management.ImpersonationLevel.Impersonate,
                                    Authentication = System.Management.AuthenticationLevel.PacketPrivacy,
                                    EnablePrivileges = true,
                                    Timeout = TimeSpan.FromSeconds(10)
                                });
                            scope.Connect();

                            // OS
                            try
                            {
                                using var q = new System.Management.ManagementObjectSearcher(scope, new System.Management.ObjectQuery("SELECT Caption, LastBootUpTime FROM Win32_OperatingSystem"));
                                foreach (System.Management.ManagementObject o in q.Get())
                                {
                                    os = o["Caption"]?.ToString() ?? "-";
                                    var bootStr = o["LastBootUpTime"]?.ToString();
                                    if (!string.IsNullOrEmpty(bootStr))
                                        lastBoot = System.Management.ManagementDateTimeConverter.ToDateTime(bootStr).ToString("dd.MM.yyyy HH:mm");
                                }
                            }
                            catch { }

                            // CPU
                            try
                            {
                                using var q = new System.Management.ManagementObjectSearcher(scope, new System.Management.ObjectQuery("SELECT Name FROM Win32_Processor"));
                                foreach (System.Management.ManagementObject o in q.Get()) { cpu = o["Name"]?.ToString() ?? "-"; break; }
                            }
                            catch { }

                            // RAM
                            try
                            {
                                long toplamRam = 0;
                                using var q = new System.Management.ManagementObjectSearcher(scope, new System.Management.ObjectQuery("SELECT Capacity FROM Win32_PhysicalMemory"));
                                foreach (System.Management.ManagementObject o in q.Get())
                                    toplamRam += Convert.ToInt64(o["Capacity"] ?? 0);
                                if (toplamRam > 0) ram = (toplamRam / 1073741824.0).ToString("F1");
                            }
                            catch { }

                            // Disk C:
                            try
                            {
                                using var q = new System.Management.ManagementObjectSearcher(scope, new System.Management.ObjectQuery("SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID='C:'"));
                                foreach (System.Management.ManagementObject o in q.Get())
                                {
                                    double size = Convert.ToDouble(o["Size"] ?? 0) / 1073741824.0;
                                    double free = Convert.ToDouble(o["FreeSpace"] ?? 0) / 1073741824.0;
                                    diskToplam = size.ToString("F1");
                                    diskBos = free.ToString("F1");
                                    diskPct = size > 0 ? ((size - free) / size * 100.0).ToString("F1") : "-";
                                }
                            }
                            catch { }
                        }

                        string satir = $"\"{pc.IP}\";\"{pc.Hostname}\";\"{os}\";\"{cpu}\";\"{ram}\";\"{diskToplam}\";\"{diskBos}\";\"{diskPct}\";\"{lastBoot}\"";
                        lock (satirlar) satirlar.Add(satir);
                        Dispatcher.Invoke(() => Logla($"  ✅ {pc.IP} tamamlandı."));
                    }
                    catch (Exception ex)
                    {
                        string satir = $"\"{pc.IP}\";\"{pc.Hostname}\";\"BAĞLANTI HATASI: {ex.Message.Replace("\"", "'")}\";;;;;;";
                        lock (satirlar) satirlar.Add(satir);
                        Dispatcher.Invoke(() => Logla($"  ⚠️ {pc.IP} hata: {ex.Message}"));
                    }
                }
            });

            try
            {
                System.IO.File.WriteAllLines(dosyaYolu, satirlar, System.Text.Encoding.UTF8);
                Logla($"✅ Rapor kaydedildi: {dosyaYolu}");
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dosyaYolu}\"");
            }
            catch (Exception ex)
            {
                Logla($"❌ Dosya yazma hatası: {ex.Message}");
            }
            finally
            {
                btnRaporla.IsEnabled = true;
            }
        }

        // -------------------------------------------------------
        // --- 9. PAYLAŞILAN KLASÖRLER ---
        // -------------------------------------------------------
        private async void btnGetShares_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null) { MessageBox.Show("Lütfen sol listeden bir PC seçin.", "Paylaşımlar", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            Logla($"📂 {pc.IP} paylaşımları sorgulanıyor...");
            gridShares.ItemsSource = null;
            var liste = new System.Collections.Generic.List<ShareItem>();

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(400);
                liste.Add(new ShareItem { Name = "C$", Path = "C:\\", ShareType = "💾 Admin", Description = "Varsayılan Paylaşım", RemoteIP = pc.IP });
                liste.Add(new ShareItem { Name = "D$", Path = "D:\\", ShareType = "💾 Admin", Description = "Varsayılan Paylaşım", RemoteIP = pc.IP });
                liste.Add(new ShareItem { Name = "Ortak", Path = "C:\\Ortak", ShareType = "💾 Disk", Description = "Bölüm Paylaşımı", RemoteIP = pc.IP });
                gridShares.ItemsSource = liste;
                Logla($"✅ (DEMO MODU) Paylaşılan klasörler başarıyla simüle edildi.");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    using var kims = new NetworkImpersonation(_domain, _kullanici, _sifre);
                    var scope = new System.Management.ManagementScope(
                        $@"\\{pc.IP}\root\cimv2",
                        new System.Management.ConnectionOptions
                        {
                            Username = $@"{_domain}\{_kullanici}",
                            Password = _sifre,
                            Impersonation = System.Management.ImpersonationLevel.Impersonate,
                            Authentication = System.Management.AuthenticationLevel.PacketPrivacy,
                            EnablePrivileges = true,
                            Timeout = TimeSpan.FromSeconds(10)
                        });
                    scope.Connect();

                    using var q = new System.Management.ManagementObjectSearcher(scope,
                        new System.Management.ObjectQuery("SELECT Name, Path, Type, Description FROM Win32_Share"));
                    foreach (System.Management.ManagementObject o in q.Get())
                    {
                        uint tip = (uint)(o["Type"] ?? 0u);
                        string tipStr;
                        if (tip == 0) tipStr = "💾 Disk";
                        else if (tip == 1) tipStr = "🖨️ Yazıcı";
                        else if (tip == 2) tipStr = "🔌 Aygıt";
                        else if (tip == 2147483648u) tipStr = "💾 Admin";
                        else if (tip == 2147483649u) tipStr = "🖨️ Yzc(A)";
                        else tipStr = $"#{tip}";
                        liste.Add(new ShareItem
                        {
                            Name = o["Name"]?.ToString() ?? "",
                            Path = o["Path"]?.ToString() ?? "",
                            ShareType = tipStr,
                            Description = o["Description"]?.ToString() ?? "",
                            RemoteIP = pc.IP
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"❌ Paylaşım sorgu hatası: {ex.Message}"));
                }
            });

            gridShares.ItemsSource = liste;
            Logla($"✅ {pc.IP} üzerinde {liste.Count} paylaşım bulundu.");
        }

        private void MenuOpenShare_Click(object sender, RoutedEventArgs e)
        {
            if (gridShares.SelectedItem is ShareItem s)
            {
                var unc = $@"\\{s.RemoteIP}\{s.Name}";
                try { System.Diagnostics.Process.Start("explorer.exe", unc); }
                catch (Exception ex) { Logla($"❌ Explorer açılamadı: {ex.Message}"); }
            }
        }

        private void MenuCopyShareUNC_Click(object sender, RoutedEventArgs e)
        {
            if (gridShares.SelectedItem is ShareItem s)
            {
                var unc = $@"\\{s.RemoteIP}\{s.Name}";
                Clipboard.SetText(unc);
                Logla($"📋 UNC yolu kopyalandı: {unc}");
            }
        }

        // ==========================================
        // ⚡ MAKRO ŞABLONLARI METOTLARI & OLAYLARI
        // ==========================================
        private List<MacroItem> _macros = new List<MacroItem>();
        private const string MacroPath = @"\\192.168.1.100\d$\Programlar\OtoProgram\macros.json";

        private void LoadMacros()
        {
            try
            {
                using (new NetworkImpersonation(_domain, _kullanici, _sifre))
                {
                    if (File.Exists(MacroPath))
                    {
                        string json = File.ReadAllText(MacroPath);
                        _macros = System.Text.Json.JsonSerializer.Deserialize<List<MacroItem>>(json);
                    }
                }
            }
            catch (Exception ex)
            {
                Logla($"⚠️ Makrolar sunucudan yüklenemedi: {ex.Message}");
            }

            if (_macros == null || _macros.Count == 0)
            {
                _macros = new List<MacroItem>
                {
                    new MacroItem { Ad = "🔄 GP Güncelle", Komut = "gpupdate /force" },
                    new MacroItem { Ad = "🌐 DNS Temizle", Komut = "ipconfig /flushdns" },
                    new MacroItem { Ad = "🔁 Yeniden Başlat", Komut = "shutdown -r -t 0" },
                    new MacroItem { Ad = "📡 Ağ Durumu", Komut = "ipconfig /all" },
                    new MacroItem { Ad = "🔍 Disk Kontrol", Komut = "chkdsk C: /f" }
                };
                SaveMacros();
            }
            
            RefreshMacroButtons();
        }

        private void SaveMacros()
        {
            try
            {
                using (new NetworkImpersonation(_domain, _kullanici, _sifre))
                {
                    string dir = Path.GetDirectoryName(MacroPath);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    string json = System.Text.Json.JsonSerializer.Serialize(_macros, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(MacroPath, json);
                }
            }
            catch (Exception ex)
            {
                Logla($"❌ Makrolar sunucuya kaydedilemedi: {ex.Message}");
            }
        }

        private void RefreshMacroButtons()
        {
            pnlMacros.Children.Clear();
            
            var lbl = new TextBlock
            {
                Text = "⚡ Makrolar: ",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)new BrushConverter().ConvertFrom("#475569"),
                Margin = new Thickness(0, 0, 8, 0)
            };
            pnlMacros.Children.Add(lbl);

            foreach (var macro in _macros)
            {
                var btn = new Button
                {
                    Content = macro.Ad,
                    Tag = macro.Komut,
                    Margin = new Thickness(0, 0, 6, 4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = (Brush)new BrushConverter().ConvertFrom("#E2E8F0"),
                    Foreground = (Brush)new BrushConverter().ConvertFrom("#1E293B"),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = macro.Komut
                };
                btn.Resources.Add(typeof(Border), new Style(typeof(Border))
                {
                    Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) }
                });
                btn.Click += BtnMacro_Click;
                pnlMacros.Children.Add(btn);
            }

            var btnAdd = new Button
            {
                Content = "➕ Ekle",
                Margin = new Thickness(6, 0, 4, 4),
                Padding = new Thickness(8, 4, 8, 4),
                Background = (Brush)new BrushConverter().ConvertFrom("#10B981"),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btnAdd.Resources.Add(typeof(Border), new Style(typeof(Border))
            {
                Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) }
            });
            btnAdd.Click += BtnAddMacro_Click;
            pnlMacros.Children.Add(btnAdd);

            var btnEdit = new Button
            {
                Content = "✏️ Düzenle",
                Margin = new Thickness(2, 0, 0, 4),
                Padding = new Thickness(8, 4, 8, 4),
                Background = (Brush)new BrushConverter().ConvertFrom("#6366F1"),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btnEdit.Resources.Add(typeof(Border), new Style(typeof(Border))
            {
                Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) }
            });
            btnEdit.Click += BtnEditMacros_Click;
            pnlMacros.Children.Add(btnEdit);
        }

        private void BtnMacro_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string cmd)
            {
                txtRemoteCmd.Text = cmd;
                txtRemoteCmd.Foreground = Brushes.Black;
                
                RecentManager.AddRecentAction(
                    btn.Content.ToString(),
                    cmd.Length > 20 ? cmd.Substring(0, 17) + "..." : cmd,
                    "⚡",
                    "#F59E0B",
                    "Macro",
                    cmd);
            }
        }

        private void BtnAddMacro_Click(object sender, RoutedEventArgs e)
        {
            var editor = new MacroEditorWindow(_macros);
            editor.Owner = this;
            if (editor.ShowDialog() == true)
            {
                _macros.Clear();
                foreach (var item in editor.Macros)
                {
                    _macros.Add(item);
                }
                SaveMacros();
                RefreshMacroButtons();
            }
        }

        private void BtnEditMacros_Click(object sender, RoutedEventArgs e)
        {
            BtnAddMacro_Click(sender, e);
        }

        // ==========================================
        // 🔧 SERVİS YÖNETİCİSİ METOTLARI & OLAYLARI
        // ==========================================
        private List<ServiceItem> _allServices = new List<ServiceItem>();

        private async void btnGetServices_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnGetServices.IsEnabled = false;
            btnGetServices.Content = "⏳ Getiriliyor...";
            Logla($"🔍 {pc.IP} adresinden servis listesi alınıyor...");

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(500);
                var fakeServices = new List<ServiceItem>
                {
                    new ServiceItem { Name = "wuauserv", DisplayName = "Windows Update", State = "Running", StartMode = "Auto" },
                    new ServiceItem { Name = "Spooler", DisplayName = "Print Spooler", State = "Running", StartMode = "Auto" },
                    new ServiceItem { Name = "LanmanServer", DisplayName = "Server", State = "Running", StartMode = "Auto" },
                    new ServiceItem { Name = "RemoteRegistry", DisplayName = "Remote Registry", State = "Stopped", StartMode = "Manual" },
                    new ServiceItem { Name = "WinRM", DisplayName = "Windows Remote Management (WS-Management)", State = "Running", StartMode = "Auto" }
                };
                _allServices = fakeServices;
                ApplyServiceFilter();
                Logla($"✅ (DEMO MODU) Servis listesi başarıyla simüle edildi.");
                btnGetServices.IsEnabled = true;
                btnGetServices.Content = "🔄 Servisleri Getir";
                return;
            }
            
            try
            {
                var services = await Task.Run(() => GetRemoteServices(pc.IP));
                _allServices = services;
                ApplyServiceFilter();
                Logla($"✅ {pc.IP} servis listesi başarıyla alındı. Toplam {services.Count} servis.");
            }
            catch (Exception ex)
            {
                Logla($"❌ Servis listesi alınamadı: {ex.Message}");
                MessageBox.Show($"Servis listesi alınırken hata oluştu: {ex.Message}", "Hata");
            }
            finally
            {
                btnGetServices.IsEnabled = true;
                btnGetServices.Content = "🔄 Servisleri Getir";
            }
        }

        private List<ServiceItem> GetRemoteServices(string ip)
        {
            var list = new List<ServiceItem>();
            var options = new ConnectionOptions
            {
                Username = string.IsNullOrEmpty(_kullanici) ? null : $"{_domain}\\{_kullanici}",
                Password = string.IsNullOrEmpty(_sifre) ? null : _sifre,
                Impersonation = ImpersonationLevel.Impersonate,
                EnablePrivileges = true,
                Timeout = TimeSpan.FromSeconds(10)
            };

            var scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
            scope.Connect();

            var query = new ObjectQuery("SELECT Name, DisplayName, State, StartMode FROM Win32_Service");
            using (var searcher = new ManagementObjectSearcher(scope, query))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    list.Add(new ServiceItem
                    {
                        Name = obj["Name"]?.ToString() ?? "",
                        DisplayName = obj["DisplayName"]?.ToString() ?? "",
                        State = obj["State"]?.ToString() ?? "",
                        StartMode = obj["StartMode"]?.ToString() ?? ""
                    });
                }
            }

            return list;
        }

        private void txtServiceFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyServiceFilter();
        }

        private void cmbServiceStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyServiceFilter();
        }

        private void ApplyServiceFilter()
        {
            if (_allServices == null) return;

            string filterText = txtServiceFilter?.Text?.Trim()?.ToLower() ?? "";
            int statusIndex = cmbServiceStatusFilter?.SelectedIndex ?? 0;

            var filtered = _allServices.Where(s =>
            {
                bool matchesText = string.IsNullOrEmpty(filterText) ||
                                   s.Name.ToLower().Contains(filterText) ||
                                   s.DisplayName.ToLower().Contains(filterText);

                bool matchesStatus = true;
                if (statusIndex == 1) // Running
                    matchesStatus = s.State.Equals("Running", StringComparison.OrdinalIgnoreCase);
                else if (statusIndex == 2) // Stopped
                    matchesStatus = s.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase);

                return matchesText && matchesStatus;
            }).ToList();

            gridServices.ItemsSource = filtered;
        }

        private void BtnQuickServicePicker_Click(object sender, RoutedEventArgs e)
        {
            var picker = new QuickServicesWindow();
            picker.Owner = this;
            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedServiceName))
            {
                string serviceName = picker.SelectedServiceName;
                txtServiceFilter.Text = serviceName;
                cmbServiceStatusFilter.SelectedIndex = 0; // Tümü
                ApplyServiceFilter();

                if (gridServices.ItemsSource is List<ServiceItem> items)
                {
                    var found = items.FirstOrDefault(x => x.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
                    if (found != null)
                    {
                        gridServices.SelectedItem = found;
                        gridServices.ScrollIntoView(found);
                    }
                }
            }
        }

        // ==========================================
        // 📋 UZAK OLAY GÜNLÜĞÜ (EVENT LOG VIEWER)
        // ==========================================
        private async void btnGetEventLogs_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnGetEventLogs.IsEnabled = false;
            btnGetEventLogs.Content = "Yükleniyor...";
            Logla($"\n📋 {pc.IP} olay günlükleri (hata/uyarı) sorgulanıyor...");

            gridEventLogs.ItemsSource = null;
            int filterIndex = cmbEventLogType.SelectedIndex; // 0: All, 1: Error, 2: Warning
            string ip = pc.IP;

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(600);
                var fakeEvents = new List<EventLogItem>();
                if (filterIndex == 0 || filterIndex == 1)
                {
                    fakeEvents.Add(new EventLogItem { Type = "Error", Logfile = "System", TimeGenerated = DateTime.Now.AddHours(-1).ToString("dd.MM.yyyy HH:mm"), SourceName = "Service Control Manager", EventCode = "7031", Message = "Print Spooler servisi beklenmedik bir şekilde sonlandı." });
                    fakeEvents.Add(new EventLogItem { Type = "Error", Logfile = "Application", TimeGenerated = DateTime.Now.AddHours(-3).ToString("dd.MM.yyyy HH:mm"), SourceName = "Application Error", EventCode = "1000", Message = "hbys.exe uygulaması çöktü. Hata modülü: oraclient.dll" });
                }
                if (filterIndex == 0 || filterIndex == 2)
                {
                    fakeEvents.Add(new EventLogItem { Type = "Warning", Logfile = "System", TimeGenerated = DateTime.Now.AddMinutes(-15).ToString("dd.MM.yyyy HH:mm"), SourceName = "LsaSrv", EventCode = "40961", Message = "Kerberos kimlik doğrulama hatası alındı." });
                    fakeEvents.Add(new EventLogItem { Type = "Warning", Logfile = "Application", TimeGenerated = DateTime.Now.AddHours(-5).ToString("dd.MM.yyyy HH:mm"), SourceName = "MSSQLSERVER", EventCode = "19053", Message = "SQL Server veritabanı yedekleme uyarısı." });
                }
                gridEventLogs.ItemsSource = fakeEvents;
                btnGetEventLogs.IsEnabled = true;
                btnGetEventLogs.Content = "🔄 Günlükleri Getir";
                Logla($"✅ (DEMO MODU) Olay günlükleri başarıyla simüle edildi.");
                return;
            }

            await Task.Run(() =>
            {
                var events = new List<EventLogItem>();
                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(8)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    // WMI Event Log Query (Limit to errors/warnings)
                    string wmiQuery = "SELECT Logfile, SourceName, Type, EventCode, Message, TimeGenerated FROM Win32_NTLogEvent WHERE ";
                    if (filterIndex == 1)
                        wmiQuery += "Type='Error'";
                    else if (filterIndex == 2)
                        wmiQuery += "Type='Warning'";
                    else
                        wmiQuery += "(Type='Error' OR Type='Warning')";

                    ObjectQuery query = new ObjectQuery(wmiQuery);
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                    {
                        int count = 0;
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            if (count >= 50) break; // Limit to last 50 for performance
                            count++;

                            string typeVal = obj["Type"]?.ToString() ?? "-";
                            // Türkçe'ye çevir
                            if (typeVal.Equals("Error", StringComparison.OrdinalIgnoreCase)) typeVal = "❌ Hata";
                            else if (typeVal.Equals("Warning", StringComparison.OrdinalIgnoreCase)) typeVal = "⚠️ Uyarı";

                            string rawTime = obj["TimeGenerated"]?.ToString();
                            string parsedTime = "-";
                            if (!string.IsNullOrEmpty(rawTime) && rawTime.Length >= 14)
                            {
                                try
                                {
                                    // WMI datetime format: yyyymmddhhmmss.xxxxxx±UUU
                                    string y = rawTime.Substring(0, 4);
                                    string m = rawTime.Substring(4, 2);
                                    string d = rawTime.Substring(6, 2);
                                    string hh = rawTime.Substring(8, 2);
                                    string mm = rawTime.Substring(10, 2);
                                    string ss = rawTime.Substring(12, 2);
                                    parsedTime = $"{d}.{m}.{y} {hh}:{mm}:{ss}";
                                }
                                catch { }
                            }

                            events.Add(new EventLogItem
                            {
                                Type = typeVal,
                                Logfile = obj["Logfile"]?.ToString() ?? "-",
                                TimeGenerated = parsedTime,
                                SourceName = obj["SourceName"]?.ToString() ?? "-",
                                EventCode = obj["EventCode"]?.ToString() ?? "-",
                                Message = obj["Message"]?.ToString() ?? "-"
                            });
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        gridEventLogs.ItemsSource = events;
                        Logla($"   ✅ {events.Count} adet olay günlüğü listelendi.");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"   ❌ Olay Günlüğü Hatası: {ex.Message}"));
                }
            });

            btnGetEventLogs.IsEnabled = true;
            btnGetEventLogs.Content = "🔄 Günlükleri Getir";
        }

        private void GridEventLogs_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (gridEventLogs.SelectedItem is EventLogItem item)
            {
                MessageBox.Show(item.Message, $"Detaylı Hata Kaydı (Kod: {item.EventCode})", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ==========================================
        // 🔌 BAŞLANGIÇ YÖNETİMİ (STARTUP MANAGER)
        // ==========================================
        private async void btnGetStartupItems_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnGetStartupItems.IsEnabled = false;
            btnGetStartupItems.Content = "Yükleniyor...";
            Logla($"\n🔌 {pc.IP} başlangıç programları sorgulanıyor...");

            gridStartupItems.ItemsSource = null;
            string ip = pc.IP;

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(500);
                var fakeStartups = new List<StartupItem>
                {
                    new StartupItem { Name = "Kaspersky Endpoint Security", Command = "\"C:\\Program Files\\Kaspersky Lab\\KES.exe\" -run", Location = "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", User = "All Users" },
                    new StartupItem { Name = "Oracle Client TNS", Command = "C:\\orant\\BIN\\TNSLSNR.EXE", Location = "Common Startup", User = "All Users" },
                    new StartupItem { Name = "OneDrive", Command = "\"C:\\Users\\demo\\AppData\\Local\\Microsoft\\OneDrive\\OneDrive.exe\" /background", Location = "HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", User = "demo" }
                };
                gridStartupItems.ItemsSource = fakeStartups;
                btnGetStartupItems.IsEnabled = true;
                btnGetStartupItems.Content = "🔄 Programları Getir";
                Logla($"✅ (DEMO MODU) Başlangıç programları başarıyla simüle edildi.");
                return;
            }

            await Task.Run(() =>
            {
                var list = new List<StartupItem>();
                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    ObjectQuery query = new ObjectQuery("SELECT Name, Command, Location, User FROM Win32_StartupCommand");
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            list.Add(new StartupItem
                            {
                                Name = obj["Name"]?.ToString() ?? "-",
                                Command = obj["Command"]?.ToString() ?? "-",
                                Location = obj["Location"]?.ToString() ?? "-",
                                User = obj["User"]?.ToString() ?? "-"
                            });
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        gridStartupItems.ItemsSource = list;
                        Logla($"   ✅ {list.Count} adet başlangıç kaydı listelendi.");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"   ❌ Başlangıç Sorgu Hatası: {ex.Message}"));
                }
            });

            btnGetStartupItems.IsEnabled = true;
            btnGetStartupItems.Content = "🔄 Programları Getir";
        }

        private async void MenuDeleteStartup_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null || gridStartupItems.SelectedItem == null) return;

            var item = gridStartupItems.SelectedItem as StartupItem;
            var res = MessageBox.Show($"'{item.Name}' kaydını başlangıçtan kaldırmak istediğinize emin misiniz?", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            Logla($"\n🔌 {pc.IP} üzerindeki '{item.Name}' başlangıç kaydı siliniyor...");

            await Task.Run(() =>
            {
                try
                {
                    string regPath = item.Location;
                    string cmd = "";

                    if (regPath.Contains("Run"))
                    {
                        cmd = $@"reg delete ""{regPath}"" /v ""{item.Name}"" /f";
                    }
                    else
                    {
                        cmd = $@"cmd.exe /c del /f /q ""{item.Command}""";
                    }

                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{pc.IP}\root\cimv2", options);
                    scope.Connect();

                    var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                    var inParams = processClass.GetMethodParameters("Create");
                    inParams["CommandLine"] = cmd;
                    processClass.InvokeMethod("Create", inParams, null);

                    Logla($"   ✅ Sildirme komutu uzak bilgisayara iletildi.");
                    
                    Dispatcher.Invoke(() => btnGetStartupItems_Click(null, null));
                }
                catch (Exception ex)
                {
                    Logla($"   ❌ Başlangıç Silme Hatası: {ex.Message}");
                }
            });
        }

        // ==========================================
        // 📦 KURULU PROGRAMLAR (INSTALLED SOFTWARE)
        // ==========================================
        private async void btnGetInstalledApps_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnGetInstalledApps.IsEnabled = false;
            btnGetInstalledApps.Content = "Taranıyor...";
            Logla($"\n📦 {pc.IP} kurulu yazılımları listeleniyor (Kayıt Defteri taranıyor)...");

            gridInstalledApps.ItemsSource = null;
            _allInstalledApps.Clear();
            string ip = pc.IP;

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(800);
                _allInstalledApps = new List<InstalledAppItem>
                {
                    new InstalledAppItem { DisplayName = "Google Chrome", Publisher = "Google LLC", DisplayVersion = "124.0.6367.201", UninstallString = "MsiExec.exe /I{1234-5678}" },
                    new InstalledAppItem { DisplayName = "Microsoft Edge", Publisher = "Microsoft Corporation", DisplayVersion = "124.0.2478.80", UninstallString = "MsiExec.exe /I{9999-8888}" },
                    new InstalledAppItem { DisplayName = "Oracle Database 11g Client", Publisher = "Oracle Corporation", DisplayVersion = "11.2.0.1", UninstallString = "C:\\orant\\deinstall\\deinstall.bat" },
                    new InstalledAppItem { DisplayName = "HBYS Otomasyon Modülleri", Publisher = "HBYS Yazılım Ltd.", DisplayVersion = "3.6.2", UninstallString = "C:\\hbys_otomasyon\\uninstall.exe" },
                    new InstalledAppItem { DisplayName = "7-Zip 24.05 (x64)", Publisher = "Igor Pavlov", DisplayVersion = "24.05", UninstallString = "\"C:\\Program Files\\7-Zip\\Uninstall.exe\"" }
                };
                gridInstalledApps.ItemsSource = _allInstalledApps;
                btnGetInstalledApps.IsEnabled = true;
                btnGetInstalledApps.Content = "🔄 Programları Listele";
                Logla($"✅ (DEMO MODU) Kurulu programlar başarıyla simüle edildi.");
                return;
            }

            await Task.Run(async () =>
            {
                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");

                try
                {
                    string psCmd = "powershell -ExecutionPolicy Bypass -NoProfile -Command \"" +
                                   "Get-ItemProperty HKLM:\\Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*, HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\* " +
                                   "| Where-Object { $_.DisplayName } " +
                                   "| Select-Object DisplayName, DisplayVersion, Publisher, UninstallString " +
                                   "| ConvertTo-Json -Compress\" > C:\\Windows\\Temp\\apps_out.json";

                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                    var inParams = processClass.GetMethodParameters("Create");
                    inParams["CommandLine"] = psCmd;
                    processClass.InvokeMethod("Create", inParams, null);

                    await Task.Delay(3000);

                    string sharePath = $@"\\{ip}\c$\Windows\Temp\apps_out.json";
                    if (File.Exists(sharePath))
                    {
                        string jsonOutput = File.ReadAllText(sharePath);
                        try { File.Delete(sharePath); } catch { }

                        var list = ParseAppsJson(jsonOutput);
                        _allInstalledApps = list.OrderBy(x => x.DisplayName).ToList();

                        Dispatcher.Invoke(() =>
                        {
                            gridInstalledApps.ItemsSource = _allInstalledApps;
                            Logla($"   ✅ {_allInstalledApps.Count} adet kurulu program listelendi.");
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() => Logla("   ⚠️ Program listesi alınamadı (Json çıktısı oluşmadı)."));
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"   ❌ Yazılım Tarama Hatası: {ex.Message}"));
                }

                KomutCalistir("net", $@"use \\{pc.IP}\ipc$ /delete /y");
            });

            btnGetInstalledApps.IsEnabled = true;
            btnGetInstalledApps.Content = "🔄 Programları Listele";
        }

        private List<InstalledAppItem> ParseAppsJson(string json)
        {
            var list = new List<InstalledAppItem>();
            if (string.IsNullOrEmpty(json)) return list;

            json = json.Trim();
            if (json.StartsWith("[")) json = json.Substring(1, json.Length - 2);

            string[] objects = System.Text.RegularExpressions.Regex.Split(json, @"\}\s*,\s*\{");
            foreach (var rawObj in objects)
            {
                string objStr = rawObj;
                if (!objStr.StartsWith("{")) objStr = "{" + objStr;
                if (!objStr.EndsWith("}")) objStr = objStr + "}";

                string displayName = ExtractJsonValue(objStr, "DisplayName");
                if (string.IsNullOrEmpty(displayName)) continue;

                list.Add(new InstalledAppItem
                {
                    DisplayName = displayName,
                    DisplayVersion = ExtractJsonValue(objStr, "DisplayVersion"),
                    Publisher = ExtractJsonValue(objStr, "Publisher"),
                    UninstallString = ExtractJsonValue(objStr, "UninstallString")
                });
            }
            return list;
        }

        private string ExtractJsonValue(string json, string key)
        {
            string pattern = $@"""{key}""\s*:\s*""([^""]*)""";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            pattern = $@"""{key}""\s*:\s*([^,\}}]+)";
            match = System.Text.RegularExpressions.Regex.Match(json, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string val = match.Groups[1].Value.Trim().Replace("\"", "");
                if (val.Equals("null", StringComparison.OrdinalIgnoreCase)) return "";
                return val;
            }
            return "";
        }

        private void TxtAppFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_allInstalledApps == null) return;
            string filter = txtAppFilter.Text.ToLower().Trim();
            if (string.IsNullOrEmpty(filter))
            {
                gridInstalledApps.ItemsSource = _allInstalledApps;
            }
            else
            {
                gridInstalledApps.ItemsSource = _allInstalledApps.Where(x => 
                    (x.DisplayName != null && x.DisplayName.ToLower().Contains(filter)) ||
                    (x.Publisher != null && x.Publisher.ToLower().Contains(filter))
                ).ToList();
            }
        }

        private async void MenuUninstallApp_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            var app = gridInstalledApps.SelectedItem as InstalledAppItem;
            if (pc == null || app == null) return;

            if (string.IsNullOrEmpty(app.UninstallString))
            {
                MessageBox.Show("Seçilen programın kaldırma komutu kayıt defterinde bulunamadı.", "Komut Yok");
                return;
            }

            var res = MessageBox.Show($"'{app.DisplayName}' programını uzak bilgisayardan SESSİZCE kaldırmak istediğinize emin misiniz?\n\nKomut: {app.UninstallString}", "Kaldırma Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            string uninstallCmd = app.UninstallString;
            
            if (uninstallCmd.ToLower().Contains("msiexec.exe"))
            {
                uninstallCmd = System.Text.RegularExpressions.Regex.Replace(uninstallCmd, @"/I", "/X", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                uninstallCmd = System.Text.RegularExpressions.Regex.Replace(uninstallCmd, @"/i", "/X", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!uninstallCmd.Contains("/qn")) uninstallCmd += " /qn /norestart";
            }

            Logla($"\n📦 {pc.IP} üzerinde '{app.DisplayName}' programı kaldırılıyor...");
            Logla($"   Komut: {uninstallCmd}");

            await Task.Run(() =>
            {
                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{pc.IP}\root\cimv2", options);
                    scope.Connect();

                    var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
                    var inParams = processClass.GetMethodParameters("Create");
                    inParams["CommandLine"] = uninstallCmd;
                    processClass.InvokeMethod("Create", inParams, null);

                    Logla($"   ✅ Kaldırma komutu uzak makineye gönderildi.");
                }
                catch (Exception ex)
                {
                    Logla($"   ❌ Kaldırma Hatası: {ex.Message}");
                }
            });
        }

        private void MenuCopyUninstallString_Click(object sender, RoutedEventArgs e)
        {
            if (gridInstalledApps.SelectedItem is InstalledAppItem app && !string.IsNullOrEmpty(app.UninstallString))
            {
                Clipboard.SetText(app.UninstallString);
                MessageBox.Show("Kaldırma komutu panoya kopyalandı:\n" + app.UninstallString, "Kopyalandı");
            }
        }

        private async void btnStartService_Click(object sender, RoutedEventArgs e)
        {
            await ControlService("StartService");
        }

        private async void btnStopService_Click(object sender, RoutedEventArgs e)
        {
            await ControlService("StopService");
        }

        private async void btnRestartService_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null || gridServices.SelectedItem == null)
            {
                MessageBox.Show("Lütfen sol taraftan bir PC ve sağ taraftan bir servis seçin.", "Seçim Eksik");
                return;
            }
            
            var service = gridServices.SelectedItem as ServiceItem;
            if (service == null) return;

            Logla($"🔄 {pc.IP} üzerinde {service.Name} servisi yeniden başlatılıyor...");
            try
            {
                await Task.Run(() =>
                {
                    ExecuteServiceMethod(pc.IP, service.Name, "StopService");
                    System.Threading.Thread.Sleep(1500);
                    ExecuteServiceMethod(pc.IP, service.Name, "StartService");
                });
                Logla($"✅ {service.Name} servisi başarıyla yeniden başlatıldı.");
                
                await Task.Delay(1000);
                btnGetServices_Click(null, null);
            }
            catch (Exception ex)
            {
                Logla($"❌ Servis yeniden başlatılamadı: {ex.Message}");
                MessageBox.Show($"İşlem başarısız: {ex.Message}", "Hata");
            }
        }

        private async Task ControlService(string method)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            var service = gridServices.SelectedItem as ServiceItem;
            if (service == null)
            {
                MessageBox.Show("Lütfen sağ listeden işlem yapmak istediğiniz servisi seçin.", "Servis Seçilmedi");
                return;
            }

            string actionText = method == "StartService" ? "başlatılıyor" : "durduruluyor";
            Logla($"⚙️ {pc.IP} üzerinde {service.Name} servisi {actionText}...");

            try
            {
                await Task.Run(() => ExecuteServiceMethod(pc.IP, service.Name, method));
                Logla($"✅ {service.Name} servisine '{method}' komutu başarıyla gönderildi.");
                
                await Task.Delay(1000);
                btnGetServices_Click(null, null);
            }
            catch (Exception ex)
            {
                Logla($"❌ Servis işlemi başarısız: {ex.Message}");
                MessageBox.Show($"İşlem başarısız: {ex.Message}", "Hata");
            }
        }

        private void ExecuteServiceMethod(string ip, string serviceName, string method)
        {
            var options = new ConnectionOptions
            {
                Username = string.IsNullOrEmpty(_kullanici) ? null : $"{_domain}\\{_kullanici}",
                Password = string.IsNullOrEmpty(_sifre) ? null : _sifre,
                Impersonation = ImpersonationLevel.Impersonate,
                EnablePrivileges = true
            };

            var scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
            scope.Connect();

            string path = $"Win32_Service.Name='{serviceName}'";
            using (var serviceObj = new ManagementObject(scope, new ManagementPath(path), null))
            {
                var outParams = serviceObj.InvokeMethod(method, null, null);
                uint returnValue = (uint)(outParams["ReturnValue"]);
                if (returnValue != 0)
                {
                    throw new Exception($"WMI ReturnValue = {returnValue}");
                }
            }
        }

        private void MenuStartService_Click(object sender, RoutedEventArgs e) => btnStartService_Click(sender, e);
        private void MenuStopService_Click(object sender, RoutedEventArgs e) => btnStopService_Click(sender, e);
        private void MenuRestartService_Click(object sender, RoutedEventArgs e) => btnRestartService_Click(sender, e);

        private async void MenuSetStartType_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null || gridServices.SelectedItem == null)
            {
                MessageBox.Show("Lütfen sol taraftan bir PC ve sağ taraftan bir servis seçin.", "Seçim Eksik");
                return;
            }

            var service = gridServices.SelectedItem as ServiceItem;
            if (service == null || !(sender is MenuItem item) || item.Tag == null) return;

            string startMode = item.Tag.ToString();
            Logla($"⚙️ {pc.IP} üzerinde {service.Name} başlangıç türü {startMode} olarak ayarlanıyor...");

            try
            {
                await Task.Run(() => ChangeServiceStartMode(pc.IP, service.Name, startMode));
                Logla($"✅ {service.Name} başlangıç türü başarıyla güncellendi.");
                
                await Task.Delay(1000);
                btnGetServices_Click(null, null);
            }
            catch (Exception ex)
            {
                Logla($"❌ Başlangıç türü değiştirilemedi: {ex.Message}");
                MessageBox.Show($"İşlem başarısız: {ex.Message}", "Hata");
            }
        }

        private void ChangeServiceStartMode(string ip, string serviceName, string startMode)
        {
            var options = new ConnectionOptions
            {
                Username = string.IsNullOrEmpty(_kullanici) ? null : $"{_domain}\\{_kullanici}",
                Password = string.IsNullOrEmpty(_sifre) ? null : _sifre,
                Impersonation = ImpersonationLevel.Impersonate,
                EnablePrivileges = true
            };

            var scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
            scope.Connect();

            string path = $"Win32_Service.Name='{serviceName}'";
            using (var serviceObj = new ManagementObject(scope, new ManagementPath(path), null))
            {
                var inParams = serviceObj.GetMethodParameters("ChangeStartMode");
                inParams["StartMode"] = startMode;
                var outParams = serviceObj.InvokeMethod("ChangeStartMode", inParams, null);
                uint returnValue = (uint)(outParams["ReturnValue"]);
                if (returnValue != 0)
                {
                    throw new Exception($"WMI ReturnValue = {returnValue}");
                }
        }
    }

        // ==========================================
        // 14. KULLANICI PROFİL YEDEKLEME & GERİ YÜKLEME SİSTEMİ (v4.0)
        // ==========================================

        private bool IsLocalIP(string ip)
        {
            if (ip == "127.0.0.1" || ip.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                foreach (var addr in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (ip == addr.ToString()) return true;
                }
            }
            catch { }
            return false;
        }

        private async void btnQueryActiveUser_Click(object sender, RoutedEventArgs e)
        {
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            if (pc == null)
            {
                MessageBox.Show("Lütfen sol taraftaki bilgisayar listesinden bir adet PC seçin.", "PC Seçilmedi");
                return;
            }

            btnQueryActiveUser.IsEnabled = false;
            btnQueryActiveUser.Content = "Bulunuyor...";
            string ip = pc.IP;

            if (MainWindow.IsDemoMode)
            {
                await Task.Delay(400);
                txtBackupUsername.Text = "kullanici.adi";
                txtRestoreUsername.Text = "kullanici.adi";
                btnQueryActiveUser.IsEnabled = true;
                btnQueryActiveUser.Content = "👤 Kullanıcıyı Bul";
                Logla($"✅ (DEMO MODU) Aktif kullanıcı 'kullanici.adi' olarak simüle edildi.");
                return;
            }

            await Task.Run(() =>
            {
                string resolvedUser = "";
                try
                {
                    ConnectionOptions options = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_kullanici}",
                        Password = _sifre,
                        Impersonation = ImpersonationLevel.Impersonate,
                        EnablePrivileges = true,
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                    ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                    scope.Connect();

                    ObjectQuery q = new ObjectQuery("SELECT UserName FROM Win32_ComputerSystem");
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, q))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            string rawUser = obj["UserName"]?.ToString();
                            if (!string.IsNullOrEmpty(rawUser))
                            {
                                if (rawUser.Contains("\\"))
                                {
                                    resolvedUser = rawUser.Split('\\')[1];
                                }
                                else
                                {
                                    resolvedUser = rawUser;
                                }
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Logla($"❌ Kullanıcı bulma hatası ({ip}): {ex.Message}"));
                }

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(resolvedUser))
                    {
                        txtBackupUsername.Text = resolvedUser;
                        txtRestoreUsername.Text = resolvedUser;
                        Logla($"   👤 Aktif kullanıcı tespit edildi: {resolvedUser}");
                    }
                    else
                    {
                        MessageBox.Show("Aktif oturum açmış kullanıcı bulunamadı veya WMI bağlantı hatası.", "Bilgi");
                    }
                    btnQueryActiveUser.IsEnabled = true;
                    btnQueryActiveUser.Content = "👤 Kullanıcıyı Bul";
                });
            });
        }

        private void btnBrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                txtBackupPath.Text = dialog.FolderName;
            }
        }

        private void btnBrowseRestore_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                txtRestorePath.Text = dialog.FolderName;
            }
        }

        private List<string> QueryPrinters(string ip)
        {
            var printers = new List<string>();
            try
            {
                ConnectionOptions options = new ConnectionOptions
                {
                    Username = $"{_domain}\\{_kullanici}",
                    Password = _sifre,
                    Impersonation = ImpersonationLevel.Impersonate,
                    EnablePrivileges = true,
                    Timeout = TimeSpan.FromSeconds(5)
                };
                ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                scope.Connect();

                ObjectQuery query = new ObjectQuery("SELECT Name, PortName, DriverName, Shared, ShareName FROM Win32_Printer");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? "";
                        string port = obj["PortName"]?.ToString() ?? "";
                        string driver = obj["DriverName"]?.ToString() ?? "";
                        bool shared = Convert.ToBoolean(obj["Shared"] ?? false);
                        string shareName = obj["ShareName"]?.ToString() ?? "";
                        
                        if (name.StartsWith("\\\\") || port.Contains(".") || port.StartsWith("IP_"))
                        {
                            printers.Add($"{name}|{port}|{driver}");
                        }
                    }
                }
            }
            catch { }
            return printers;
        }

        private List<string> QueryNetworkDrives(string ip)
        {
            var drives = new List<string>();
            try
            {
                ConnectionOptions options = new ConnectionOptions
                {
                    Username = $"{_domain}\\{_kullanici}",
                    Password = _sifre,
                    Impersonation = ImpersonationLevel.Impersonate,
                    EnablePrivileges = true,
                    Timeout = TimeSpan.FromSeconds(5)
                };
                ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                scope.Connect();

                ObjectQuery query = new ObjectQuery("SELECT LocalName, RemoteName FROM Win32_NetworkConnection");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string local = obj["LocalName"]?.ToString() ?? "";
                        string remote = obj["RemoteName"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(local) && !string.IsNullOrEmpty(remote))
                        {
                            drives.Add($"{local}|{remote}");
                        }
                    }
                }
            }
            catch { }
            return drives;
        }

        private void SaveBackupMetadata(string destFolder, string username, string computerName, List<string> printers, List<string> drives)
        {
            try
            {
                var data = new
                {
                    Username = username,
                    ComputerName = computerName,
                    Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Printers = printers,
                    Drives = drives
                };
                string json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(destFolder, "backup_metadata.json"), json);
            }
            catch { }
        }

        private async void btnStartBackup_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            string ip = pc != null ? pc.IP : "127.0.0.1";
            string hostname = pc != null ? pc.Hostname : Environment.MachineName;

            string username = txtBackupUsername.Text.Trim();
            string destRoot = txtBackupPath.Text.Trim();

            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("Lütfen yedeklenecek kullanıcı adını girin.", "Giriş Hatası");
                return;
            }
            if (string.IsNullOrEmpty(destRoot))
            {
                MessageBox.Show("Lütfen yedekleme kayıt hedefini girin.", "Giriş Hatası");
                return;
            }

            // Capture UI values on the UI thread before background execution
            bool backupNetPrinters = chkBackupNetPrinters.IsChecked == true;
            bool backupDesktop = chkBackupDesktop.IsChecked == true;
            bool backupDocuments = chkBackupDocuments.IsChecked == true;
            bool backupDownloads = chkBackupDownloads.IsChecked == true;
            bool backupChrome = chkBackupChrome.IsChecked == true;
            bool backupEdge = chkBackupEdge.IsChecked == true;
            bool backupOutlookSignatures = chkBackupOutlookSignatures.IsChecked == true;
            bool backupOracleTns = chkBackupOracleTns.IsChecked == true;

            btnStartBackup.IsEnabled = false;
            lblBackupStatus.Text = "Durum: Yedekleniyor...";
            Logla($"\n💾 {ip} üzerinde '{username}' kullanıcısı için yedekleme başlatılıyor...");

            string backupDest = Path.Combine(destRoot, $"{hostname}_{username}");

            await Task.Run(async () =>
            {
                try
                {
                    if (!Directory.Exists(backupDest))
                    {
                        Directory.CreateDirectory(backupDest);
                    }

                    bool isLocal = IsLocalIP(ip);
                    if (!isLocal)
                    {
                        KomutCalistir("net", $@"use \\{ip}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");
                    }

                    List<string> printers = new List<string>();
                    List<string> drives = new List<string>();
                    if (backupNetPrinters)
                    {
                        Dispatcher.Invoke(() => lblBackupStatus.Text = "Durum: Yazıcılar ve Ağ Sürücüleri sorgulanıyor...");
                        printers = QueryPrinters(ip);
                        drives = QueryNetworkDrives(ip);
                    }

                    SaveBackupMetadata(backupDest, username, hostname, printers, drives);

                    string userProfile = isLocal 
                        ? Path.Combine("C:\\Users", username)
                        : $@"\\{ip}\C$\Users\{username}";

                    var copyTasks = new List<Tuple<string, string, string>>();

                    if (backupDesktop)
                        copyTasks.Add(Tuple.Create(Path.Combine(userProfile, "Desktop"), Path.Combine(backupDest, "Desktop"), "Masaüstü"));

                    if (backupDocuments)
                        copyTasks.Add(Tuple.Create(Path.Combine(userProfile, "Documents"), Path.Combine(backupDest, "Documents"), "Belgeler"));

                    if (backupDownloads)
                        copyTasks.Add(Tuple.Create(Path.Combine(userProfile, "Downloads"), Path.Combine(backupDest, "Downloads"), "İndirilenler"));

                    if (backupChrome)
                        copyTasks.Add(Tuple.Create(Path.Combine(userProfile, @"AppData\Local\Google\Chrome\User Data\Default"), Path.Combine(backupDest, "Chrome"), "Chrome Profil"));

                    if (backupEdge)
                        copyTasks.Add(Tuple.Create(Path.Combine(userProfile, @"AppData\Local\Microsoft\Edge\User Data\Default"), Path.Combine(backupDest, "Edge"), "Edge Profil"));

                    if (backupOutlookSignatures)
                        copyTasks.Add(Tuple.Create(Path.Combine(userProfile, @"AppData\Roaming\Microsoft\Signatures"), Path.Combine(backupDest, "Signatures"), "Outlook İmzaları"));

                    if (backupOracleTns)
                    {
                        string o1 = isLocal ? @"C:\orant\network\admin" : $@"\\{ip}\C$\orant\network\admin";
                        string o2 = isLocal ? @"C:\instantclient_23_6\network\admin" : $@"\\{ip}\C$\instantclient_23_6\network\admin";
                        if (Directory.Exists(o1))
                            copyTasks.Add(Tuple.Create(o1, Path.Combine(backupDest, "OracleTNS"), "Oracle TNS 11g"));
                        else if (Directory.Exists(o2))
                            copyTasks.Add(Tuple.Create(o2, Path.Combine(backupDest, "OracleTNS"), "Oracle TNS 23c"));
                    }

                    Dispatcher.Invoke(() => lblBackupStatus.Text = "Durum: Dosyalar taranıyor...");
                    var allFiles = new List<Tuple<string, string>>();
                    foreach (var task in copyTasks)
                    {
                        if (Directory.Exists(task.Item1))
                        {
                            try
                            {
                                var files = Directory.GetFiles(task.Item1, "*", SearchOption.AllDirectories);
                                foreach (var f in files)
                                {
                                    string rel = f.Substring(task.Item1.Length).TrimStart('\\', '/');
                                    string df = Path.Combine(task.Item2, rel);
                                    allFiles.Add(Tuple.Create(f, df));
                                }
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() => Logla($"   ⚠️ {task.Item3} klasörü okunamadı: {ex.Message}"));
                            }
                        }
                    }

                    int total = allFiles.Count;
                    if (total == 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            lblBackupStatus.Text = "Durum: Kopyalanacak dosya bulunamadı.";
                            MessageBox.Show("Yedeklenecek dosya bulunamadı.", "Bilgi");
                        });
                        return;
                    }

                    Dispatcher.Invoke(() => lblBackupStatus.Text = "Durum: Dosyalar kopyalanıyor...");
                    int copied = 0;
                    foreach (var file in allFiles)
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(file.Item2);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                            using (FileStream sourceStream = File.Open(file.Item1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (FileStream destStream = File.Create(file.Item2))
                            {
                                await sourceStream.CopyToAsync(destStream);
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => Logla($"   ⚠️ Kopyalanamadı: {Path.GetFileName(file.Item1)} -> {ex.Message}"));
                        }

                        copied++;
                        int pct = (int)((double)copied / total * 100);
                        Dispatcher.Invoke(() =>
                        {
                            barBackupProgress.Value = pct;
                            lblBackupProgressPct.Text = $"%{pct}";
                            lblBackupProgressFile.Text = $"Kopyalanan: {Path.GetFileName(file.Item1)}";
                        });
                    }

                    if (!isLocal)
                    {
                        KomutCalistir("net", $@"use \\{ip}\ipc$ /delete /y");
                    }

                    Dispatcher.Invoke(() =>
                    {
                        lblBackupStatus.Text = "Durum: Başarıyla Tamamlandı";
                        lblBackupProgressFile.Text = $"Yedek klasörü: {backupDest}";
                        Logla($"   ✅ Yedekleme başarıyla tamamlandı. Konum: {backupDest}");
                        MessageBox.Show("Yedekleme işlemi tamamlandı.", "Başarılı");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        lblBackupStatus.Text = "Durum: Hata oluştu!";
                        Logla($"   ❌ Yedekleme Hatası: {ex.Message}");
                        MessageBox.Show($"Yedekleme sırasında hata oluştu:\n{ex.Message}", "Hata");
                    });
                }
            });

            btnStartBackup.IsEnabled = true;
        }

        private string GetUserSid(string ip, string username)
        {
            try
            {
                ConnectionOptions options = new ConnectionOptions
                {
                    Username = $"{_domain}\\{_kullanici}",
                    Password = _sifre,
                    Impersonation = ImpersonationLevel.Impersonate,
                    EnablePrivileges = true,
                    Timeout = TimeSpan.FromSeconds(5)
                };
                ManagementScope scope = new ManagementScope($@"\\{ip}\root\cimv2", options);
                scope.Connect();

                ObjectQuery query = new ObjectQuery($"SELECT SID FROM Win32_UserAccount WHERE Name = '{username}'");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["SID"]?.ToString() ?? "";
                    }
                }
            }
            catch { }
            return "";
        }

        private void MapRemoteNetworkDrive(string ip, string sid, string driveLetter, string remotePath)
        {
            try
            {
                driveLetter = driveLetter.Replace(":", "").Trim();

                using (var registry = Microsoft.Win32.RegistryKey.OpenRemoteBaseKey(Microsoft.Win32.RegistryHive.Users, ip))
                {
                    string subkeyPath = $@"{sid}\Network\{driveLetter}";
                    using (var key = registry.CreateSubKey(subkeyPath))
                    {
                        if (key != null)
                        {
                            key.SetValue("RemotePath", remotePath, Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("ProviderName", "Microsoft Windows Network", Microsoft.Win32.RegistryValueKind.String);
                            key.SetValue("ProviderType", 131072, Microsoft.Win32.RegistryValueKind.DWord);
                            key.SetValue("ConnectionType", 1, Microsoft.Win32.RegistryValueKind.DWord);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logla($"   ⚠️ Ağ sürücüsü eşlenemedi ({driveLetter} -> {remotePath}): {ex.Message}");
            }
        }

        private void MapRemotePrinter(string ip, string printerName)
        {
            try
            {
                string cmd = $"rundll32.exe printui.dll,PrintUIEntry /ga /n\"{printerName}\"";
                PsExecAgdanCalistir(ip, "", cmd);
            }
            catch (Exception ex)
            {
                Logla($"   ⚠️ Yazıcı eklenemedi ({printerName}): {ex.Message}");
            }
        }

        private async void btnStartRestore_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var pc = BulunanCihazlar.FirstOrDefault(x => x.IsSelected);
            string ip = pc != null ? pc.IP : "127.0.0.1";
            string hostname = pc != null ? pc.Hostname : Environment.MachineName;

            string targetUser = txtRestoreUsername.Text.Trim();
            string sourceBackupDir = txtRestorePath.Text.Trim();

            if (string.IsNullOrEmpty(targetUser))
            {
                MessageBox.Show("Lütfen hedef kullanıcı adını girin.", "Giriş Hatası");
                return;
            }
            if (string.IsNullOrEmpty(sourceBackupDir) || !Directory.Exists(sourceBackupDir))
            {
                MessageBox.Show("Lütfen geçerli bir yedek klasör yolu girin.", "Giriş Hatası");
                return;
            }

            btnStartRestore.IsEnabled = false;
            lblBackupStatus.Text = "Durum: Geri yükleniyor...";
            Logla($"\n📤 {ip} üzerinde '{targetUser}' kullanıcısı için profil geri yükleme başlatılıyor...");

            await Task.Run(async () =>
            {
                try
                {
                    bool isLocal = IsLocalIP(ip);
                    if (!isLocal)
                    {
                        KomutCalistir("net", $@"use \\{ip}\ipc$ /user:{_domain}\{_kullanici} {_sifre}");
                    }

                    string targetProfile = isLocal 
                        ? Path.Combine("C:\\Users", targetUser)
                        : $@"\\{ip}\C$\Users\{targetUser}";

                    var restoreTasks = new List<Tuple<string, string, string>>();

                    string dDesktop = Path.Combine(sourceBackupDir, "Desktop");
                    if (Directory.Exists(dDesktop))
                        restoreTasks.Add(Tuple.Create(dDesktop, Path.Combine(targetProfile, "Desktop"), "Masaüstü"));

                    string dDocuments = Path.Combine(sourceBackupDir, "Documents");
                    if (Directory.Exists(dDocuments))
                        restoreTasks.Add(Tuple.Create(dDocuments, Path.Combine(targetProfile, "Documents"), "Belgeler"));

                    string dDownloads = Path.Combine(sourceBackupDir, "Downloads");
                    if (Directory.Exists(dDownloads))
                        restoreTasks.Add(Tuple.Create(dDownloads, Path.Combine(targetProfile, "Downloads"), "İndirilenler"));

                    string dChrome = Path.Combine(sourceBackupDir, "Chrome");
                    if (Directory.Exists(dChrome))
                        restoreTasks.Add(Tuple.Create(dChrome, Path.Combine(targetProfile, @"AppData\Local\Google\Chrome\User Data\Default"), "Chrome"));

                    string dEdge = Path.Combine(sourceBackupDir, "Edge");
                    if (Directory.Exists(dEdge))
                        restoreTasks.Add(Tuple.Create(dEdge, Path.Combine(targetProfile, @"AppData\Local\Microsoft\Edge\User Data\Default"), "Edge"));

                    string dSignatures = Path.Combine(sourceBackupDir, "Signatures");
                    if (Directory.Exists(dSignatures))
                        restoreTasks.Add(Tuple.Create(dSignatures, Path.Combine(targetProfile, @"AppData\Roaming\Microsoft\Signatures"), "Outlook İmzaları"));

                    string dOracleTNS = Path.Combine(sourceBackupDir, "OracleTNS");
                    if (Directory.Exists(dOracleTNS))
                    {
                        string o1 = isLocal ? @"C:\orant\network\admin" : $@"\\{ip}\C$\orant\network\admin";
                        string o2 = isLocal ? @"C:\instantclient_23_6\network\admin" : $@"\\{ip}\C$\instantclient_23_6\network\admin";
                        if (Directory.Exists(o1))
                            restoreTasks.Add(Tuple.Create(dOracleTNS, o1, "Oracle TNS 11g"));
                        else if (Directory.Exists(o2))
                            restoreTasks.Add(Tuple.Create(dOracleTNS, o2, "Oracle TNS 23c"));
                    }

                    Dispatcher.Invoke(() => lblBackupStatus.Text = "Durum: Geri yüklenecek dosyalar taranıyor...");
                    var allFiles = new List<Tuple<string, string>>();
                    foreach (var task in restoreTasks)
                    {
                        if (Directory.Exists(task.Item1))
                        {
                            try
                            {
                                var files = Directory.GetFiles(task.Item1, "*", SearchOption.AllDirectories);
                                foreach (var f in files)
                                {
                                    string rel = f.Substring(task.Item1.Length).TrimStart('\\', '/');
                                    string df = Path.Combine(task.Item2, rel);
                                    allFiles.Add(Tuple.Create(f, df));
                                }
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() => Logla($"   ⚠️ {task.Item3} klasörü taranamadı: {ex.Message}"));
                            }
                        }
                    }

                    int total = allFiles.Count;
                    if (total > 0)
                    {
                        Dispatcher.Invoke(() => lblBackupStatus.Text = "Durum: Dosyalar kopyalanıyor...");
                        int copied = 0;
                        foreach (var file in allFiles)
                        {
                            try
                            {
                                string dir = Path.GetDirectoryName(file.Item2);
                                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                                using (FileStream sourceStream = File.Open(file.Item1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                using (FileStream destStream = File.Create(file.Item2))
                                {
                                    await sourceStream.CopyToAsync(destStream);
                                }
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() => Logla($"   ⚠️ Geri yüklenemedi: {Path.GetFileName(file.Item1)} -> {ex.Message}"));
                            }

                            copied++;
                            int pct = (int)((double)copied / total * 100);
                            Dispatcher.Invoke(() =>
                            {
                                barBackupProgress.Value = pct;
                                lblBackupProgressPct.Text = $"%{pct}";
                                lblBackupProgressFile.Text = $"Geri Yüklenen: {Path.GetFileName(file.Item1)}";
                            });
                        }
                    }

                    string metadataFile = Path.Combine(sourceBackupDir, "backup_metadata.json");
                    if (File.Exists(metadataFile))
                    {
                        Dispatcher.Invoke(() => lblBackupStatus.Text = "Durum: Yazıcılar ve ağ sürücüleri yapılandırılıyor...");
                        string json = File.ReadAllText(metadataFile);
                        
                        using (var doc = System.Text.Json.JsonDocument.Parse(json))
                        {
                            var root = doc.RootElement;
                            
                            if (root.TryGetProperty("Drives", out var drivesProperty))
                            {
                                string sid = GetUserSid(ip, targetUser);
                                if (!string.IsNullOrEmpty(sid))
                                {
                                    foreach (var driveElement in drivesProperty.EnumerateArray())
                                    {
                                        string rawDrive = driveElement.GetString() ?? "";
                                        var parts = rawDrive.Split('|');
                                        if (parts.Length == 2)
                                        {
                                            string driveLetter = parts[0];
                                            string remotePath = parts[1];
                                            MapRemoteNetworkDrive(ip, sid, driveLetter, remotePath);
                                            Dispatcher.Invoke(() => Logla($"   🔗 Ağ Sürücüsü eşlendi: {driveLetter} -> {remotePath}"));
                                        }
                                    }
                                }
                                else
                                {
                                    Dispatcher.Invoke(() => Logla("   ⚠️ Hedef kullanıcının SID bilgisi çözülemedi. Ağ sürücüleri otomatik bağlanamadı."));
                                }
                            }

                            if (root.TryGetProperty("Printers", out var printersProperty))
                            {
                                foreach (var printerElement in printersProperty.EnumerateArray())
                                {
                                    string rawPrinter = printerElement.GetString() ?? "";
                                    var parts = rawPrinter.Split('|');
                                    if (parts.Length >= 1)
                                    {
                                        string printerName = parts[0];
                                        MapRemotePrinter(ip, printerName);
                                        Dispatcher.Invoke(() => Logla($"   🖨️ Ağ Yazıcısı eklendi: {printerName}"));
                                    }
                                }
                            }
                        }
                    }

                    if (!isLocal)
                    {
                        KomutCalistir("net", $@"use \\{ip}\ipc$ /delete /y");
                    }

                    Dispatcher.Invoke(() =>
                    {
                        lblBackupStatus.Text = "Durum: Geri Yükleme Başarıyla Tamamlandı";
                        lblBackupProgressFile.Text = "-";
                        Logla($"   ✅ Profil geri yükleme başarıyla tamamlandı.");
                        MessageBox.Show("Geri yükleme işlemi tamamlandı.", "Başarılı");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        lblBackupStatus.Text = "Durum: Hata oluştu!";
                        Logla($"   ❌ Geri Yükleme Hatası: {ex.Message}");
                        MessageBox.Show($"Geri yükleme sırasında hata oluştu:\n{ex.Message}", "Hata");
                    });
                }
            });

            btnStartRestore.IsEnabled = true;
        }
    }

    public class ShareItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string ShareType { get; set; }
        public string Description { get; set; }
        public string RemoteIP { get; set; }
    }

    public class ProcessItem
    {
        public string Name { get; set; }
        public int Id { get; set; }
        public string MemoryUsage { get; set; }
    }

    public class PrinterItem
    {
        public string Name { get; set; }
        public string Port { get; set; }
        public string Driver { get; set; }
        public bool Shared { get; set; }
        public string ShareName { get; set; }
    }

    public class ActiveSessionItem
    {
        public string Username { get; set; }
        public string SessionName { get; set; }
        public string SessionId { get; set; }
        public string State { get; set; }
    }

    public class MacroItem
    {
        public string Ad { get; set; }
        public string Komut { get; set; }
    }

    public class ServiceItem
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string State { get; set; }
        public string StartMode { get; set; }
    }

    public class RecentItem
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }
        public string ActionType { get; set; }
        public string ActionParam { get; set; }
        public DateTime When { get; set; }
    }

    public class EventLogItem
    {
        public string Type { get; set; }
        public string Logfile { get; set; }
        public string TimeGenerated { get; set; }
        public string SourceName { get; set; }
        public string EventCode { get; set; }
        public string Message { get; set; }
    }

    public class StartupItem
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string Location { get; set; }
        public string User { get; set; }
    }

    public class InstalledAppItem
    {
        public string DisplayName { get; set; }
        public string DisplayVersion { get; set; }
        public string Publisher { get; set; }
        public string UninstallString { get; set; }
    }

    public static class RecentManager
    {
        private static readonly string RecentPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OtoProgram",
            "recent.json");

        public static List<RecentItem> LoadRecent()
        {
            try
            {
                if (File.Exists(RecentPath))
                {
                    string json = File.ReadAllText(RecentPath);
                    return System.Text.Json.JsonSerializer.Deserialize<List<RecentItem>>(json) ?? new List<RecentItem>();
                }
            }
            catch { }
            return new List<RecentItem>();
        }

        public static void AddRecentAction(string title, string subtitle, string icon, string color, string actionType, string actionParam)
        {
            try
            {
                var list = LoadRecent();
                list.RemoveAll(x => x.ActionType == actionType && x.ActionParam == actionParam);
                list.Insert(0, new RecentItem
                {
                    Title = title,
                    Subtitle = subtitle,
                    Icon = icon,
                    Color = color,
                    ActionType = actionType,
                    ActionParam = actionParam,
                    When = DateTime.Now
                });

                if (list.Count > 8)
                {
                    list = list.GetRange(0, 8);
                }

                string dir = Path.GetDirectoryName(RecentPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(RecentPath, json);
            }
            catch { }
        }
    }
}