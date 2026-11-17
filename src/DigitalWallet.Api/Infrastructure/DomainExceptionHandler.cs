using DigitalWallet.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace DigitalWallet.Api.Infrastructure;

/// <summary>
/// İş kuralı ihlalini (<see cref="DomainException"/>) 422 ProblemDetails'e çevirir.
/// `code` alanı istemcinin hatayı metne bakmadan ayırt etmesi içindir (ör. "insufficient_funds").
/// Diğer istisnalara dokunmaz (false döner; UseExceptionHandler'ın varsayılan davranışı devam eder).
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "İş kuralı ihlali",
                Detail = domainException.Message,
                Extensions = { ["code"] = domainException.Code },
            },
        });
    }
}
