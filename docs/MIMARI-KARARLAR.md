# Mimari Kararlar

Her karar şu biçimde yazılır: bağlam, seçenekler, karar, takas. Kararlar alındıkları andaki bilgiyle
yazıldı ve sonradan yeniden yazılmadı; değişen bir şey olursa kararın altına **Sonraki durum** notu eklendi.

## Özet

| # | Konu | Karar | Neden | Nasıl doğrulandı |
|---|------|-------|-------|------------------|
| 1 | Kod organizasyonu | Vertical slice + ayrı `Domain/` | Transfer gibi akışların transaction/kilit kararı tek dosyada okunsun | Domain birim testleri |
| 2 | Migration uygulama | Yerelde açılışta (bayrakla); Docker'da bundle (karar 9) | Aspire şifreyi/portu kendisi üretiyor | Testler boş DB'ye uygular; "unutulmuş migration" testi |
| 3 | Ledger | Çift taraflı kayıt + bakiye kolonu, sistem cüzdanları, `decimal` | Her kuruşun izi; okuma O(1) | Her testten sonra defter denetimi (toplam 0) |
| 4 | Eşzamanlılık | `SELECT ... FOR UPDATE`, id sırasıyla | Kilitsiz kodda 50 × 1 TL yatırma 4 TL bırakıyordu | 3 eşzamanlılık testi; `FOR UPDATE` silinince üçü de kırmızı |
| 5 | Idempotency | `Idempotency-Key`, kayıt PostgreSQL'de, para hareketiyle aynı transaction | Redis DB transaction'ına katılamaz | Aynı anahtarla 20 eşzamanlı istek → tek para hareketi |
| 6 | Mesajlaşma | Elle Transactional Outbox + RabbitMQ.Client, inbox, DLQ | İkili yazma (dual write) olay kaybettirir; MassTransit 9 ticari | RabbitMQ kapalıyken API 201 + olay bekliyor; tekrar mesaj tek bildirim |
| 7 | Dağıtık iz | Outbox'ta `traceparent` (ebeveyn), kök Client span'leri örnekleme dışı | Transfer iki ayrı iz görünüyordu; yoklama gürültüsü | Dashboard'da tek iz: 4 kaynak, 16 span |
| 8 | Test | xUnit v3 + Testcontainers (gerçek PostgreSQL/RabbitMQ) | SQLite `FOR UPDATE`/`SKIP LOCKED`/CHECK'i taklit edemez | 74 test, CI'da yeşil |
| 9 | Dağıtım | Dockerfile + EF bundle + Compose + GitHub Actions | Aspire'sız tek komut, her push'ta sınama | CI'da compose duman testi |

## Plandan sapmalar
- **Redis kullanılmadı** (karar 5). Planda idempotency için düşünülmüştü; ama Redis'e yazma, para hareketinin
  DB transaction'ına katılamaz. Doğruluk PostgreSQL'le sağlandı; Redis'in gerçekten fayda sağlayacağı bir
  yer (önbellek, hız sınırlama) bu kapsamda çıkmadı.
- **MassTransit kullanılmadı** (karar 6). 9.x ticari lisansa geçti; 8.x yeni geliştirme almıyor. Outbox,
  inbox ve dead-letter elle yazıldı.
- **Aspire.Hosting.Testing kullanılmadı** (karar 8). Eşzamanlılık testleri için ağır; Windows'ta dcp
  engellenebiliyor.

## Açık kalanlar (bilerek kapsam dışı)
- Kimlik doğrulama/yetkilendirme yok; id'yi bilen cüzdanı görür.
- Sistem cüzdanı hot row: aynı para birimindeki yatırmalar sıraya girer (50 paralel yatırmada istek başına
  0,8-1 sn ölçüldü). `lock_timeout` yok.
- `idempotency_keys`, `outbox_messages`, `processed_messages` temizlenmiyor.
- Ledger kayıtlarının değişmezliği DB tetikleyicisiyle değil, kodla korunuyor.
- Telemetri kalıcı bir arka uca (Jaeger/Tempo) gitmiyor; dead-letter kuyruğu için yeniden işleme aracı yok.

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

**Sonraki durum:** Aşama 4'te `DigitalWallet.Contracts` eklendi (yalnız olay kayıtları ve RabbitMQ adları).
Bildirim servisi aynı slice + `Data/` + `Messaging/` düzenini izledi. Mimari test (NetArchTest) eklenmedi.

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

