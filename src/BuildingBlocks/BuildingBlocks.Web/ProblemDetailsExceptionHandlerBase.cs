using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildingBlocks.Web;

/// <summary>
/// Bilinen exception'ları ProblemDetails'e çeviren ortak taban. Servis, yalnızca kendi domain/application
/// exception eşlemesini <see cref="MapException"/> ile verir; <see cref="BadHttpRequestException"/> burada ele alınır.
/// Eşlenmeyen exception varsayılan 500 akışına düşer.
/// </summary>
public abstract class ProblemDetailsExceptionHandlerBase(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    /// <summary>Servise özel exception eşlemesi; tanımadığı exception için null döner.</summary>
    protected abstract ProblemDetails? MapException(Exception exception);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            BadHttpRequestException e => new ProblemDetails
            {
                Status = e.StatusCode,
                Title = "Malformed request.",
                Detail = e.Message,
            },
            _ => MapException(exception),
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
