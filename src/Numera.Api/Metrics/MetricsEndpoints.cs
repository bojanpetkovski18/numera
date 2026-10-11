using Microsoft.AspNetCore.Mvc;
using Numera.Application.Metrics;
using Numera.Infrastructure.Metrics;

namespace Numera.Api.Metrics;

/// <summary>Filter query parameters shared by the metrics endpoints. Lists repeat the key: ?region=Europe&amp;region=Asia-Pacific.</summary>
public sealed record MetricsFilterQuery(
    [FromQuery] string? From,
    [FromQuery] string? To,
    [FromQuery] string[]? Region,
    [FromQuery] string[]? Channel,
    [FromQuery] string[]? Category,
    [FromQuery] string[]? Status)
{
    public OrderFilterInput ToInput() => new(From, To, Region, Channel, Category, Status);
}

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metrics");
        group.MapGet("/summary", SummaryAsync);
        group.MapGet("/timeseries", TimeseriesAsync);
        group.MapGet("/breakdown", BreakdownAsync);

        // Shared by the dashboard filter bar and (M08) the orders grid.
        app.MapGet("/api/filter-options", async (GetFilterOptionsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)));
        return app;
    }

    private static async Task<IResult> SummaryAsync(
        [AsParameters] MetricsFilterQuery query, GetMetricsSummaryHandler handler, CancellationToken ct)
    {
        var (filter, errors) = OrderFilterParser.Parse(query.ToInput(), allowStatus: false);
        return errors.Count > 0 ? Results.ValidationProblem(errors) : Results.Ok(await handler.HandleAsync(filter, ct));
    }

    private static async Task<IResult> TimeseriesAsync(
        [AsParameters] MetricsFilterQuery query,
        [FromQuery] string? granularity,
        GetRevenueTimeseriesHandler handler,
        CancellationToken ct)
    {
        var (filter, errors) = OrderFilterParser.Parse(query.ToInput(), allowStatus: false);
        if (!Periods.TryParseGranularity(granularity, out var unit))
        {
            errors["granularity"] = ["granularity must be one of: day, week, month."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var series = await handler.HandleAsync(filter, unit, ct);
        return series is null
            ? Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["granularity"] = [$"The range has more than {Periods.MaxPoints:N0} {unit.ToString().ToLowerInvariant()}s; choose a shorter range or a larger granularity."],
            })
            : Results.Ok(series);
    }

    private static async Task<IResult> BreakdownAsync(
        [AsParameters] MetricsFilterQuery query,
        [FromQuery] string? by,
        GetRevenueBreakdownHandler handler,
        CancellationToken ct)
    {
        var (filter, errors) = OrderFilterParser.Parse(query.ToInput(), allowStatus: false);
        if (!Periods.TryParseDimension(by, out var dimension))
        {
            errors["by"] = ["by must be one of: category, region, channel."];
        }

        return errors.Count > 0 ? Results.ValidationProblem(errors) : Results.Ok(await handler.HandleAsync(filter, dimension, ct));
    }
}