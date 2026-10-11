using Microsoft.AspNetCore.Mvc;
using Numera.Api.Metrics;
using Numera.Application.Orders;
using Numera.Infrastructure.Orders;

namespace Numera.Api.Orders;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/orders", ListAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] MetricsFilterQuery filter,
        [FromQuery] string? q,
        [FromQuery] string? sort,
        [FromQuery] string? dir,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        GetOrdersHandler handler,
        CancellationToken ct)
    {
        var (query, errors) = OrdersQueryParser.Parse(filter.ToInput(), q, sort, dir, page, pageSize);
        return errors.Count > 0 ? Results.ValidationProblem(errors) : Results.Ok(await handler.HandleAsync(query, ct));
    }
}