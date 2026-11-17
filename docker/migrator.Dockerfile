# Migration imajı (karar 9): iki servisin EF migration bundle'ı. Compose'da bir kez çalışır ve çıkar.
# Derleme bağlamı repo kökü: docker build -f docker/migrator.Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# dotnet-ef sürümü repodaki yerel araç bildiriminden (dotnet-tools.json).
COPY global.json dotnet-tools.json ./
RUN dotnet tool restore

COPY src/DigitalWallet.Api/DigitalWallet.Api.csproj src/DigitalWallet.Api/
COPY src/DigitalWallet.Notifications/DigitalWallet.Notifications.csproj src/DigitalWallet.Notifications/
COPY src/DigitalWallet.ServiceDefaults/DigitalWallet.ServiceDefaults.csproj src/DigitalWallet.ServiceDefaults/
COPY src/DigitalWallet.Contracts/DigitalWallet.Contracts.csproj src/DigitalWallet.Contracts/
RUN dotnet restore src/DigitalWallet.Api/DigitalWallet.Api.csproj \
 && dotnet restore src/DigitalWallet.Notifications/DigitalWallet.Notifications.csproj

COPY src/ src/

# Bundle: migration'ları içeren tek dosyalık program. Self-contained: çalışma imajında .NET gerekmez.
RUN dotnet ef migrations bundle --project src/DigitalWallet.Api --self-contained -r linux-x64 \
      -o /bundles/wallet-efbundle \
 && dotnet ef migrations bundle --project src/DigitalWallet.Notifications --self-contained -r linux-x64 \
      -o /bundles/notifications-efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /bundles
COPY --from=build /bundles/ ./
COPY docker/migrate.sh ./
USER $APP_UID
ENTRYPOINT ["/bin/sh", "/bundles/migrate.sh"]
