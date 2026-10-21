# Vacuo

Fazlalığı çeker, gerekene dokunmaz.

Vacuo, sistem tepsisinde duran küçük bir Windows 11 bakım aracı. Sağ alttaki simgesine tıklayınca açılan panel diskini, belleğini ve dağınık klasörlerini tek yerde gösterir:

- **Disk:** kullanılan alanın nereye gittiği, geliştirici ve yapay zekâ önbellekleri (pip, uv, conda, Hugging Face, PyTorch, npm, Rust `target/`, eski `.venv` ve `node_modules`) ve bunları yeniden indirmenin bedeli, kendi büyük dosyaların ve kalması gerekenler.
- **Windows kalıntıları:** geçici klasörün; yönetici onayıyla Windows Temp, güncelleme indirmeleri ve DISM bileşen temizliği.
- **Bellek:** "% kullanılan" yerine bellek baskısı, saatlerdir boşta duran uygulamalar ve onları kapattıktan sonraki gerçek fark.
- **Düzen:** Masaüstü, İndirilenler ya da istediğin klasör için kurallar; bir şey taşınmadan önce önizleme, tek tıkla geri alma.
- **Kopya dosyalar:** içeriği aynı olan dosyalar; hangi kopyanın kalacağını sen seçersin.
- **Kaldır:** her programın kendi kaldırıcısı, ardından geride kalanların listesi.
- **Pil:** dizüstü bilgisayarlarda pil sürücüsünden okunan pil sağlığı.
- **Otomatik Bakım:** varsayılan olarak kapalı; açılınca bilgisayar boştayken ve prizdeyken günde en çok bir kez.
- **Bu hafta neler boşaldı** ve **Günlük:** açılan yer, yapılan her işlem ve nasıl geri alınacağı.

[English](README.md)

## İndir

[Sürümler](https://github.com/AlicanKaya192/vacuo/releases/latest) sayfasından `Vacuo-X.Y.Z-setup.exe` dosyasını indirip çalıştır. Yalnızca senin kullanıcın için kurulur, yönetici izni istemez. Vacuo kendini aynı sayfadan günceller.

Kurulum dosyası kod imzalı değil, Windows SmartScreen uyarı verebilir: "Ek bilgi"ye, sonra "Yine de çalıştır"a bas. Akıllı Uygulama Denetimi (Smart App Control) engelleyebilir.

## Güvenli tasarım

- Senin seçimin olmadan hiçbir şey yapılmaz. Otomatik Bakım varsayılan olarak kapalı ve yalnızca geri alınabilecek şeyleri taşır.
- Kaldırdığın dosyalar Geri Dönüşüm Kutusu'na gider, Vacuo onu boşaltmaz. Tek istisna Windows ekranındaki yönetici temizliği (Windows Temp, güncelleme indirmeleri, DISM): yönetici onayı ister, geri alınamayacağını söyler ve Günlük'e yazılır.
- "RAM temizleme" ve zorla kapatma yok. Kapatma normal bir kapatma isteği gönderir; kaydedilmemiş işi olan uygulama önce sana sorabilir.
- Kayıt defteri temizliği, servis, sürücü ya da sistem ayarı yok. Vacuo'nun kendisi hiçbir zaman yönetici olarak çalışmaz; yalnızca Windows ekranındaki temizlik için ayrı, kısa ömürlü bir yardımcı izin ister.
- Hesap ve telemetri yok. Tek ağ bağlantısı bu deponun sürümlerine yapılan güncelleme denetimi; Ayarlar'dan kapatılabilir.

C#, WinUI 3, Windows.UI.Composition ve Win2D ile yazıldı.
