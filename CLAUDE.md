# CLAUDE.md

## Proje
Dijital cüzdan / ödeme sistemi backend'i (portföy projesi). Kullanıcı cüzdan açar, para yatırır,
cüzdanlar arası transfer yapar. Amaç ürünün kendisinden çok mülakatta sorulan konuları doğru
çözmek: çift taraflı kayıt (ledger), eşzamanlılık, idempotency, outbox, dağıtık izleme.

Stack: .NET 10, Minimal API, PostgreSQL, EF Core + Npgsql, .NET Aspire (AppHost + ServiceDefaults),
Redis, RabbitMQ + MassTransit, OpenTelemetry. Mimari: `docs/MIMARI-KARARLAR.md`'ye bakın.

## Aşamalar
1. İskelet: Aspire, PostgreSQL, cüzdan oluşturma (`POST /wallets`) ve bakiye (`GET /wallets/{id}`).
   Bitti: derleniyor, API Aspire ile (olmazsa Docker'daki PostgreSQL'e karşı) açılıyor, ilk migration
   uygulandı, iki endpoint elle denendi, mimari karar belgelendi.
2. Ledger: para yatırma, transfer, çift taraflı kayıt, tek DB transaction.
   Bitti: her işlemin satırları toplamı sıfır, yetersiz bakiye reddediliyor.
3. Eşzamanlılık ve idempotency: satır kilidi veya optimistic concurrency, `Idempotency-Key` + Redis.
   Bitti: paralel transferde bakiye eksiye düşmüyor, tekrarlanan istek para hareket ettirmiyor.
4. Mesajlaşma: ayrı bildirim veya fraud servisi, RabbitMQ + MassTransit + Transactional Outbox.
   Bitti: DB'ye yazılan her transferin olayı kaybolmadan ikinci servise ulaşıyor.
5. OpenTelemetry: bir transferin izi API → DB → RabbitMQ → ikinci servis tek trace olarak görünüyor.
6. Test, CI, Docker: birim + Testcontainers (gerçek PostgreSQL, eşzamanlılık testi dahil),
   GitHub Actions yeşil, `docker compose up` ile ayağa kalkıyor.
7. Mimari kararlar belgesi ve README tamam.

- Durum: Aşama 1-5 tamam (`stage-1` ... `stage-5`), `main`'de. Sıradaki: Aşama 6 (test, CI, Docker).
- Aşama 4'te MassTransit yerine elle outbox + `RabbitMQ.Client` seçildi (MassTransit 9 ticari lisanslı;
  karar 6).
- Her aşama bitince `stage-N` etiketi, aşamanın doğrulanmış son commit'ine konur.
- Kapsam dışı: gerçek banka/kart entegrasyonu, frontend, Kubernetes, 3'ten fazla servis,
  kur dönüşümü (cüzdan tek para birimli, transfer aynı para birimi arasında),
  gelişmiş kimlik doğrulama.

## Geliştirici ve çalışma şekli
Geliştirici 3. sınıf yazılım mühendisliği öğrencisi, hedefi Türkiye'de backend (C#/ASP.NET Core)
staj/iş. İlk portföy projesi `crypto-fx-tracker` (.NET 8, katmanlı mimari). Bu proje öğrene öğrene
ilerler: "çalışıyor" yetmez, neden öyle yapıldığı anlaşılmalı.
- Türkçe, sade ve dürüst anlat.
- Her aşamada önce kavramı anlat (neden gerekli, alternatifleri ne), sonra küçük adımlarla kod yaz.
- "Çalışıyor" deme: neyi çalıştırdığını, neyi çalıştıramadığını açıkça yaz.
- Geri alınamaz işlemlerden önce (migration uygulama, DELETE/UPDATE, push, force push)
  ne yapacağını göster ve onay iste.
- Mimari karar alırken seçenekleri ve takasları yaz; `docs/MIMARI-KARARLAR.md`'ye
  bağlam, seçenekler, karar, takas biçiminde ekle.

