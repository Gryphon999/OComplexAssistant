using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace OComplexAssistant.Llm;

/// <summary>Вызов Claude через официальный SDK со структурированным выводом (JSON по схеме).</summary>
public sealed class AnthropicLlmClient(IOptions<LlmOptions> options) : ILlmClient
{
    private readonly AnthropicClient _client = new();
    private readonly LlmOptions _options = options.Value;

    public async Task<LlmResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = _options.ApiModel,
            MaxTokens = 16000,
            System = systemPrompt,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Low,
                Format = new JsonOutputFormat { Schema = AssistantSchema.Build() },
            },
            Messages = [new() { Role = Role.User, Content = userPrompt }],
        };

        Message response;
        try
        {
            response = await _client.Messages.Create(parameters, cancellationToken: ct);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new LlmException("Claude API: превышен лимит запросов, повторите позже.", ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new LlmException($"Claude API вернул ошибку: {ex.Message}", ex);
        }

        if (response.StopReason == "refusal")
        {
            throw new LlmException("Модель отказалась отвечать на это обращение.");
        }

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return new LlmResult(text, "Claude API", _options.ApiModel, response.Usage.InputTokens, response.Usage.OutputTokens);
    }
}

public static class AssistantSchema
{
    public static Dictionary<string, JsonElement> Build() => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(
            new[] { "intent", "client_reply", "upsell", "escalate", "escalate_reason", "sources" }),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            intent = new { type = "string" },
            client_reply = new { type = "string" },
            upsell = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "offer", "reason", "script" },
                properties = new
                {
                    offer = new { type = "string" },
                    reason = new { type = "string" },
                    script = new { type = "string" },
                },
            },
            escalate = new { type = "boolean" },
            escalate_reason = new { type = "string" },
            sources = new { type = "array", items = new { type = "string" } },
        }),
    };
}
