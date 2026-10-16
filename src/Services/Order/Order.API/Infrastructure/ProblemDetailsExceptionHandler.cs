using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Order.Application.Orders;
using Order.Domain.Orders;

namespace Order.API.Infrastructure;

/// <summary>Bilinen exception'ları ProblemDetails'e çevirir; diğerleri varsayılan 500 akışına düşer.</summary>
internal sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            OrderValidationException e => new HttpValidationProblemDetails(
                new Dictionary<string, string[]> { [e.PropertyName] = [e.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            IdempotencyKeyConflictException e => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Idempotency key reused with a different request body.",
                Detail = e.Message,
            },
            InvalidOrderStateTransitionException e => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Invalid order state transition.",
                Detail = e.Message,
            },
            BadHttpRequestException e => new ProblemDetails
            {
                Status = e.StatusCode,
                Title = "Malformed request.",
                Detail = e.Message,
            },
            _ => null,
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
