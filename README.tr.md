<div align="right">
  <b>Türkçe</b> · <a href="./README.md">English</a>
</div>

<h1 align="center">VACUO</h1>

<p align="center"><i>Fazlalığı çeker, gerekene dokunmaz.</i></p>

<p align="center">
  <a href="https://github.com/AlicanKaya192/Vacuo/releases/latest"><img alt="Son sürüm" src="https://img.shields.io/github/v/release/AlicanKaya192/Vacuo?style=for-the-badge&label=s%C3%BCr%C3%BCm&color=9D7CFF&labelColor=14101D"></a>
  <img alt="Windows 11" src="https://img.shields.io/badge/Windows-11-9D7CFF?style=for-the-badge&labelColor=14101D">
  <img alt=".NET 10 ve WinUI 3" src="https://img.shields.io/badge/.NET%2010-WinUI%203-9D7CFF?style=for-the-badge&labelColor=14101D">
  <br>
  <a href="LICENSE"><img alt="MIT lisansı" src="https://img.shields.io/github/license/AlicanKaya192/Vacuo?style=for-the-badge&label=lisans&color=9D7CFF&labelColor=14101D"></a>
  <img alt="Türkçe ve İngilizce" src="https://img.shields.io/badge/Diller-TR%20%7C%20EN-9D7CFF?style=for-the-badge&labelColor=14101D">
  <img alt="Telemetri yok" src="https://img.shields.io/badge/Telemetri-yok-9D7CFF?style=for-the-badge&labelColor=14101D">
</p>

<p align="center">
  <img src="docs/media/overview.gif" alt="Vacuo paneli: Disk, Windows, Bellek, Düzen ve Pil kartlarının üstünde parçacıkları çeken kara delik" width="600">
</p>

Vacuo, sistem tepsisinde duran bir Windows 11 bakım aracı. Simgesine tıklayınca açılan tek panel diskini, Windows'un biriktirdiği kalıntıları, belleğini ve en dağınık klasörlerini gösterir; *ne gidebilir, bedeli ne, nasıl geri gelir* sorularına açık cevap verir.

Tek bir kurala göre yapıldı: **dürüst ölç, sana sormadan hiçbir şeyi değiştirme.** Önbellekler yeniden indirmenin bedeliyle gösterilir. "RAM hızlandırıcı" ve zorla kapatma yoktur. Kaldırdığın her şey Geri Dönüşüm Kutusu'na gider ve Günlük'ten geri alınır; tek istisna olan Windows'un yönetici klasörleri bunu çalışmadan önce söyler.

## Bir bakış

**Alan nereye gidiyor.** Disk ekranı kullanılan alanı uygulamalar, belgeler, geliştirici araçları, yapay zekâ ajanları ve sistem olarak böler; taramanın bulduğu her dosya kendi kovasına akar: önbellek ve geçici dosyalar, kendi büyük dosyaların ya da *Kalsın*. Her önbellek seçmeden önce nasıl geri geldiğini söyler (yeniden indirilir, bir komutla yeniden kurulur).

<p align="center">
  <img src="docs/media/disk-flow.gif" alt="Beş kategoriden önbellek, kişisel dosyalar ve Kalsın kovalarına akan parçacıklar" width="720">
</p>

**Windows kalıntıları.** Geçici klasörün tek tek listelenir; birkaçını seç ya da *Tümünü seç*'e bas, Geri Dönüşüm Kutusu'na taşımayı bir çubuk izler. Windows Temp, güncelleme indirmeleri ve DISM bileşen temizliği sağ tarafta: yönetici onayı ister, kalıcı silinir ve Vacuo bunu başlamadan önce söyler.

<p align="center">
  <img src="docs/media/windows-cleanup.gif" alt="Bütün geçici dosyaları seçme, onaylama ve Geri Dönüşüm Kutusu'na taşımayı sayan ilerleme çubuğu" width="600">
</p>

**Bu hafta neler boşaldı.** Başlıktaki geçmiş simgesi son yedi günü yavaşça dönen bir karusele çevirir: Vacuo'nun her bölümünün ne kadar yer açtığı, kaç öğe ve ne zaman. Windows'ta animasyonlar kapalıysa düz bir listedir.

<p align="center">
  <img src="docs/media/freed-this-week.gif" alt="Disk, Windows kalıntıları ve Otomatik Bakım kartları arasında dönen Bu hafta karuseli" width="600">
</p>

**Bellek** "% kullanılan" yerine baskıyı gösterir, saatlerdir boşta duran uygulamaları listeler, seçtiklerini normal bir kapatma isteğiyle kapatır (kaydedilmemiş işi olan uygulama yine sana sorabilir) ve ardından gerçek farkı ölçer.

**Düzen** Masaüstü'nü, İndirilenler'i ya da istediğin klasörü kurallara göre toplar; bir şey taşınmadan önce dosyaların klasörlerine akışını gösterir, bütün işlemi tek tıkla geri alır.

