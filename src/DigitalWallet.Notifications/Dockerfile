# Bildirim servisi imajı. Derleme bağlamı repo kökü: docker build -f src/DigitalWallet.Notifications/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY src/DigitalWallet.Notifications/DigitalWallet.Notifications.csproj src/DigitalWallet.Notifications/
COPY src/DigitalWallet.ServiceDefaults/DigitalWallet.ServiceDefaults.csproj src/DigitalWallet.ServiceDefaults/
COPY src/DigitalWallet.Contracts/DigitalWallet.Contracts.csproj src/DigitalWallet.Contracts/
RUN dotnet restore src/DigitalWallet.Notifications/DigitalWallet.Notifications.csproj

COPY src/DigitalWallet.Notifications/ src/DigitalWallet.Notifications/
COPY src/DigitalWallet.ServiceDefaults/ src/DigitalWallet.ServiceDefaults/
COPY src/DigitalWallet.Contracts/ src/DigitalWallet.Contracts/
RUN dotnet publish src/DigitalWallet.Notifications/DigitalWallet.Notifications.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DigitalWallet.Notifications.dll"]
