using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AntiBridge.Core.Models;
using AntiBridge.Core.Translator;

namespace AntiBridge.Core.Services;

/// <summary>
/// HTTP Proxy Server that translates Claude/OpenAI API requests to Antigravity
/// </summary>
public class ProxyServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly AntigravityExecutor _executor;
    private readonly AccountStorageService _accountStorage;
    private readonly ProxyConfig _config;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public bool IsRunning { get; private set; }
    public string BaseUrl => $"http://{_config.Host}:{_config.Port}";

    public event Action<string>? OnLog;
    public event Action<string>? OnError;
    public event Action<bool>? OnStatusChanged;

    public ProxyServer(
        AntigravityExecutor executor,
        AccountStorageService accountStorage,
        ProxyConfig? config = null)
    {
        _executor = executor;
        _accountStorage = accountStorage;
        _config = config ?? new ProxyConfig();
        _listener = new HttpListener();
        
        _executor.OnLog += msg => OnLog?.Invoke($"[Executor] {msg}");
    }

    public void Start()
    {
        if (IsRunning) return;

        try
        {
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://{_config.Host}:{_config.Port}/");
            _listener.Start();
            
            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => ListenAsync(_cts.Token));
            
            IsRunning = true;
            OnStatusChanged?.Invoke(true);
            OnLog?.Invoke($"Proxy server started on {BaseUrl}");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to start server: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        try
        {
            _cts?.Cancel();
            _listener.Stop();
            
            if (_serverTask != null)
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Error stopping server: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            OnStatusChanged?.Invoke(false);
            OnLog?.Invoke("Proxy server stopped");
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Listener error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Add CORS headers
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Api-Key, anthropic-version");

            // Handle preflight
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            var path = request.Url?.AbsolutePath ?? "";
            OnLog?.Invoke($"{request.HttpMethod} {path}");

            // Route requests
            if (path == "/" || path == "")
            {
                await HandleRootAsync(response);
            }
            else if (path == "/v1/models" || path == "/v1/models/")
            {
                await HandleModelsAsync(request, response, cancellationToken);
            }
            else if (path == "/v1/chat/completions")
            {
                await HandleOpenAIChatCompletionsAsync(request, response, cancellationToken);
            }
            else if (path == "/v1/messages")
            {
                await HandleClaudeMessagesAsync(request, response, cancellationToken);
            }
            else if (path == "/v1/messages/count_tokens")
            {
                await HandleClaudeCountTokensAsync(request, response, cancellationToken);
            }
            else
            {
                response.StatusCode = 404;
                await WriteJsonResponseAsync(response, new { error = "Not found" });
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Request error: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonResponseAsync(response, new { error = ex.Message });
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    private async Task HandleRootAsync(HttpListenerResponse response)
    {
        await WriteJsonResponseAsync(response, new
        {
            message = "AntiBridge Proxy Server",
            version = "1.0.0",
            endpoints = new[]
            {
                "GET /v1/models",
                "POST /v1/chat/completions (OpenAI format)",
                "POST /v1/messages (Claude format)",
                "POST /v1/messages/count_tokens"
            }
        });
    }

    private async Task HandleModelsAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var account = GetActiveAccount();
        if (account == null)
        {
            response.StatusCode = 401;
            await WriteJsonResponseAsync(response, new { error = "No active account" });
            return;
        }

        var userAgent = request.Headers["User-Agent"] ?? "";
        var isClaude = userAgent.StartsWith("claude-cli");

        var models = await _executor.FetchModelsAsync(account, cancellationToken);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (isClaude)
        {
            // Claude format
            var claudeModels = models.Select(m => new
            {
                id = m,
                display_name = m,
                created_at = now
            }).ToList();

            await WriteJsonResponseAsync(response, new { data = claudeModels });
        }
        else
        {
            // OpenAI format
            var openaiModels = models.Select(m => new
            {
                id = m,
                @object = "model",
                created = now,
                owned_by = "antigravity"
            }).ToList();

            await WriteJsonResponseAsync(response, new { @object = "list", data = openaiModels });
        }
    }

    private async Task HandleOpenAIChatCompletionsAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var account = GetActiveAccount();
        if (account == null)
        {
            response.StatusCode = 401;
            await WriteJsonResponseAsync(response, new { error = new { message = "No active account" } });
            return;
        }

        var body = await ReadRequestBodyAsync(request);
        var requestNode = JsonHelper.Parse(body);
        if (requestNode == null)
        {
            response.StatusCode = 400;
            await WriteJsonResponseAsync(response, new { error = new { message = "Invalid JSON" } });
            return;
        }

        var modelName = JsonHelper.GetString(requestNode, "model") ?? _config.DefaultModel ?? "gemini-2.5-pro";
        var stream = JsonHelper.GetBool(requestNode, "stream") ?? false;

        OnLog?.Invoke($"OpenAI request: model={modelName}, stream={stream}");

        if (stream)
        {
            await HandleOpenAIStreamAsync(account, modelName, body, response, cancellationToken);
        }
        else
        {
            await HandleOpenAINonStreamAsync(account, modelName, body, response, cancellationToken);
        }
    }

    private async Task HandleOpenAIStreamAsync(Account account, string modelName, byte[] body, HttpListenerResponse response, CancellationToken cancellationToken)
    {
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        var state = new OpenAIStreamState { UnixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

        await foreach (var chunk in _executor.ExecuteStreamAsync(account, modelName, body, "openai", cancellationToken))
        {
            var translated = AntigravityToOpenAIResponse.ConvertStream(modelName, chunk, state);
            foreach (var t in translated)
            {
                var sseData = $"data: {t}\n\n";
                var bytes = Encoding.UTF8.GetBytes(sseData);
                await response.OutputStream.WriteAsync(bytes, cancellationToken);
                await response.OutputStream.FlushAsync(cancellationToken);
            }
        }

        // Send [DONE]
        var doneBytes = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
        await response.OutputStream.WriteAsync(doneBytes, cancellationToken);
    }

    private async Task HandleOpenAINonStreamAsync(Account account, string modelName, byte[] body, HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync(account, modelName, body, "openai", cancellationToken);
        var translated = AntigravityToOpenAIResponse.ConvertNonStream(modelName, result);
        
        response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(translated);
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private async Task HandleClaudeMessagesAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var account = GetActiveAccount();
        if (account == null)
        {
            response.StatusCode = 401;
            await WriteJsonResponseAsync(response, new { type = "error", error = new { type = "authentication_error", message = "No active account" } });
            return;
        }

        var body = await ReadRequestBodyAsync(request);
        var requestNode = JsonHelper.Parse(body);
        if (requestNode == null)
        {
            response.StatusCode = 400;
            await WriteJsonResponseAsync(response, new { type = "error", error = new { type = "invalid_request_error", message = "Invalid JSON" } });
            return;
        }

        var modelName = JsonHelper.GetString(requestNode, "model") ?? _config.DefaultModel ?? "claude-sonnet-4-20250514";
        var stream = JsonHelper.GetBool(requestNode, "stream") ?? false;

        OnLog?.Invoke($"Claude request: model={modelName}, stream={stream}");

        if (stream)
        {
            await HandleClaudeStreamAsync(account, modelName, body, response, cancellationToken);
        }
        else
        {
            await HandleClaudeNonStreamAsync(account, modelName, body, response, cancellationToken);
        }
    }

    private async Task HandleClaudeStreamAsync(Account account, string modelName, byte[] body, HttpListenerResponse response, CancellationToken cancellationToken)
    {
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        var state = new ClaudeStreamState();

        await foreach (var chunk in _executor.ExecuteStreamAsync(account, modelName, body, "claude", cancellationToken))
        {
            var translated = AntigravityToClaudeResponse.ConvertStream(modelName, chunk, state);
            foreach (var t in translated)
            {
                var bytes = Encoding.UTF8.GetBytes(t);
                await response.OutputStream.WriteAsync(bytes, cancellationToken);
                await response.OutputStream.FlushAsync(cancellationToken);
            }
        }
    }

    private async Task HandleClaudeNonStreamAsync(Account account, string modelName, byte[] body, HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync(account, modelName, body, "claude", cancellationToken);
        var translated = AntigravityToClaudeResponse.ConvertNonStream(modelName, result);
        
        response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(translated);
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private async Task HandleClaudeCountTokensAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var account = GetActiveAccount();
        if (account == null)
        {
            response.StatusCode = 401;
            await WriteJsonResponseAsync(response, new { type = "error", error = new { type = "authentication_error", message = "No active account" } });
            return;
        }

        var body = await ReadRequestBodyAsync(request);
        var requestNode = JsonHelper.Parse(body);
        var modelName = JsonHelper.GetString(requestNode, "model") ?? "claude-sonnet-4-20250514";

        var count = await _executor.CountTokensAsync(account, modelName, body, "claude", cancellationToken);
        
        await WriteJsonResponseAsync(response, new { input_tokens = count });
    }

    private Account? GetActiveAccount()
    {
        var accounts = _accountStorage.ListAccounts();
        if (accounts.Count == 0) return null;

        // Get current account or first available
        var currentId = _accountStorage.GetCurrentAccountId();
        if (!string.IsNullOrEmpty(currentId))
        {
            var current = accounts.FirstOrDefault(a => a.Id == currentId);
            if (current != null) return current;
        }

        return accounts.First();
    }

    private static async Task<byte[]> ReadRequestBodyAsync(HttpListenerRequest request)
    {
        using var ms = new MemoryStream();
        await request.InputStream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static async Task WriteJsonResponseAsync(HttpListenerResponse response, object data)
    {
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
        var bytes = Encoding.UTF8.GetBytes(json);
        await response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener.Close();
        _cts?.Dispose();
    }
}