**Kopya dosyalar** seçtiğin klasörlerde içeriği aynı dosyaları bulur (önce boyut, sonra hızlı bir parmak izi, sonra tam SHA-256); hiçbiri önceden seçili değildir, hangi kopyanın kalacağına sen karar verirsin. **Kaldır** her programın kendi kaldırıcısını başlatır, ardından geride bıraktığı klasörleri önerir.

**Pil** dizüstü bilgisayarlarda sağlığı, kapasiteyi ve aşınmayı doğrudan pil sürücüsünden okur; masaüstünde kart hiç görünmez.

<p align="center">
  <img src="docs/media/battery.png" alt="Pil ekranı: sağlık yüzde 100, tasarım ve tam şarj kapasitesi, aşınma, şarj ve tahmini süre" width="600">
</p>

**Otomatik Bakım** sen açana kadar kapalıdır. Açınca bilgisayar boştayken ve prizdeyken günde en çok bir kez yalnızca geri alınabilecek şeyleri taşır ve köşede küçük bir kartla haber verir. Bellek sıkışınca tepsi seni uyarabilir; hiçbir şeyi kendi başına kapatmaz.

## Başlarken

**1. İndir:** [Sürümler](https://github.com/AlicanKaya192/Vacuo/releases/latest) sayfasından `Vacuo-<sürüm>-setup.exe`. Windows 11, 64 bit.

**2. Çalıştır.** Vacuo yalnızca senin kullanıcın için `%LOCALAPPDATA%\Programs\Vacuo` altına kurulur, yönetici izni istemez. Başlat menüsüne kısayol ekler ve işareti kaldırmazsan bilgisayar açılınca başlar. Kurulum bitince panel açılır, Vacuo'nun nerede durduğunu görürsün.

**3. Windows ilk seferde uyarabilir.** "Windows bilgisayarınızı korudu" yazan mavi kutu SmartScreen'dir: kurulum dosyası kod imzalı değil, Windows yayıncıyı henüz tanımıyor. **Ek bilgi**'ye, sonra **Yine de çalıştır**'a bas. Akıllı Uygulama Denetimi (Smart App Control) açıksa kurulumu tamamen engelleyebilir.

### Verilerin nerede durur

Ayarların, Günlük, yönetici temizliklerinin sonuçları ve güncelleme durumu programdan ayrı, `%LOCALAPPDATA%\Vacuo\` klasöründe tutulur. Güncelleme ya da yeniden kurulum onlara dokunmaz; kaldırma da onları bırakır, her şeyin gitmesini istiyorsan o klasörü de sil. Tarama sonuçları, kopya araması ve program listesi yalnızca bellekte durur.

### Güncelleme

Panel açılınca (en çok altı saatte bir) ya da Ayarlar'da **Şimdi denetle**'ye basınca Vacuo bu deponun sürüm listesini okur. Yeni sürüm varsa başlıkta yazar. **Güncelle**'ye basınca kurulum dosyasını indirir, boyutunu, imza baytlarını ve SHA-256'sını denetler, kapanır; yeni sürüm sessizce kurulup yeniden açılır. İndirme sürerken Windows'u kullanmaya devam edebilirsin; tepsi simgesi yüzdeyi ve kalan süreyi gösterir. Bir sonraki sürümden itibaren güncellemeler yalnızca değişen dosyaları indirir.

Ayarlar'da **Güncellemeleri denetle**'yi kapatırsan Vacuo hiç internete çıkmaz. Tepsi simgesi hiçbir zaman çıkmaz.

### Kaldırma

**Ayarlar › Uygulamalar › Yüklü uygulamalar › Vacuo › Kaldır** programı ve başlangıç kaydını kaldırır.

## Güvenli tasarım

- Senin seçimin olmadan hiçbir şey yapılmaz. Hiçbir şey önceden seçili değildir, Otomatik Bakım varsayılan olarak kapalıdır.
- Kaldırılan dosyalar Geri Dönüşüm Kutusu'na gider, Vacuo onu boşaltmaz. Kalıcı silinen yalnızca Windows Temp, güncelleme indirmeleri ve DISM temizliğidir; bunu söyleyen bir yönetici onayından sonra çalışır ve Günlük'e yazılır.
- Vacuo'nun kendisi hiçbir zaman yönetici olarak çalışmaz. Yalnızca o temizlik için ayrı, kısa ömürlü bir yardımcı izin ister; ona yol değil, sabit listedeki hangi kalemin temizleneceği söylenir.
- Kayıt defteri temizliği, servis, sürücü, güç ya da sistem ayarı yok.
- Hesap, telemetri, reklam yok. Tek ağ bağlantısı güncelleme denetimidir.

## Neyle yapıldı

.NET 10 üzerinde C#, WinUI 3 (Windows App SDK), animasyonlar için Windows.UI.Composition ve Win2D, kurulum için Inno Setup, sürümler için GitHub Actions.

Bu sayfadaki görüntüler uygulamanın İngilizce arayüzünden.

## Lisans

[MIT](LICENSE) © 2026 Alican Kaya
