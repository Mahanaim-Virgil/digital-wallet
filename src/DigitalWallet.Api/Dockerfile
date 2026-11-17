# API imajı. Derleme bağlamı repo kökü: docker build -f src/DigitalWallet.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Önce yalnız proje dosyaları: kod değişip paketler değişmediğinde restore katmanı önbellekten gelir.
COPY global.json ./
COPY src/DigitalWallet.Api/DigitalWallet.Api.csproj src/DigitalWallet.Api/
COPY src/DigitalWallet.ServiceDefaults/DigitalWallet.ServiceDefaults.csproj src/DigitalWallet.ServiceDefaults/
COPY src/DigitalWallet.Contracts/DigitalWallet.Contracts.csproj src/DigitalWallet.Contracts/
RUN dotnet restore src/DigitalWallet.Api/DigitalWallet.Api.csproj

COPY src/DigitalWallet.Api/ src/DigitalWallet.Api/
COPY src/DigitalWallet.ServiceDefaults/ src/DigitalWallet.ServiceDefaults/
COPY src/DigitalWallet.Contracts/ src/DigitalWallet.Contracts/
RUN dotnet publish src/DigitalWallet.Api/DigitalWallet.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Resmi .NET imajlarındaki root olmayan kullanıcı.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DigitalWallet.Api.dll"]
