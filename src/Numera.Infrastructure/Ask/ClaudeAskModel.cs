using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Numera.Application.Ask;

namespace Numera.Infrastructure.Ask;

/// <summary>
/// Asks Claude to turn a question into an <see cref="AskInterpretation"/>, as JSON constrained by
/// <see cref="AskSchema"/> (structured output). Low effort: it is a translation into a fixed shape, not open reasoning.
/// The fixed instructions are a cached system block; the data's dates and filter values follow in a second block.
/// Server-side fallbacks re-serve a request a safety classifier declines on another model, instead of failing it.
/// </summary>
public sealed class ClaudeAskModel(AnthropicClient client, IOptions<AskOptions> options, ILogger<ClaudeAskModel> logger) : IAskModel
{
    private static readonly Dictionary<string, JsonElement> Schema = AskSchema.Build();

    public async Task<AskModelResult> InterpretAsync(string question, AskContext context, CancellationToken ct)
    {
        try
        {
            var response = await client.Beta.Messages.Create(
                new MessageCreateParams
                {
                    Model = options.Value.Model,
                    MaxTokens = 4096,
                    Betas = ["server-side-fallback-2026-07-01"],
                    Fallbacks = new Default(),
                    System = new List<BetaTextBlockParam>
                    {
                        new() { Text = AskPrompt.Instructions, CacheControl = new BetaCacheControlEphemeral() },
                        new() { Text = AskPrompt.Context(context) },
                    },
                    Messages = [new BetaUserMessageParam { Content = question }],
                    OutputConfig = new BetaOutputConfig
                    {
                        Effort = Effort.Low,
                        Format = new BetaJsonOutputFormat { Schema = Schema },
                    },
                },
                ct);

            if (response.StopReason == "refusal")
            {
                return new AskModelResult(null, AskModelFailure.Refused);
            }

            var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
            logger.LogInformation(
                "Ask model: {Model}, stop {StopReason}, input {Input} (cache read {CacheRead}), output {Output} tokens",
                response.Model, response.StopReason, response.Usage.InputTokens, response.Usage.CacheReadInputTokens, response.Usage.OutputTokens);
            if (response.StopReason != "end_turn" || text.Length == 0)
            {
                return new AskModelResult(null, AskModelFailure.Invalid);
            }

            var interpretation = AskReply.TryParse(text);
            return interpretation is null ? new AskModelResult(null, AskModelFailure.Invalid) : new AskModelResult(interpretation, null);
        }
        catch (AnthropicRateLimitException ex)
        {
            logger.LogWarning(ex, "Ask model rate limited");
            return new AskModelResult(null, AskModelFailure.Unavailable);
        }
        catch (Anthropic5xxException ex)
        {
            logger.LogWarning(ex, "Ask model server error");
            return new AskModelResult(null, AskModelFailure.Unavailable);
        }
        catch (AnthropicIOException ex)
        {
            logger.LogWarning(ex, "Ask model unreachable");
            return new AskModelResult(null, AskModelFailure.Unavailable);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Ask model timed out");
            return new AskModelResult(null, AskModelFailure.Unavailable);
        }
        // 4xx other than 429 (a bad key, a bad request) are configuration or code errors: they surface as 500s and are logged.
    }
}
