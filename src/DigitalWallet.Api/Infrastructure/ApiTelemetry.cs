using System.Diagnostics;

namespace DigitalWallet.Api.Infrastructure;

/// <summary>
/// API'nin kendi span'leri. Kaynak adı uygulama adıyla aynı; ServiceDefaults
/// `AddSource(ApplicationName)` ile zaten kayıtlı (karar 7).
/// </summary>
public static class ApiTelemetry
{
    public const string SourceName = "DigitalWallet.Api";

    public static readonly ActivitySource Source = new(SourceName);
}