**Sonraki durum:** Docker yolu karar 9 ile kuruldu (`migrator` imajı, iki EF bundle; servislerde bayrak
kapalı). Entegrasyon testleri de bayrakla boş veritabanına tüm migration'ları uygular.

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

**Sonraki durum:** Lost update Aşama 3'te gerçekten gösterildi ve karar 4 ile çözüldü. Bakiye kolonu +
ledger kararının faydası da o sırada görüldü: kilitsiz denemede kolonlar bozuldu ama ledger kayıtları
(INSERT) bozulmadığı için denetim sorgusu farkı tam olarak gösterdi. Hot row karar 7'deki izlerle ölçüldü.

## 4. Eşzamanlılık: satır kilidi (`SELECT ... FOR UPDATE`)

**Tarih:** 2026-11-17 (Aşama 3)

### Bağlam
Aşama 2'de bakiye "oku → bellekte değiştir → yaz" ile güncelleniyordu. Gerçekten paralel isteklerle
(tek `curl --parallel` süreci) denendi: 50 × 1 TL yatırma sonunda bakiye 50 yerine 4 TL; 100 TL'lik
cüzdandan 20 × 10 TL transferin 20'si de başarılı oldu (defterde −100 TL). Hepsi 201 döndü. Ledger
kayıtları INSERT olduğu için kaybolmadı; denetim sorgusu tutarsızlığı hemen gösterdi. CHECK kısıtı
yakalamadı çünkü kolona hiç eksi değer yazılmadı.

### Seçenekler
1. **Pessimistic:** transaction içinde cüzdan satırlarını `FOR UPDATE` ile kilitlemek. Aynı cüzdana gelen
   ikinci istek bekler, sonra güncel bakiyeyi görür.
2. **Optimistic:** PostgreSQL `xmin` kolonunu versiyon olarak kullanmak; çakışmada
   `DbUpdateConcurrencyException`, yeniden deneme veya 409.
3. **Atomik SQL:** `UPDATE ... SET balance = balance - @x WHERE id = @id AND balance >= @x`.

### Karar
Seçenek 1. Para yatırma ve transfer şu adımlarla çalışır:
- EF'in yeniden deneme stratejisi (`CreateExecutionStrategy`) içinde açık transaction.
- İlgili cüzdanlar tek sorguda `ORDER BY id FOR UPDATE` ile kilitlenir. Kilitler hep id sırasıyla
  alındığı için A→B ve B→A transferleri birbirini sonsuza kadar beklemez (deadlock olmaz).
- Domain kuralları kilitli, güncel satırlar üzerinde çalışır; `SaveChanges` + `Commit`.

### Takas
- Aynı cüzdana gelen istekler sıraya girer. Bir cüzdanda çakışma sık beklendiği için "bekle, doğru sonucu
  ver" yaklaşımı, "hata ver, istemci yeniden denesin" yaklaşımından uygun.
- Para birimi başına tek sistem cüzdanı olduğu için **tüm para yatırmalar aynı satırı kilitler** (hot row):
  aynı para biriminde para yatırma işlemleri sırayla işlenir. Bu ölçekte kabul edildi. Gerekirse sistem
  cüzdanının bakiyesi kolonda tutulmayıp ledger'dan hesaplanır veya birden fazla sistem alt hesabı kullanılır.
- Kilit bekleme süresi sınırı (`lock_timeout`) konmadı; bekleme komut zaman aşımına (30 sn) kadar sürebilir.
- Optimistic yöntemde olduğu gibi yeniden deneme mantığı gerekmez; ama transaction süresince bağlantı tutulur.

## 5. Idempotency: `Idempotency-Key` başlığı, kayıt PostgreSQL'de

**Tarih:** 2026-11-17 (Aşama 3)

### Bağlam
İstemci yanıtı alamazsa (ağ kopması, zaman aşımı) aynı para isteğini yeniden gönderir; para iki kez
hareket etmemeli. Kilit bunu çözmez: iki istek de geçerli, ikisi de sırayla işlenir.

### Seçenekler
1. **PostgreSQL:** anahtar kaydı para hareketiyle aynı DB transaction'ında yazılır.
2. **Redis:** hızlı, TTL hazır; ama DB transaction'ına katılamaz. Para yazılıp Redis yazılmadan çökme
   olursa tekrar gelen istek parayı ikinci kez hareket ettirir.
3. **PostgreSQL + Redis:** PostgreSQL asıl kaynak; Redis işlenmekte olan anahtarları ve yanıtları önbellekler.

