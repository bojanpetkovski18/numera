using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Numera.Application.Ask;
using Numera.Infrastructure.Metrics;

namespace Numera.Infrastructure.Ask;

public enum AskOutcomeKind
{
    Answered,
    DailyLimitReached,
    Unavailable,
}

public sealed record AskOutcome(AskOutcomeKind Kind, AskAnswer? Answer = null);

/// <summary>
/// Counts questions per UTC day across all users, so the hosted demo has a hard daily ceiling on model calls.
/// In memory: it restarts with the process, which is acceptable for a single-instance demo (see D061).
/// </summary>
public sealed class AskDailyBudget(TimeProvider clock, IOptions<AskOptions> options)
{
    private readonly Lock _gate = new();
    private DateOnly _day;
    private int _used;

    public bool TryTake()
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        lock (_gate)
        {
            if (today != _day)
            {
                (_day, _used) = (today, 0);
            }

            if (_used >= options.Value.DailyLimit)
            {
                return false;
            }

            _used++;
            return true;
        }
    }
}

/// <summary>
/// Answers a question: the model turns it into a request, <see cref="AskValidator"/> checks that request like any
/// from the UI, and the dashboard's own query handlers run it. Nothing the model returns runs as code or SQL.
/// The question text is never logged, only its length and the outcome.
/// </summary>
public sealed class AskHandler(
    IAskModel model,
    AskDailyBudget budget,
    GetFilterOptionsHandler filterOptions,
    GetMetricsSummaryHandler summary,
    GetRevenueTimeseriesHandler timeseries,
    GetRevenueBreakdownHandler breakdown,
    TimeProvider clock,
    ILogger<AskHandler> logger)
{
    public async Task<AskOutcome> HandleAsync(string question, CancellationToken ct)
    {
        if (!budget.TryTake())
        {
            logger.LogWarning("Ask: daily question limit reached");
            return new AskOutcome(AskOutcomeKind.DailyLimitReached);
        }

        var options = await filterOptions.HandleAsync(ct);
        var context = new AskContext(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), options);
        var reply = await model.InterpretAsync(question, context, ct);
        if (reply.Failure == AskModelFailure.Unavailable)
        {
            logger.LogWarning("Ask: model unavailable (question of {Length} characters)", question.Length);
            return new AskOutcome(AskOutcomeKind.Unavailable);
        }

        if (reply.Interpretation is not { } interpretation)
        {
            logger.LogInformation("Ask: no usable interpretation ({Failure}, question of {Length} characters)", reply.Failure, question.Length);
            return Answered(AskAnswer.CannotAnswer("That question could not be turned into a request Numera can run. Try asking about revenue, orders, average order value or refund rate."));
        }

        var (plan, problem) = AskValidator.Validate(interpretation, options);
        if (plan is null)
        {
            logger.LogInformation("Ask: cannot answer (question of {Length} characters)", question.Length);
            return Answered(AskAnswer.CannotAnswer(problem!));
        }

        object? result = plan.View switch
        {
            AskView.Total => await summary.HandleAsync(plan.Filter, plan.Comparison, ct),
            AskView.OverTime => await timeseries.HandleAsync(plan.Filter, plan.Granularity, plan.Comparison, ct),
            _ => await breakdown.HandleAsync(plan.Filter, plan.Dimension, ct),
        };
        if (result is null)
        {
            return Answered(AskAnswer.CannotAnswer("That period has too many points for this grouping. Ask for a shorter period or a larger grouping, such as by month."));
        }

        logger.LogInformation("Ask: answered {Metric} {View} (question of {Length} characters)", plan.Metric, plan.View, question.Length);
        return Answered(new AskAnswer("answered", AskValidator.ToDto(plan), result, null));
    }

    private static AskOutcome Answered(AskAnswer answer) => new(AskOutcomeKind.Answered, answer);
}
