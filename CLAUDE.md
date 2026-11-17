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

## Komutlar
- Derle: `dotnet build` (kök klasörde).
- (Aşama 1 ilerledikçe çalıştırma, migration ve deneme komutları buraya eklenecek.)

## Git
- Commit mesajı `feat:`, `fix:`, `docs:`, `test:`, `ci:` önekiyle; küçük ve tek konulu commit.
- Push etmeden önce sor. GitHub reposu oluşturmak da onay ister. Force push ayrıca onay ister.
- Git kimliği global ayardan gelir (GitHub noreply adresi). Repoya yerel `user.name` veya
  `user.email` yazma.
- Gizli anahtar veya şifreyi koda, repoya ya da belgeye yazma.
