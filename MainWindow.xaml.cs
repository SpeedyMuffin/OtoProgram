using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data; // Converter Interface'i için gerekli
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OtoProgram
{
    // --- YARDIMCI MODELLER ---

    public class ChatUser : INotifyPropertyChanged
    {
        public string Name { get; set; }
        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set { _isOnline = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        // ComboBox'ta görünecek metin
        public string DisplayName => IsOnline ? $"{Name} (● Aktif)" : Name;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class OnlineColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            // Gelen değer true ise Yeşil (Aktif), false ise Gri (Çevrimdışı) döndür
            bool isOnline = (value is bool) && (bool)value;
            return isOnline ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;
    }

    // --- DÜZELTİLEN CONVERTER (Static Değişken Kullanır) ---
    public class MessageAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string gonderen = value?.ToString();
            // MainWindow üzerindeki statik değişkenden "Ben" kimim öğreniyoruz
            string ben = MainWindow.CurrentUserName;

            // Eğer mesajı ben attıysam veya sistem "Bana" dediyse SAĞA yasla
            if (!string.IsNullOrEmpty(gonderen) && (gonderen == ben || gonderen.StartsWith("Bana")))
            {
                return HorizontalAlignment.Right;
            }

            // Diğer herkes SOLDA kalsın
            return HorizontalAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;
    }

    public class SohbetMesaji
    {
        public string Gonderen { get; set; }
        public string Icerik { get; set; }
        public bool TitresimVarMi { get; set; }
        public DateTime Zaman { get; set; }
        public string UserColor { get; set; } // YENİ: Renk kodu burada duracak
    }

    public class InstallItem : INotifyPropertyChanged
    {
        private bool _isSelected = false;
        private bool _isCompleted = false;

        public string FileName { get; set; }
        public string FullPath { get; set; } // HATA BURADAYDI: Bu satırı ekledik.

        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public bool IsCompleted
        {
            get { return _isCompleted; }
            set { _isCompleted = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class CategoryItem : INotifyPropertyChanged
    {
        public string CategoryName { get; set; }
        public ObservableCollection<InstallItem> Apps { get; set; } = new ObservableCollection<InstallItem>();

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                foreach (var app in Apps) app.IsSelected = value;
                OnPropertyChanged();
            }
        }

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public bool IsCompleted => Apps.Count > 0 && Apps.All(a => a.IsCompleted);

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void RefreshStatus() => OnPropertyChanged(nameof(IsCompleted));
    }

    public class MusicCategory
    {
        public string Name { get; set; }
        public ObservableCollection<MusicTrack> Tracks { get; set; } = new ObservableCollection<MusicTrack>();
    }

    public class MusicTrack
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
    }

    // --- ANA PENCERE MANTIĞI ---

    public partial class MainWindow : Window
    {
        // --- GLOBAL DEĞİŞKENLER ---
        public static string CurrentUserName = ""; // Converter'ın erişebilmesi için STATİK yaptık
        public static bool IsDemoMode { get; set; } = false;

        // Temel Yapılandırma ve Ağ Yolları
        private string networkPath = @"\\192.168.1.100\d$\Programlar\OtoProgram";
        private string domainAdresi = "domain.local";
        private List<string> basariliKurulumlar = new List<string>();

        // Müzik Sistemi Değişkenleri
        private MediaPlayer musicPlayer = new MediaPlayer();
        private bool isPanelExpanded = false;
        private System.Windows.Threading.DispatcherTimer timerProgress;
        private bool isDraggingSlider = false;
        private string loginPassword; // Domain şifresini müzik IP'leri için saklar

        // Sohbet Sistemi Değişkenleri
        private FileSystemWatcher mesajIzleyici;
        private FileSystemWatcher ozelMesajIzleyici;
        // Sohbet Online Sistemi
        private string onlineStatusPath;
        private System.Windows.Threading.DispatcherTimer onlineTimer;
        // Sohbet mesajlarını hafızada tutan ve arayüze bağlayan ana liste
        private ObservableCollection<SohbetMesaji> MesajListesi { get; set; } = new ObservableCollection<SohbetMesaji>();
        // Alıcı listesini tutan dinamik koleksiyon
        private ObservableCollection<ChatUser> AktifKullanicilar { get; set; } = new ObservableCollection<ChatUser>
        {
            new ChatUser { Name = "Genel", IsOnline = true }
        };
        private string sohbetKlasoru;

        // Bu değişkeni sınıfın en üstüne (private alanına) ekle:
        private bool isChatOpen = false;
        private ObservableCollection<CategoryItem> Kategoriler { get; set; } = new();

        public MainWindow()
        {
            InitializeComponent();
            txtKullanici.Focus();

            // Müzik İlerleme Zamanlayıcısı Kurulumu
            timerProgress = new System.Windows.Threading.DispatcherTimer();
            timerProgress.Interval = TimeSpan.FromSeconds(1);
            timerProgress.Tick += TimerProgress_Tick;

            // timerDashboard.Start();

            // Müzik Olay Abonelikleri
            musicPlayer.MediaOpened += MusicPlayer_MediaOpened;
            musicPlayer.MediaEnded += MusicPlayer_MediaEnded;

            // Sohbet
            cmbAlici.ItemsSource = AktifKullanicilar; // Listeyi ComboBox'a bağla
        }

        // --- NAVİGASYON VE KLAVYE KONTROLLERİ ---

        private void txtKullanici_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) txtSifre.Focus();
        }

        private void txtSifre_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) btnGiris_Click(this, new RoutedEventArgs());
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                this.WindowState = WindowState.Maximized;
                btnMaximize.Content = "❐"; // İkonu değiştir (İki kare gibi düşün)
            }
            else
            {
                this.WindowState = WindowState.Normal;
                btnMaximize.Content = "⬜"; // İkonu değiştir (Tek kare)
            }
        }

        // Pencereyi sürükleme ve Çift Tıklama ile Tam Ekran yapma
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2) // Çift tıklama
                {
                    BtnMaximize_Click(null, null);
                }
                else
                {
                    this.DragMove();
                }
            }
        }

        // 2. KAPAT VE MİNİMİZE BUTONLARI (Sağ Üst Köşe)
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }


        // --- DOMAIN GİRİŞ VE BYPASS SİSTEMİ  ---

        private async void btnGiris_Click(object sender, RoutedEventArgs e)
        {
            // 1. KULLANICI GİRDİLERİNİ AL
            string user = txtKullanici.Text.Trim();
            string pass = txtSifre.Password;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                MessageBox.Show("Lütfen kullanıcı adı ve şifre giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Domain seçimini ComboBox'tan oku
            domainAdresi = cmbLoginDomain.Text.Trim();
            IsDemoMode = false;

            // UI GERİ BİLDİRİMİ (DONMAYI ENGELLE)
            this.loginPassword = pass; // Global değişkene at (İlerde lazım olabilir)
            CurrentUserName = user; // Statik değişkene at (Converter için)

            btnGiris.IsEnabled = false;
            btnGiris.Content = "Doğrulanıyor...";
            Mouse.OverrideCursor = Cursors.Wait; // İmleci kum saati yap

            try
            {
                string fullName = user;
                bool isValid = false;
                bool isDomainJoin = true;
                string domainName = domainAdresi; // Domain adı login ekranından alınıyor

                // 2. KİMLİK DOĞRULAMA İŞLEMİ (ARKA PLAN)
                isValid = await Task.Run(() =>
                {
                    try
                    {
                        // A) Active Directory Kontrolü
                        using (PrincipalContext pc = new PrincipalContext(ContextType.Domain, domainName))
                        {
                            bool validated = pc.ValidateCredentials(user, pass);
                            if (validated)
                            {
                                // İsim bilgisini çekmeye çalış
                                UserPrincipal up = UserPrincipal.FindByIdentity(pc, user);
                                if (up != null && !string.IsNullOrEmpty(up.DisplayName))
                                {
                                    fullName = up.DisplayName;
                                }
                            }
                            return validated;
                        }
                    }
                    catch
                    {
                        // AD hatası alınırsa (PC domainde değilse) buraya düşer
                        isDomainJoin = false;
                        return false;
                    }
                });

                // B) Eğer Domain Yoksa IP Üzerinden Manuel Kontrol (Bypass)
                if (!isValid && !isDomainJoin)
                {
                    string[] testIPs = { "192.168.1.100", "192.168.1.10", "192.168.1.150" };

                    isValid = await Task.Run(() =>
                    {
                        foreach (string ip in testIPs)
                        {
                            try
                            {
                                // IPC$ paylaşımına bağlanmayı dene
                                string cmd = $@"use \\{ip}\ipc$ /user:{domainName}\{user} {pass}";
                                ProcessStartInfo psi = new ProcessStartInfo("net", cmd)
                                {
                                    WindowStyle = ProcessWindowStyle.Hidden,
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                };

                                using (Process p = Process.Start(psi))
                                {
                                    p?.WaitForExit();
                                    if (p != null && p.ExitCode == 0)
                                    {
                                        // Bağlantı başarılı olduysa hemen bağlantıyı kes (Temizlik)
                                        Process.Start(new ProcessStartInfo("net", $@"use \\{ip}\ipc$ /delete /y")
                                        { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });

                                        return true;
                                    }
                                }
                            }
                            catch { continue; }
                        }
                        return false;
                    });
                }

                // 3. SONUÇ VE EKRAN GEÇİŞİ
                if (isValid)
                {
                    // --- GİRİŞ BAŞARILI ---

                    // Karşılama Metnini Güncelle
                    lblGreeting.Text = $"Hoş geldiniz, {fullName}";

                    // A) Login Ekranını Kaybet
                    LoginGrid.Visibility = Visibility.Collapsed;

                    // B) Ana Ekranı Aç
                    MainGrid.Visibility = Visibility.Visible;

                    // C) Sağ Üstteki Ana Pencere Butonlarını Göster
                    try { WindowControls.Visibility = Visibility.Visible; } catch { }

                    // *** KRİTİK: ARKAPLANI DOLDUR (Şeffaflığı kaldır) ***
                    MainBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"));

                    // D) Animasyonlu Büyüme (Pencereyi Genişlet)
                    double targetWidth = 1350;
                    double targetHeight = 1050;

                    // Ekran boyutunu taşmasın
                    if (targetWidth > SystemParameters.PrimaryScreenWidth) targetWidth = SystemParameters.PrimaryScreenWidth - 50;
                    if (targetHeight > SystemParameters.PrimaryScreenHeight) targetHeight = SystemParameters.PrimaryScreenHeight - 50;

                    var duration = TimeSpan.FromMilliseconds(800);
                    var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };

                    this.BeginAnimation(Window.WidthProperty, new DoubleAnimation(this.Width, targetWidth, duration) { EasingFunction = ease });
                    this.BeginAnimation(Window.HeightProperty, new DoubleAnimation(this.Height, targetHeight, duration) { EasingFunction = ease });

                    // İçeriği yavaşça göster
                    MainGrid.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(0, 1, duration));

                    // Pencereyi Ekranın Ortasına Al
                    this.Left = (SystemParameters.PrimaryScreenWidth - targetWidth) / 2;
                    this.Top = (SystemParameters.PrimaryScreenHeight - targetHeight) / 2;

                    // 4. ARKA PLAN GÖREVLERİNİ BAŞLAT (Sistem Kasmasın Diye Async)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000); // Animasyonun bitmesini bekle

                        // IP Adresini Bul ve Yaz
                        string localIp = GetLocalIPAddress();
                        Dispatcher.Invoke(() => lblIPAdresi.Text = "Yerel IP: " + localIp);

                        // Paralel Görevler (Klasör Tarama + Sohbet Başlatma)
                        var taskKlasor = Task.Run(() => KlasoruTara());
                        var taskSohbet = SohbetiBaslatAsync();

                        await Task.WhenAll(taskKlasor, taskSohbet);

                        // Online Sistemini Aç ve Log Yaz
                        Dispatcher.Invoke(() =>
                        {
                            OnlineSisteminiBaslat();
                            LogEkle("Sistem başarıyla yüklendi. Modüller aktif.");
                        });
                    });
                }
                else
                {
                    // --- GİRİŞ BAŞARISIZ ---
                    MessageBox.Show("Kullanıcı adı veya şifre hatalı!\nLütfen bilgilerinizi kontrol ediniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);

                    btnGiris.IsEnabled = true;
                    btnGiris.Content = "Sisteme Giriş Yap";
                    txtSifre.Clear();
                    txtSifre.Focus();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Beklenmedik bir hata oluştu:\n" + ex.Message, "Sistem Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                btnGiris.IsEnabled = true;
                btnGiris.Content = "Sisteme Giriş Yap";
            }
            finally
            {
                Mouse.OverrideCursor = null; // İmleci normale döndür
            }
        }

        // --- DEMO GİRİŞ (Ağ bağlantısı olmadan uygulamayı keşfetme) ---
        private void btnDemoGiris_Click(object sender, RoutedEventArgs e)
        {
            IsDemoMode = true;
            // Demo bilgilerini ayarla
            string demoUser = "demo";
            string demoPass = "demo";

            domainAdresi = cmbLoginDomain.Text.Trim();
            this.loginPassword = demoPass;
            CurrentUserName = demoUser;
            txtKullanici.Text = demoUser;

            // Karşılama Metnini Güncelle
            lblGreeting.Text = $"Hoş geldiniz, Demo Kullanıcı";

            // A) Login Ekranını Kaybet
            LoginGrid.Visibility = Visibility.Collapsed;

            // B) Ana Ekranı Aç
            MainGrid.Visibility = Visibility.Visible;

            // C) Sağ Üstteki Ana Pencere Butonlarını Göster
            try { WindowControls.Visibility = Visibility.Visible; } catch { }

            // *** KRİTİK: ARKAPLANI DOLDUR (Şeffaflığı kaldır) ***
            MainBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"));

            // D) Animasyonlu Büyüme (Pencereyi Genişlet)
            double targetWidth = 1350;
            double targetHeight = 1050;

            if (targetWidth > SystemParameters.PrimaryScreenWidth) targetWidth = SystemParameters.PrimaryScreenWidth - 50;
            if (targetHeight > SystemParameters.PrimaryScreenHeight) targetHeight = SystemParameters.PrimaryScreenHeight - 50;

            var duration = TimeSpan.FromMilliseconds(800);
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };

            this.BeginAnimation(Window.WidthProperty, new DoubleAnimation(this.Width, targetWidth, duration) { EasingFunction = ease });
            this.BeginAnimation(Window.HeightProperty, new DoubleAnimation(this.Height, targetHeight, duration) { EasingFunction = ease });

            MainGrid.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(0, 1, duration));

            this.Left = (SystemParameters.PrimaryScreenWidth - targetWidth) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight - targetHeight) / 2;

            // Arka plan görevleri (Ağ yok, sadece IP göster + Log)
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);

                string localIp = GetLocalIPAddress();
                Dispatcher.Invoke(() =>
                {
                    lblIPAdresi.Text = "Yerel IP: " + localIp;
                    LogEkle("⚠️ DEMO MODU AKTİF — Ağ işlemleri devre dışıdır.");
                    LogEkle("Arayüzü keşfetmek için modüllere göz atabilirsiniz.");
                });
            });
        }

        // --- SOHBET AÇ/KAPA (YENİ ANIMASYON) ---

        private void btnChatToggle_Click(object sender, RoutedEventArgs e)
        {
            // Sohbet açık mı kapalı mı kontrol et
            // Eğer açıksa hedef genişlik 0 (Kapat), kapalıysa 320 (Aç)
            double targetWidth = isChatOpen ? 0 : 320;

            // Animasyonu oluştur (300 milisaniye sürsün, yumuşak dursun)
            DoubleAnimation anim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(300));
            anim.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };

            // ChatPanel'in "Width" özelliğini hedef değere götür
            ChatPanel.BeginAnimation(Border.WidthProperty, anim);

            // Durumu tersine çevir
            isChatOpen = !isChatOpen;
        }

        // --- DOSYA LİSTELEME ---

        private async Task KlasoruTara()
        {
            // UI Hazırlığı
            Dispatcher.Invoke(() =>
            {
                Kategoriler.Clear();
                itemsKategoriler.ItemsSource = Kategoriler;
            });

            await Task.Run(async () =>
            {
                try
                {
                    if (!Directory.Exists(networkPath)) return;

                    var directories = Directory.GetDirectories(networkPath);

                    foreach (var dir in directories)
                    {
                        // Kategoriyi arka planda hazırla
                        var category = new CategoryItem { CategoryName = Path.GetFileName(dir) };

                        var files = Directory.EnumerateFiles(dir, "*.*")
                                            .Where(s => s.EndsWith(".exe") || s.EndsWith(".msi") || s.EndsWith(".bat"));

                        foreach (var file in files)
                        {
                            category.Apps.Add(new InstallItem { FileName = Path.GetFileName(file), FullPath = file });
                        }

                        if (category.Apps.Count > 0)
                        {
                            // --- KRİTİK NOKTA ---
                            // DispatcherPriority.Background: "Arayüz meşgulse bekle, boşsa ekle."
                            // Bu sayede tıklamalar asla donmaz.
                            await Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                            {
                                Kategoriler.Add(category);
                            }));

                            // Küçük bir gecikme işlemciyi rahatlatır
                            await Task.Delay(10);
                        }
                    }
                }
                catch { }
            });
        }

        // --- ANA KURULUM DÖNGÜSÜ ---

        private async void btnKurulumBaslat_Click(object sender, RoutedEventArgs e)
        {
            if (IsDemoMode)
            {
                MessageBox.Show("Bu işlem Demo modunda devre dışıdır. Tüm özellikleri kullanmak için lütfen Domain hesabı ile giriş yapın.", "Demo Modu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 1. GİRİŞ BİLGİLERİNİ BAŞTA AL (Arka plan görevleri UI elemanlarına erişemez!)
            string currentUser = txtKullanici.Text;
            string currentPass = txtSifre.Password;

            // 2. SEÇİLİ UYGULAMALARI TOPLA
            var allSelected = Kategoriler.SelectMany(c => c.Apps).Where(x => x.IsSelected).ToList();

            // Uygulamaları Gruplandır
            var normalApps = allSelected.Where(x => !x.FileName.ToLower().Contains("kaspersky") &&
                                                    !x.FileName.ToLower().Contains("kes") &&
                                                    !x.FileName.ToLower().Contains("kagent")).ToList();

            var kasperskyApps = allSelected.Where(x => x.FileName.ToLower().Contains("kaspersky") ||
                                                       x.FileName.ToLower().Contains("kes") ||
                                                       x.FileName.ToLower().Contains("kagent")).ToList();

            // 3. TOPLAM İŞLEM SAYISINI HESAPLA
            int totalSteps = allSelected.Count +
                             (chkHbysKur.IsChecked == true ? 1 : 0) +
                             (chkDomaineKatil.IsChecked == true ? 1 : 0) +
                             (chkDotNet35.IsChecked == true ? 1 : 0) +
                             (chkGucAyarlariniYap.IsChecked == true ? 1 : 0) +
                             (chkBGInfoKur.IsChecked == true ? 1 : 0) +
                             (chkDefenderKapat.IsChecked == true ? 1 : 0) +
                             (chkSmartScreenKapat.IsChecked == true ? 1 : 0);

            if (totalSteps == 0) { MessageBox.Show("Lütfen yapılacak işlemleri seçin."); return; }

            // UI Hazırlığı
            btnKurulumBaslat.IsEnabled = false;
            progressBar1.Maximum = totalSteps;
            progressBar1.Value = 0;
            basariliKurulumlar.Clear();
            lblDurum.Text = "İşlemler başlatılıyor (Paralel Mod)...";

            try
            {
                // --- ADIM 1: SİSTEM HAZIRLIKLARI (Hızlı İşlemler) ---
                if (chkDefenderKapat.IsChecked == true)
                {
                    await Task.Run(() => DefenderVeFirewallKapat());
                    progressBar1.Value++;
                }
                if (chkSmartScreenKapat.IsChecked == true)
                {
                    Dispatcher.Invoke(() => lblDurum.Text = "SmartScreen (Geçmiş Temelli) Kapatılıyor...");
                    await Task.Run(() => SmartScreenKapat());
                    progressBar1.Value++;
                }
                if (chkGucAyarlariniYap.IsChecked == true)
                {
                    await Task.Run(() => GucAyarlariniOptimizeEt());
                    progressBar1.Value++;
                }
                if (chkBGInfoKur.IsChecked == true)
                {
                    // DÜZELTME: Artık parametre gönderiyoruz ve await ile bekliyoruz
                    await BGInfoKurulum(currentUser, currentPass);
                    progressBar1.Value++;
                }
                if (chkDotNet35.IsChecked == true)
                {
                    lblDurum.Text = ".NET 3.5 Etkinleştiriliyor...";
                    await Task.Run(() => KomutCalistir("dism.exe", "/online /enable-feature /featurename:NetFX3 /all /norestart"));
                    progressBar1.Value++;
                }

                // --- ADIM 2: PARALEL KURULUM MOTORU ---
                // HBYS ve Normal Uygulamalar aynı anda çalışacak

                List<Task> paralelGorevler = new List<Task>();

                // A) HBYS Görevi (Parametreli versiyonu çağırıyoruz)
                if (chkHbysKur.IsChecked == true)
                {
                    Task hbysGorevi = Task.Run(async () =>
                    {
                        // DÜZELTME: UI'dan aldığımız currentUser ve currentPass'i kullanıyoruz.
                        // HbysOzelKurulum metodunun parametre alan versiyonunu kullanmalısın!
                        await HbysOzelKurulum(currentUser, currentPass);

                        Dispatcher.Invoke(() =>
                        {
                            progressBar1.Value++;
                            basariliKurulumlar.Add("HBYS Otomasyonu");
                        });
                    });
                    paralelGorevler.Add(hbysGorevi);
                }

                // B) Standart Uygulamalar Döngüsü Görevi
                if (normalApps.Count > 0)
                {
                    Task uygulamalarGorevi = Task.Run(async () =>
                    {
                        foreach (var item in normalApps)
                        {
                            Dispatcher.Invoke(() => lblDurum.Text = $"Kuruluyor: {item.FileName}...");

                            // DÜZELTME: Kategoriler listesine erişimi Dispatcher içine aldık (Cross-thread hatasını çözer)
                            CategoryItem parentCat = null;
                            Dispatcher.Invoke(() =>
                            {
                                parentCat = Kategoriler.FirstOrDefault(c => c.Apps.Contains(item));
                            });

                            // Kurulumu başlat (Burası işlemciyi kullanır)
                            await Task.Run(() => SessizKur(item.FullPath));

                            // Sonucu UI'ya yaz
                            Dispatcher.Invoke(() =>
                            {
                                item.IsCompleted = true;
                                parentCat?.RefreshStatus();
                                progressBar1.Value++;
                                basariliKurulumlar.Add(item.FileName);
                            });
                        }
                    });
                    paralelGorevler.Add(uygulamalarGorevi);
                }

                // --- ADIM 3: HEPSİNİN BİTMESİNİ BEKLE ---
                await Task.WhenAll(paralelGorevler);

                // --- ADIM 4: MANUEL İŞLEMLER (Kaspersky vb.) ---
                foreach (var item in kasperskyApps)
                {
                    Dispatcher.Invoke(() => lblDurum.Text = $"Manuel İşlem: {item.FileName}");

                    // Aynı şekilde burada da kategori bulma işini güvenli yapalım
                    CategoryItem parentCat = null;
                    Dispatcher.Invoke(() => { parentCat = Kategoriler.FirstOrDefault(c => c.Apps.Contains(item)); });

                    await Task.Run(() => SessizKur(item.FullPath));

                    Dispatcher.Invoke(() =>
                    {
                        item.IsCompleted = true;
                        parentCat?.RefreshStatus();
                        progressBar1.Value++;
                        basariliKurulumlar.Add(item.FileName);
                    });
                }

                // --- ADIM 5: DOMAINE KATILMA ---
                if (chkDomaineKatil.IsChecked == true)
                {
                    Dispatcher.Invoke(() => lblDurum.Text = "Domaine dahil ediliyor...");
                    string secilenDomain = "";
                    string yeniAd = "";

                    // UI elemanlarına güvenli erişim
                    Dispatcher.Invoke(() =>
                    {
                        secilenDomain = (cmbDomainSecimi.SelectedItem as ComboBoxItem).Content.ToString();
                        yeniAd = txtYeniPcAdi.Text;
                    });

                    bool sonuc = await Task.Run(() => JoinDomain(secilenDomain, currentUser, currentPass, yeniAd));

                    if (sonuc)
                    {
                        progressBar1.Value++;
                        LogEkle("!!! İşlem Başarılı. Sistem 10 saniye içinde yeniden başlatılacak.");
                        MessageBox.Show("Bilgisayar domaine dahil edildi. Yeniden başlatılıyor...", "Bitti");
                        Process.Start("shutdown.exe", "/r /t 10 /f");
                    }
                    else
                    {
                        LogEkle("HATA: Domaine katılım başarısız.");
                    }
                }

                lblDurum.Text = "Tüm işlemler tamamlandı!";
                btnKurulumBaslat.IsEnabled = true;
                MessageBox.Show("Kurulumlar başarıyla tamamlandı.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Genel Hata: " + ex.Message);
                btnKurulumBaslat.IsEnabled = true;
            }
        }

        // --- KURULUM MOTORU ---

        private void SessizKur(string netPath)
        {
            string fileName = Path.GetFileName(netPath);
            string local = Path.Combine(Path.GetTempPath(), fileName);

            try
            {
                LogEkle($"{fileName} kopyalanıyor...");
                Environment.SetEnvironmentVariable("SEE_MASK_NOZONECHECKS", "1");
                File.Copy(netPath, local, true);

                ProcessStartInfo psi = new ProcessStartInfo { UseShellExecute = true, Verb = "runas" };
                string ext = Path.GetExtension(local).ToLower();
                string lowName = fileName.ToLower();

                bool isKaspersky = lowName.Contains("kaspersky") || lowName.Contains("kes") || lowName.Contains("kagent");

                if (isKaspersky)
                {
                    Dispatcher.Invoke(() => {
                        MessageBox.Show($"{fileName} manuel kuruluma yönlendirildi. Lütfen ekrandaki sihirbazı takip edin.", "Kaspersky", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    psi.FileName = local;
                    psi.WindowStyle = ProcessWindowStyle.Normal;
                    psi.CreateNoWindow = false;
                }
                else if (ext == ".msi")
                {
                    psi.FileName = "msiexec.exe";
                    psi.Arguments = $"/i \"{local}\" /quiet /qn /norestart ALLUSERS=1";
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    psi.CreateNoWindow = true;
                }
                else
                {
                    psi.FileName = local;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    psi.CreateNoWindow = true;

                    if (lowName.Contains("adobe") || lowName.Contains("acrobat"))
                        psi.Arguments = "/sAll /rs /msi EULA_ACCEPT=YES";
                    else if (lowName.Contains("chrome"))
                        psi.Arguments = "/silent /install";
                    else if (lowName.Contains("jre") || lowName.Contains("java"))
                        psi.Arguments = "/s REBOOT=0 SPONSORS=0";
                    else
                        psi.Arguments = "/S /verysilent /suppressmsgboxes /norestart";
                }

                using (Process p = Process.Start(psi)) { p?.WaitForExit(); }
                LogEkle($"{fileName} işlemi bitti.");
            }
            catch (Exception ex) { LogEkle($"HATA ({fileName}): {ex.Message}"); }
            finally { if (File.Exists(local)) try { File.Delete(local); } catch { } }
        }

        private async Task HbysOzelKurulum(string user, string pass)
        {
            LogEkle(">>> HBYS OTOMASYONU KURULUMU BAŞLATILDI...");

            try
            {
                LogEkle("Sunucu yetkilendirmesi (192.168.1.10)...");

                // Parametre olarak gelen user ve pass kullanılıyor (Hata vermez)
                await Task.Run(() => KomutCalistir("net", $@"use \\192.168.1.10\p /user:{domainAdresi}\{user} {pass}"));

                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                // CommonDesktopDirectory = C:\Users\Public\Desktop (Tüm kullanıcılar için)
                string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

                string[] cmds = {
                    $@"xcopy \\192.168.1.10\p\hbys_otomasyon\robocopy.exe {windir}\ /D /C /H /R /Y",
                    @"robocopy \\192.168.1.10\p\x64Hbys_kurulum\orantx64\orant\ c:\orant\ /MIR",
                    @"robocopy \\192.168.1.10\p\hbys_otomasyon\hastane\ c:\hbys_otomasyon\hastane\ /MIR",
                    @"robocopy \\192.168.1.10\p\hbys_otomasyon\pll\ c:\hbys_otomasyon\pll\ /MIR",
                    @"robocopy \\192.168.1.10\p\hbys_otomasyon\icons\ c:\hbys_otomasyon\icons\ /MIR",
                    @"robocopy \\192.168.1.10\p\hbys_otomasyon\icons\ c:\icons\ /MIR",
                    @"regedit.exe /s \\192.168.1.10\p\x64Hbys_kurulum\ek_dosyalar\Orareg1.reg",
                    @"regedit.exe /s \\192.168.1.10\p\x64Hbys_kurulum\ek_dosyalar\Orareg2.reg"
                };

                foreach (var c in cmds)
                {
                    string file = c.Split(' ')[0];
                    string args = c.Substring(file.Length).Trim();
                    await Task.Run(() => KomutCalistir(file, args));
                    LogEkle($"İşlem bitti: {file}");
                }

                // --- ÖZEL KISAYOL VE YETKİ KİLİTLEME İŞLEMİ ---
                try
                {
                    LogEkle("Kısayollar tüm kullanıcılar için kilitleniyor...");

                    string lnk1 = "HBYS Otomasyonu.lnk";
                    string lnk2 = "YedekHBYS.lnk";
                    string kaynakDizin = @"C:\hbys_otomasyon\hastane\";

                    // 1. Kısayolları Ortak Masaüstüne Kopyala
                    if (File.Exists(kaynakDizin + lnk1))
                        File.Copy(kaynakDizin + lnk1, Path.Combine(publicDesktop, lnk1), true);

                    if (File.Exists(kaynakDizin + lnk2))
                        File.Copy(kaynakDizin + lnk2, Path.Combine(publicDesktop, lnk2), true);

                    // 2. Yetkileri Düzenle (icacls kullanarak)
                    // /inheritance:r -> Miras alınan yetkileri temizle
                    // /grant:r System:(F) -> Sisteme tam yetki ver
                    // /grant:r Administrators:(F) -> Adminlere tam yetki ver
                    // /grant:r Users:(RX) -> Standart kullanıcılara SADECE Okuma ve Çalıştırma ver (Silemezler)
                    string icaclsArgs1 = $@"""{Path.Combine(publicDesktop, lnk1)}"" /inheritance:r /grant:r System:(F) /grant:r Administrators:(F) /grant:r Users:(RX)";
                    string icaclsArgs2 = $@"""{Path.Combine(publicDesktop, lnk2)}"" /inheritance:r /grant:r System:(F) /grant:r Administrators:(F) /grant:r Users:(RX)";

                    await Task.Run(() => KomutCalistir("icacls.exe", icaclsArgs1));
                    await Task.Run(() => KomutCalistir("icacls.exe", icaclsArgs2));

                    LogEkle("BAŞARILI: Kısayollar masaüstüne sabitlendi ve silinmeye karşı korundu.");
                }
                catch (Exception ex)
                {
                    LogEkle("HATA (Kısayol Sabitleme): " + ex.Message);
                }
            }
            finally
            {
                await Task.Run(() => KomutCalistir("net", @"use \\192.168.1.10\p /delete /y"));
                LogEkle(">>> HBYS OTOMASYONU KURULUMU TAMAMLANDI.");
            }
        }

        // --- ENVANTER KAYIT (CSV RAPORLAMA) ---

        private async void btnRaporOlustur_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string pcName = string.IsNullOrEmpty(txtYeniPcAdi.Text) ? Environment.MachineName : txtYeniPcAdi.Text;
                string teknisyen = txtKullanici.Text;
                string kurulanlar = string.Join(" | ", basariliKurulumlar);

                lblDurum.Text = "Donanım verileri çekiliyor...";

                await Task.Run(() =>
                {
                    string cpu = GetHardwareInfo("Win32_Processor", "Name");
                    string ramRaw = GetHardwareInfo("Win32_ComputerSystem", "TotalPhysicalMemory");
                    string ram = !string.IsNullOrEmpty(ramRaw) ? (long.Parse(ramRaw) / 1073741824 + 1).ToString() + " GB" : "Bilinmiyor";
                    string gpu = GetHardwareInfo("Win32_VideoController", "Name");

                    string path = Path.Combine(networkPath, "Raporlar", "Tum_Envanter_Listesi.csv");
                    if (!Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(Path.GetDirectoryName(path));

                    bool exists = File.Exists(path);
                    using (StreamWriter sw = new StreamWriter(path, true, Encoding.UTF8))
                    {
                        if (!exists) sw.WriteLine("TARIH;BILGISAYAR;TEKNISYEN;CPU;RAM;GPU;PROGRAMLAR");
                        sw.WriteLine($"{DateTime.Now};{pcName};{teknisyen};{cpu};{ram};{gpu};{kurulanlar}");
                    }
                });
                MessageBox.Show("Envanter listesi güncellendi.", "Başarılı");
            }
            catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
        }

        private void btnAnaListeyiAc_Click(object sender, RoutedEventArgs e)
        {
            string p = Path.Combine(networkPath, "Raporlar", "Tum_Envanter_Listesi.csv");
            if (File.Exists(p)) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
        }

        // 1. ESKİ BUTON (Aynen kalıyor, eski pencereyi açar)
        private void btnDigerModuller_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new DigerModullerWindow(networkPath).Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Modül açılırken hata: " + ex.Message);
            }
        }

        // Instagram Reklam ve Destek Butonu Yönlendirmesi
        private void btnInstagram_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.instagram.com/cikolatalidondurma35/") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Instagram sayfası açılamadı: " + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // 2. YENİ BUTON (Yeni yaptığımız Uzak Kurulum penceresini açar)
        private void btnUzakKurulum_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(loginPassword))
            {
                MessageBox.Show("Lütfen önce giriş yapınız.");
                return;
            }

            // Kullanıcı adı, şifre ve ana listedeki kategorileri gönderiyoruz
            UzakKurulumWindow uzakWin = new UzakKurulumWindow(txtKullanici.Text, loginPassword, Kategoriler);
            uzakWin.Show();
        }

        // ==========================================
        // 📌 HIZLI ERİŞİM VE SON EYLEMLER SİSTEMİ
        // ==========================================
        private void btnRecentToggle_Click(object sender, RoutedEventArgs e)
        {
            var win = new RecentActionsWindow();
            win.Owner = this;
            if (win.ShowDialog() == true && win.SelectedItem != null)
            {
                ExecuteRecentAction(win.SelectedItem);
            }
        }

        private string GetActionTypeName(string actionType)
        {
            switch (actionType)
            {
                case "Module": return "MODÜL";
                case "Macro": return "MAKRO";
                case "PC": return "BİLGİSAYAR";
                default: return "İŞLEM";
            }
        }

        private void ExecuteRecentAction(RecentItem item)
        {
            if (item.ActionType == "Module" && item.ActionParam == "UzakKurulum")
            {
                btnUzakKurulum_Click(null, null);
            }
            else if (item.ActionType == "Macro")
            {
                Clipboard.SetText(item.ActionParam);
                MessageBox.Show($"'{item.Title}' komutu panoya kopyalandı!\n\nKomut: {item.ActionParam}", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (item.ActionType == "PC")
            {
                if (string.IsNullOrEmpty(loginPassword))
                {
                    MessageBox.Show("Lütfen önce giriş yapınız.");
                    return;
                }
                var uzakWin = new UzakKurulumWindow(txtKullanici.Text, loginPassword, Kategoriler, item.ActionParam);
                uzakWin.Show();
            }
        }



        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPanel.Visibility == Visibility.Visible)
                SettingsPanel.Visibility = Visibility.Collapsed;
            else
                SettingsPanel.Visibility = Visibility.Visible;
        }

        // --- MÜZİK KÜTÜPHANESİ VE DONMAYI ENGELLEYEN YAPI ---

        private void btnToggleMusic_Click(object sender, RoutedEventArgs e)
        {
            if (!isPanelExpanded) { colMusic.Width = new GridLength(350); LoadMusicSources(); }
            else { colMusic.Width = new GridLength(0); }
            isPanelExpanded = !isPanelExpanded;
        }


        private void LoadMusicSources()
        {
            try
            {
                string p = Path.Combine(networkPath, "Muzik", "muzikKisiselPc.txt");
                if (File.Exists(p))
                {
                    lstMusicSources.ItemsSource = File.ReadAllLines(p).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                }
            }
            catch { }
        }

        private async void lstMusicSources_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstMusicSources.SelectedItem == null) return;

            string selectedPath = lstMusicSources.SelectedItem.ToString();
            string user = txtKullanici.Text;
            string domain = domainAdresi;
            string pass = loginPassword;
            string serverIP = selectedPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            lblNowPlaying.Text = "Bağlanılıyor...";

            try
            {
                var library = new ObservableCollection<MusicCategory>();
                await Task.Run(() =>
                {
                    // Yetkilendirme
                    string authCmd = $@"use \\{serverIP} /user:{domain}\{user} {pass}";
                    ProcessStartInfo psiAuth = new ProcessStartInfo("net", authCmd) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true };
                    using (Process p = Process.Start(psiAuth)) { p?.WaitForExit(); }

                    if (Directory.Exists(selectedPath))
                    {
                        foreach (var dir in Directory.GetDirectories(selectedPath))
                        {
                            var cat = new MusicCategory { Name = Path.GetFileName(dir) };
                            foreach (var file in Directory.GetFiles(dir, "*.mp3"))
                            {
                                cat.Tracks.Add(new MusicTrack { Name = Path.GetFileName(file), FullPath = file });
                            }
                            if (cat.Tracks.Count > 0) Dispatcher.Invoke(() => library.Add(cat));
                        }

                        var general = new MusicCategory { Name = "Genel (Kök)" };
                        foreach (var file in Directory.GetFiles(selectedPath, "*.mp3"))
                        {
                            general.Tracks.Add(new MusicTrack { Name = Path.GetFileName(file), FullPath = file });
                        }
                        if (general.Tracks.Count > 0) Dispatcher.Invoke(() => library.Add(general));
                    }
                });
                treeMusicLibrary.ItemsSource = library;
                lblNowPlaying.Text = "Kütüphane hazır.";
            }
            catch { lblNowPlaying.Text = "Hata oluştu."; }
        }

        // --- MÜZİK PLAYER KONTROLLERİ ---

        private void TimerProgress_Tick(object sender, EventArgs e)
        {
            if (!isDraggingSlider && musicPlayer.NaturalDuration.HasTimeSpan)
            {
                sldMusicProgress.Value = musicPlayer.Position.TotalSeconds;
                lblCurrentTime.Text = musicPlayer.Position.ToString(@"mm\:ss");
            }
        }

        private void MusicPlayer_MediaOpened(object sender, EventArgs e)
        {
            if (musicPlayer.NaturalDuration.HasTimeSpan)
            {
                sldMusicProgress.Maximum = musicPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                lblTotalTime.Text = musicPlayer.NaturalDuration.TimeSpan.ToString(@"mm\:ss");
                timerProgress.Start();
            }
        }

        private void sldMusicProgress_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { isDraggingSlider = true; }
        private void sldMusicProgress_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDraggingSlider = false;
            musicPlayer.Position = TimeSpan.FromSeconds(sldMusicProgress.Value);
        }

        private void sldMusicProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isDraggingSlider) lblCurrentTime.Text = TimeSpan.FromSeconds(sldMusicProgress.Value).ToString(@"mm\:ss");
        }

        private void MusicPlayer_MediaEnded(object sender, EventArgs e) { PlayNextTrack(); }

        private void PlayNextTrack()
        {
            if (treeMusicLibrary.ItemsSource is ObservableCollection<MusicCategory> library)
            {
                foreach (var category in library)
                {
                    var current = category.Tracks.FirstOrDefault(t => t.Name == lblNowPlaying.Text.Replace("Oynatılıyor: ", ""));
                    if (current != null)
                    {
                        int nextIdx = category.Tracks.IndexOf(current) + 1;
                        var next = nextIdx < category.Tracks.Count ? category.Tracks[nextIdx] : category.Tracks[0];
                        musicPlayer.Open(new Uri(next.FullPath));
                        musicPlayer.Play();
                        lblNowPlaying.Text = "Oynatılıyor: " + next.Name;
                        Dispatcher.BeginInvoke(new Action(() => StartMarqueeAnimation()), System.Windows.Threading.DispatcherPriority.Render);
                        return;
                    }
                }
            }
        }

        // Müzik çalar metotlarının bittiği yere ekle
        private void StartMarqueeAnimation()
        {
            // Önce arayüzü zorla güncelletiyoruz ki yazının yeni genişliğini ölçebilelim
            lblNowPlaying.UpdateLayout();

            double textWidth = lblNowPlaying.ActualWidth;
            double canvasWidth = canvMarquee.ActualWidth;

            // Önceki animasyonu temizleyip yazıyı sıfırlayalım
            lblNowPlaying.BeginAnimation(Canvas.LeftProperty, null);

            // Eğer yazı canvas'a sığıyorsa kaydırmaya gerek yok, ortada/solda kalsın
            if (textWidth <= canvasWidth)
            {
                Canvas.SetLeft(lblNowPlaying, 0);
                return;
            }

            // Animasyon: Sağdan girsin, soldan çıksın
            DoubleAnimation doubleAnimation = new DoubleAnimation();
            doubleAnimation.From = canvasWidth; // Sağ taraftan başla
            doubleAnimation.To = -textWidth;    // Kendi uzunluğu kadar sola git (tamamen kaybolana kadar)
            doubleAnimation.Duration = TimeSpan.FromSeconds(10); // Hız (Yavaşlatmak için 15 yapabilirsin)
            doubleAnimation.RepeatBehavior = RepeatBehavior.Forever;

            lblNowPlaying.BeginAnimation(Canvas.LeftProperty, doubleAnimation);
        }

        private void treeMusicLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Tıklanan öğenin bir MusicTrack (şarkı) olup olmadığını kontrol et
            if (treeMusicLibrary.SelectedItem is MusicTrack track)
            {
                // Şarkıyı aç ve oynat
                musicPlayer.Open(new Uri(track.FullPath));
                musicPlayer.Play();

                // Şarkı ismini etikete yaz
                lblNowPlaying.Text = "Oynatılıyor: " + track.Name;

                // Kayar yazı animasyonunu tetikle (Daha önce eklediğimiz metot)
                Dispatcher.BeginInvoke(new Action(() => {
                    StartMarqueeAnimation();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            // Not: Eğer bir klasöre çift tıklanırsa bu blok çalışmaz, sadece şarkılarda çalışır.
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (treeMusicLibrary.SelectedItem is MusicTrack track)
            {
                musicPlayer.Open(new Uri(track.FullPath));
                musicPlayer.Play();
                lblNowPlaying.Text = "Oynatılıyor: " + track.Name;

                // Yazı değiştikten hemen sonra ölçüm yapabilmek için çok kısa bir bekleme (Dispatcher) ile çağırıyoruz
                Dispatcher.BeginInvoke(new Action(() => {
                    StartMarqueeAnimation();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            else { musicPlayer.Play(); }
        }

        private void btnPause_Click(object sender, RoutedEventArgs e) { musicPlayer.Pause(); }
        private void sldVolumeMusic_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { musicPlayer.Volume = sldVolumeMusic.Value; }

        // --- SİSTEM YARDIMCILARI ---

        private void DefenderVeFirewallKapat()
        {
            KomutCalistir("powershell", "Set-MpPreference -DisableRealtimeMonitoring $true");
            KomutCalistir("netsh", "advfirewall set allprofiles state off");
        }

        private void SmartScreenKapat()
        {
            LogEkle("SmartScreen (Geçmiş Temelli Koruma) kapatılıyor...");
            KomutCalistir("reg.exe", "add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\" /v \"SmartScreenEnabled\" /t REG_SZ /d \"Off\" /f");
            KomutCalistir("reg.exe", "add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\" /v \"EnableSmartScreen\" /t REG_DWORD /d 0 /f");
            KomutCalistir("reg.exe", "add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\" /v \"ShellSmartScreenLevel\" /t REG_SZ /d \"Off\" /f");
            KomutCalistir("reg.exe", "add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppHost\" /v \"EnableWebContentEvaluation\" /t REG_DWORD /d 0 /f");
            LogEkle("BAŞARILI: SmartScreen / Geçmiş Temelli Koruma kapatıldı.");
        }

        private void LogEkle(string mesaj)
        {
            Dispatcher.Invoke(() => {
                txtHbysLog.Text += $"\n[{DateTime.Now:HH:mm:ss}] {mesaj}";
                scrollLog.ScrollToEnd();
            });
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "IP Bulunamadı";
        }

        private string GetHardwareInfo(string win32Class, string classProperty)
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher($"SELECT {classProperty} FROM {win32Class}"))
                    foreach (ManagementObject obj in s.Get()) return obj[classProperty]?.ToString().Trim();
            }
            catch { }
            return "Bilinmiyor";
        }

        private bool JoinDomain(string dom, string user, string pass, string newName)
        {
            try
            {
                // 1. ADIM: Log Bilgisi Gönder
                Dispatcher.Invoke(() => LogEkle($">>> {dom} hedefine katılım ve isim değişikliği işlemi başlatıldı..."));

                // 2. ADIM: PowerShell Komutunu Hazırla
                // Şifreyi güvenli bir şekilde komuta gömüyoruz.
                // Eğer yeni isim girilmişse -NewName parametresini ekliyoruz.
                string nameParam = !string.IsNullOrEmpty(newName) ? $"-NewName '{newName}'" : "";

                // PowerShell script: Kimlik bilgilerini oluştur ve Add-Computer komutunu çalıştır.
                string script = $@"
            $password = ConvertTo-SecureString '{pass}' -AsPlainText -Force;
            $credential = New-Object System.Management.Automation.PSCredential('{dom}\{user}', $password);
            Add-Computer -DomainName '{dom}' {nameParam} -Credential $credential -Force;";

                // 3. ADIM: İşlemi Çalıştır
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = "runas" // Yönetici yetkisiyle çalıştır
                };

                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();

                    if (p.ExitCode == 0)
                    {
                        Dispatcher.Invoke(() => LogEkle("BAŞARILI: Bilgisayar adı değiştirildi ve domaine dahil edildi."));
                        return true;
                    }
                    else
                    {
                        Dispatcher.Invoke(() => LogEkle($"HATA: PowerShell işlem kodu: {p.ExitCode}. Yetki veya ağ hatası."));
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => LogEkle("KRİTİK SİSTEM HATASI: " + ex.Message));
                return false;
            }
        }

        // --- SEÇİM KONTROLLERİ ---

        private void btnTumunuSec_Click(object sender, RoutedEventArgs e)
        {
            // Kategoriler listesindeki her bir klasörü (ve dolayısıyla içindeki her şeyi) seçer
            foreach (var cat in Kategoriler) cat.IsSelected = true;
        }

        private void btnSecimiKaldir_Click(object sender, RoutedEventArgs e)
        {
            // Kategoriler listesindeki tüm seçimleri temizler
            foreach (var cat in Kategoriler) cat.IsSelected = false;
        }

        private void CategoryHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Sadece ÇİFT TIKLAMA (ClickCount == 2) yapıldığında çalışsın
            if (e.ClickCount == 2)
            {
                var grid = sender as FrameworkElement;
                if (grid?.DataContext is CategoryItem category)
                {
                    // Durumu tersine çevir (Açıksa kapat, kapalıysa aç)
                    category.IsExpanded = !category.IsExpanded;
                }
            }
        }

        private void GucAyarlariniOptimizeEt()
        {
            try
            {
                LogEkle("Güç ayarları optimize ediliyor (Ekran/Uyku/Disk -> Asla)...");

                // AC (Fişe takılı) durumları için ayarlar
                KomutCalistir("powercfg", "/change monitor-timeout-ac 0"); // Ekran kapanması
                KomutCalistir("powercfg", "/change disk-timeout-ac 0");    // Disk kapanması
                KomutCalistir("powercfg", "/change standby-timeout-ac 0"); // Uyku modu
                KomutCalistir("powercfg", "/hibernate off");              // Hazırda bekletmeyi kapat

                LogEkle("BAŞARILI: Güç planı 'Yüksek Performans' moduna göre optimize edildi.");
            }
            catch (Exception ex)
            {
                LogEkle("HATA (Güç Ayarları): " + ex.Message);
            }
        }

        // Parametre alan ve Güvenli BGInfo Kurulumu
        private async Task BGInfoKurulum(string user, string pass)
        {
            string serverPath = @"\\Tepecikeah-dc01\NETLOGON";
            try
            {
                LogEkle(">>> BGINFO KURULUMU BAŞLATILDI (.cmd Kopyalama)...");

                // DÜZELTME: Uzantı .cmd olarak ayarlandı
                string serverFile = Path.Combine(serverPath, "bginfo.cmd");

                // Hedef: Tüm Kullanıcılar için Başlangıç Klasörü
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                // DÜZELTME: Hedef dosya adı da .cmd yapıldı
                string localFile = Path.Combine(startupFolder, "bginfo.cmd");

                // 1. GEÇİCİ YETKİLENDİRME (Sunucudaki dosyayı almak için)
                await Task.Run(() => KomutCalistir("net", $@"use {serverPath} /user:{domainAdresi}\{user} {pass}"));

                // 2. DOSYAYI KOPYALA
                if (File.Exists(serverFile))
                {
                    // true: Dosya varsa üzerine yaz
                    File.Copy(serverFile, localFile, true);
                    LogEkle($"Dosya kopyalandı: {localFile}");
                }
                else
                {
                    LogEkle("HATA: Sunucuda bginfo.cmd bulunamadı!");
                    return;
                }

                // 3. ANLIK ÇALIŞTIR (Masaüstü hemen güncellensin)
                // Bağlantı hala açıkken çalıştırıyoruz ki, cmd dosyası sunucudaki exe'ye erişebilsin.
                await Task.Run(() =>
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c \"{localFile}\"", // Kopyaladığımız yerel .cmd dosyasını çalıştır
                            WindowStyle = ProcessWindowStyle.Hidden,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };

                        using (Process p = Process.Start(psi))
                        {
                            p.WaitForExit();
                        }

                        // BGInfo'nun resmi çizmesi için süre tanı
                        System.Threading.Thread.Sleep(3000);
                        LogEkle("BGInfo tetiklendi ve arkaplan güncellendi.");
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                LogEkle("HATA (BGInfo): " + ex.Message);
            }
            finally
            {
                await Task.Run(() => KomutCalistir("net", $@"use {serverPath} /delete /y"));
            }
        }

        private void KomutCalistir(string dosya, string arguman)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(dosya, arguman) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, UseShellExecute = true, Verb = "runas" };
                using (Process p = Process.Start(psi)) p?.WaitForExit();
            }
            catch { }
        }

        // --- SOHBET VE TİTREŞİM MOTORU ---

        public void TitresimGonder()
        {
            Dispatcher.Invoke(() =>
            {
                // Programı öne getir ve sars
                if (this.WindowState == WindowState.Minimized) this.WindowState = WindowState.Normal;
                this.Activate();

                var originalLeft = this.Left;
                DoubleAnimation shake = new DoubleAnimation
                {
                    From = originalLeft,
                    To = originalLeft + 10,
                    Duration = TimeSpan.FromMilliseconds(50),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(5)
                };
                this.BeginAnimation(Window.LeftProperty, shake);
            });
        }

        private void OnlineSisteminiBaslat()
        {
            try
            {
                // Klasör kontrolü
                onlineStatusPath = Path.Combine(networkPath, "Sohbet", "OnlineStatus");
                if (!Directory.Exists(onlineStatusPath)) Directory.CreateDirectory(onlineStatusPath);

                // Timer kurulumu
                onlineTimer = new System.Windows.Threading.DispatcherTimer();
                onlineTimer.Interval = TimeSpan.FromSeconds(30);

                // Timer tetiklendiğinde yapılacak işler (Lambda Expression)
                // Bunu bir değişkene (Action) atıyoruz ki hem Timer'a verelim hem de manuel çağıralım.
                EventHandler timerAction = (s, e) =>
                {
                    try
                    {
                        // 1. KENDİ DURUMUNU GÜNCELLE (Heartbeat)
                        string userFile = Path.Combine(onlineStatusPath, txtKullanici.Text + ".status");
                        File.WriteAllText(userFile, DateTime.Now.ToString());

                        // 2. LİSTEDEKİ DİĞERLERİNİ KONTROL ET
                        foreach (var user in AktifKullanicilar)
                        {
                            if (user.Name == "Genel") continue;

                            // Kullanıcının online durumunu yardımcı metoda sor
                            user.IsOnline = KullaniciOnlineMi(user.Name);
                        }

                        // 3. EKRANDAKİ "X Kişi Çevrimiçi" YAZISINI GÜNCELLE
                        GuncelleOnlineSayisi();
                    }
                    catch { }
                };

                // Event'i Timer'a bağla
                onlineTimer.Tick += timerAction;

                // --- İLK ÇALIŞTIRMA (30 saniye beklememek için) ---
                timerAction(null, null);

                // Timer'ı başlat
                onlineTimer.Start();

                // Pencere kapanırken durumu sil
                this.Closed += (s, e) =>
                {
                    try
                    {
                        string userFile = Path.Combine(onlineStatusPath, txtKullanici.Text + ".status");
                        if (File.Exists(userFile)) File.Delete(userFile);
                    }
                    catch { }
                };
            }
            catch { }
        }

        private bool KullaniciOnlineMi(string kullaniciAdi)
        {
            try
            {
                string statusFile = Path.Combine(networkPath, "Sohbet", "OnlineStatus", kullaniciAdi + ".status");
                if (File.Exists(statusFile))
                {
                    // Dosya son 90 saniye içinde güncellenmişse online'dır
                    return (DateTime.Now - File.GetLastWriteTime(statusFile)).TotalSeconds < 90;
                }
            }
            catch { }
            return false; // Dosya yoksa veya hata varsa çevrimdışıdır
        }

        private void GuncelleOnlineSayisi()
        {
            Dispatcher.Invoke(() =>
            {
                // "Genel" hariç, IsOnline değeri true olanları say
                int sayi = AktifKullanicilar.Count(u => u.IsOnline && u.Name != "Genel");

                // lblOnlineCount null check yapıyoruz
                // MainWindow içinde bu label'a ulaşamıyorsanız x:Name="lblOnlineCount" XAML'de tanımlı olmalı
                // lblOnlineCount.Text = $"{sayi} Çevrimiçi"; 
                // lblOnlineCount.Foreground = sayi > 0 ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : Brushes.Gray;
            });
        }

        // --- YENİ ASENKRON SOHBET YÜKLEYİCİ ---
        private async Task SohbetiBaslatAsync()
        {
            // Sohbet listesini UI thread'e bağla
            Dispatcher.Invoke(() => chatMessages.ItemsSource = MesajListesi);
            string userName = "";
            Dispatcher.Invoke(() => userName = txtKullanici.Text);

            await Task.Run(() =>
            {
                try
                {
                    // 1. KLASÖRLERİ OLUŞTUR (Hızlı)
                    sohbetKlasoru = Path.Combine(networkPath, "Sohbet", "Genel");
                    if (!Directory.Exists(sohbetKlasoru)) Directory.CreateDirectory(sohbetKlasoru);

                    string ozelKlasor = Path.Combine(networkPath, "Sohbet", userName);
                    if (!Directory.Exists(ozelKlasor)) Directory.CreateDirectory(ozelKlasor);

                    // 2. İZLEYİCİLERİ BAŞLAT (Yeni mesaj gelirse anında yakala)
                    mesajIzleyici = new FileSystemWatcher(sohbetKlasoru, "*.msg");
                    mesajIzleyici.Created += (s, e) => MesajOkuVeListeyeEkle(e.FullPath, true, false);
                    mesajIzleyici.EnableRaisingEvents = true;

                    ozelMesajIzleyici = new FileSystemWatcher(ozelKlasor, "*.msg");
                    ozelMesajIzleyici.Created += (s, e) => MesajOkuVeListeyeEkle(e.FullPath, true, true);
                    ozelMesajIzleyici.EnableRaisingEvents = true;

                    // 3. TÜM GEÇMİŞİ ARKA PLANDA TOPLA (Hafızada işlem)
                    // Bu kısım UI'ya gitmediği için çok hızlıdır.
                    var tumDosyalar = new List<string>();
                    tumDosyalar.AddRange(Directory.GetFiles(sohbetKlasoru, "*.msg"));
                    tumDosyalar.AddRange(Directory.GetFiles(ozelKlasor, "*.msg"));

                    var geciciListe = new List<SohbetMesaji>();
                    var yeniKullanicilar = new List<string>(); // Yeni kullanıcıları da burada tespit edelim

                    foreach (var dosya in tumDosyalar)
                    {
                        try
                        {
                            string icerik = File.ReadAllText(dosya);
                            var msg = System.Text.Json.JsonSerializer.Deserialize<SohbetMesaji>(icerik);

                            msg.UserColor = GetUserColor(msg.Gonderen);

                            // Özel mesaj kontrolü (Dosya yolu içinde kullanıcı adı var mı?)
                            if (dosya.Contains(Path.Combine("Sohbet", userName)))
                                msg.Icerik = "🔒 (Özel) " + msg.Icerik;

                            geciciListe.Add(msg);

                            // Kullanıcı listesine eklenecekleri not al
                            if (msg.Gonderen != userName && !yeniKullanicilar.Contains(msg.Gonderen))
                                yeniKullanicilar.Add(msg.Gonderen);
                        }
                        catch { }
                    }

                    // 4. TARİHE GÖRE SIRALA (Eskiden Yeniye)
                    // İstersen .TakeLast(50) diyerek sadece son 50 mesajı alıp daha da hızlandırabilirsin.
                    var siraliMesajlar = geciciListe.OrderBy(x => x.Zaman).ToList();

                    // 5. TEK SEFERDE EKRANA BAS (Toplu Güncelleme)
                    Dispatcher.Invoke(() =>
                    {
                        // Önce kullanıcıları ekle
                        foreach (var gonderen in yeniKullanicilar)
                        {
                            if (!AktifKullanicilar.Any(u => u.Name == gonderen))
                                AktifKullanicilar.Add(new ChatUser { Name = gonderen, IsOnline = true });
                        }

                        // Sonra mesajları ekle
                        foreach (var msg in siraliMesajlar)
                        {
                            MesajListesi.Add(msg);
                        }

                        // En son aşağı kaydır
                        scrollChat.ScrollToEnd();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => LogEkle("Sohbet Yükleme Hatası: " + ex.Message));
                }
            });
        }

        // Mesaj okuma işlemini tek bir metoda toplayalım (Kod tekrarı olmasın)
        private void MesajOkuVeListeyeEkle(string dosyaYolu, bool titresimIzni, bool isPrivate)
        {
            try
            {
                // Sadece anlık gelen (yeni) mesajlarda dosya yazma işleminin bitmesini bekle
                // Geçmişi yüklerken beklemeye gerek yok, bu sayede açılış hızlanır.
                if (titresimIzni) System.Threading.Thread.Sleep(200);

                if (!File.Exists(dosyaYolu)) return;

                string icerik = File.ReadAllText(dosyaYolu);
                var msg = System.Text.Json.JsonSerializer.Deserialize<SohbetMesaji>(icerik);
                msg.UserColor = GetUserColor(msg.Gonderen);

                if (isPrivate) msg.Icerik = "🔒 (Özel) " + msg.Icerik;

                Dispatcher.Invoke(() =>
                {
                    // 1. KULLANICIYI ALICI LİSTESİNE EKLE (Eğer yoksa ve ben değilsem)
                    if (!AktifKullanicilar.Any(u => u.Name == msg.Gonderen) && msg.Gonderen != txtKullanici.Text)
                    {
                        // DÜZELTME BURADA:
                        // Ezbere "true" demek yerine, status dosyasına bakıp karar veriyoruz.
                        bool suankiDurum = KullaniciOnlineMi(msg.Gonderen);

                        AktifKullanicilar.Add(new ChatUser
                        {
                            Name = msg.Gonderen,
                            IsOnline = suankiDurum
                        });

                        // Yeni kişi eklendiği için sayacı hemen güncelle
                        GuncelleOnlineSayisi();
                    }

                    // 2. MESAJI LİSTEYE EKLE
                    if (!MesajListesi.Any(m => m.Zaman == msg.Zaman && m.Gonderen == msg.Gonderen))
                    {
                        MesajListesi.Add(msg);
                        scrollChat.ScrollToEnd();

                        // Titreşim sadece yeni mesajsa ve gönderen ben değilsem çalışsın
                        if (titresimIzni && msg.TitresimVarMi && msg.Gonderen != txtKullanici.Text)
                            TitresimGonder();
                    }
                });
            }
            catch { }
        }

        // --- YARDIMCI METOT: İsime göre her zaman aynı rengi üretir ---
        private string GetUserColor(string username)
        {
            string[] palette = {
        "#E53935", "#D81B60", "#8E24AA", "#5E35B1", "#3949AB",
        "#1E88E5", "#039BE5", "#00ACC1", "#00897B", "#43A047",
        "#F4511E", "#6D4C41", "#546E7A", "#2E7D32", "#C62828"
    };

            if (string.IsNullOrEmpty(username)) return "#333333";

            // İsmin karakter kodlarını toplayarak eşsiz bir sayı (hash) üretir
            int hash = 0;
            foreach (char c in username) hash += (int)c;

            // Listenin uzunluğuna göre mod alarak paletten renk seçer (Her zaman aynı isme aynı renk)
            int index = Math.Abs(hash) % palette.Length;
            return palette[index];
        }
        // --- SOHBET ARAYÜZÜ OLAYLARI ---

        // Mesaj gönderme butonu tıklandığında çalışır
        private void btnSendMessage_Click(object sender, RoutedEventArgs e)
        {
            string mesaj = txtMessage.Text.Trim();

            // Mesaj boşsa veya sadece boşluktan oluşuyorsa gönderme
            if (string.IsNullOrEmpty(mesaj)) return;

            // 1. Seçili alıcıyı direkt string olarak al (Eğer seçim yoksa "Genel" kabul et)
            string secilenAlici = cmbAlici.SelectedValue as string; // SelectedValuePath="Name" olduğu için string gelir
            if (string.IsNullOrEmpty(secilenAlici)) secilenAlici = "Genel";

            // 2. Mesaj dosyasını ağdaki ilgili klasöre yaz
            // Not: Titreşim parametresi burada 'false' gönderiliyor, normal mesaj çünkü.
            MesajDosyasiYaz(secilenAlici, mesaj, false);

            // 3. Eğer ÖZEL MESAJ gönderdiysem, bunu anında kendi ekranımda da görmeliyim
            // (Çünkü FileSystemWatcher sadece başkasının klasörüne düşen mesajı yakalar)
            if (secilenAlici != "Genel" && secilenAlici != txtKullanici.Text)
            {
                MesajListesi.Add(new SohbetMesaji
                {
                    Gonderen = $"Bana ({secilenAlici})",
                    Icerik = "🔒 " + mesaj,
                    Zaman = DateTime.Now,
                    UserColor = "#999999" // Kendi gönderdiğimiz özel mesajlar gri görünsün
                });
            }
            else if (secilenAlici == txtKullanici.Text)
            {
                // Kendine mesaj atma durumu (isteğe bağlı)
                LogEkle("Not: Kendinize mesaj gönderdiniz.");
            }

            // 4. Arayüzü temizle ve odakla
            txtMessage.Clear();
            txtMessage.Focus();

            // 5. Mesaj listesini en aşağıya (en yeni mesaja) kaydır
            scrollChat.ScrollToEnd();
        }
        // Titreşim (Buzz) butonu tıklandığında çalışır
        private void btnBuzz_Click(object sender, RoutedEventArgs e)
        {
            // "Genel" klasörüne titreşimli mesaj gönder (Titresim: true)
            MesajDosyasiYaz("Genel", "Sana bir titreşim gönderdi! 🔔", true);
            TitresimGonder(); // Kendi ekranımızı da sarsalım
        }

        // Mesaj kutusunda bir tuşa basıldığında çalışır (Enter ile gönderim için)
        private void txtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                btnSendMessage_Click(this, new RoutedEventArgs());
                e.Handled = true; // Enter'ın alt satıra geçmesini engelle
            }
        }

        private void MesajDosyasiYaz(string alici, string mesaj, bool titresim)
        {
            try
            {
                string hedefYol = Path.Combine(networkPath, "Sohbet", alici);
                if (!Directory.Exists(hedefYol)) Directory.CreateDirectory(hedefYol);

                var msg = new SohbetMesaji
                {
                    Gonderen = txtKullanici.Text,
                    Icerik = mesaj,
                    TitresimVarMi = titresim,
                    Zaman = DateTime.Now
                };

                // Mesajı JSON formatında dosyaya yazıyoruz
                string json = System.Text.Json.JsonSerializer.Serialize(msg);
                string dosyaAdi = $"{DateTime.Now:yyyyMMddHHmmssfff}_{txtKullanici.Text}.msg";
                File.WriteAllText(Path.Combine(hedefYol, dosyaAdi), json);
            }
            catch (Exception ex) { LogEkle("Mesaj Gönderilemedi: " + ex.Message); }
        }
    }
}