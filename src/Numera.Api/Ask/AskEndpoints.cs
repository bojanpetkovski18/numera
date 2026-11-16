using Microsoft.Extensions.Options;
using Numera.Application.Ask;
using Numera.Infrastructure.Ask;

namespace Numera.Api.Ask;

public sealed record AskRequest(string? Question);

/// <summary>
/// "Ask in plain English" (M17). Off unless <c>Ai:ApiKey</c> is configured: then the status says so and asking is a
/// 404, so the app works fully without it. Every question is limited per user per minute and per day overall.
/// </summary>
public static class AskEndpoints
{
    public const string RateLimitPolicy = "ask";

    public static IEndpointRouteBuilder MapAskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ask");

        group.MapGet("/status", (IOptions<AskOptions> options) =>
            Results.Ok(new { enabled = options.Value.Enabled, maxQuestionLength = options.Value.MaxQuestionLength }));

        group.MapPost("/", AskAsync).RequireRateLimiting(RateLimitPolicy);
        return app;
    }

    private static async Task<IResult> AskAsync(AskRequest body, IOptions<AskOptions> options, AskHandler handler, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not available", detail: "Questions in plain English are not enabled.");
        }

        var question = body.Question?.Trim() ?? string.Empty;
        if (question.Length == 0 || question.Length > settings.MaxQuestionLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["question"] = [$"Ask a question of 1 to {settings.MaxQuestionLength} characters."],
            });
        }

        var outcome = await handler.HandleAsync(question, ct);
        return outcome.Kind switch
        {
            AskOutcomeKind.Answered => Results.Ok(outcome.Answer),
            AskOutcomeKind.DailyLimitReached => Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Daily question limit reached",
                detail: $"The demo answers up to {settings.DailyLimit} questions a day across all visitors. Try again tomorrow, or use the filters below."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Answers are not available right now",
                detail: "The question could not be answered right now. Try again in a minute."),
        };
    }
}
