using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OComplexAssistant.Llm;

/// <summary>
/// Вызов Claude через Claude Code CLI в headless-режиме (claude -p).
/// Позволяет запускать прототип по подписке, без отдельного API-ключа.
/// </summary>
public sealed class ClaudeCliLlmClient(IOptions<LlmOptions> options) : ILlmClient
{
    private readonly LlmOptions _options = options.Value;

    public async Task<LlmResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        // Системный промпт передаём файлом: многострочный русский текст в аргументах cmd ломается.
        var promptFile = Path.Combine(Path.GetTempPath(), $"ocomplex-system-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(promptFile, systemPrompt, new UTF8Encoding(false), ct);

        try
        {
            var args = "-p --safe-mode --strict-mcp-config --tools \"\" --no-session-persistence " +
                       $"--output-format json --model {_options.CliModel} --system-prompt-file \"{promptFile}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "claude",
                Arguments = OperatingSystem.IsWindows() ? $"/c claude {args}" : args,
                WorkingDirectory = Path.GetTempPath(),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo)
                ?? throw new LlmException("Не удалось запустить Claude Code CLI.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.CliTimeoutSeconds));

            await process.StandardInput.WriteAsync(userPrompt);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                throw new LlmException($"Claude Code CLI не ответил за {_options.CliTimeoutSeconds} с.");
            }

            var stdout = await stdoutTask;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                throw new LlmException($"Claude Code CLI завершился с кодом {process.ExitCode}: {await stderrTask}");
            }

            return Parse(stdout);
        }
        finally
        {
            File.Delete(promptFile);
        }
    }

    private LlmResult Parse(string stdout)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(stdout).RootElement;
        }
        catch (JsonException ex)
        {
            throw new LlmException("Claude Code CLI вернул ответ не в формате JSON.", ex);
        }

        if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
        {
            throw new LlmException($"Claude Code CLI сообщил об ошибке: {root.GetProperty("result").GetString()}");
        }

        var usage = root.GetProperty("usage");
        var model = root.TryGetProperty("modelUsage", out var modelUsage)
            ? modelUsage.EnumerateObject().Select(p => p.Name).FirstOrDefault() ?? _options.CliModel
            : _options.CliModel;

        return new LlmResult(
            root.GetProperty("result").GetString() ?? string.Empty,
            "Claude Code CLI",
            model,
            // CLI кэширует промпт, поэтому полный вход = новые токены + записанные в кэш + прочитанные из кэша.
            Tokens(usage, "input_tokens") + Tokens(usage, "cache_creation_input_tokens") + Tokens(usage, "cache_read_input_tokens"),
            Tokens(usage, "output_tokens"));
    }

    private static long Tokens(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
}
