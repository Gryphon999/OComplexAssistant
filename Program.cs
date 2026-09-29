using OComplexAssistant.Assistant;
using OComplexAssistant.Knowledge;
using OComplexAssistant.Llm;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.AddSingleton<KnowledgeBase>();
builder.Services.AddSingleton<AssistantService>();

var provider = builder.Configuration["Llm:Provider"] ?? "auto";
var hasApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
if (provider == "api" || (provider == "auto" && hasApiKey))
{
    builder.Services.AddSingleton<ILlmClient, AnthropicLlmClient>();
}
else
{
    builder.Services.AddSingleton<ILlmClient, ClaudeCliLlmClient>();
}

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/kb", (KnowledgeBase kb) => kb.Articles);

app.MapPost("/api/assist", async (AssistRequest request, AssistantService assistant, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Пустое обращение клиента." });
    }

    try
    {
        return Results.Ok(await assistant.AssistAsync(request, ct));
    }
    catch (LlmException ex)
    {
        app.Logger.LogError(ex, "LLM call failed");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();
