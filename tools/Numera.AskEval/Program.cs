// Manual evaluation of "Ask in plain English" (M17) against the real model. It sends each question in questions.json
// to Claude through the app's own ClaudeAskModel and AskValidator, with a fixed "today" and the demo data's values,
// and checks the resulting request against the expected fields. Costs real money: about 20 short requests.
//
//   set Ai__ApiKey (or ANTHROPIC_API_KEY), optionally Ai__Model, then:
//   dotnet run --project tools/Numera.AskEval
using System.Text.Json;
using Anthropic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Numera.Application.Ask;
using Numera.Application.Metrics;
using Numera.Infrastructure.Ask;

var key = Environment.GetEnvironmentVariable("Ai__ApiKey") ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("Set Ai__ApiKey or ANTHROPIC_API_KEY to run the evaluation. Nothing was sent.");
    return 2;
}

var options = new AskOptions { ApiKey = key, Model = Environment.GetEnvironmentVariable("Ai__Model") ?? new AskOptions().Model };
using var loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
var model = new ClaudeAskModel(
    new AnthropicClient { ApiKey = key, Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds), MaxRetries = 1 },
    Options.Create(options),
    loggers.CreateLogger<ClaudeAskModel>());

// The demo data's values and dates, and a fixed "today", so relative questions ("last month") have one right answer.
var demo = new FilterOptions(
    ["Asia-Pacific", "Europe", "Latin America", "North America"],
    ["Marketplace", "Mobile App", "Online Store", "Wholesale"],
    ["Accessories", "Blends", "Equipment", "Single Origin", "Subscriptions"],
    new DateOnly(2024, 10, 1),
    new DateOnly(2026, 9, 30));
var context = new AskContext(new DateOnly(2026, 10, 15), demo);

var cases = JsonSerializer.Deserialize<EvalCase[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "questions.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
var passed = 0;
foreach (var (question, expect) in cases)
{
    var reply = await model.InterpretAsync(question, context, CancellationToken.None);
    var (plan, problem) = reply.Interpretation is { } i ? AskValidator.Validate(i, demo) : (null, $"model: {reply.Failure}");
    var differences = Grade(expect, plan is null ? null : AskValidator.ToDto(plan));
    passed += differences.Count == 0 ? 1 : 0;
    Console.WriteLine($"{(differences.Count == 0 ? "PASS" : "FAIL")}  {question}");
    Console.WriteLine(plan is null ? $"      cannot answer: {problem}" : $"      {JsonSerializer.Serialize(AskValidator.ToDto(plan))}");
    foreach (var difference in differences)
    {
        Console.WriteLine($"      expected {difference}");
    }
}

Console.WriteLine($"\n{passed} of {cases.Length} as expected (model {options.Model}, today {context.Today:yyyy-MM-dd}).");
return passed == cases.Length ? 0 : 1;

// Only the fields a case names are checked. A null date means "no limit"; lists compare as sets.
static List<string> Grade(JsonElement expect, AskRequestDto? actual)
{
    var differences = new List<string>();
    var answerable = !expect.TryGetProperty("answerable", out var a) || a.GetBoolean();
    if (!answerable || actual is null)
    {
        if (answerable != (actual is not null))
        {
            differences.Add(answerable ? "an answer" : "no answer");
        }

        return differences;
    }

    foreach (var property in expect.EnumerateObject())
    {
        string? Text(string? value) => value;
        var (wanted, got) = property.Name switch
        {
            "metric" => (property.Value.GetString(), actual.Metric),
            "view" => (property.Value.GetString(), actual.View),
            "granularity" => (property.Value.GetString(), actual.Granularity),
            "dimension" => (property.Value.GetString(), actual.Dimension),
            "compare" => (property.Value.GetString(), actual.Compare),
            "from" => (Text(property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString()), actual.From?.ToString("yyyy-MM-dd")),
            "to" => (Text(property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString()), actual.To?.ToString("yyyy-MM-dd")),
            "regions" => (Set(property.Value), string.Join(", ", actual.Regions.Order())),
            "channels" => (Set(property.Value), string.Join(", ", actual.Channels.Order())),
            "categories" => (Set(property.Value), string.Join(", ", actual.Categories.Order())),
            _ => (null, null),
        };
        if (property.Name != "answerable" && wanted != got)
        {
            differences.Add($"{property.Name} = {wanted ?? "(none)"}, got {got ?? "(none)"}");
        }
    }

    return differences;

    static string Set(JsonElement values) => string.Join(", ", values.EnumerateArray().Select(v => v.GetString()).Order());
}

internal sealed record EvalCase(string Question, JsonElement Expect);
