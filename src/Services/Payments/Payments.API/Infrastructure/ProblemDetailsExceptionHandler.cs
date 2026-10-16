using BuildingBlocks.Web;
using Payments.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Payments.API.Infrastructure;

internal sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetailsService)
    : ProblemDetailsExceptionHandlerBase(problemDetailsService)
{
    protected override ProblemDetails? MapException(Exception exception) => exception switch
    {
        PaymentsDomainException e => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid payment request.",
            Detail = e.Message,
        },
        _ => null,
    };
}
