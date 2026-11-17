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

## 2. Migration'ların uygulanması: yerelde açılışta, bayrakla

**Tarih:** 2026-11-17 (Aşama 1)

### Bağlam
PostgreSQL'i yerelde Aspire başlatıyor. Aspire şifreyi kendisi üretir (AppHost user-secrets'ında
saklar) ve container portunu atar. Bu yüzden elle `dotnet ef database update` için bağlantı dizesini
her seferinde dashboard'dan almak gerekir.

### Seçenekler
1. **Elle `dotnet ef database update`.** Açık ve kontrollü; ama Aspire ile her seferinde bağlantı
   dizesi kopyalamak gerekir.
2. **API açılırken `Database.MigrateAsync()`.** Pratik. Ama birden fazla API kopyası aynı anda
   migration'a girişebilir ve uygulamanın DB kullanıcısı şema değiştirme yetkisine ihtiyaç duyar.
3. **Ayrı migration worker projesi** (Aspire'ın önerdiği desen). Temiz; ama bir proje daha ekler.

### Karar
Seçenek 2, ama `Database:ApplyMigrationsOnStartup` bayrağıyla. Bayrağın varsayılanı `false`; yalnızca
AppHost (yerel geliştirme) `true` verir. Docker/üretim yolu Aşama 6'da EF migration bundle ile ayrı bir
adım olacak (seçenek 3'e yakın).

### Takas
- Yerelde tek API kopyası çalıştığı için yarış sorunu pratikte yok. Ölçeklenen bir ortamda bayrak
  açılmamalı.
- Elle uygulama yolu da açık kalıyor: `ConnectionStrings__walletdb` ortam değişkeniyle
  `dotnet ef database update` (bkz. `DesignTimeDbContextFactory`).

## 3. Ledger tasarımı: çift taraflı kayıt, bakiye kolonu, sistem cüzdanları

**Tarih:** 2026-11-17 (Aşama 2)

### Bağlam
Para yatırma ve transfer eklenince "bu bakiye neden bu?" sorusunun cevaplanabilmesi, para yoktan var
olmaması ve yarım kalan işlem olmaması gerekiyor. Her hareket kayda geçmeli, kayıtlar değişmemeli.

### Seçenekler ve kararlar
**a) Bakiye nerede?**
1. Yalnızca ledger: bakiye = `SUM(amount)`. Tutarsızlık imkansız; ama kayıt arttıkça okuma yavaşlar ve
   "eksiye düşmesin" kontrolü için yine kilitlenecek bir satır gerekir.
2. **Seçilen:** `wallets.balance` kolonu + ledger, aynı DB transaction'ında. Okuma O(1); Aşama 3'te kilit
   veya versiyon bu satıra konur. Ledger asıl kaynaktır; kolon onun özetidir ve SQL ile denetlenir.

**b) Tutar tipi?**
1. `long` en küçük birim (kuruş): yuvarlama yok; ama her sınırda dönüşüm, okuması zor.
2. **Seçilen:** `decimal` (`numeric(19,4)`), kural: tutar > 0, en fazla 2 ondalık, üst sınır var.
   API yanıtlarında tutarlar hep 2 ondalıkla döner (`Money.Normalize`).

**c) Para yatırmanın karşı tarafı?**
1. Ayrı `accounts` tablosu (muhasebe tarzı): genel, ama bu kapsam için fazla tablo.
2. **Seçilen:** `wallets.type` (`Customer`/`System`). Her para birimi için bir sistem cüzdanı migration ile
   eklenir ("dış dünya"). Sistem cüzdanı eksiye düşebilir; eksi bakiyesi müşterilere olan toplam borçtur.

**Ortak kurallar**
- Her işlem (`ledger_transactions`) en az iki kayıt (`ledger_entries`) içerir; kayıtların toplamı 0.
  Bu kural `LedgerTransaction` içinde kontrol edilir. Kayıtlar güncellenmez/silinmez.
- Her kayıtta `balance_after` tutulur (ekstre ve satır bazında denetim için).
- İşlem kayıtları ve bakiye güncellemeleri tek `SaveChanges` ile yazılır; EF Core bunu tek DB transaction'ı
  içinde yapar.
- Veritabanı da korur: müşteri cüzdanında `balance >= 0` (CHECK), kayıt tutarı `<> 0`, para birimi başına
  tek sistem cüzdanı (filtreli unique index).
- İş kuralı ihlali (yetersiz bakiye, para birimi uyuşmazlığı...) `DomainException` → `422` ProblemDetails
  (`code` alanıyla). Girdi hatası `400`, olmayan cüzdan `404`.

### Takas
- Bakiye iki yerde (kolon + ledger) tutuluyor; tutarlılık kodla sağlanıyor, SQL denetim sorgusuyla
  doğrulanıyor (`SUM(ledger_entries.amount)` cüzdan bakiyesine eşit, tüm kayıtların toplamı 0).
- **Eşzamanlılık bu aşamada çözülmedi:** bakiye "oku, değiştir, yaz" ile güncelleniyor. Aynı cüzdana
  paralel istekte "lost update" olabilir. CHECK kısıtı müşteri bakiyesinin eksiye düşmesini engeller ama
  kaybolan güncellemeyi engellemez. Aşama 3'ün konusu.
- Her para yatırma aynı sistem cüzdanı satırını günceller; Aşama 3'te kilit eklenince bu satır darboğaz
  olabilir (hot row). Gerekirse sistem cüzdanının bakiyesi kolonda tutulmayıp ledger'dan hesaplanabilir.
- Kayıtların değiştirilemezliği şimdilik yalnızca kodla korunuyor; DB tetikleyicisi (UPDATE/DELETE
  reddi) ileride eklenebilir.
- İş kuralı hatalarını istisna ile taşımak basit ama "kontrol akışı için istisna" sayılır; hata sayısı
  az ve beklenmedik olduğu için kabul edildi.
