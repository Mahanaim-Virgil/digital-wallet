# Mimari Kararlar

Her karar şu biçimde yazılır: bağlam, seçenekler, karar, takas.

## 1. Kod organizasyonu: vertical slice + ayrı Domain klasörü

**Tarih:** 2026-11-17 (Aşama 1)

### Bağlam
Proje bir dijital cüzdan / ödeme sistemi. Asıl zorluklar CRUD değil: çift taraflı kayıt (ledger),
transfer sırasında eşzamanlılık (kilit ya da optimistic concurrency), idempotency ve outbox.
Bu konuların her biri özelliğe özgü transaction ve kilit kararları gerektirir. Geliştiricinin önceki
projesi (`crypto-fx-tracker`) katmanlı / Clean Architecture benzeri bir yapıdaydı.

### Seçenekler
1. **Clean Architecture** (Domain / Application / Infrastructure / Api projeleri).
   - Artı: Katman sınırını derleyici zorlar (Domain EF Core'a referans veremez). Kurumsal ilanlarda
     yaygın ve tanıdık.
   - Eksi: Bir özellik dört projeye dağılır. Repository arayüzleri çoğu zaman EF Core'u yalnızca sarar.
     Transfer gibi transaction ve kilit içeren bir akış, birkaç katmana bölündüğünde okunması zorlaşır.
2. **Saf vertical slice** (her özellik tek dosya/klasör, ortak domain modeli yok).
   - Artı: En az dosya ve tören.
   - Eksi: "Bakiye eksiye düşemez" gibi kurallar birden fazla slice'ta tekrar edilip dağılabilir.
3. **Vertical slice + ayrı `Domain` klasörü** (tek API projesi: `Features/`, `Domain/`,
   `Infrastructure/`).
   - Artı: Bir özelliğin endpoint'i, isteği, doğrulaması ve veri erişimi tek yerde durur. Transaction ve
     kilit stratejisi slice içinde açıkça görünür. Para ve bakiye kuralları entity'lerde toplanır.
     Minimal API ile doğal uyuşur. Önceki projeden farklı bir yaklaşımı gösterir.
   - Eksi: Katman sınırını derleyici değil disiplin korur. Slice'lar arasında küçük tekrarlar olabilir.

### Karar
Seçenek 3. Yapı:

```
src/DigitalWallet.Api/
  Features/<Özellik>/<İşlem>.cs   // endpoint eşlemesi + istek/yanıt + iş akışı
  Domain/                         // entity'ler, value object'ler, iş kuralları (EF Core'a bağımlı değil)
  Infrastructure/                 // DbContext, EF eşlemeleri, migration'lar
src/DigitalWallet.AppHost/        // .NET Aspire: PostgreSQL ve servisleri ayağa kaldırır
src/DigitalWallet.ServiceDefaults/ // OpenTelemetry, health check, service discovery ortak ayarları
```

- MediatR kullanılmıyor. Minimal API endpoint'i doğrudan handler görevi görüyor. Pipeline davranışı
  (loglama, doğrulama) gerekirse endpoint filtreleriyle yapılır.
- Slice'lar `DbContext`'i doğrudan kullanır. Repository soyutlaması yok.
- Kural: `Domain/` klasörü `Microsoft.EntityFrameworkCore` veya ASP.NET Core türlerini kullanmaz.

### Takas
- Derleyici katman ihlalini yakalamaz. Gerekirse ileride bir mimari testi (ör. NetArchTest)
  eklenebilir ya da `Domain` ayrı projeye taşınabilir.
- Slice'lar `DbContext`'e doğrudan bağlı olduğu için birim testleri DB'siz yazmak zorlaşır. Bu nedenle
  testler ağırlıkla Testcontainers ile gerçek PostgreSQL üzerinde (Aşama 6) yapılacak. Domain kuralları
  ise DB'siz birim testiyle sınanır.
- Aşama 4'te ikinci bir servis eklenince ortak kod (mesaj sözleşmeleri) için küçük bir paylaşılan
  proje gerekebilir.
