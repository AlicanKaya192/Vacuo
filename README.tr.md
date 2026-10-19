# Vacuo

Windows'ta yer aç, dürüstçe.

Vacuo, sistem tepsisinde duran küçük bir Windows 11 bakım aracı. Sağ alttaki simgesine tıklayınca açılan panel diskini, belleğini ve dağınık klasörlerini tek yerde gösterir:

- **Disk:** geliştirici ve yapay zekâ önbellekleri (pip, uv, conda, Hugging Face, PyTorch, npm, Rust `target/`, eski `.venv` ve `node_modules`) ve bunları yeniden indirmenin bedeli, kendi büyük dosyaların, aynı dosyalar ve kalması gerekenler.
- **Bellek:** "% kullanılan" yerine bellek baskısı, saatlerdir boşta duran uygulamalar ve onları kapattıktan sonraki gerçek fark.
- **Düzen:** Masaüstü, İndirilenler ya da istediğin klasör için kurallar; bir şey taşınmadan önce önizleme, tek tıkla geri alma.
- **Kaldır:** her programın kendi kaldırıcısı, ardından geride kalanların listesi.
- **Günlük:** yapılan her işlem ve nasıl geri alınacağı.

[English](README.md)

## Güvenli tasarım

- Senin seçimin olmadan hiçbir şey yapılmaz. Otomatik Bakım varsayılan olarak kapalı.
- Hiçbir şey kalıcı olarak silinmez: dosyalar Geri Dönüşüm Kutusu'na gider, Vacuo onu boşaltmaz.
- "RAM temizleme" ve zorla kapatma yok. Kapatma normal bir kapatma isteği gönderir; kaydedilmemiş işi olan uygulama önce sana sorabilir.
- Kayıt defteri temizliği, servis, sürücü ya da sistem ayarı yok. Vacuo yönetici izni istemez.
- Ağ erişimi ve telemetri yok.

## Durum

Geliştirmenin başında. Henüz kullanıma hazır değil.

C#, WinUI 3, Windows.UI.Composition ve Win2D ile yazılıyor.
