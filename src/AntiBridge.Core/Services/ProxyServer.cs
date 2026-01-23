using System.Diagnostics;
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
    private readonly ISignatureCache? _signatureCache;
    private readonly ILoadBalancer? _loadBalancer;
    private readonly IModelRouter? _modelRouter;
    private readonly TrafficMonitor? _trafficMonitor;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    /// <summary>
    /// Default max tokens for Flash models (1M tokens)
    /// </summary>
    private const int FlashMaxTokens = 1_000_000;

    /// <summary>
    /// Default max tokens for Pro models (2M tokens)
    /// </summary>
    private const int ProMaxTokens = 2_000_000;

    public bool IsRunning { get; private set; }
    public string BaseUrl => $"http://{_config.Host}:{_config.Port}";

    public event Action<string>? OnLog;
    public event Action<string>? OnError;
    public event Action<bool>? OnStatusChanged;

    public ProxyServer(
        AntigravityExecutor executor,
        AccountStorageService accountStorage,
        ProxyConfig? config = null,
        ISignatureCache? signatureCache = null,
        ILoadBalancer? loadBalancer = null,
        IModelRouter? modelRouter = null,
        TrafficMonitor? trafficMonitor = null)
    {
        _executor = executor;
        _accountStorage = accountStorage;
        _config = config ?? new ProxyConfig();
        _signatureCache = signatureCache;
        _loadBalancer = loadBalancer;
        _modelRouter = modelRouter;
        _trafficMonitor = trafficMonitor;
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
        var stopwatch = Stopwatch.StartNew();
        TrafficLog? trafficLog = null;
        Account? account = null;
        string? originalModel = null;
        string? mappedModel = null;
        bool compressionApplied = false;

        try
        {
            account = GetActiveAccount();
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

            originalModel = JsonHelper.GetString(requestNode, "model") ?? _config.DefaultModel ?? "gemini-2.5-pro";
            var stream = JsonHelper.GetBool(requestNode, "stream") ?? false;

            // Apply model routing (Task 8.1)
            mappedModel = _modelRouter?.ResolveModel(originalModel) ?? originalModel;
            OnLog?.Invoke($"OpenAI request: model={originalModel} -> {mappedModel}, stream={stream}");

            // Update the model in the request
            if (requestNode is JsonObject requestObj)
            {
                requestObj["model"] = mappedModel;
            }

            // Apply context compression (Task 8.3)
            var maxTokens = GetMaxTokensForModel(mappedModel);
            var compressionResult = ContextManager.ApplyProgressiveCompression(requestNode, maxTokens);
            compressionApplied = compressionResult.WasCompressed;
            if (compressionApplied)
            {
                OnLog?.Invoke($"Context compression applied: layers={string.Join(",", compressionResult.LayersApplied)}, pressure={compressionResult.FinalPressure}%");
                response.Headers.Add("X-Context-Purified", "true");
            }

            // Apply device profile headers (Task 8.4)
            ApplyDeviceProfileHeaders(account, request);

            // Create traffic log at request start (Task 8.2)
            trafficLog = new TrafficLog
            {
                Method = request.HttpMethod,
                Url = request.Url?.AbsolutePath ?? "",
                Model = originalModel,
                MappedModel = mappedModel,
                AccountEmail = account.Email,
                Protocol = "openai",
                RequestBody = TruncateBody(Encoding.UTF8.GetString(body))
            };

            // Re-serialize the modified request
            body = Encoding.UTF8.GetBytes(JsonHelper.Stringify(requestNode) ?? "{}");

            if (stream)
            {
                await HandleOpenAIStreamAsync(account, mappedModel, body, response, cancellationToken);
            }
            else
            {
                await HandleOpenAINonStreamAsync(account, mappedModel, body, response, cancellationToken);
            }

            // Update traffic log with success
            if (trafficLog != null)
            {
                trafficLog.Status = 200;
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"OpenAI request error: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonResponseAsync(response, new { error = new { message = ex.Message } });

            // Update traffic log with error
            if (trafficLog != null)
            {
                trafficLog.Status = 500;
                trafficLog.Error = ex.Message;
            }
        }
        finally
        {
            stopwatch.Stop();

            // Log request in finally block (Task 8.2.4)
            if (trafficLog != null)
            {
                trafficLog.DurationMs = stopwatch.ElapsedMilliseconds;
                _trafficMonitor?.LogRequest(trafficLog);
            }
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
        var stopwatch = Stopwatch.StartNew();
        TrafficLog? trafficLog = null;
        Account? account = null;
        string? originalModel = null;
        string? mappedModel = null;
        bool compressionApplied = false;

        try
        {
            account = GetActiveAccount();
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

            originalModel = JsonHelper.GetString(requestNode, "model") ?? _config.DefaultModel ?? "claude-sonnet-4-20250514";
            var stream = JsonHelper.GetBool(requestNode, "stream") ?? false;

            // Apply model routing (Task 8.1)
            mappedModel = _modelRouter?.ResolveModel(originalModel) ?? originalModel;
            OnLog?.Invoke($"Claude request: model={originalModel} -> {mappedModel}, stream={stream}");

            // Update the model in the request
            if (requestNode is JsonObject requestObj)
            {
                requestObj["model"] = mappedModel;
            }

            // Apply context compression (Task 8.3)
            var maxTokens = GetMaxTokensForModel(mappedModel);
            var compressionResult = ContextManager.ApplyProgressiveCompression(requestNode, maxTokens);
            compressionApplied = compressionResult.WasCompressed;
            if (compressionApplied)
            {
                OnLog?.Invoke($"Context compression applied: layers={string.Join(",", compressionResult.LayersApplied)}, pressure={compressionResult.FinalPressure}%");
                response.Headers.Add("X-Context-Purified", "true");
            }

            // Apply device profile headers (Task 8.4)
            ApplyDeviceProfileHeaders(account, request);

            // Create traffic log at request start (Task 8.2)
            trafficLog = new TrafficLog
            {
                Method = request.HttpMethod,
                Url = request.Url?.AbsolutePath ?? "",
                Model = originalModel,
                MappedModel = mappedModel,
                AccountEmail = account.Email,
                Protocol = "anthropic",
                RequestBody = TruncateBody(Encoding.UTF8.GetString(body))
            };

            // Re-serialize the modified request
            body = Encoding.UTF8.GetBytes(JsonHelper.Stringify(requestNode) ?? "{}");

            if (stream)
            {
                await HandleClaudeStreamAsync(account, mappedModel, body, response, cancellationToken);
            }
            else
            {
                await HandleClaudeNonStreamAsync(account, mappedModel, body, response, cancellationToken);
            }

            // Update traffic log with success
            if (trafficLog != null)
            {
                trafficLog.Status = 200;
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Claude request error: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonResponseAsync(response, new { type = "error", error = new { type = "api_error", message = ex.Message } });

            // Update traffic log with error
            if (trafficLog != null)
            {
                trafficLog.Status = 500;
                trafficLog.Error = ex.Message;
            }
        }
        finally
        {
            stopwatch.Stop();

            // Log request in finally block (Task 8.2.4)
            if (trafficLog != null)
            {
                trafficLog.DurationMs = stopwatch.ElapsedMilliseconds;
                _trafficMonitor?.LogRequest(trafficLog);
            }
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
        // If LoadBalancer is configured, use it for account selection
        // Requirements 5.1, 5.2, 5.3, 5.4, 5.5
        if (_loadBalancer != null)
        {
            var account = _loadBalancer.GetNextAccount();
            if (account != null)
                return account;
            
            // Fallback to storage if LoadBalancer returns null (all rate limited)
            // This allows manual account selection to still work
        }

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

    /// <summary>
    /// Get the maximum token limit for a model.
    /// Flash models: 1M tokens, Pro models: 2M tokens.
    /// </summary>
    /// <param name="modelName">The model name to check.</param>
    /// <returns>Maximum token limit for the model.</returns>
    private static int GetMaxTokensForModel(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return FlashMaxTokens;

        // Pro models have 2M token limit
        if (modelName.Contains("pro", StringComparison.OrdinalIgnoreCase))
            return ProMaxTokens;

        // Flash and other models have 1M token limit
        return FlashMaxTokens;
    }

    /// <summary>
    /// Apply device profile headers to the request.
    /// Falls back to system defaults when no profile is set.
    /// </summary>
    /// <param name="account">The account to get device profile from.</param>
    /// <param name="request">The HTTP request (for potential header modification).</param>
    private void ApplyDeviceProfileHeaders(Account account, HttpListenerRequest request)
    {
        var profile = account.DeviceProfile;
        
        if (profile != null)
        {
            // Log that we're using the account's device profile
            OnLog?.Invoke($"Using device profile: version={profile.Version}, machine_id={profile.MachineId[..8]}...");
            
            // The device profile headers would be applied to outgoing requests to Antigravity
            // This is handled by the executor, but we log it here for visibility
            _executor.SetDeviceProfile(profile);
        }
        else
        {
            // Fall back to system defaults (no custom profile)
            OnLog?.Invoke("Using system default device identifiers");
            _executor.SetDeviceProfile(null);
        }
    }

    /// <summary>
    /// Truncate request/response body for logging.
    /// Limits to 10KB to prevent excessive storage.
    /// </summary>
    /// <param name="body">The body string to truncate.</param>
    /// <returns>Truncated body string.</returns>
    private static string? TruncateBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return body;

        const int maxLength = 10 * 1024; // 10KB
        if (body.Length <= maxLength)
            return body;

        return body[..maxLength] + "...[truncated]";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener.Close();
        _cts?.Dispose();
    }
}
