using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Numera.Api.Metrics;
using Numera.Application.Orders;
using Numera.Infrastructure.Orders;

namespace Numera.Api.Orders;

public static class OrderEndpoints
{
    /// <summary>Response header with the number of data rows in an export (the client shows it when done).</summary>
    public const string RowCountHeader = "X-Row-Count";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/orders", ListAsync);
        app.MapGet("/api/orders/export", ExportAsync);
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

    /// <summary>
    /// Every row the grid shows for the same filter, search and sort (paging parameters are ignored), as CSV.
    /// More than <see cref="ExportOrdersHandler.MaxRows"/> rows is refused rather than truncated, so a file never
    /// silently holds less than the grid.
    /// </summary>
    private static async Task<IResult> ExportAsync(
        [AsParameters] MetricsFilterQuery filter,
        [FromQuery] string? q,
        [FromQuery] string? sort,
        [FromQuery] string? dir,
        ExportOrdersHandler handler,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken ct)
    {
        var (query, errors) = OrdersQueryParser.Parse(filter.ToInput(), q, sort, dir, page: null, pageSize: null);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var export = await handler.StartAsync(query, ct);
        if (export.RowCount > ExportOrdersHandler.MaxRows)
        {
            var rows = export.RowCount;
            await export.DisposeAsync();
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Export too large",
                detail: $"{rows.ToString("N0", CultureInfo.InvariantCulture)} order lines match these filters. " +
                        $"An export can hold at most {ExportOrdersHandler.MaxRows.ToString("N0", CultureInfo.InvariantCulture)}; " +
                        "narrow the filters and try again.",
                extensions: new Dictionary<string, object?> { ["rowCount"] = rows, ["maxRows"] = ExportOrdersHandler.MaxRows });
        }

        response.Headers[RowCountHeader] = export.RowCount.ToString(CultureInfo.InvariantCulture);
        response.Headers.CacheControl = "no-store";
        return Results.Stream(
            async stream =>
            {
                await using (export)
                {
                    await export.WriteAsync(stream, ct);
                }
            },
            contentType: "text/csv; charset=utf-8",
            fileDownloadName: ExportFileName.For(query, clock.GetUtcNow()));
    }
}
