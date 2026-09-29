namespace OComplexAssistant.Llm;

public sealed record LlmResult(string Text, string Provider, string Model, long InputTokens, long OutputTokens);

public interface ILlmClient
{
    Task<LlmResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}

public sealed class LlmOptions
{
    /// <summary>"auto" — API при наличии ANTHROPIC_API_KEY, иначе Claude Code CLI; "api" или "cli" — принудительно.</summary>
    public string Provider { get; set; } = "auto";
    public string ApiModel { get; set; } = "claude-opus-5";
    public string CliModel { get; set; } = "sonnet";
    public int CliTimeoutSeconds { get; set; } = 90;
}

public sealed class LlmException(string message, Exception? inner = null) : Exception(message, inner);
