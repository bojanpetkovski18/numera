using System.Text.Json;
using Microsoft.Extensions.Options;
using Numera.Application.Ask;
using Numera.Application.Metrics;
using Numera.Infrastructure.Ask;

namespace Numera.UnitTests;

public class AskTests
{
    private static readonly FilterOptions Options = new(
        ["Asia-Pacific", "Europe", "Latin America", "North America"],
        ["Marketplace", "Mobile App", "Online Store", "Wholesale"],
        ["Accessories", "Blends", "Equipment", "Single Origin", "Subscriptions"],
        new DateOnly(2024, 10, 1),
        new DateOnly(2026, 9, 30));

    private static AskInterpretation Reply(
        string metric = "revenue", string view = "total", string from = "2026-07-01", string to = "2026-09-30",
        string[]? regions = null, string compare = "none", string granularity = "month", string dimension = "region",
        bool answerable = true, string reason = "") =>
        new(answerable, reason, metric, view, granularity, dimension, from, to, regions ?? [], [], [], compare);

    private static AskPlan Valid(AskInterpretation reply)
    {
        var (plan, problem) = AskValidator.Validate(reply, Options);
        Assert.Null(problem);
        return plan!;
    }

    private static string Refused(AskInterpretation reply)
    {
        var (plan, problem) = AskValidator.Validate(reply, Options);
        Assert.Null(plan);
        return problem!;
    }

    // validation ------------------------------------------------------------------------------------

