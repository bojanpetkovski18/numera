using Microsoft.AspNetCore.Mvc;
using Numera.Application.Metrics;
using Numera.Application.Views;
using Numera.Infrastructure.Metrics;

namespace Numera.Api.Views;

/// <summary>What a shared page needs to describe itself: the view's name, the dates and filters it shows, and the expiry.</summary>
public sealed record SharedViewInfo(
    string Name,
    string Preset,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> Categories,
    string Granularity,
    ComparisonDates? Comparison,
    DateTimeOffset ExpiresAt);

/// <summary>
/// The public, read-only side of a share link: the view's dashboard figures, without signing in. Every request uses
/// the view's saved filter and comparison; query parameters cannot widen it (only the chart granularity and the
/// breakdown dimension are taken from the query). No order rows are exposed. An unknown, malformed, revoked or
/// expired token is the same 404.
/// </summary>
public static class SharedEndpoints
{
    public const string RateLimitPolicy = "shared";

    public static IEndpointRouteBuilder MapSharedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shared/{token}")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .AddEndpointFilter(PrivateHeaders);

        group.MapGet("/", async (string token, ShareLinksHandler links, CancellationToken ct) =>
            await WithViewAsync(token, links, ct, view => Task.FromResult(Results.Ok(Info(view)))));

        group.MapGet("/summary", async (string token, ShareLinksHandler links, GetMetricsSummaryHandler handler, CancellationToken ct) =>
            await WithViewAsync(token, links, ct, async view =>
                Results.Ok(await handler.HandleAsync(view.Resolved.Filter, view.Resolved.Comparison, ct))));

        group.MapGet("/timeseries", async (string token, [FromQuery] string? granularity, ShareLinksHandler links, GetRevenueTimeseriesHandler handler, CancellationToken ct) =>
            await WithViewAsync(token, links, ct, async view =>
            {
                var unit = view.Resolved.Granularity;
                if (!string.IsNullOrWhiteSpace(granularity) && !Periods.TryParseGranularity(granularity, out unit))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["granularity"] = ["granularity must be one of: day, week, month."] });
                }

                var series = await handler.HandleAsync(view.Resolved.Filter, unit, view.Resolved.Comparison, ct);
                return series is null
                    ? Results.ValidationProblem(new Dictionary<string, string[]> { ["granularity"] = ["The range is too long for this granularity; choose a larger one."] })
                    : Results.Ok(series);
            }));

        group.MapGet("/breakdown", async (string token, [FromQuery] string? by, ShareLinksHandler links, GetRevenueBreakdownHandler handler, CancellationToken ct) =>
            await WithViewAsync(token, links, ct, async view =>
                Periods.TryParseDimension(by, out var dimension)
                    ? Results.Ok(await handler.HandleAsync(view.Resolved.Filter, dimension, ct))
                    : Results.ValidationProblem(new Dictionary<string, string[]> { ["by"] = ["by must be one of: category, region, channel."] })));

        group.MapGet("/drivers", async (string token, [FromQuery] string? by, ShareLinksHandler links, GetRevenueDriversHandler handler, CancellationToken ct) =>
            await WithViewAsync(token, links, ct, async view =>
            {
                if (view.Resolved.Comparison is not { } comparison)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No comparison", detail: "This view does not compare periods.");
                }

                return Periods.TryParseDimension(by, out var dimension)
                    ? Results.Ok(await handler.HandleAsync(view.Resolved.Filter, dimension, comparison, ct))
                    : Results.ValidationProblem(new Dictionary<string, string[]> { ["by"] = ["by must be one of: category, region, channel."] });
            }));

        return app;
    }

    private static async Task<IResult> WithViewAsync(string token, ShareLinksHandler links, CancellationToken ct, Func<SharedView, Task<IResult>> work)
    {
        var view = await links.ResolveAsync(token, ct);
        return view is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link not available",
                detail: "This link has expired, was revoked or does not exist.")
            : await work(view);
    }

    private static SharedViewInfo Info(SharedView view)
    {
        var filter = view.Resolved.Filter;
        var comparison = view.Resolved.Comparison;
        return new SharedViewInfo(
            view.Name,
            view.Settings.Preset ?? "all",
            filter.From,
            filter.To,
            filter.Regions,
            filter.Channels,
            filter.Categories,
            view.Resolved.Granularity.ToString().ToLowerInvariant(),
            comparison is null ? null : new ComparisonDates(comparison.Name, comparison.From, comparison.To),
            view.ExpiresAt);
    }

    /// <summary>The token is in the URL: keep responses out of caches, referrers and search engines.</summary>
    private static async ValueTask<object?> PrivateHeaders(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var headers = context.HttpContext.Response.Headers;
        headers.CacheControl = "no-store";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Robots-Tag"] = "noindex, nofollow";
        return await next(context);
    }
}
