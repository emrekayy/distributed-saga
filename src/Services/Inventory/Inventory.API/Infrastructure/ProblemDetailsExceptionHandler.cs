using BuildingBlocks.Web;
using Inventory.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.API.Infrastructure;

internal sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetailsService)
    : ProblemDetailsExceptionHandlerBase(problemDetailsService)
{
    protected override ProblemDetails? MapException(Exception exception) => exception switch
    {
        InventoryDomainException e => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid inventory request.",
            Detail = e.Message,
        },
        _ => null,
    };
}
