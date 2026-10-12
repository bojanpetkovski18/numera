using System.Security.Claims;
using Numera.Infrastructure.Seeding;

namespace Numera.Api.Demo;

public sealed record DemoResetResponse(int OrderLines, DateTimeOffset CompletedAt);

public static class DemoEndpoints
{
    public const string ResetRateLimitPolicy = "demo-reset";

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/demo/reset", ResetAsync).RequireRateLimiting(ResetRateLimitPolicy);
        return app;
    }

    /// <summary>
    /// Restores the original demo data: removes imported orders and import history, then reloads the deterministic
    /// seed. Refused with 409 while an import or another reset is running (they share one database lock).
    /// </summary>
    private static async Task<IResult> ResetAsync(
        DemoSeeder seeder,
        IHostApplicationLifetime lifetime,
        TimeProvider clock,
        ClaimsPrincipal user,
        ILoggerFactory loggers)
    {
        // Runs to completion even if the caller disconnects (one transaction: it either fully happens or not at all);
        // only application shutdown cancels it.
        var result = await seeder.TryResetAndSeedAsync(ct: lifetime.ApplicationStopping);
        if (result is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Demo data is busy",
                detail: "An import or another reset is running. Try again in a moment.");
        }

        loggers.CreateLogger(typeof(DemoEndpoints)).LogInformation(
            "Demo data reset by user {UserId}: {Rows} order lines", user.FindFirstValue("sub"), result.RowsInserted);
        return Results.Ok(new DemoResetResponse(result.RowsInserted, clock.GetUtcNow()));
    }
}