### Karar
Seçenek 1. Redis, doğruluğa katkısı olmadığı için burada kullanılmadı; gerçekten işe yarayacağı bir
yerde (ör. hız sınırlama, önbellek) sonra değerlendirilecek.
- `POST /wallets/{id}/deposits` ve `POST /transfers` için `Idempotency-Key` başlığı **zorunlu**
  (yoksa 400). En fazla 100 karakter.
- Tablo `idempotency_keys`: birincil anahtar `(scope, key)`; isteğin SHA-256 özeti; oluşan
  `transaction_id`. Yanıt gövdesi saklanmaz; tekrar gelen istekte yanıt değişmeyen ledger işleminden
  yeniden üretilir (`Idempotent-Replayed: true` başlığıyla, aynı 201).
- Akış (tek transaction): önce anahtar satırı INSERT edilir → cüzdanlar kilitlenir → domain → kayıt
  işleme bağlanır → commit. Aynı anahtarla eşzamanlı ikinci istek unique index'te birincinin bitmesini
  bekler, sonra benzersizlik hatası (23505) alır ve ilk sonucu döner.
- Aynı anahtar farklı gövdeyle gelirse 422 (`idempotency_key_reused`).
- Yalnızca başarılı sonuç saklanır: iş kuralı hatası (422) transaction'ı geri aldığı için anahtar da
  kaydedilmez; aynı anahtarla yeniden denenebilir.

### Takas
- Anahtarlar silinmiyor (süre sınırı/temizlik işi yok); tablo büyür. İleride `created_at`'e göre temizlik.
- Anahtar kapsamı uç nokta bazında (`scope`); kullanıcı bazında değil (kimlik doğrulama kapsam dışı).
- Başarısız sonuçları saklamamak, "ilk denemede 422, sonra aynı anahtarla 201" durumuna izin verir; Stripe
  gibi hatayı da saklayan sistemlerden farklı ama bu kapsamda daha anlaşılır.

## 6. Mesajlaşma: elle yazılmış Transactional Outbox + RabbitMQ, bildirim servisi

**Tarih:** 2026-11-17 (Aşama 4)

### Bağlam
Para hareketinden sonra başka işler (bildirim, izleme) yapılmalı ama API'yi yavaşlatmamalı ve onlar
çökünce transfer başarısız olmamalı. API olay yayınlar, ikinci servis RabbitMQ'dan alır.
"Önce DB commit, sonra publish" ikili yazmadır (dual write): ikisi atomik değil; commit sonrası publish
başarısız olursa olay kaybolur, önce publish edilip commit başarısız olursa olmamış bir olay yayınlanır.

### Seçenekler
1. **MassTransit 9:** hazır outbox/inbox; ama 9.x ticari lisanslı (NuGet `licenseUrl`: massient.com/license).
2. **MassTransit 8.5 (Apache-2.0):** hazır EF Core outbox/inbox, ilanlarda sık geçer; ama ana geliştirme 9'a
   geçti, 8'in bakım süresi sınırlı; mekanizma kütüphanede gizli kalır.
3. **Wolverine (MIT):** yerleşik outbox; Türkiye ilanlarında nadir.
4. **Elle outbox + `RabbitMQ.Client` (Aspire bileşeni):** daha çok kod, lisans riski yok, mekanizma açık.

### Karar
Seçenek 4. İkinci servis: **bildirim servisi** (`DigitalWallet.Notifications`), kendi veritabanı
`notificationsdb` (aynı PostgreSQL sunucusu, ayrı veritabanı; servisler veri paylaşmaz).
- **Sözleşmeler** `DigitalWallet.Contracts`: `DepositCompleted`, `TransferCompleted`. RabbitMQ'da topic
  exchange `wallet.events`, routing key'ler `deposit.completed`, `transfer.completed`.
- **Outbox (API):** olay, para hareketiyle aynı DB transaction'ında `outbox_messages`'a yazılır.
- **Yayıncı (API, BackgroundService):** `published_at IS NULL` mesajları `ORDER BY id LIMIT n FOR UPDATE
  SKIP LOCKED` ile alır (birden çok kopya aynı mesajı almaz), kalıcı (persistent) ve `mandatory` olarak
  gönderir, **publisher confirm** bekler, sonra `published_at` işaretler. RabbitMQ yoksa veya mesaj hiçbir
  kuyruğa yönlenemiyorsa mesaj outbox'ta kalır, `attempts`/`last_error` güncellenir, sonra yeniden denenir.