## Ortam (Windows 11)
- Kullanıcının terminali `cmd` (PowerShell değil). Satır devamı `^`.
- .NET SDK 10.0.403.
- Windows Akıllı Uygulama Denetimi imzasız exe'leri, bazen derlenmiş dll'leri de engelliyor
  (`0x800711C7`). Kapatmayı önerme, geri açılamaz. Derlenmiş dll engellenirse
  `dotnet build --no-incremental -p:Deterministic=false` ile yeniden derleyip tekrar dene
  (deterministik derleme aynı engelli dll'i üretir).
- Windows'ta `dotnet test` çalışmaz (test dll'leri engellenir). Testler GitHub Actions'ta veya
  `mcr.microsoft.com/dotnet/sdk:10.0` Linux container'ında çalıştırılır.
- Aspire'ın başlattığı dcp/dashboard exe'leri engellenebilir. Olursa açıkça raporla; yedek yol
  API'yi Docker'daki PostgreSQL'e karşı doğrudan çalıştırmak.
- Docker Desktop kurulu (`%LOCALAPPDATA%\Programs\DockerDesktop\resources\bin`). Container'lar
  Linux'ta çalıştığı için Akıllı Uygulama Denetimi onları etkilemez.

## Yapı
- `src/DigitalWallet.AppHost`: Aspire. PostgreSQL container'ı (`postgres`, volume
  `digital-wallet-postgres-data`), veritabanları `walletdb` ve `notificationsdb`; RabbitMQ (`messaging`,
  yönetim arayüzü dahil, volume `digital-wallet-rabbitmq-data`); `api` ve `notifications` projeleri.
  API RabbitMQ'yu bilerek beklemez (`WaitFor` yok; outbox).
- `src/DigitalWallet.Contracts`: servisler arası olaylar (`DepositCompleted`, `TransferCompleted`) ve
  RabbitMQ adları (`WalletEventTopology`). Yayınlanmış sözleşmeden alan silme/yeniden adlandırma yapma.
- `src/DigitalWallet.Notifications` (karar 6): RabbitMQ tüketicisi, kendi DB'si (`notifications`,
  `processed_messages` = inbox). `GET /wallets/{id}/notifications` (port 5110). Manuel ack; tekrar mesaj
  `message_id` ile atlanır; bozuk/bilinmeyen tür veya ikinci kez hata veren mesaj
  `notifications.wallet-events.dlq` kuyruğuna gider. Migration'lar `Data/Migrations`.
- Outbox (API, karar 6): olay para hareketiyle aynı transaction'da `outbox_messages`'a (slice'larda
  `LedgerEvents.ToOutboxMessage`). `OutboxPublisher` her 1 sn bekleyenleri `FOR UPDATE SKIP LOCKED` ile alır,
  `mandatory` + publisher confirm ile gönderir, `published_at_utc` işaretler; hata → `attempts`/`last_error`,
  5 sn bekleme. API'de RabbitMQ health check kapalı. Teslim en az bir kez.
- İzleme (karar 7): outbox satırı olayın `trace_parent`/`trace_state`'ini saklar; yayıncı mesaj başına
  `outbox publish <Type>` span'ini bu bağlamdan (ebeveyn) başlatır, RabbitMQ.Client bağlamı mesaj başlığıyla
  tüketiciye taşır → bir transfer dashboard'da tek iz (istek → DB → `wallet.lock` → outbox → RabbitMQ →
  bildirim → notificationsdb). `RootClientSpanFilteringSampler` (ServiceDefaults) ebeveynsiz Client span'leri
  (arka plan yoklama sorguları) kaydetmez. Özel span/etiketler: `wallet.lock` (kilit bekleme),
  `idempotency.outcome`, `ledger.transaction.id/type`, `outbox.delay_ms`, `inbox.duplicate`.
  `ActivitySource` adı = uygulama adı (`ApiTelemetry.Source`). Etikete kişisel veri (sahip adı) koyma.
- Dashboard'da bir izi açmak: `http://localhost:15115/traces/detail/<trace_id>`; trace_id
  `outbox_messages.trace_parent`'ın ikinci parçası.
