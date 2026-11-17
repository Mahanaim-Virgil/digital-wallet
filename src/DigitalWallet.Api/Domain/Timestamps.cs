namespace DigitalWallet.Api.Domain;

public static class Timestamps
{
    /// <summary>
    /// PostgreSQL `timestamptz` mikro saniye tutar, .NET 100 ns (tick). Kaydetmeden önce kırparız ki
    /// oluşturma yanıtı ile sonradan okunan değer aynı olsun.
    /// </summary>
    public static DateTimeOffset ToDatabasePrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
}