- **Teslim garantisi:** en az bir kez (at-least-once). Yayıncı publish ile işaretleme arasında çökerse mesaj
  tekrar gider.
- **Tüketici (bildirim servisi):** manuel ack; mesaj kimliği (`message_id` = outbox id) `processed_messages`
  tablosuna bildirimle aynı transaction'da yazılır (inbox). Aynı mesaj tekrar gelirse atlanır ve ack'lenir.
  Çözümlenemeyen veya ikinci denemede de hata veren mesaj dead-letter kuyruğuna gider.

### Takas
- Kod ve parça sayısı arttı (outbox tablosu, yayıncı, inbox, DLQ).
- Olay gecikmesi yayıncının yoklama aralığı kadar (≈1 sn). PostgreSQL `LISTEN/NOTIFY` ile azaltılabilir.
- Sıra: tek yayıncıda id (Guid v7, zaman sıralı) sırasıyla gider; birden çok yayıncıda ve yeniden denemede
  global sıra garanti değil. Bildirimler sıraya bağımlı değil.
- Yayıncı, RabbitMQ'ya gönderirken DB transaction'ını (ve satır kilitlerini) açık tutar; parti küçük tutuldu.
- Outbox ve inbox tabloları temizlenmiyor (Aşama 3'teki idempotency tablosu gibi).

## 7. Dağıtık iz: outbox'ta trace bağlamı (ebeveyn), arka plan gürültüsünü örnekleme dışı bırakma

**Tarih:** 2026-11-17 (Aşama 5)

### Bağlam
ServiceDefaults OpenTelemetry'yi kuruyor; Aspire'ın Npgsql ve RabbitMQ bileşenleri span üretiyor ve
RabbitMQ.Client 7 `traceparent`'ı mesaj başlığında yayıncıdan tüketiciye kendiliğinden taşıyor. Ama bir
transfer dashboard'da **iki ayrı iz** olarak görünüyordu: `POST /transfers` (HTTP + DB) commit'te bitiyor;
outbox yayıncısı arka planda, ortamda iz yokken çalıştığı için `publish transfer.completed` yeni bir iz
başlatıyordu. Ayrıca yayıncının saniyede bir yoklama sorgusu her seferinde kök iz üretiyordu
(birkaç dakikada 640+ iz).

### Seçenekler
1. **Ebeveyn:** olay outbox'a yazılırken o anki `traceparent`/`tracestate` kolona kaydedilir; yayıncı mesaj
   başına bu bağlamdan çocuk span başlatır. Tek iz: istek → DB → outbox yayını → RabbitMQ → bildirim → DB.
2. **Bağlantı (link):** yayın kendi izinde kalır, orijinal ize link verir (OpenTelemetry mesajlaşma
   kurallarının toplu/asenkron işler için tercih ettiği yol); dashboard'da iki iz.

### Karar
Seçenek 1. Ek olarak:
- `RootClientSpanFilteringSampler` (ServiceDefaults): ebeveyni olmayan `Client` türü span'ler (arka plandaki
  sahipsiz DB sorguları) kaydedilmez; diğerleri `ParentBased(AlwaysOn)` ile aynı.
- Özel span/etiketler (`ActivitySource` adı = uygulama adı, ServiceDefaults zaten kayıtlı):
  `wallet.lock` (satır kilidi bekleme süresi), istek span'inde `idempotency.outcome` ve
  `ledger.transaction.id`, yayıncıda `outbox.publish <Type>` (bekleme süresi `outbox.delay_ms`),
  tüketicide `inbox.duplicate`.
- Etiketlere kişisel veri (sahip adı) konmaz; kimlikler ve tutar yeterli.

### Takas
- İzin toplam süresi outbox bekleme süresini (≈1 sn yoklama) içerir; "istek süresi" için kök HTTP span'ine
  bakılmalı.