- `src/DigitalWallet.ServiceDefaults`: OpenTelemetry, health check (`/health`, `/alive`), service discovery.
- `src/DigitalWallet.Api`: `Features/<Özellik>/<İşlem>.cs` (endpoint + iş), `Domain/` (EF/ASP.NET'e
  bağımlı değil), `Infrastructure/` (`WalletDbContext`, migration'lar). Mimari: `docs/MIMARI-KARARLAR.md`.
- Endpoint'ler: `POST /wallets` (`{"ownerName","currency"}` → 201), `GET /wallets/{id}` (200/404),
  `POST /wallets/{id}/deposits` (`{"amount"}`), `POST /transfers` (`{"fromWalletId","toWalletId","amount"}`),
  `GET /transactions/{id}`, `GET /wallets/{id}/entries?limit=50` (en fazla 200).
  Hata kodları: girdi 400 (ValidationProblem), bulunamadı 404, iş kuralı 422 (`DomainException` →
  `DomainExceptionHandler`, yanıtta `code`: `insufficient_funds`, `currency_mismatch`, `same_wallet`,
  `system_wallet_not_allowed`, `invalid_amount`, `idempotency_key_reused`).
  Para birimleri `Domain/Currencies.cs`: TRY, USD, EUR.
- Eşzamanlılık (karar 4): para yatırma ve transfer `WalletLocking.InTransactionAsync` (EF yeniden deneme
  stratejisi içinde açık transaction) + `LockWalletsAsync` (`ORDER BY id FOR UPDATE`). Varlık kontrolü
  (404) kilitten önce. Domain kuralları yalnızca kilitli yüklenmiş cüzdanlarla çağrılmalı.
- Idempotency (karar 5): para yatırma ve transferde `Idempotency-Key` başlığı zorunlu (en fazla 100,
  boşluksuz ASCII). `IdempotentExecution.ExecuteIdempotentAsync`: anahtar satırı önce INSERT → kilit +
  domain → commit. Tekrar istek aynı 201 + `Idempotent-Replayed: true`; aynı anahtar farklı gövde → 422.
  Parmak izinde tutar 2 haneye normalleştirilir. Kapsam (`scope`) uç nokta bazında. Yalnız başarılı
  sonuç saklanır (422 sonrası aynı anahtarla yeniden denenebilir).
- Ledger (karar 3): `ledger_transactions` + `ledger_entries` (işaretli tutar, `balance_after`). Her işlemin
  kayıt toplamı 0; kayıtlar ve bakiyeler yalnızca `LedgerTransaction.Deposit/Transfer` ile oluşur/değişir.
  `wallets.balance` ledger'ın özetidir. Sistem cüzdanları (dış kaynak, eksiye düşebilir) sabit id'li:
  TRY `...0949`, USD `...0840`, EUR `...0978` (`Domain/SystemWallets.cs`). Tutar: > 0, en fazla 2 ondalık,
  en fazla 1.000.000; yanıtlarda hep 2 ondalık (`Money.Normalize`).
- DB kısıtları: müşteri bakiyesi `>= 0` (CHECK), kayıt tutarı `<> 0`, işlem tutarı `> 0`,
  para birimi başına tek sistem cüzdanı (filtreli unique index).

## Komutlar
- Derle: `dotnet build` (kök klasörde). Projelerde `UseAppHost=false`: imzasız `.exe` üretilmez,
  çünkü Akıllı Uygulama Denetimi `DigitalWallet.AppHost.exe` ve `DigitalWallet.Api.exe`'yi her
  seferinde engelliyordu. Bunu geri açma.
- Aspire ile çalıştır: `dotnet run --project src\DigitalWallet.AppHost --launch-profile http`.
  Dashboard `http://localhost:15115` (giriş linki konsolda), API `http://localhost:5043`,
  bildirim servisi `http://localhost:5110`. RabbitMQ yönetim arayüzü portu `docker port <messaging-xxxx> 15672`.
  Kaynaklar dashboard'dan durdurulup başlatılabilir (kesinti denemesi). Durdurulmuş bir projenin portuna
  `curl` Aspire proxy'si yüzünden asılı kalır; `-m 3` gibi zaman aşımı ver.
- Proje/migration ekledikten sonra Aspire'dan önce TÜM çözümü derle (`dotnet build --no-incremental
  -p:Deterministic=false`): yalnızca bir projeyi derlemek diğerinin eski dll'ini bırakır (Aşama 4'te
  API, migration'ı içermeyen dll ile açıldı ve tablo oluşmadı).
  Docker Desktop açık olmalı. İlk açılışta Aspire geliştirme sertifikasına güvenmek için Windows
  uyarısı çıkarabilir (`http` profili için şart değil).
- Dll engeli (`0x800711C7`) aralıklı: AppHost dll'i engellenirse
  `dotnet build src\DigitalWallet.AppHost --no-incremental -p:Deterministic=false` ve tekrar çalıştır.
  API dll'i engellenirse dashboard'da `api` "Finished" görünür: AppHost açıkken
  `dotnet build src\DigitalWallet.Api --no-incremental -p:Deterministic=false`, sonra dashboard'dan
  `api`'yi başlat. Hangi dosyanın engellendiğini görmek için (PowerShell):
  `Get-WinEvent -LogName 'Microsoft-Windows-CodeIntegrity/Operational' -MaxEvents 3`.
  `dotnet run` yerine `dotnet bin\Debug\net10.0\DigitalWallet.AppHost.dll` de olur; o zaman
  launch profilindeki ortam değişkenlerini (ASPNETCORE_URLS, ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL,
  ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL, ASPIRE_ALLOW_UNSECURED_TRANSPORT=true) elle ver.
- Migration'lar: AppHost `Database__ApplyMigrationsOnStartup=true` verir, API açılışta uygular
  (karar 2). İlk açılışta `__EFMigrationsHistory` için iki "Failed executing DbCommand" hata logu
  normaldir (tablo henüz yok, EF oluşturur).
- `dotnet-ef` yerel araç (`dotnet-tools.json`, 10.0.14); global 8.x'i kullanma.
  Migration ekle: `dotnet build src\DigitalWallet.Api --no-incremental -p:Deterministic=false`, sonra
  `dotnet ef migrations add ADI --project src\DigitalWallet.Api --output-dir Infrastructure\Migrations --no-build`.
  "Could not load assembly" hatası dll engelidir: yeniden derleyip tekrar dene.
  Migration dosyaları ve `WalletDbContextModelSnapshot.cs` birlikte commit edilir; elle yazılmaz.
  İstisna: var olan satırlar için varsayılan değer gibi veri düzeltmeleri; değişen satırı yorumla işaretle
  (ör. `AddLedger`'da `wallets.type` için `defaultValue: "Customer"`).
- Defter denetimi (üçü de 0 dönmeli): `select sum(amount) from ledger_entries;`,
  işlem bazında `group by transaction_id having sum(amount) <> 0`, cüzdan bakiyesi ile
  `sum(ledger_entries.amount)` farkı olan cüzdan sayısı.
- Eşzamanlılık denemesi: `xargs -P` Windows'ta süreç başlatma yavaş olduğu için istekleri gerçekten
  paralel göndermez ve yarışı gizler. Tek süreçle `curl --parallel --parallel-immediate -K istekler.cfg`
  kullan (config'te gövde `data = "{\"amount\":1}"` biçiminde, tek tırnak çalışmaz;
  `write-out = "%{http_code} %header{idempotent-replayed}\n"`).
- DB sorgusu (şifre ekrana basılmaz):
  `docker exec <postgres-container> sh -c "PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -U postgres -d walletdb -c \"select * from wallets;\""`
  (container adı `docker ps`'te `postgres-xxxx`). PostgreSQL şifresini Aspire üretir, AppHost
  user-secrets'ında durur.
- Elle deneme: Türkçe karakterli JSON'u `curl -d '...'` ile Git Bash/cmd'den gönderme, UTF-8
  olmayabilir ve 400 döner. Gövdeyi UTF-8 dosyaya yazıp `--data-binary @dosya.json` kullan.

## Bilinen eksikler
- Test projesi yok (Aşama 6).
- Para birimi başına tek sistem cüzdanı: aynı para birimindeki tüm para yatırmalar aynı satırı kilitler
  (hot row), sırayla işlenir. Kilit bekleme sınırı (`lock_timeout`) yok.
- `idempotency_keys`, `outbox_messages` ve `processed_messages` hiç temizlenmiyor (süre sınırı yok).
- Outbox gecikmesi yoklama aralığı kadar (≈1 sn); `LISTEN/NOTIFY` yok. Birden çok yayıncıda global sıra
  garanti değil. RabbitMQ'ya hiç bağlanılamadığında `attempts` artmaz, yalnızca loglanır.
- Dead-letter kuyruğundaki mesajları yeniden işleme aracı yok (elle yönetim arayüzünden).
- İzleme: yayıncının parti SELECT/UPDATE sorguları izde görünmez (örnekleme dışı). Telemetri yalnızca
  Aspire dashboard'una gidiyor (bellekte, kalıcı değil); Jaeger/Tempo gibi kalıcı bir arka uç yok.
  Ölçülen: 50 paralel TRY yatırmada istek başına 0,8-1 sn (sistem cüzdanı hot row), yetersiz bakiyeyle
  reddedilen transferde sürenin ~%77'si `wallet.lock` beklemesi.
- Bash'te başlatma betiği çıktısını `| tail`/`| tee` ile borulama: arka plandaki AppHost boruyu açık tutar,
  komut bitmez.
- RabbitMQ kimlik bilgilerini Aspire üretir; elle deneme için container ortamından okunur
  (`docker exec <messaging> printenv RABBITMQ_DEFAULT_PASS`), ekrana/dosyaya yazma.
- Yerel test DB'sinde bilerek bırakılmış tutarsızlık: Aşama 3'teki kilitsiz paralel denemeden kalan
  "Yaris A", "Yaris Alici", "Yaris C" (2026-11-17 18:30 UTC) ve TRY sistem cüzdanının bakiye kolonu defterle
  uyuşmuyor (lost update kanıtı). Defter denetimi bu 4 cüzdanı gösterir; yeni bir hata değildir.
  Sıfırlamak için volume silinebilir (geri alınamaz, onay iste).
- Otomatik test yok: lost update, deadlock ve aynı anahtarla eşzamanlı istek yalnızca elle (curl --parallel)
  denendi. Aşama 6'da Testcontainers ile eşzamanlılık testi yazılmalı.
- Ledger kayıtlarının değişmezliği yalnızca kodla korunuyor (DB tetikleyicisi yok).
- Ekstrede cursor sayfalama yok, yalnızca `limit`.
- `AddLedger` sonrası `wallets.type` kolonunda DB tarafında `DEFAULT 'Customer'` kaldı; EF modeli
  bunu bilmiyor (zararsız, uygulama her zaman değer yazıyor).
- Kimlik doğrulama yok: id'yi bilen herkes cüzdanı görebilir (kapsamda bilinçli olarak basit).
- HTTPS profili denenmedi; yalnızca `http` profili çalıştırıldı.

## Git
- Commit mesajı `feat:`, `fix:`, `docs:`, `test:`, `ci:` önekiyle; küçük ve tek konulu commit.
- Push etmeden önce sor. GitHub reposu oluşturmak da onay ister. Force push ayrıca onay ister.
- Git kimliği global ayardan gelir (GitHub noreply adresi). Repoya yerel `user.name` veya
  `user.email` yazma.
- Gizli anahtar veya şifreyi koda, repoya ya da belgeye yazma.
- Commit mesajlarına ve PR açıklamalarına `Co-Authored-By: Claude` veya "Generated with Claude Code"
  satırı ekleme (geliştiricinin kararı, 2026-11-17). Yapay zeka kullanımı Aşama 7'de README'de
  geliştiricinin kendi cümleleriyle açıklanacak.