    [Fact]
    public void A_valid_reply_becomes_a_plan_with_the_datas_spelling_of_each_value()
    {
        var plan = Valid(Reply(regions: [" europe ", "EUROPE", "north america"], compare: "previous"));

        Assert.Equal(AskMetric.Revenue, plan.Metric);
        Assert.Equal(AskView.Total, plan.View);
        Assert.Equal(["Europe", "North America"], plan.Filter.Regions);
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)), (plan.Filter.From!.Value, plan.Filter.To!.Value));
        Assert.Equal(ComparisonMode.Previous, plan.Comparison!.Mode);
    }

    [Fact]
    public void Each_view_keeps_only_what_it_uses()
    {
        var overTime = AskValidator.ToDto(Valid(Reply(view: "over_time", granularity: "week", compare: "year")));
        Assert.Equal(("over_time", "week", (string?)null, "year"), (overTime.View, overTime.Granularity, overTime.Dimension, overTime.Compare));

        var byGroup = AskValidator.ToDto(Valid(Reply(metric: "orders", view: "by_dimension", dimension: "category", compare: "previous")));
        Assert.Equal(("by_dimension", (string?)null, "category", "none"), (byGroup.View, byGroup.Granularity, byGroup.Dimension, byGroup.Compare));

        var allTime = AskValidator.ToDto(Valid(Reply(from: "", to: "")));
        Assert.Equal(((DateOnly?)null, (DateOnly?)null), (allTime.From, allTime.To));
    }

    [Fact]
    public void A_value_that_is_not_in_the_data_is_refused_by_name()
    {
        Assert.Equal("\"Antarctica\" is not a region, channel or category in the data.", Refused(Reply(regions: ["Europe", "Antarctica"])));
        Assert.Contains("is not a region", Refused(Reply(regions: ["Europe'; DROP TABLE orders; --"])));
    }

    [Theory]
    [InlineData("profit", "total")]
    [InlineData("revenue", "forecast")]
    public void An_unknown_metric_or_view_is_refused(string metric, string view) =>
        Assert.Equal("The question could not be turned into a request Numera understands.", Refused(Reply(metric: metric, view: view)));

    [Fact]
    public void Ratios_are_totals_only()
    {
        Assert.StartsWith("Average order value and refund rate are available as totals", Refused(Reply(metric: "refund_rate", view: "over_time")));
        Assert.Equal(AskMetric.AverageOrderValue, Valid(Reply(metric: "average_order_value")).Metric);
    }

    [Theory]
    [InlineData("2026-09-30", "2026-07-01")]
    [InlineData("30/09/2026", "")]
    [InlineData("2026-02-30", "")]
    public void Bad_dates_are_refused(string from, string to) =>
        Assert.Equal("The dates in the question could not be read as a valid range.", Refused(Reply(from: from, to: to)));

    [Fact]
    public void A_comparison_needs_both_dates_and_a_known_mode()
    {
        Assert.StartsWith("A comparison needs a period with a start and an end", Refused(Reply(from: "2026-07-01", to: "", compare: "previous")));
        Assert.Equal("The question could not be turned into a request Numera understands.", Refused(Reply(compare: "quarter")));
    }

    [Fact]
    public void A_question_the_model_declined_keeps_its_reason_or_gets_a_plain_one()
    {
        Assert.Equal("Numera has no profit figures.", Refused(Reply(answerable: false, reason: " Numera has no profit figures. ")));
        Assert.Equal("That question is not about the sales figures Numera shows.", Refused(Reply(answerable: false)));
        Assert.Equal(301, Refused(Reply(answerable: false, reason: new string('x', 500))).Length); // 300 and an ellipsis
    }

    // the reply and the schema -----------------------------------------------------------------------

    private const string GoodJson = """
        {"answerable":true,"reason":"","metric":"orders","view":"by_dimension","granularity":"month","dimension":"channel",
         "from":"2026-01-01","to":"2026-03-31","regions":["Europe"],"channels":[],"categories":[],"compare":"none"}
        """;

    [Fact]
    public void A_reply_in_the_schema_is_read()
    {
        var reply = AskReply.TryParse(GoodJson)!;

        Assert.Equal(("orders", "by_dimension", "channel"), (reply.Metric, reply.View, reply.Dimension));
        Assert.Equal(["Europe"], reply.Regions);
    }

    [Theory]
    [InlineData("""{"answerable":true}""")] // fields missing
    [InlineData("not json")]
    [InlineData("""{"answerable":true,"reason":"","metric":"orders","view":"total","granularity":"month","dimension":"region","from":"","to":"","regions":[],"channels":[],"categories":[],"compare":"none","sql":"SELECT 1"}""")] // an extra field
    [InlineData("""{"answerable":true,"reason":null,"metric":"orders","view":"total","granularity":"month","dimension":"region","from":"","to":"","regions":[],"channels":[],"categories":[],"compare":"none"}""")] // null
    public void A_reply_outside_the_schema_is_not_read(string json) => Assert.Null(AskReply.TryParse(json));

    [Fact]
    public void The_schema_requires_every_field_and_allows_no_others()
    {
        var schema = AskSchema.Build();

        Assert.False(schema["additionalProperties"].GetBoolean());
        var properties = schema["properties"].EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(properties, schema["required"].EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(12, properties.Length);
        Assert.Equal(["revenue", "orders", "average_order_value", "refund_rate"],
            schema["properties"].GetProperty("metric").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        // Every field of the record the reply is read into is in the schema.
        Assert.Equal(typeof(AskInterpretation).GetProperties().Length, properties.Length);
    }

    [Fact]
    public void The_prompt_context_lists_today_the_data_dates_and_every_allowed_value()
    {
        var context = AskPrompt.Context(new AskContext(new DateOnly(2026, 11, 16), Options));

        Assert.Contains("Today's date: 2026-11-16.", context);
        Assert.Contains("The data covers 2024-10-01 to 2026-09-30.", context);
        Assert.Contains("Regions: Asia-Pacific; Europe; Latin America; North America", context);
        Assert.Contains("Categories: Accessories; Blends; Equipment; Single Origin; Subscriptions", context);
        Assert.Contains("There is no data yet.", AskPrompt.Context(new AskContext(new DateOnly(2026, 11, 16), new FilterOptions([], [], [], null, null))));
        Assert.DoesNotContain("2026-11-16", AskPrompt.Instructions); // the cached part never changes
    }

    // the daily budget -------------------------------------------------------------------------------

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void The_daily_budget_stops_at_the_limit_and_starts_again_the_next_utc_day()
    {
        var clock = new Clock(new DateTimeOffset(2026, 11, 16, 23, 0, 0, TimeSpan.Zero));
        var budget = new AskDailyBudget(clock, Microsoft.Extensions.Options.Options.Create(new AskOptions { DailyLimit = 2 }));

        Assert.Equal([true, true, false], new[] { budget.TryTake(), budget.TryTake(), budget.TryTake() });
        clock.Now = clock.Now.AddHours(1);
        Assert.True(budget.TryTake());
    }

    [Fact]
    public void The_feature_is_on_only_with_a_key()
    {
        Assert.False(new AskOptions().Enabled);
        Assert.False(new AskOptions { ApiKey = "  " }.Enabled);
        Assert.True(new AskOptions { ApiKey = "test-key" }.Enabled);
        Assert.Equal("claude-opus-5-5", new AskOptions().Model);
    }
}