- Yayıncının parti sorguları (SELECT ... FOR UPDATE SKIP LOCKED, işaretleme UPDATE'i) kök Client span
  olduğu için artık görünmez; yayıncı sorunları loglardan izlenir.
- Örnekleyici kuralı tüm projeler için geçerli: ileride arka planda yapılan ve izlenmesi istenen bir
  istemci çağrısı varsa önce bir ebeveyn span açılmalı.

## 8. Test stratejisi: birim + gerçek PostgreSQL/RabbitMQ ile entegrasyon (Testcontainers)

**Tarih:** 2026-11-18 (Aşama 6)

### Bağlam
Aşama 3-5'teki davranışlar (lost update, deadlock, eşzamanlı idempotency, outbox kesintisi, inbox
tekrarı) yalnızca elle (`curl --parallel`, dashboard) doğrulandı. Bunlar PostgreSQL'e özgü özelliklere
dayanıyor: `FOR UPDATE`, `SKIP LOCKED`, CHECK kısıtları, `jsonb`, benzersizlik hatası kodları.
Windows'ta `dotnet test` engelleniyor; testler Linux container'ında ve CI'da çalışmalı.

### Seçenekler
1. **SQLite / EF InMemory:** hızlı; ama yukarıdaki özellikleri desteklemez veya farklı davranır →
   test geçer, üretim bozulur.
2. **WebApplicationFactory + Testcontainers:** API bellekte, PostgreSQL ve RabbitMQ gerçek container.
3. **Aspire.Hosting.Testing:** tüm AppHost test içinde; gerçek uçtan uca ama yavaş, dcp gerektirir
   (Windows'ta engellenebiliyor), eşzamanlılık testleri için ağır.

### Karar
Seçenek 2. Araçlar: xUnit v3 (Apache-2.0), Testcontainers (MIT), `Microsoft.AspNetCore.Mvc.Testing`.
FluentAssertions 8 ticari lisansa geçtiği için kullanılmadı; xUnit `Assert` yeterli.
- `tests/DigitalWallet.UnitTests`: domain ve saf yardımcılar, DB yok.
- `tests/DigitalWallet.Api.IntegrationTests`: koleksiyon başına bir PostgreSQL ve bir RabbitMQ container'ı;
  her test kendi cüzdanlarını açar (testler birbirinin verisine dokunmaz, DB sıfırlanmaz). Eşzamanlılık
  testleri gerçekten paralel istek atar (`Task.WhenAll`).
- `tests/DigitalWallet.Notifications.IntegrationTests`: işlemci + gerçek PostgreSQL; tüketici + gerçek
  RabbitMQ (tekrar ve bozuk mesaj).
- Uçtan uca: `docker compose` ile duman testi (CI).

### Takas
- Entegrasyon testleri container başlatma süresi yüzünden yavaş (koleksiyon başına birkaç saniye);
  CI'da kabul edilebilir.
- Test için üretim koduna küçük bir ayar eklendi: `Outbox:PublisherEnabled` (varsayılan `true`); outbox'ın
  "bekliyor" durumu yayıncı kapalıyken belirlenimci (deterministic) test edilebilsin diye.
- Testler sıfırdan bir veritabanına migration uygular; "unutulmuş migration" testi model ile snapshot'ı
  karşılaştırır (`HasPendingModelChanges`).

## 9. Dağıtım: Dockerfile'lar, EF migration bundle, Docker Compose, GitHub Actions

**Tarih:** 2026-11-18 (Aşama 6)

### Bağlam
Karar 2'de migration'ların yalnızca yerelde (Aspire) açılışta uygulandığı, Docker yolunun ayrı bir adım
olacağı yazılmıştı. Proje Aspire olmadan da tek komutla ayağa kalkabilmeli ve her push CI'da sınanmalı.

### Karar
- Servis başına çok aşamalı Dockerfile: `sdk:10.0` ile publish, `aspnet:10.0` ile root olmayan
  kullanıcıyla (`app`) çalıştırma.
- Migration'lar için **EF migration bundle** (`dotnet ef migrations bundle`): ayrı `migrator` imajı,
  compose'da bir kez çalışır; servisler `service_completed_successfully` koşuluyla bekler. Uygulamaların
  açılışta migration bayrağı compose'da kapalı.
- Compose: `postgres`, `rabbitmq`, `migrator`, `api`, `notifications`, `aspire-dashboard` (OTLP alıcı ve iz
  arayüzü). Şifreler `.env`'den (repoda yalnız `.env.example`).
- GitHub Actions: (1) derleme + birim + entegrasyon testleri, (2) compose ile duman testi (CI'da rastgele
  şifreler üretilir).

### Takas
- Compose'daki Aspire dashboard kimlik doğrulamasız (yalnız yerel geliştirme için); üretim için değil.
- Bundle imajı her migration değişikliğinde yeniden derlenir.
- ServiceDefaults `/health` uç noktalarını yalnız Development'ta açtığı için compose'da servis
  sağlık kontrolü yerine duman testi kullanılır.
