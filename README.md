# 🛠️ Otomasyon Kurulum & Domain Asistanı (v3.6)

Bu proje, kurumlar ve kurumsal Active Directory (Domain) ağlarındaki bilgisayarları yönetmek, otomatikleştirmek, uzaktan yazılım/yazıcı kurmak ve envanter toplamak için geliştirilmiş bir **WPF (.NET 8.0)** masaüstü uygulamasıdır.

Uygulama, ağa bağlı olmadan da tüm özellikleri inceleyebilmeniz için **🔓 Gelişmiş Demo Modu** desteğiyle birlikte gelmektedir.

---

## 🚀 Öne Çıkan Özellikler

### 1. 💻 Merkezi Kurulum Paneli
* Toplu uygulama ve sistem paketi dağıtımı.
* `.NET Framework 3.5`, `BGInfo` vb. sistem bileşenlerini otomatik etkinleştirme/kurma.
* Güç ayarlarını optimize etme ve güvenlik duvarı (Firewall/Defender) yönetimi.

### 2. 🔌 Uzak Kurulum ve PC Yönetim Paneli
* **Cihaz Arama:** Belirli IP aralıklarındaki veya Active Directory üzerindeki cihazları tarayıp listeleme (Demo modunda simüle edilir).
* **WMI ile Sistem Bilgisi:** Hedef bilgisayarın İşletim Sistemi, CPU, RAM, Disk doluluk/sağlık (S.M.A.R.T) durumunu sorgulama.
* **Uzak Süreç (Process) Yönetimi:** Çalışan süreçleri listeleme ve sonlandırma (WMI ve taskkill fallback).
* **Servis Yöneticisi:** Windows servislerini listeleme, başlatma, durdurma ve yeniden başlatma.
* **Uzak Terminal:** Hedef bilgisayarlarda CMD/PowerShell komutlarını uzaktan çalıştırabilme.
* **Olay Günlükleri (Event Logs):** Hata ve uyarı loglarını uzaktan sorgulama.
* **Yedekleme & Geri Yükleme:** Kullanıcı profillerini uzaktan yedekleme ve geri yükleme.
* **Uzak Paylaşımlar ve Yazıcılar:** Bilgisayara bağlı yazıcıları ve paylaşılan klasörleri yönetme.

### 3. 🔑 Active Directory (AD) Entegrasyonu
* Kullanıcı hesabı oluşturma, kilit açma, şifre sıfırlama.
* Grup ilkelerini (Group Policy) tetikleme ve yönetici (Local Admin) yetkilendirme işlemleri.

---

## 🔓 Demo Modu Nedir?
Uygulamayı herhangi bir kurumsal Active Directory ağına veya sunucu altyapısına bağlı kalmadan güvenle test edebilirsiniz. Giriş ekranındaki **"🔓 Demo Modu ile Giriş"** butonuna basarak:
* Tüm yönetim pencerelerine erişebilir,
* WMI sistem raporlama, servis, süreç, yazıcı ve log sorgulamalarında **simüle edilmiş (sahte) verileri** görebilir,
* Ağdaki sistemlere zarar verebilecek eylem butonlarında (silme, komut çalıştırma, kurma vb.) güvenlik engellerini test edebilirsiniz.

---

## 🛠️ Kurulum ve Derleme
Proje `.NET 8.0 SDK` ve COM bağımlılıkları (`WUApiLib`) içermektedir. Bu nedenle projeyi derlemek için **Visual Studio MSBuild** aracını kullanmalısınız:

```powershell
# NuGet Paketlerini Geri Yükleyin
msbuild -t:Restore

# Release Konfigürasyonunda Derleyin
msbuild /p:Configuration=Release
```

---

## 📞 İletişim ve Destek
Kurumunuza, şirketinize veya ağınıza özel otomasyon çözümleri, kurumsal kurulum asistanı projeleri ve destek talepleriniz için bana ulaşabilirsiniz:

* **Instagram:** [@cikolatalidondurma35](https://www.instagram.com/cikolatalidondurma35/)
