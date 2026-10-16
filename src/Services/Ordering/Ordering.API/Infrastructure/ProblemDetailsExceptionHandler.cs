using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using Ordering.Application.Orders;
using Ordering.Domain.Orders;

namespace Ordering.API.Infrastructure;

/// <summary>Ordering'e özel exception eşlemesi; ortak taban BadHttpRequestException'ı da karşılar.</summary>
internal sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetailsService)
    : ProblemDetailsExceptionHandlerBase(problemDetailsService)
{
    protected override ProblemDetails? MapException(Exception exception) => exception switch
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
        _ => null,
    };
}
