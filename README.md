# KasaPOS (çalışma adı)

Yeme-içme işletmeleri için internetsiz çalışan Windows kasa (POS) programı.

## Özellikler (v0.1)
- İlk kurulum sihirbazı, isteğe bağlı örnek döner menüsü
- PIN ile giriş, yönetici / kasiyer yetkileri
- Satış: Masa, Al Götür, Paket (her biri için ayrı fiyat)
- Açık siparişler (masada bekleyen, kapıda ödemeli paket)
- Ürün notu, sipariş notu, tutar veya yüzde indirim
- Bölünmüş ödeme (nakit + kart + yemek kartları), para üstü
- Paket müşteri kaydı (telefonla bulma), KVKK için müşteri silme
- Fiş ve mutfak fişi (Windows'a kurulu her yazıcı, 80/58 mm)
- Giderler, gün sonu raporu (ödeme türü, sipariş türü, ürün bazında)
- İş günü kapanış saati (ör. 06:00)
- Günlük otomatik yedek (son 30)

## Veri
`%LOCALAPPDATA%\KasaPOS\kasa.db` (SQLite). Yedekler aynı klasörde `Yedekler`.

## Derleme
`main` dalına her gönderimde GitHub Actions, Windows'ta tek dosyalık `KasaPOS.exe` üretir ve **Releases** altında yayınlar.

Ürün adı `src/KasaPos/AppInfo.cs` ve `KasaPos.csproj` içinden değişir.
